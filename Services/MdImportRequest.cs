using System.Text.Json;

namespace Novara.Services;

public static class MdImportRequest
{

    public const string EventName =
#if DEBUG
        @"Local\Novara.MdImport.Dev";
#else
        @"Local\Novara.MdImport";
#endif
    public static string PendingMd => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        App.DataDirName, "pending-import-md.json");

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
            if (File.Exists(PendingMd))
            {
                var json = File.ReadAllText(PendingMd);
                var j = JsonSerializer.Deserialize<PendingMdFile>(json);

                if (j != null && !string.IsNullOrWhiteSpace(j.Path))
                {
                    try { File.Delete(PendingMd); } catch { }
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
            var dir = Path.GetDirectoryName(PendingMd);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingMd, JsonSerializer.Serialize(new PendingMdFile { Path = path }));
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

public class PendingMdFile
{
    public string Path { get; set; } = "";
}
