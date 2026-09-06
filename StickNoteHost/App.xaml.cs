using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace StickNoteHost;

public partial class App : Application
{
    public static Dictionary<string, StickyNoteWindow> NoteWindows { get; } = new();
    public static TaskbarIcon? TrayIcon { get; private set; }
    private static FileSystemWatcher? _watcher;
    private static bool _syncing;
    private static bool _pendingSync; // E1-17: merge instead of drop - a write during a sync schedules one more round
    // N4-40 (ignored, owner rule 3.10#4): plain bool echo guard without event identity - the R4-SN9
    // watcher buffer-overflow compensation read can consume it one event early in a narrow race,
    // swallowing one legitimate sync. Self-heals on the next write; a real fix needs event
    // correlation the watcher does not provide (disproportionate for the exposure).
    private static bool _skipNextSync; // self-write guard: skip re-render on our own todo write-back
    private static bool _firstSyncDone; // E4-13: the expired-reminder cleanup below runs only on the cold-start sync
    private static Mutex? _singleInstanceMutex;
    // UI-thread queue captured at launch; FileSystemWatcher callbacks run on a worker
    // thread where DispatcherQueue.GetForCurrentThread() returns null (NRE -> crash).
    private static Microsoft.UI.Dispatching.DispatcherQueue? _uiQueue;

    /// <summary>Same isolation rule as the main app: Debug (dev) uses Novara-Dev, Release uses Novara.</summary>
    public static string DataDirName =>
#if DEBUG
        "Novara-Dev";
#else
        "Novara";
#endif

    public static string JsonPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        DataDirName, "stickies.json");

    /// <summary>Single-instance mutex name, isolated per config like the main app's mutex.</summary>
    public static string SingleInstanceMutexName =>
#if DEBUG
        @"Local\StickNoteHost.Dev";
#else
        @"Local\StickNoteHost";
