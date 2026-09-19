using Novara.Services;
using Xunit;

namespace Novara.Tests;



[Collection("CoreSequential")]
public class SyncAuditLogTests : IDisposable
{
    private readonly string _dir;

    public SyncAuditLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-syncaudit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SyncAuditLog.SetBaseDir(_dir);
    }

    public void Dispose()
    {
        try { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
        SyncAuditLog.SetBaseDir(null);
    }

    private static SyncAuditEvent Evt(string ev = SyncAuditEvents.Push, long ver = 3, string detail = "",
        bool ok = true, string space = "sp_0123456789abcdef0123456789abcdef")
        => new()
        {
            Ev = ev,
            Server = "https://novara.example.com",
            Space = space,
            Device = "MacBook",
            Ver = ver,
            Detail = detail,
            Ok = ok,
        };

    [Fact]
    public void Write_ThenReadLatest_RoundTripsFields_AndShortensTheSpaceId()
    {
        SyncAuditLog.Write(Evt(ver: 7));

        var back = Assert.Single(SyncAuditLog.ReadLatest(10));
        Assert.Equal(SyncAuditEvents.Push, back.Ev);
        Assert.Equal("https://novara.example.com", back.Server);
        Assert.Equal("sp_01234", back.Space);
        Assert.Equal("MacBook", back.Device);
        Assert.Equal(7, back.Ver);
        Assert.True(back.Ok);
        Assert.True(back.Ts.Length > 0);
    }

    [Fact]
    public void ReadLatest_IsNewestFirst_AndRespectsMax()
    {
        for (var i = 0; i < 5; i++) SyncAuditLog.Write(Evt(ver: i));

        var top2 = SyncAuditLog.ReadLatest(2);
        Assert.Equal(2, top2.Count);
        Assert.Equal(4, top2[0].Ver);
        Assert.Equal(3, top2[1].Ver);
    }

    [Fact]
    public void ReadLatest_AndCountAll_SpanArchivedFiles()
    {

        SyncAuditLog.Write(Evt(ver: 1));
        File.WriteAllText(
            Path.Combine(_dir, "sync-audit-20260101-000000-000.log"),
            "{\"ts\":\"2026-01-01 00:00:00\",\"ev\":\"pair\",\"ver\":9,\"ok\":true}\n");

        Assert.Equal(2, SyncAuditLog.CountAll());

        var all = SyncAuditLog.ReadLatest(10);
        Assert.Equal(2, all.Count);
        Assert.Equal(9, all[1].Ver);
    }

    [Fact]
    public void ClearAll_RemovesTheCurrentFileAndEveryArchive()
    {
        SyncAuditLog.Write(Evt());
        File.WriteAllText(Path.Combine(_dir, "sync-audit-20260101-000000-000.log"), "{}\n");
        Assert.Equal(2, SyncAuditLog.CountAll());

        SyncAuditLog.ClearAll();

        Assert.Empty(SyncAuditLog.ReadLatest(10));
        Assert.Equal(0, SyncAuditLog.CountAll());
    }

    [Fact]
    public void SecretShapedFields_AreMasked_EvenWhenTheyComeFromUserInput()
    {

        SyncAuditLog.Write(new SyncAuditEvent
        {
            Ev = SyncAuditEvents.Error,
            Server = "https://host/?token=abcdefgh12345678",
            Device = "Bearer abcdefgh12345678",
            Detail = "key=sk-abcdefghijklmnop1234 refused",
            Ok = false,
        });

        var back = Assert.Single(SyncAuditLog.ReadLatest(1));
        Assert.DoesNotContain("abcdefgh12345678", back.Server);
        Assert.DoesNotContain("abcdefgh12345678", back.Device);
        Assert.DoesNotContain("sk-abcdefghijklmnop", back.Detail);
        Assert.Contains("***", back.Server);
        Assert.Contains("sk-***", back.Detail);
    }

    [Fact]
    public void LongDetail_IsCappedSoOneEventStaysOneReasonableLine()
    {
        SyncAuditLog.Write(Evt(detail: new string('x', 1000)));

        var back = Assert.Single(SyncAuditLog.ReadLatest(1));
        Assert.True(back.Detail.Length <= 301);
        Assert.EndsWith("…", back.Detail);
    }

    [Fact]
    public void CorruptLines_AreSkipped_WithoutLosingTheRest()
    {
        SyncAuditLog.Write(Evt(ver: 1));
        File.AppendAllText(Path.Combine(_dir, "sync-audit.log"), "{ this is not json\n");
        SyncAuditLog.Write(Evt(ver: 2));

        var all = SyncAuditLog.ReadLatest(10);
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all[0].Ver);
        Assert.Equal(1, all[1].Ver);
    }

    [Fact]
    public void ShortId_KeepsShortIdsIntact()
    {
        Assert.Equal("", SyncAuditLog.ShortId(null));
        Assert.Equal("", SyncAuditLog.ShortId(""));
        Assert.Equal("abc", SyncAuditLog.ShortId("abc"));
        Assert.Equal("12345678", SyncAuditLog.ShortId("123456789"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentWrites_ProducePerLineValidJson()
    {
        const int n = 32;
        await System.Threading.Tasks.Task.WhenAll(
            Enumerable.Range(0, n).Select(_ => System.Threading.Tasks.Task.Run(() => SyncAuditLog.Write(Evt()))));

        Assert.Equal(n, SyncAuditLog.ReadLatest(n * 2).Count);
    }



    [Fact]
    public void WriteOrdered_KeepsSubmissionOrder_NotLockOrder()
    {



        const int n = 50;
        for (var i = 0; i < n; i++) SyncAuditLog.WriteOrdered(Evt(ver: i));
        SyncAuditLog.WaitForPendingWrites();

        var all = SyncAuditLog.ReadLatest(n * 2);
        Assert.Equal(n, all.Count);
        for (var i = 0; i < n; i++) Assert.Equal(n - 1 - i, all[i].Ver);
    }

    [Fact]
    public void ClearAllWithMarker_LeavesExactlyTheMarker_EvenWithWritesStillQueued()
    {



        for (var i = 0; i < 200; i++) SyncAuditLog.WriteOrdered(Evt(ver: i));
        SyncAuditLog.ClearAllWithMarker();

        var all = SyncAuditLog.ReadLatest(10);
        var only = Assert.Single(all);
        Assert.Equal(SyncAuditEvents.AuditCleared, only.Ev);
        Assert.Equal(1, SyncAuditLog.CountAll());
    }

    [Fact]
    public void ClearAllWithMarker_RecordsTheAuditClearedTrace()
    {

        SyncAuditLog.Write(Evt());
        Assert.NotEmpty(SyncAuditLog.ReadLatest(10));

        SyncAuditLog.ClearAllWithMarker();

        var back = Assert.Single(SyncAuditLog.ReadLatest(10));
        Assert.Equal(SyncAuditEvents.AuditCleared, back.Ev);
        Assert.True(back.Ok);
        Assert.False(string.IsNullOrEmpty(back.Ts));
    }

    [Fact]
    public void WriteOrdered_IsNonBlocking_AndStillLands()
    {

        for (var i = 0; i < 20; i++) SyncAuditLog.WriteOrdered(Evt(ver: i));
        SyncAuditLog.WaitForPendingWrites();
        Assert.Equal(20, SyncAuditLog.CountAll());
    }
}
