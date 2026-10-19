namespace Novara.Services;





public sealed record ApiProbeHistoryEntry(DateTime Utc, string Kind, string Host, string Status, long LatencyMs, string Detail);

public static class ApiProbeHistoryLog
{
    private const string KindProbe = "probe";
    private const string KindDiag = "diagnose";
    private const string KindRelay = "relay";

    private static readonly JsonlAuditStore<ApiProbeHistoryEntry> Store = new("api-probe-history", 1_000_000, 3);


    internal static void SetBaseDirForTests(string? dir) => Store.SetBaseDir(dir);


    internal static void WaitForPendingWritesForTests() => Store.WaitForPendingWrites();


    public static void RecordProbe(string host, ApiProbeStatus status, long latencyMs, string detail)
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, KindProbe, CleanHost(host), status.ToString(), latencyMs, Clamp(detail)));

    public static void RecordDiagnose(string host, bool reachable, ApiProbeStatus reachability, long latencyMs, string detail)
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, KindDiag, CleanHost(host),
            (reachable ? "reachable/" : "unreachable/") + reachability, latencyMs, Clamp(detail)));


    public static void RecordDiagnoseFailure(string host, string status)
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, KindDiag, CleanHost(host), status, -1, ""));

    public static void RecordRelay(string host, string verdict, string detail)
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, KindRelay, CleanHost(host), verdict, -1, Clamp(detail)));

    public static void RecordRelayFailure(string host, string status)
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, KindRelay, CleanHost(host), status, -1, ""));




    public static List<ApiProbeHistoryEntry> ReadRecent(int max)
    {
        if (max <= 0) return new List<ApiProbeHistoryEntry>();
        return Store.ReadLatest(max + 64)
            .Where(e => e.Kind != "clear")
            .Take(max)
            .ToList();
    }


    internal static void RecordLegacyClearForTests()
        => Store.AppendOrdered(new ApiProbeHistoryEntry(DateTime.UtcNow, "clear", "", "", -1, ""));


    public static bool Clear() => Store.ClearAllOrdered();


    public static void WaitForPendingWrites() => Store.WaitForPendingWrites();



    private static string CleanHost(string? host) => AuditSanitizer.Clean(host);




    private static string Clamp(string detail)
    {
        detail = AuditSanitizer.Clean(detail);
        detail = detail.Replace('\n', ' ').Replace('\r', ' ');
        return detail.Length <= 120 ? detail : detail[..120];
    }
}
