using System.Text.Json;

namespace Novara.Services;








public static class EditRequest
{


    public const string EventName =
#if DEBUG
        @"Local\Novara.EditRequest.Dev";
#else
        @"Local\Novara.EditRequest";
#endif
    public static string PendingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-edit.json");

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
                            var g = ReadPending();
                            if (g.HasValue)
                            {
                                try { handler(g.Value); } catch { }
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




    public static Guid? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                var j = JsonSerializer.Deserialize<PendingEdit>(json);

                if (j != null && Guid.TryParse(j.Id, out var g))
                {
                    try { File.Delete(PendingPath); } catch { }
                    return g;
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
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(new PendingEdit { Id = id.ToString() }));
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

public class PendingEdit
{
    public string Id { get; set; } = "";
}
