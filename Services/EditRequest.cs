using System.Text.Json;

namespace Novara.Services;

/// <summary>
/// Single-instance edit-request bridge (stage-2 batch 4):
/// The sticky-note host raises an edit request via a pending-edit.json file + a named
/// event; the already-running main instance picks it up and opens the note editor.
/// If the main app is not running, the host launches it and the startup path reads
/// the same pending file.
/// </summary>
public static class EditRequest
{
    // E1-13: event name isolated per config (Debug=.Dev) so a dev build cannot wake the installed
    // release's listener and vice versa (aligned with DataDirName / SingleInstanceMutexName).
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

    /// <summary>Listen for edit requests on a background thread; handler runs on that thread.</summary>
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
        catch { /* event already held or failed - degrade to startup-only handling */ }
    }

    // N5W2-04: Stop() removed (zero callers) - same fake-wake-up trap as the other request bridges.

    /// <summary>Consume a pending request if one exists (used at startup).</summary>
    public static Guid? ReadPending()
    {
        try
        {
            if (File.Exists(PendingPath))
            {
                var json = File.ReadAllText(PendingPath);
                File.Delete(PendingPath);
                var j = JsonSerializer.Deserialize<PendingEdit>(json);
                if (j != null && Guid.TryParse(j.Id, out var g)) return g;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Write the request file and signal the running instance (host side).</summary>
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
            catch { /* no running instance - the launched process handles it at startup */ }
        }
        catch { }
    }
}

public class PendingEdit
{
    public string Id { get; set; } = "";
}
