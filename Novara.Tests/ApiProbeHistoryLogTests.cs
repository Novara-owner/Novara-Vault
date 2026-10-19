using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class ApiProbeHistoryLogTests : IDisposable
{
    private readonly string _dir;

    public ApiProbeHistoryLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-probe-history-tests-" + Guid.NewGuid().ToString("N"));
        ApiProbeHistoryLog.SetBaseDirForTests(_dir);
    }

    public void Dispose()
    {


        ApiProbeHistoryLog.WaitForPendingWritesForTests();
        ApiProbeHistoryLog.SetBaseDirForTests(null);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ReadRecent_ReturnsNewestFirst_AcrossAllEntryKinds()
    {
        ApiProbeHistoryLog.RecordProbe("api.example.com", ApiProbeStatus.Success, 321, "");
        ApiProbeHistoryLog.RecordDiagnose("api.example.com", reachable: true, ApiProbeStatus.Success, 1500, "ok");
        ApiProbeHistoryLog.RecordRelay("relay.example.net", "trusted", "score=93");
        ApiProbeHistoryLog.WaitForPendingWritesForTests();

        var all = ApiProbeHistoryLog.ReadRecent(10);
        Assert.Equal(3, all.Count);
        Assert.Equal("relay", all[0].Kind);
        Assert.Equal("diagnose", all[1].Kind);
        Assert.Equal("probe", all[2].Kind);
        Assert.Equal("reachable/Success", all[1].Status);
        Assert.Equal(1500, all[1].LatencyMs);
        Assert.Equal(321, all[2].LatencyMs);
    }

    [Fact]
    public void Clear_EmptiesHistoryWithoutLeavingMarker()
    {
        ApiProbeHistoryLog.RecordProbe("a.example.com", ApiProbeStatus.RateLimited, 88, "");
        ApiProbeHistoryLog.WaitForPendingWritesForTests();
        ApiProbeHistoryLog.Clear();



        var all = ApiProbeHistoryLog.ReadRecent(10);
        Assert.Empty(all);
    }

    [Fact]
    public void Detail_IsClampedTo120Chars()
    {
        var detail = new string('x', 500);
        ApiProbeHistoryLog.RecordProbe("c.example.com", ApiProbeStatus.Unknown, -1, detail);
        ApiProbeHistoryLog.WaitForPendingWritesForTests();
        var all = ApiProbeHistoryLog.ReadRecent(5);
        Assert.Single(all);
        Assert.Equal(120, all[0].Detail.Length);
    }

    [Fact]
    public void Detail_IsSanitizedLikeHost_BeforeClamping()
    {


        ApiProbeHistoryLog.RecordProbe("d.example.com", ApiProbeStatus.NetworkError, -1,
            "上游报错: sk-abcdefghijklmnop token=secretvalue123");
        ApiProbeHistoryLog.WaitForPendingWritesForTests();

        var all = ApiProbeHistoryLog.ReadRecent(5);
        Assert.Single(all);
        Assert.DoesNotContain("abcdefghijklmnop", all[0].Detail);
        Assert.DoesNotContain("secretvalue123", all[0].Detail);
        Assert.Contains("sk-***", all[0].Detail);
    }

    [Fact]
    public void ReadRecent_SkipsLegacyClearRowsWithoutLosingRealEntries()
    {


        ApiProbeHistoryLog.RecordProbe("a.example.com", ApiProbeStatus.Success, 1, "");
        ApiProbeHistoryLog.RecordProbe("b.example.com", ApiProbeStatus.Success, 2, "");
        ApiProbeHistoryLog.RecordProbe("c.example.com", ApiProbeStatus.Success, 3, "");
        ApiProbeHistoryLog.RecordLegacyClearForTests();
        ApiProbeHistoryLog.WaitForPendingWritesForTests();

        var all = ApiProbeHistoryLog.ReadRecent(3);
        Assert.Equal(3, all.Count);
        Assert.All(all, e => Assert.NotEqual("clear", e.Kind));
    }

    [Fact]
    public void ReadRecent_OnEmptyStore_ReturnsEmptyList()
    {
        ApiProbeHistoryLog.WaitForPendingWritesForTests();
        Assert.Empty(ApiProbeHistoryLog.ReadRecent(10));
    }
}
