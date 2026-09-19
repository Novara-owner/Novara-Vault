namespace Novara.Sync.Server;


public sealed class SyncServerOptions
{

    public string StorageRoot { get; set; } = "data";


    public long DefaultQuotaBytes { get; set; } = 500L * 1024 * 1024;


    public int DefaultMaxVersions { get; set; } = 10;


    public long MaxPayloadBytes { get; set; } = 64L * 1024 * 1024;


    public int MaxAuthFailures { get; set; } = 10;

    public TimeSpan AuthFailureWindow { get; set; } = TimeSpan.FromMinutes(1);
}
