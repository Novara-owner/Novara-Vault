using System.Text.Json;

namespace Novara.Services;

public static class AddPathRequest
{

    public const string EventName =
#if DEBUG
        @"Local\Novara.AddPathRequest.Dev";
#else
        @"Local\Novara.AddPathRequest";
#endif
    public static string PendingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-path.json");

    private static EventWaitHandle? _evt;
    private static Thread? _thread;
    private static volatile bool _running;


    public static void StartListening(Action<string> handler)
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




    public static string? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                var j = JsonSerializer.Deserialize<PendingPathFile>(json);

                if (j != null && !string.IsNullOrWhiteSpace(j.Path))
                {
                    try { File.Delete(PendingPath); } catch { }
                    return j.Path;
                }
            }
        }
        catch { }
        return null;
    }


    public static void Raise(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(new PendingPathFile { Path = path }));
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

public class PendingPathFile
{
    public string Path { get; set; } = "";
}
