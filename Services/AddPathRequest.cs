/* ========== AddPathRequest - Right-Click "Add to Path Backup" Bridge ==========
Function: Single-instance add-path bridge (stage-3): a second instance (from the Windows
right-click menu) passes a file/folder path to the already-running main instance via a
pending-path.json file + a named event; if the app is closed, the launched process reads
the same pending path at startup.
Corresponding UI: FilePathPage (new-path dialog pre-filled)
Logic Range: Whole file business logic of this module
*/
using System.Text.Json;

namespace Novara.Services;

public static class AddPathRequest
{
    // E1-13: event name isolated per config (Debug=.Dev) - see EditRequest.cs
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

    /// <summary>Listen for add-path requests on a background thread; handler runs on that thread.</summary>
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

    // N5W2-04: Stop() removed (zero callers) - same fake-wake-up trap as the other request bridges.

    /// <summary>Consume a pending path if one exists (used at startup and by the listener).</summary>
    public static string? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                File.Delete(PendingPath);
                var j = JsonSerializer.Deserialize<PendingPathFile>(json);
                if (j != null && !string.IsNullOrWhiteSpace(j.Path)) return j.Path;
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
            var dir = Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(new PendingPathFile { Path = path }));
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

public class PendingPathFile
{
    public string Path { get; set; } = "";
}
