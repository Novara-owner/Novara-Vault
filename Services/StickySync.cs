using System.Diagnostics;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;







public static class StickySync
{
    public static string JsonPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "stickies.json");

    public static string? HostExePath { get; } = FindHostExe();



    private static void Save(StickyData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var tmp = JsonPath + ".tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, JsonPath, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }


    private static string? FindHostExe()
    {




        var subFolder = Path.Combine(AppContext.BaseDirectory, "Host", "StickNoteHost.exe");
        if (File.Exists(subFolder)) return subFolder;


        var sideBySide = Path.Combine(AppContext.BaseDirectory, "StickNoteHost.exe");
        if (File.Exists(sideBySide)) return sideBySide;





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
                data.Language = App.CurrentLanguage;
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
                data.Language = App.CurrentLanguage;
                data.Notes = notes;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.AddReminder 失败: {ex}");
        }


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
                if (existing == null) return;
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


    public static bool HasDesktopCards()
    {
        try { return Load()?.Notes?.Count > 0; } catch { return false; }
    }





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


    public static bool Contains(string id)
    {
        try { return Load()?.Notes?.Any(n => n.Id == id) == true; } catch { return false; }
    }



    public static bool IsOnDesktop(string id) => Contains(id);


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
                if (existing == null) return;
                existing.Kind = kind; existing.Title = title; existing.Content = content; existing.Items = items;
                data.Theme = ResolveTheme();
                data.Language = App.CurrentLanguage;
                Save(data);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StickySync.UpdateNote 失败: {ex}");
        }
    }


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







    private static FileSystemWatcher? _watcher;
    private static Action<Guid, List<bool>>? _onTodoChanged;
    private static bool _applying;


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

            _watcher.Error += (_, _) => App.UiQueue?.TryEnqueue(() =>
            {
                try { _watcher?.Dispose(); } catch { }
                _watcher = null;
                StartWatching(_onTodoChanged!);
                ApplyTodoChanges();
            });
        }
        catch { }
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e) =>
        App.UiQueue?.TryEnqueue(ApplyTodoChanges);




    private static void ApplyTodoChanges()
    {
        if (_applying) return;


        if (App.Store?.IsSaveSuppressed == true) return;
        _applying = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;
            var data = Load();
            if (data?.Notes is not { Count: > 0 }) return;
            bool changed = false;
            foreach (var n in data.Notes)
            {
                if (n.Kind != "todo" || n.Items == null) continue;
                if (!Guid.TryParse(n.Id, out var id)) continue;
                var todo = db.TodoCards.FirstOrDefault(x => x.Id == id);
                if (todo == null) continue;
                var states = BuildStatesFromItems(todo, n.Items);
                if (StatesEqual(todo.CheckedStates, states)) continue;
                todo.CheckedStates = states;
                changed = true;
                _onTodoChanged?.Invoke(id, states);
            }


            if (changed) App.Store?.SaveAsync();
        }
        catch { }
        finally { _applying = false; }
    }



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
    public string Language { get; set; } = "zh-CN";
    public List<StickyNote>? Notes { get; set; } = new();
}

public class StickyNote
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "todo";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

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
    private static readonly Mutex Mutex = new(false, MutexName);

    private static string MutexName =>
#if DEBUG
        @"Local\Novara.Stickies.Dev";
#else
        @"Local\Novara.Stickies";
#endif

    public static IDisposable Enter()
    {




        bool acquired;
        try { acquired = Mutex.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { acquired = true; }
        return new Releaser(acquired ? Mutex : null);
    }

    private sealed class Releaser : IDisposable
    {
        private Mutex? _m;
        public Releaser(Mutex? m) => _m = m;
        public void Dispose()
        {
            try { _m?.ReleaseMutex(); } catch { }
            _m = null;
        }
    }
}