#endif

    public App()
    {
        InitializeComponent();
        
        // window glitch must not take the whole host (and every other open sticky) down with it.
        // e.Handled = true keeps the process alive after logging; the corrupted window may stay
        // visually broken until the user closes it / restarts the host, which is preferable to all
        // stickies vanishing silently (the tray Exit + main-app wake paths remain fully usable).
        UnhandledException += (_, e) => { Log($"UnhandledException: {e.Exception}"); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"AppDomain Unhandled: {e.ExceptionObject}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched 入口");
        // Single instance: a second launch (from the main app's host-wake call while we are
        // already running) must exit immediately - otherwise duplicate windows appear.
        
        
        
        
        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            createdNew = true; 
            
            
            
            try { _singleInstanceMutex = Mutex.OpenExisting(SingleInstanceMutexName); }
            catch { _singleInstanceMutex = null; } 
        }
        if (!createdNew)
        {
            Log("已有 Host 实例在运行，本实例退出");
            System.Environment.Exit(0);
            return;
        }
        _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        SyncNotes();
        StartWatching();
        Log("SyncNotes 完成");

        // Tray icon: show notes / exit. Menu text + tooltip are localized and rebuilt when the
        // stickies.json language changes (D25); D26: DoubleClickCommand can never fire while
        // NoLeftClickDelay=true, so it is removed (left-click ShowNote covers the interaction).
        // 2026-08-31: the publish bundle does not carry Assets\ - a failed icon load here used to
        // kill the whole process right after the windows were created (Event-log crash, sticky
        // windows vanished ~5s). Probe exe dir first, then the parent (Host\ subfolder layout),
        // and degrade to an iconless tray instead of dying.
        System.Drawing.Icon? trayIcon = null;
        var parentDir = System.IO.Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        foreach (var dir in new[] { AppContext.BaseDirectory, parentDir })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                var probe = System.IO.Path.Combine(dir, "Assets", "128.ico");
                if (System.IO.File.Exists(probe)) { trayIcon = new System.Drawing.Icon(probe); break; }
            }
            catch { }
        }
        TrayIcon = new TaskbarIcon
        {
            ToolTipText = TrayTooltip(),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
        };
        if (trayIcon != null) TrayIcon.Icon = trayIcon; // iconless tray beats a dead host
        RebuildTrayMenu(); // assigns ContextFlyout + localized tooltip
        TrayIcon.LeftClickCommand = new RelayCommand(ShowNote);
        try { TrayIcon.ForceCreate(); } catch { TrayIcon = null; }
        _lastTrayLang = CurrentLanguage;
        Log("OnLaunched 结束");
    }

    private static string TrayTooltip() // D24: localized tray tooltip (was hard-coded English)
        => App.T("Novara 桌面便签", "Novara Sticky Notes", "Novara 桌面便籤", "Novara 스티커 메모", "Novara 付箋");

    private static string? _lastTrayLang;

    /// <summary>D25: rebuild the tray context menu + tooltip with the current language.</summary>
    private static void RebuildTrayMenu()
    {
        if (TrayIcon == null) return;
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"]
        };
        menu.Items.Add(new MenuFlyoutItem { Text = App.T("显示便签", "Show notes", "顯示便籤", "메모 표시", "メモを表示"), Command = new RelayCommand(ShowNote) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = App.T("退出", "Exit", "退出", "종료", "終了"), Command = new RelayCommand(ExitApp) });
        TrayIcon.ContextFlyout = menu;
        TrayIcon.ToolTipText = TrayTooltip();
    }

    private static void StartWatching()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(JsonPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                _watcher = new FileSystemWatcher(dir, "stickies.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += (_, _) => _uiQueue?.TryEnqueue(SyncNotes);
                _watcher.Created += (_, _) => _uiQueue?.TryEnqueue(SyncNotes);
                _watcher.Deleted += (_, _) => _uiQueue?.TryEnqueue(SyncNotes);
                _watcher.Renamed += (_, _) => _uiQueue?.TryEnqueue(SyncNotes); 
                // E1-26: watcher can die on buffer overflow (rapid writes) - recreate it
                // E4-34: Error fires on the watcher thread - marshal the recreate onto the UI thread so
                // _watcher is never disposed/rebuilt from two threads at once.
                _watcher.Error += (_, _) => _uiQueue?.TryEnqueue(() =>
                {
                    try { _watcher?.Dispose(); } catch { }
                    StartWatching();
                    SyncNotes(); 
                });
            }
        }
        catch (Exception ex) { Log($"watcher 失败: {ex.Message}"); }
    }

    /// <summary>Menu language for this host (zh-CN / en-US / zh-TW), driven by stickies.json.</summary>
    public static string CurrentLanguage { get; set; } = "zh-CN"; // zh-CN / en-US / zh-TW / ko-KR / ja-JP (i18n)

    /// <summary>Lightweight bilingual picker for the note context menu (host has no full I18N).</summary>
    public static string T(string zh, string en, string tw, string ko, string ja) => CurrentLanguage switch
    {
        "en-US" => en,
        "zh-TW" => tw,
        "ko-KR" => ko,
        "ja-JP" => ja,
        _ => zh,
    };

    /// <summary>Read stickies.json and reconcile windows: add / update / remove by note id.</summary>
    public static void SyncNotes()
    {
        if (_skipNextSync) { _skipNextSync = false; return; } // our own todo write-back: no re-render needed
        if (_syncing) { _pendingSync = true; return; } // E1-17: merge instead of drop (was: silently discard the event)
        _syncing = true;
        try
        {
            // E4-14: the cold-start load + expired-reminder cleanup is a read-modify-write of
            // stickies.json - hold the cross-process lock so a concurrent main-app write is never
            // clobbered (the cleanup used to Save over it).
            StickyData data;
            string theme;
            using (StickiesLock.Enter())
            {
                var loaded = Load();
                if (loaded == null && !System.IO.File.Exists(JsonPath))
                {
                    // N4H-02: a MISSING stickies.json is a legitimate empty state (cold start with no data,
                    // or direct exe launch) - flow to the no-cards exit below. Only an UNREADABLE file
                    // skips this round and waits for the next event (E1-03 semantics preserved).
                    loaded = new StickyData();
                }
                if (loaded == null) return; // E1-03: half-written / unreadable file - skip this round, wait for the next event
                data = loaded;

                // Reminders whose due time already passed: drop silently (no card, no sound) and persist.
                // E4-13: only on the FIRST sync (cold start) - running cards that come due afterwards are
                // handled by their own FireDue state machine (beep -> flash -> RemoveNote); an unconditional
                // cleanup here would yank cards out from under a running FireDue sequence and kill their alarm.
                if (!_firstSyncDone && data.Notes.RemoveAll(n => n.Kind == "reminder" && n.DueTime.HasValue && n.DueTime.Value <= DateTimeOffset.Now) > 0)
                {
                    Save(data);
                    Log("SyncNotes: 静默丢弃已过期的提醒卡");
                }
                _firstSyncDone = true;
            }
            theme = data.Theme ?? "light";
            CurrentLanguage = string.IsNullOrEmpty(data.Language) ? "zh-CN" : data.Language; // i18n
            // D25: rebuild the tray menu/tooltip when the language changed (it was generated once at launch).
            if (CurrentLanguage != _lastTrayLang)
            {
                _lastTrayLang = CurrentLanguage;
                RebuildTrayMenu();
                foreach (var w in NoteWindows.Values) w.RefreshLocalizedTexts(); // E1-18: refresh existing windows' title/reminder text
            }

            // Remove windows whose note disappeared from the file.
            foreach (var (id, w) in NoteWindows.ToList())
            {
                if (!data.Notes.Any(n => n.Id == id))
                {
                    NoteWindows.Remove(id);
                    try { w.Close(); } catch { }
                }
            }

            // Add / update windows.
            int idx = 0;
            foreach (var n in data.Notes)
            {
                // N3-50: per-window guard - one window throwing (corrupt note payload, UI race)
                // must NOT abort the rest of this round; the window is skipped and re-synced on
                // the next event, everything else still gets its content update.
                try
                {
                    if (NoteWindows.TryGetValue(n.Id, out var w))
                    {
                        w.SetContent(n.Title ?? "", n.Content ?? "", n.DueTime, n.Items);
                        w.ApplyTheme(theme);
                        Log($"SyncNotes: 更新便签窗口 id={n.Id}");
                    }
                    else
                    {
                        // New window: theme goes through the ctor + Loaded re-apply (ctor-only ApplyTheme
                        // was overwritten by first render - bug 2026-08-08, see StickyNoteWindow ctor).
                        // N2-61: allocate one past the current max among live reminder windows - the file
                        
                        int reminderIdx = 0;
                        foreach (var existing in App.NoteWindows.Values)
                            if (existing.IsReminder) reminderIdx = Math.Max(reminderIdx, existing.PositionIndex + 1);
                        var nw = new StickyNoteWindow(theme, n.Kind == "reminder", n.DueTime) { NoteId = n.Id, PositionIndex = n.Kind == "reminder" ? reminderIdx : idx };
                        nw.SetContent(n.Title ?? "", n.Content ?? "", n.DueTime, n.Items);
                        nw.Activate();
                        nw.HideFromTaskbar();
                        nw.Closed += (_, _) => Log($"便签 {n.Id} Closed");
                        NoteWindows[n.Id] = nw;
                        Log($"SyncNotes: 新建便签窗口 id={n.Id}");
                    }
                }
                catch (Exception ex) { Log($"SyncNotes: 便签 {n.Id} 同步失败已跳过: {ex.Message}"); }
                idx++;
            }
            Log($"SyncNotes: {NoteWindows.Count} 个便签窗口, theme={theme}");

            // Content-driven lifecycle: no cards at all -> this host has nothing to do,
            // exit (covers cold start with empty stickies.json and runtime full-close).
            if (NoteWindows.Count == 0)
            {
                Log("stickies.json 无内容，Host 自动退出");
                ExitApp();
                return;
            }
        }
        catch (Exception ex) { Log($"SyncNotes 异常: {ex.Message}"); }
        finally
        {
            _syncing = false;
            if (_pendingSync) { _pendingSync = false; _uiQueue?.TryEnqueue(SyncNotes); } // E1-17
        }
    }

    private static StickyData? Load() // E1-03: null = unreadable (half-written / missing file) - caller skips this round
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(JsonPath);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return null;
            if (!System.IO.File.Exists(JsonPath)) return null;
            var s = System.IO.File.ReadAllText(JsonPath);
            var data = JsonSerializer.Deserialize<StickyData>(s);
            if (data != null) data.Notes ??= new(); 
            return data;
        }
        catch (Exception ex) { Log($"Load 失败: {ex.Message}"); return null; } // never treat as empty - that would close all notes
    }

    /// <summary>Atomically write stickies.json (same style as the main app's writer, E2-04: tmp + move
    /// - our own FileSystemWatcher must never observe a half-written file). N2-27: returns success so
    /// callers gating side effects (echo suppression) can release them on failure.</summary>
    private static bool Save(StickyData data)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(JsonPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data);
            var tmp = JsonPath + ".tmp";
            System.IO.File.WriteAllText(tmp, json);
            System.IO.File.Move(tmp, JsonPath, true);
            return true;
        }
        catch (Exception ex) { Log($"Save 失败: {ex.Message}"); return false; }
    }

    /// <summary>Remove a card (any kind: note or reminder) from stickies.json after it is closed.</summary>
    public static void RemoveNote(string id)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return; // E1-03: unreadable file - nothing to remove this round
                int before = data.Notes.Count;
                data.Notes.RemoveAll(n => n.Id == id);
                if (data.Notes.Count == before) return;
                Save(data);
            }
            Log($"移除卡片 {id}");
        }
        catch (Exception ex) { Log($"RemoveNote 失败: {ex.Message}"); }
    }

    
    /// (debounced by StickyNoteWindow). Guarded so our own write does not trigger a re-render.</summary>
    /// <summary>N2-63b: returns true when the write landed (callers gate their dirty-flag reset on it).</summary>
    public static bool UpdateTodoItems(string id, List<StickyTodoItem> items)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return false;
                var note = data.Notes.FirstOrDefault(n => n.Id == id);
                if (note == null) return false; // card already gone - nothing to update
                
                
                var existing = note.Items ?? new List<StickyTodoItem>();
                // N3-10: merge by LABEL (N2-63a parity on the write-back side) - when the debounce tick
                // beats the watcher-synced SetContent, the host's _items still holds the OLD structure
                // while `existing` is the main program's NEW one; index-pairing wrote the check into the
                // wrong row (delete B -> B's check landed on C). Labels are the stable join key.
                var hostByLabel = new Dictionary<string, StickyTodoItem>();
                foreach (var it in items)
                    if (!string.IsNullOrEmpty(it.Label) && !hostByLabel.ContainsKey(it.Label)) hostByLabel[it.Label] = it;
                foreach (var ex in existing)
                    if (!string.IsNullOrEmpty(ex.Label) && hostByLabel.TryGetValue(ex.Label, out var src)) ex.Checked = src.Checked;
                note.Items = existing;
                _skipNextSync = true;
                // N2-27: Save() swallows its own exceptions, so the outer catch (guard reset) is
                // unreachable for write failures - release the echo guard explicitly on failure,
                // otherwise the NEXT legitimate main-program write gets swallowed once.
                if (!Save(data)) { _skipNextSync = false; Log("UpdateTodoItems: Save 失败，复位 echo 守卫"); return false; }
                return true;
            }
        }
        catch (Exception ex)
        {
            
            _skipNextSync = false;
            Log($"UpdateTodoItems 失败: {ex.Message}");
            return false;
        }
    }

    public static void Log(string msg)
    {
        try
        {
            
            // Debug keeps the desktop log (easy to spot while developing); Release writes
            // under %LocalAppData%\Novara\logs\.
            string dir;
#if DEBUG
            dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
#else
            dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Novara", "logs");
            System.IO.Directory.CreateDirectory(dir);
#endif
            var f = System.IO.Path.Combine(dir, "sticknotehost_debug.txt");
            // N2-62: rolling cap - a forever-running tray process used to grow the log without
            // bound; 1MB rotates to a single .old (kept one generation for post-mortem).
            try
            {
                if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 1_000_000)
                    System.IO.File.Move(f, f + ".old", overwrite: true);
            }
            catch { }
            System.IO.File.AppendAllText(f, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }

    public static void ShowNote()
    {
        
        // must not abort the foreach - it would leave every other sticky un-shown for this click.
        foreach (var w in NoteWindows.Values)
        {
            try { w.ShowWindow(); }
            catch (Exception ex) { Log($"ShowNote: 单窗唤起失败，跳过: {ex.Message}"); }
        }
    }


    public static void ExitApp()
    {
        // N4H-04: Environment.Exit bypasses AppWindow.Closing, so a todo check-state still inside its
        // 300ms debounce would die with the process - flush every window's pending write first.
        foreach (var w in NoteWindows.Values.ToList())
        {
            try { w.FlushPendingTodoSave(); } catch { }
        }
        TrayIcon?.Dispose();
        System.Environment.Exit(0);
    }
}

