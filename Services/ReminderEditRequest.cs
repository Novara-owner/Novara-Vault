using System.Text.Json;

namespace Novara.Services;

public static class ReminderEditRequest
{

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
        catch {  }
    }




    public static PendingReminderEdit? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                var j = JsonSerializer.Deserialize<PendingReminderEdit>(json);


                if (j != null && !string.IsNullOrWhiteSpace(j.Id))
                {
                    try { File.Delete(PendingPath); } catch { }
                    return j;
                }
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

    public string DueTime { get; set; } = "";
}
