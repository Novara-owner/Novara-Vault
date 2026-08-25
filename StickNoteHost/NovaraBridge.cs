using System.Diagnostics;
using System.Text.Json;

namespace StickNoteHost;

/// <summary>
/// Stage-2 batch 4: edit jump back to the main Novara app.
/// Writes pending-edit.json + signals the named event (handled by the running
/// main instance), and launches Novara.exe if it is not running (startup consumes
/// the pending file).
/// </summary>
public static class NovaraBridge
{
    // E4-02: event names isolated per config (Debug=.Dev), byte-identical with the main app
    // (EditRequest.cs / ReminderEditRequest.cs, E1-13) - otherwise Debug IPC never matches and
    // a Debug host can wake the installed release's listener and vice versa.
    public const string EventName =
#if DEBUG
        @"Local\Novara.EditRequest.Dev";
#else
        @"Local\Novara.EditRequest";
#endif

    public static string PendingPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-edit.json");

    public static string? NovaraExePath { get; } = FindNovaraExe();

    private static string? FindNovaraExe()
    {
        // 1) Release layout: main exe next to the host.
        var sideBySide = System.IO.Path.Combine(AppContext.BaseDirectory, "Novara.exe");
        if (System.IO.File.Exists(sideBySide)) return sideBySide;

        // 2) Dev layout (R4-MS2): walk up from the host output dir to the repo root (the dir that
        //    contains Novara.csproj), then probe bin[+\x64]\Debug|Release\... - the old code hard-coded
        //    a fixed 6-level climb onto bin\x64 while the actual output is bin\Debug (no x64 layer),
        //    so the Dev probe always missed (mirrors the main app's FindHostExe, E5-12).
        for (var dir = System.IO.Path.GetFullPath(AppContext.BaseDirectory); ; )
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "Novara.csproj")))
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    foreach (var platform in new[] { System.IO.Path.Combine("bin", "x64"), "bin" })
                    {
                        var dev = System.IO.Path.Combine(dir, platform, cfg,
                            "net8.0-windows10.0.26100.0", "win-x64", "Novara.exe");
                        if (System.IO.File.Exists(dev)) return dev;
                    }
                }
            }
            var parent = System.IO.Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    public static void EditNote(string noteId)
    {
        bool hasListener = false;
        // 1) Request file + event for the already-running main instance.
        try
        {
            var dir = System.IO.Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(PendingPath, JsonSerializer.Serialize(new { Id = noteId }));
            try
            {
                using var evt = System.Threading.EventWaitHandle.OpenExisting(EventName);
                evt.Set();
                hasListener = true;
            }
            catch { }
        }
        catch (Exception ex) { App.Log($"EditNote 写请求失败: {ex.Message}"); }

        // 2) A listener exists -> the running main instance handles it. Otherwise launch.
        if (hasListener)
        {
            App.Log($"EditNote: 已有监听实例，仅发事件 id={noteId}");
            return;
        }
        try
        {
            if (NovaraExePath != null && System.IO.File.Exists(NovaraExePath))
            {
                App.Log($"EditNote: 启动 Novara {NovaraExePath} id={noteId}");
                Process.Start(new ProcessStartInfo { FileName = NovaraExePath, UseShellExecute = true });
            }
            else
            {
                App.Log($"EditNote: NovaraExePath 无效 ({NovaraExePath})");
            }
        }
        catch (Exception ex) { App.Log($"EditNote 启动失败: {ex.Message}"); }
    }

    public const string ReminderEventName =
#if DEBUG
        @"Local\Novara.ReminderEditRequest.Dev";
#else
        @"Local\Novara.ReminderEditRequest";
#endif

    public static string ReminderPendingPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-reminder-edit.json");

    /// <summary>Edit jump for a reminder card: pass id + current content/due time to the main app.</summary>
    public static void EditReminder(string id)
    {
        string content = "", due = "";
        try
        {
            var data = JsonSerializer.Deserialize<StickyData>(System.IO.File.ReadAllText(App.JsonPath));
            var card = data?.Notes?.FirstOrDefault(n => n.Id == id); 
            if (card != null)
            {
                content = card.Content ?? "";
                due = card.DueTime?.ToString("O") ?? "";
            }
        }
        catch (Exception ex) { App.Log($"EditReminder 读卡失败: {ex.Message}"); }

        bool hasListener = false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ReminderPendingPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(ReminderPendingPath, JsonSerializer.Serialize(new { Id = id, Content = content, DueTime = due }));
            try
            {
                using var evt = System.Threading.EventWaitHandle.OpenExisting(ReminderEventName);
                evt.Set();
                hasListener = true;
            }
            catch { }
        }
        catch (Exception ex) { App.Log($"EditReminder 写请求失败: {ex.Message}"); }

        if (hasListener)
        {
            App.Log($"EditReminder: 已有监听实例，仅发事件 id={id}");
            return;
        }
        try
        {
            if (NovaraExePath != null && System.IO.File.Exists(NovaraExePath))
            {
                App.Log($"EditReminder: 启动 Novara {NovaraExePath} id={id}");
                Process.Start(new ProcessStartInfo { FileName = NovaraExePath, UseShellExecute = true });
            }
        }
        catch (Exception ex) { App.Log($"EditReminder 启动失败: {ex.Message}"); }
    }
}
