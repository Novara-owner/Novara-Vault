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
    private static bool _pendingSync;




    private static bool _skipNextSync;
    private static bool _firstSyncDone;
    private static Mutex? _singleInstanceMutex;


    private static Microsoft.UI.Dispatching.DispatcherQueue? _uiQueue;


    public static string DataDirName =>
#if DEBUG
        "Novara-Dev";
#else
        "Novara";
#endif

    public static string JsonPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        DataDirName, "stickies.json");


    public static string SingleInstanceMutexName =>
#if DEBUG
        @"Local\StickNoteHost.Dev";
#else
        @"Local\StickNoteHost";
#endif

    public App()
    {
        InitializeComponent();





        UnhandledException += (_, e) => { Log($"UnhandledException: {e.Exception}"); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"AppDomain Unhandled: {e.ExceptionObject}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched 入口");






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
        if (trayIcon != null) TrayIcon.Icon = trayIcon;
        RebuildTrayMenu();
        TrayIcon.LeftClickCommand = new RelayCommand(ShowNote);


        try { TrayIcon.ForceCreate(); } catch { TrayIcon?.Dispose(); TrayIcon = null; }
        _lastTrayLang = CurrentLanguage;
        Log("OnLaunched 结束");
    }

    private static string TrayTooltip()
        => App.T("Novara 桌面便签", "Novara Sticky Notes", "Novara 桌面便籤", "Novara 스티커 메모", "Novara 付箋");

    private static string? _lastTrayLang;


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


    public static string CurrentLanguage { get; set; } = "zh-CN";


    public static string T(string zh, string en, string tw, string ko, string ja) => CurrentLanguage switch
    {
        "en-US" => en,
        "zh-TW" => tw,
        "ko-KR" => ko,
        "ja-JP" => ja,
        _ => zh,
    };


    public static void SyncNotes()
    {
        if (_skipNextSync) { _skipNextSync = false; return; }
        if (_syncing) { _pendingSync = true; return; }
        _syncing = true;
        try
        {



            StickyData data;
            string theme;
            using (StickiesLock.Enter())
            {
                var loaded = Load();
                if (loaded == null && !System.IO.File.Exists(JsonPath))
                {



                    loaded = new StickyData();
                }
                if (loaded == null) return;
                data = loaded;





                if (!_firstSyncDone && data.Notes.RemoveAll(n => n.Kind == "reminder" && n.DueTime.HasValue && n.DueTime.Value <= DateTimeOffset.Now) > 0)
                {




                    if (Save(data)) _firstSyncDone = true;
                    else Log("SyncNotes: 过期提醒清理写盘失败，保持未完成以便下一轮重试");
                }
                else _firstSyncDone = true;
            }
            theme = data.Theme ?? "light";
            CurrentLanguage = string.IsNullOrEmpty(data.Language) ? "zh-CN" : data.Language;

            if (CurrentLanguage != _lastTrayLang)
            {
                _lastTrayLang = CurrentLanguage;
                RebuildTrayMenu();
                foreach (var w in NoteWindows.Values) w.RefreshLocalizedTexts();
            }


            foreach (var (id, w) in NoteWindows.ToList())
            {
                if (!data.Notes.Any(n => n.Id == id))
                {
                    NoteWindows.Remove(id);
                    try { w.Close(); } catch { }
                }
            }


            int idx = 0;
            foreach (var n in data.Notes)
            {



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
            if (_pendingSync) { _pendingSync = false; _uiQueue?.TryEnqueue(SyncNotes); }
        }
    }

    private static StickyData? Load()
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
        catch (Exception ex) { Log($"Load 失败: {ex.Message}"); return null; }
    }




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


    public static void RemoveNote(string id)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return;
                int before = data.Notes.Count;
                data.Notes.RemoveAll(n => n.Id == id);
                if (data.Notes.Count == before) return;
                Save(data);
            }
            Log($"移除卡片 {id}");
        }
        catch (Exception ex) { Log($"RemoveNote 失败: {ex.Message}"); }
    }




    public static bool UpdateTodoItems(string id, List<StickyTodoItem> items)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return false;
                var note = data.Notes.FirstOrDefault(n => n.Id == id);
                if (note == null) return false;


                var existing = note.Items ?? new List<StickyTodoItem>();




                var hostByLabel = new Dictionary<string, StickyTodoItem>();
                foreach (var it in items)
                    if (!string.IsNullOrEmpty(it.Label) && !hostByLabel.ContainsKey(it.Label)) hostByLabel[it.Label] = it;
                foreach (var ex in existing)
                    if (!string.IsNullOrEmpty(ex.Label) && hostByLabel.TryGetValue(ex.Label, out var src)) ex.Checked = src.Checked;
                note.Items = existing;
                _skipNextSync = true;



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


        foreach (var w in NoteWindows.Values)
        {
            try { w.ShowWindow(); }
            catch (Exception ex) { Log($"ShowNote: 单窗唤起失败，跳过: {ex.Message}"); }
        }
    }


    public static void ExitApp()
    {


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
    public string? Language { get; set; } = "zh-CN";
    public List<StickyNote> Notes { get; set; } = new();
}

public class StickyNote
{
    public string Id { get; set; } = "";
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }

    public DateTimeOffset? DueTime { get; set; }


    public List<StickyTodoItem>? Items { get; set; }
}


public class StickyTodoItem
{
    public string Label { get; set; } = "";
    public bool Checked { get; set; }
}



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
        catch (System.Threading.AbandonedMutexException) { acquired = true;  }
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
