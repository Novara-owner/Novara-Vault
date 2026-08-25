/* ========== ReminderEditRequest - Reminder Modify IPC Bridge ==========
Function: Single-instance reminder-edit bridge (stage-3 step 6): the host passes a reminder
card (id + current content/due time) to the already-running main instance via a
pending-reminder-edit.json file + a named event; if the app is closed, the launched
process reads the same pending file at startup.
Corresponding UI: PlanPage (reminder dialog in edit mode)
Logic Range: Whole file business logic of this module
*/
using System.Text.Json;

namespace Novara.Services;

public static class ReminderEditRequest
{
    // E1-13: event name isolated per config (Debug=.Dev) - see EditRequest.cs
    public const string EventName =
#if DEBUG
        @"Local\Novara.ReminderEditRequest.Dev";
#else
        @"Local\Novara.ReminderEditRequest";
#endif
    public static string PendingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-reminder-edit.json");

    private static EventWaitHandle? _evt;
    private static Thread? _thread;
    private static volatile bool _running;

    /// <summary>Listen for reminder-edit requests on a background thread; handler runs on that thread.</summary>
    public static void StartListening(Action<PendingReminderEdit> handler)
    {
        try
        {
            _evt = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _running = true;
            _thread = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        if (_evt.WaitOne(5000))
                        {
                            var p = ReadPending();
                            if (p != null)
                            {
                                try { handler(p); } catch { }
                            }
                        }
                    }
                    catch { }
                }
            })
            { IsBackground = true };
            _thread.Start();
        }
        catch { /* event already held or failed - degrade to startup-only handling */ }
    }

    // N4P-12: Stop() removed (zero callers) - same trap as ShowWindowRequest.Stop.

    /// <summary>Consume a pending reminder-edit request if one exists (used at startup and by the listener).</summary>
    public static PendingReminderEdit? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                File.Delete(PendingPath);
                var j = JsonSerializer.Deserialize<PendingReminderEdit>(json);
                if (j != null && !string.IsNullOrWhiteSpace(j.Id)) return j;
            }
        }
        catch { }
        return null;
    }
}

public class PendingReminderEdit
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>ISO-8601 string; may be empty when absent.</summary>
    public string DueTime { get; set; } = "";
}