public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    public RelayCommand(Action execute) { _execute = execute; }
    public event System.EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}

public class StickyData
{
    public string? Theme { get; set; } = "light";
    public string? Language { get; set; } = "zh-CN"; // i18n: host menu language (zh-CN / en-US / zh-TW)
    public List<StickyNote> Notes { get; set; } = new();
}

public class StickyNote
{
    public string Id { get; set; } = "";
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    /// <summary>Absolute due time (local offset) for kind=reminder cards; null for notes/todos.</summary>
    public DateTimeOffset? DueTime { get; set; }
    /// <summary>Structured todo rows for kind=todo cards (index 0 = main todo, 1..n = sub-todos).
    /// Present only for interactive desktop todos; null for note/reminder cards and old data.</summary>
    public List<StickyTodoItem>? Items { get; set; }
}

/// <summary>One row of a desktop todo card: label + checked state (mirrors TodoCard.CheckedStates semantics).</summary>
public class StickyTodoItem
{
    public string Label { get; set; } = "";
    public bool Checked { get; set; }
}

/// <summary>E4-14: cross-process named mutex serializing stickies.json read-modify-write between the
/// main app and this host. Same name as the main app's StickiesLock (isolated per config).</summary>
internal static class StickiesLock
{
    private static readonly System.Threading.Mutex Mutex = new(false, MutexName);

    private static string MutexName =>
#if DEBUG
        @"Local\Novara.Stickies.Dev";
#else
        @"Local\Novara.Stickies";
#endif

    public static IDisposable Enter()
    {
        
        
        bool acquired = false;
        try { acquired = Mutex.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (System.Threading.AbandonedMutexException) { acquired = true; /* previous holder died while holding it - we now own it */ }
        return new Releaser(acquired ? Mutex : null);
    }

    private sealed class Releaser : IDisposable
    {
        private System.Threading.Mutex? _m;
        public Releaser(System.Threading.Mutex? m) => _m = m;
        public void Dispose()
        {
            try { _m?.ReleaseMutex(); } catch { }
            _m = null;
        }
    }
}
