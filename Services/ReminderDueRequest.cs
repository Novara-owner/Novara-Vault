





using System.Text.Json;

namespace Novara.Services;

public static class ReminderDueRequest
{
    
    public const string EventName =
#if DEBUG
        @"Local\Novara.ReminderDueRequest.Dev";
#else
        @"Local\Novara.ReminderDueRequest";
#endif
    public static string PendingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-reminder-due.json");

    private static EventWaitHandle? _evt;
    private static Thread? _thread;
    private static volatile bool _running;

    
    public static void StartListening(Action<Guid> handler)
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
                            var id = ReadPending();
                            if (id.HasValue)
                            {
                                try { handler(id.Value); } catch { }
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

    // N4P-12: Stop() removed (zero callers) - same trap as ShowWindowRequest.Stop: Set() would fake one wake-up.

    
    public static Guid? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                var j = JsonSerializer.Deserialize<PendingReminderDueFile>(json);
                // N2-07 (N1-60 parity): delete only after a successful deserialize
                if (j != null && Guid.TryParse(j.Id, out var id))
                {
                    try { File.Delete(PendingPath); } catch { }
                    return id;
                }
            }
        }
        catch { }
        return null;
    }

    
    public static void Raise(Guid id)
    {
        try
        {
            var dir = Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(new PendingReminderDueFile { Id = id.ToString() }));
            try
            {
                using var evt = EventWaitHandle.OpenExisting(EventName);
                evt.Set();
            }
            catch {  }
        }
        catch { }
    }
}

public class PendingReminderDueFile
{
    public string Id { get; set; } = "";
}
