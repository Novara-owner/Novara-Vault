using System.Diagnostics;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

/// <summary>
/// Stage-2 bridge to the desktop-sticky-note host (StickNoteHost.exe):
/// - stickies.json is a plain-text sync file in the app data dir (avoids the
///   exclusive lock on data.novadb). Host watches it and updates its notes.
/// - Format: { "theme": "light|dark", "notes": [ { "id", "kind", "title", "content", "dueTime" } ] }
/// </summary>
public static class StickySync
{
    public static string JsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "stickies.json");

    public static string? HostExePath { get; } = FindHostExe();

    /// <summary>E1-03: atomic write (tmp + move) - the Host FileSystemWatcher must never see a
    /// half-written stickies.json (a truncated file used to nuke all notes and exit the host).</summary>
    private static void Save(StickyData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var tmp = JsonPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, JsonPath, true);
    }


    private static string? FindHostExe()
    {
        // 1) Release layout: host sits next to the main exe.
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "StickNoteHost.exe");
        if (File.Exists(sideBySide)) return sideBySide;

        // 2) Dev layout (E5-12): walk up from the output dir to the repo root (the first dir that
        //    contains the StickNoteHost project), then probe bin[+\x64]\Debug|Release\... - the old
        //    code hard-coded a fixed 5-level climb onto bin\x64\Debug while the actual output is
        //    bin\Debug (no x64 layer), so the Dev probe always missed.
        for (var dir = Path.GetFullPath(AppContext.BaseDirectory); ; )
        {
            if (Directory.Exists(Path.Combine(dir, "StickNoteHost")))
            {
                var hostRoot = Path.Combine(dir, "StickNoteHost");
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    foreach (var platform in new[] { Path.Combine("bin", "x64"), Path.Combine("bin") })
                    {
                        var dev = Path.Combine(hostRoot, platform, cfg,
                            "net8.0-windows10.0.26100.0", "win-x64", "StickNoteHost.exe");
                        if (File.Exists(dev)) return dev;
                    }
                }
            }
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    public static string ResolveTheme() =>
        App.MainWindow is Microsoft.UI.Xaml.Window w
            && w.Content is Microsoft.UI.Xaml.FrameworkElement fe
            && fe.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Light
            ? "light" : "dark";

    /// <summary>Upsert one note (by id) into stickies.json and launch the host. Returns true when the
    /// note is (or will be) on the desktop: written AND the host is running / successfully launched.</summary>
    public static bool SendToDesktop(string id, string kind, string title, string content, List<StickyTodoItem>? items = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(JsonPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return false; 
                var notes = data.Notes ?? new List<StickyNote>();
                var existing = notes.FirstOrDefault(n => n.Id == id);
                if (existing != null)
                {
                    existing.Kind = kind; existing.Title = title; existing.Content = content; existing.Items = items;
                }
                else
                {
                    notes.Add(new StickyNote { Id = id, Kind = kind, Title = title, Content = content, Items = items });
                }
                data.Theme = ResolveTheme();
                data.Language = App.CurrentLanguage; // i18n: carry the UI language so the host menus follow
                data.Notes = notes;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.SendToDesktop 失败: {ex}");
            return false;
        }

        
        
        if (HostRunning) return true;
        try
        {
            if (!string.IsNullOrEmpty(HostExePath) && File.Exists(HostExePath))
            {
                Process.Start(new ProcessStartInfo { FileName = HostExePath, UseShellExecute = true });
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"唤起 StickNoteHost 失败: {ex}");
        }
        return false;
    }

    /// <summary>Create a one-shot reminder card in stickies.json and launch the host.</summary>
    public static void AddReminder(string content, DateTimeOffset dueTime)
    {
        try
        {
            var dir = Path.GetDirectoryName(JsonPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return; 
                var notes = data.Notes ?? new List<StickyNote>();
                notes.Add(new StickyNote { Id = Guid.NewGuid().ToString(), Kind = "reminder", Title = "", Content = content, DueTime = dueTime });
                data.Theme = ResolveTheme();
                data.Language = App.CurrentLanguage; // i18n: carry the UI language so the host menus follow
                data.Notes = notes;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.AddReminder 失败: {ex}");
        }

        // Launch host (idempotent per its own single-instance handling if we add one).
        try
        {
            if (!string.IsNullOrEmpty(HostExePath) && File.Exists(HostExePath))
                Process.Start(new ProcessStartInfo { FileName = HostExePath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"唤起 StickNoteHost 失败: {ex}");
        }
    }

    /// <summary>Update an existing reminder card (by id) in stickies.json; host watcher picks it up.</summary>
    public static void UpdateReminder(string id, string content, DateTimeOffset dueTime)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return; 
                var notes = data.Notes ?? new List<StickyNote>();
                var existing = notes.FirstOrDefault(n => n.Id == id);
                if (existing == null) return; // card already gone (expired/closed) - nothing to update
                existing.Content = content;
                existing.DueTime = dueTime;
                data.Theme = ResolveTheme();
                data.Language = App.CurrentLanguage;
                data.Notes = notes;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.UpdateReminder 失败: {ex}");
        }
    }

    /// <summary>Launch the host if its exe is found (idempotent - its own single instance exits a duplicate).</summary>
    public static void LaunchHost()
    {
        try
        {
            if (!string.IsNullOrEmpty(HostExePath) && File.Exists(HostExePath))
                Process.Start(new ProcessStartInfo { FileName = HostExePath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"唤起 StickNoteHost 失败: {ex}");
        }
    }

    /// <summary>True when stickies.json contains any desktop card (note or reminder).</summary>
    public static bool HasDesktopCards()
    {
        try { return Load()?.Notes?.Count > 0; } catch { return false; }
    }

    /// <summary>True when the sticky-note host process is running (i.e. desktop notes may exist).
    /// E2-01: probe the host's single-instance MUTEX (the host creates a Mutex, not an Event -
    /// an EventWaitHandle.OpenExisting on a same-named Mutex always throws, making this false).
    /// Matches StickNoteHost.App.SingleInstanceMutexName, Debug/.Dev isolated.</summary>
    public static bool HostRunning
    {
        get
        {
            try
            {
                using var _ = System.Threading.Mutex.OpenExisting(HostMutexName);
                return true;
            }
            catch { return false; }
        }
    }

    private static string HostMutexName =>
#if DEBUG
        @"Local\StickNoteHost.Dev";
#else
        @"Local\StickNoteHost";
#endif

    /// <summary>True when this card id is already pinned to the desktop.</summary>
    public static bool Contains(string id)
    {
        try { return Load()?.Notes?.Any(n => n.Id == id) == true; } catch { return false; }
    }

    /// <summary>True when this note is recorded in stickies.json (i.e. pinned to the desktop).
    
    public static bool IsOnDesktop(string id) => Contains(id);

    /// <summary>Update an existing desktop note (no-op unless it was already sent).</summary>
    public static void UpdateNote(string id, string kind, string title, string content, List<StickyTodoItem>? items = null)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return; 
                var notes = data.Notes ?? new List<StickyNote>();
                var existing = notes.FirstOrDefault(n => n.Id == id);
                if (existing == null) return; // never sent to desktop -> leave the file alone
                existing.Kind = kind; existing.Title = title; existing.Content = content; existing.Items = items;
                data.Theme = ResolveTheme();
                data.Language = App.CurrentLanguage; // i18n: keep the host menu language in sync
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.UpdateNote 失败: {ex}");
        }
    }

    /// <summary>Remove a desktop note (no-op unless it was already sent).</summary>
    public static void RemoveNote(string id)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null) return; 
                var notes = data.Notes ?? new List<StickyNote>();
                if (notes.RemoveAll(n => n.Id == id) == 0) return;
                data.Notes = notes;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.RemoveNote 失败: {ex}");
        }
    }

    /// <summary>Update the theme stored in stickies.json; the host watcher recolors every note immediately. No-op when nothing is on the desktop yet.</summary>
    public static void UpdateTheme(string theme)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null || (data.Notes?.Count ?? 0) == 0) return; 
                data.Theme = theme;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.UpdateTheme 失败: {ex}");
        }
    }

    /// <summary>Update the host menu language stored in stickies.json (i18n); the host applies it on next menu open.</summary>
    public static void UpdateLanguage(string lang)
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null || (data.Notes?.Count ?? 0) == 0) return; 
                data.Language = lang;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.UpdateLanguage failed: {ex}");
        }
    }

    public static StickyData? Load()
    {
        try
        {
            if (!File.Exists(JsonPath)) return new StickyData(); 
            return JsonSerializer.Deserialize<StickyData>(File.ReadAllText(JsonPath)); 
        }
        catch { }
        return null; 
    }

    /// <summary>E3-07: clear every desktop card (import / reset). Host data is local-only - it is
    /// never imported/exported; after a full import or a database reset the host must close all
    /// notes (its watcher sees an empty list and exits). Theme/language are kept.</summary>
    public static void ClearAllNotes()
    {
        try
        {
            using (StickiesLock.Enter())
            {
                var data = Load();
                if (data == null || data.Notes is not { Count: > 0 }) return; 
                data.Notes = new List<StickyNote>();
                Save(data);
            }
        }
        catch { }
    }

    /* ========== [StickySync Reverse Channel] ==========
    Function: watch stickies.json for todo check-state changes written back by the host,
    update the authoritative TodoCard.CheckedStates in data.novadb, then notify the UI.
    Corresponding UI: PlanPage (ApplyExternalTodoState)
    Logic Range: watcher setup + apply loop
    */
    private static FileSystemWatcher? _watcher;
    private static Action<Guid, List<bool>>? _onTodoChanged;
    private static bool _applying; // re-entrancy guard (a save during an apply must not recurse)

    /// <summary>Start watching stickies.json; onTodoChanged fires on the UI thread after the store is updated.</summary>
    public static void StartWatching(Action<Guid, List<bool>> onTodoChanged)
    {
        _onTodoChanged = onTodoChanged;
        try
        {
            var dir = Path.GetDirectoryName(JsonPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            if (_watcher != null) return;
            _watcher = new FileSystemWatcher(dir, "stickies.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged; 
            // Error fires on the watcher thread (buffer overflow on rapid writes) - recreate on the UI thread.
            _watcher.Error += (_, _) => App.UiQueue?.TryEnqueue(() =>
            {
                try { _watcher?.Dispose(); } catch { }
                _watcher = null;
                StartWatching(_onTodoChanged!);
                ApplyTodoChanges(); // N4H-03: mirror the Host-side R4-SN9 fix - changes lost during the overflow window are absorbed immediately instead of waiting for the next unrelated event
            });
        }
        catch { }
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e) =>
        App.UiQueue?.TryEnqueue(ApplyTodoChanges);

    /// <summary>Reconcile host-written todo check states into the authoritative store. Content
    /// comparison prevents a loop: our own SendToDesktop/UpdateNote writes produce the same states
    /// already in the store, so no redundant write or UI refresh occurs.</summary>
    private static void ApplyTodoChanges()
    {
        if (_applying) return;
        _applying = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;
            var data = Load();
            if (data?.Notes is not { Count: > 0 }) return;
            foreach (var n in data.Notes)
            {
                if (n.Kind != "todo" || n.Items == null) continue;
                if (!Guid.TryParse(n.Id, out var id)) continue;
                var todo = db.TodoCards.FirstOrDefault(x => x.Id == id);
                if (todo == null) continue;
                var states = BuildStatesFromItems(todo, n.Items);
                if (StatesEqual(todo.CheckedStates, states)) continue;
                todo.CheckedStates = states;
                App.Store?.SaveAsync();
                _onTodoChanged?.Invoke(id, states);
            }
        }
        catch { }
        finally { _applying = false; }
    }

    /// <summary>Build TodoCard.CheckedStates (1 + subtexts) from the host's structured items, guarded
    /// against length drift (missing/extra items default to false).</summary>
    private static List<bool> BuildStatesFromItems(TodoCard todo, List<StickyTodoItem> items)
    {
        int total = 1 + (todo.SubTexts?.Count ?? 0);
        var states = new List<bool>(total);
        for (int i = 0; i < total; i++) states.Add(i < items.Count && items[i].Checked);
        return states;
    }

    private static bool StatesEqual(List<bool> a, List<bool> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }
}

public class StickyData
{
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "zh-CN"; // i18n: host menu language (zh-CN / en-US)
    public List<StickyNote>? Notes { get; set; } = new();
}

public class StickyNote
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "todo";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
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

/// so neither process can interleave a read with the other's write (which used to drop updates).</summary>
internal static class StickiesLock
{
    private static readonly Mutex Mutex = new(false, MutexName);

    private static string MutexName =>
#if DEBUG
        @"Local\Novara.Stickies.Dev";
#else
        @"Local\Novara.Stickies";
#endif

    public static IDisposable Enter()
    {
        try { Mutex.WaitOne(); }
        catch (AbandonedMutexException) { /* previous holder died while holding it - we now own it */ }
        return new Releaser(Mutex);
    }

    private sealed class Releaser : IDisposable
    {
        private Mutex? _m;
        public Releaser(Mutex m) => _m = m;
        public void Dispose()
        {
            try { _m?.ReleaseMutex(); } catch { }
            _m = null;
        }
    }
}
