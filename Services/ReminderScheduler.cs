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
        try
        {
            var svcType = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Schedule.Service COM class is not registered.");
            dynamic svc = Activator.CreateInstance(svcType)!;
            svc.Connect();
            dynamic folder = svc.GetFolder("\\");
            dynamic definition = svc.NewTask(0);



            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.StartWhenAvailable = false;
            dynamic trigger = definition.Triggers.Create(1);

            trigger.StartBoundary = due.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            trigger.Enabled = true;
            dynamic action = definition.Actions.Create(0);
            action.Path = GetExecutablePath();
            action.Arguments = "--reminder " + id.ToString("D", CultureInfo.InvariantCulture);



            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            folder.RegisterTaskDefinition(TaskPrefix + id, definition, 6, null, null, 3, "D:P(A;OICI;FA;;;" + sid + ")");
        }
        catch (Exception ex)
        {



            Debug.WriteLine("ReminderScheduler.Schedule 失败: " + ex.Message);
            CrashLogger.LogNote("ReminderScheduler.Schedule", ex.ToString());
        }
    }


    public static void Cancel(Guid id)
    {
        try
        {
            var svcType = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Schedule.Service COM class is not registered.");
            dynamic svc = Activator.CreateInstance(svcType)!;
            svc.Connect();
            dynamic folder = svc.GetFolder("\\");
            folder.DeleteTask(TaskPrefix + id, 0);
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002))
        {



        }
        catch (Exception ex)
        {
            Debug.WriteLine("ReminderScheduler.Cancel 失败: " + ex.Message);
            CrashLogger.LogNote("ReminderScheduler.Cancel", ex.ToString());
        }
    }
}
