using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Novara.Services;

public static class ReminderScheduler
{








    public static void Teardown(Guid id, DateTime? reminderAt, object? entity)
    {
        if (reminderAt == null) return;
        Cancel(id);
        ClearFields(entity);
    }




    public static void ClearFields(object? entity)
    {
        switch (entity)
        {
            case Novara.Models.NoteCard n: n.ReminderAt = null; n.ReminderSetAt = null; break;
            case Novara.Models.TodoCard t: t.ReminderAt = null; t.ReminderSetAt = null; break;
        }
    }













    public static Dictionary<Guid, DateTime>? Snapshot(Novara.Models.NovaraDatabase? db)
    {
        var map = new Dictionary<Guid, DateTime>();
        if (db is null) return map;
        try
        {
            var now = DateTime.Now;
            foreach (var t in db.TodoCards)
                if (!t.IsDeleted && t.ReminderAt is { } ta && ta > now) map[t.Id] = ta;
            foreach (var n in db.NoteCards)
                if (!n.IsDeleted && n.ReminderAt is { } na && na > now) map[n.Id] = na;
        }
        catch (Exception ex) { Debug.WriteLine("Reminder snapshot failed: " + ex.Message); return null; }
        return map;
    }










    public static void Reconcile(Dictionary<Guid, DateTime> before, Dictionary<Guid, DateTime>? after)
    {
        if (after is null) return;
        try
        {
            foreach (var id in before.Keys)
                if (!after.ContainsKey(id)) Cancel(id);
            foreach (var kv in after)
                if (!before.TryGetValue(kv.Key, out var prev) || prev != kv.Value) Schedule(kv.Key, kv.Value);
        }
        catch (Exception ex) { Debug.WriteLine("Reminder reconcile failed: " + ex.Message); }
    }

    private static string TaskPrefix =>
#if DEBUG
        "NovaraReminder.Dev_";
#else
        "NovaraReminder_";
#endif

    private static string GetExecutablePath()
    {
        var p = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(p) ? Path.Combine(AppContext.BaseDirectory, "Novara.exe") : p;
    }


    public static void Schedule(Guid id, DateTime due)
    {
        var taskName = TaskPrefix + id;



        var st = due.ToString("t", CultureInfo.CurrentCulture);
        var sd = due.ToString("d", CultureInfo.CurrentCulture);
        var tr = "\\\"" + GetExecutablePath() + "\\\" --reminder " + id;
        var args = "/create /tn \"" + taskName + "\" /tr \"" + tr + "\" /sc once /st " + st + " /sd " + sd + " /f";
        Run(args);
    }


    public static void Cancel(Guid id)
    {
        Run("/delete /tn \"" + TaskPrefix + id + "\" /f");
    }

    private static void Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(5000);


            if (p is { HasExited: true } && p.ExitCode != 0)
                Debug.WriteLine($"ReminderScheduler 失败(exit {p.ExitCode}): schtasks {args}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("ReminderScheduler 失败: " + ex.Message);
        }
    }
}
