using System.Diagnostics;
using System.Text.Json;

namespace StickNoteHost;







public static class NovaraBridge
{



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

        var sideBySide = System.IO.Path.Combine(AppContext.BaseDirectory, "Novara.exe");
        if (System.IO.File.Exists(sideBySide)) return sideBySide;





        for (var dir = System.IO.Path.GetFullPath(AppContext.BaseDirectory); ; )
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "Novara.csproj")))
            {
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    foreach (var platform in new[] { System.IO.Path.Combine("bin", "x64"), "bin" })
                    {



                        var cfgDir = System.IO.Path.Combine(dir, platform, cfg);
                        if (!System.IO.Directory.Exists(cfgDir)) continue;
                        var noTfm = System.IO.Path.Combine(cfgDir, "win-x64", "Novara.exe");
                        if (System.IO.File.Exists(noTfm)) return noTfm;
                        foreach (var tfmDir in System.IO.Directory.GetDirectories(cfgDir, "net8.0-windows*"))
                        {
                            var dev = System.IO.Path.Combine(tfmDir, "win-x64", "Novara.exe");
                            if (System.IO.File.Exists(dev)) return dev;
                        }
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

        try
        {
            var dir = System.IO.Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            N5S12AtomicWrite(PendingPath, JsonSerializer.Serialize(new { Id = noteId }));
            try
            {
                using var evt = System.Threading.EventWaitHandle.OpenExisting(EventName);
                evt.Set();
                hasListener = true;
            }
            catch { }
        }
        catch (Exception ex) { App.Log($"EditNote 写请求失败: {ex.Message}"); }


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



    private static void N5S12AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, content);
        System.IO.File.Move(tmp, path, true);
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


    public static void EditReminder(string id)
    {
        string content = "", due = "";
        try
        {

            using (StickiesLock.Enter())
            {
                var data = JsonSerializer.Deserialize<StickyData>(System.IO.File.ReadAllText(App.JsonPath));
                var card = data?.Notes?.FirstOrDefault(n => n.Id == id);
                if (card == null)
                {


                    App.Log("EditReminder: 卡已不存在，放弃本次编辑跳转");
                    return;
                }
                content = card.Content ?? "";
                if (card.DueTime == null)
                {



                    App.Log("EditReminder: 卡 DueTime 为空（损坏数据），放弃本次编辑跳转");
                    return;
                }
                due = card.DueTime.Value.ToString("O");
            }
        }
        catch (Exception ex) { App.Log($"EditReminder 读卡失败: {ex.Message}"); return; }

        bool hasListener = false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ReminderPendingPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            N5S12AtomicWrite(ReminderPendingPath, JsonSerializer.Serialize(new { Id = id, Content = content, DueTime = due }));
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
