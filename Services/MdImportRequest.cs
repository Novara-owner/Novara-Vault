/* ========== MdImportRequest - Right-Click "Import to Novara" Bridge ==========
Function: Single-instance md-import bridge: a second instance (launched from the
.md file right-click menu) passes a markdown file path to the already-running main
instance via a pending-import-md.json file + a named event; if the app is closed,
the launched process reads the same pending path at startup.
Corresponding UI: DiaryPage (records list gains one markdown document)
Logic Range: Whole file business logic of this module
*/
using System.Text.Json;

namespace Novara.Services;

public static class MdImportRequest
{
    // Version-isolated like AddPathRequest.EventName (Debug=.Dev)
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

    /// <summary>Listen for md-import requests on a background thread; handler runs on that thread.</summary>
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
        catch { /* event already held or failed - degrade to startup-only handling */ }
    }

    /// <summary>Consume a pending md path if one exists (used at startup and by the listener).</summary>
    public static string? ReadPending()
    {
        try
        {
            if (File.Exists(PendingMd))
            {
                var json = File.ReadAllText(PendingMd);
                var j = JsonSerializer.Deserialize<PendingMdFile>(json);
                // N2-07 (N1-60 parity): delete only after a successful deserialize
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

    /// <summary>Write the request file and signal the running instance (second-instance side).</summary>
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
            catch { /* no running instance - the launched process handles it at startup */ }
        }
        catch { }
    }
}

public class PendingMdFile
{
    public string Path { get; set; } = "";
}
