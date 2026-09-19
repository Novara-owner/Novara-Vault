using System.Text.Json;
using Novara.Models;

namespace Novara.Services;






public sealed class SyncStateStore
{
    public const string FileName = "sync-state.json";

    private readonly string _path;

    public SyncStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("state path is required", nameof(path));
        _path = path;
    }

    public string Path => _path;






    private static readonly object SaveGate = new();


    public SyncState Load()
    {
        if (!File.Exists(_path)) return new SyncState();
        try { return SyncJson.DeserializeState(File.ReadAllText(_path)); }
        catch (IOException) { return new SyncState(); }
        catch (UnauthorizedAccessException) { return new SyncState(); }
    }


    public void Save(SyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        lock (SaveGate)
        {
            try
            {
                File.WriteAllText(temp, SyncJson.SerializeState(state), System.Text.Encoding.UTF8);
                File.Move(temp, _path, overwrite: true);
            }
            finally
            {


                try { if (File.Exists(temp)) File.Delete(temp); } catch {  }
            }
        }
    }
}
