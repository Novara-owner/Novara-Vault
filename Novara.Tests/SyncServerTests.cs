using System.Text;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Policy;
using Novara.Sync.Server.Security;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;

public class SyncServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-syncsrv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SpaceService NewService(long quotaBytes = 0, int maxVersions = 10, int maxAuthFailures = 10)
    {
        var options = new SyncServerOptions
        {
            StorageRoot = _dir,
            DefaultQuotaBytes = quotaBytes > 0 ? quotaBytes : 500L * 1024 * 1024,
            DefaultMaxVersions = maxVersions,
            MaxAuthFailures = maxAuthFailures,
        };
        return new SpaceService(new FileSpaceStore(_dir), options);
    }

    private static string SealedEnvelope(string spaceKey, string spaceId, string deviceId, long baseVersion, string payload = "{\"k\":\"v\"}")
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes(payload), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = spaceId,
            Device = deviceId,
            Base = baseVersion,
            Version = baseVersion + 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return SyncEnvelopeCodec.Serialize(envelope);
    }

    private (SpaceService Service, FileSpaceStore Store, string SpaceKey, string SpaceId, string DeviceId) NewPairedServer(
        long quotaBytes = 0, int maxVersions = 10, int maxAuthFailures = 10)
    {
        var service = NewService(quotaBytes, maxVersions, maxAuthFailures);
        var store = new FileSpaceStore(_dir);

        var created = service.CreateSpace("test");
        Assert.True(created.Success);


        var space = created.Value!;

        var spaceKey = SyncKeyWrap.CreateSpaceKey();

        var registered = service.RegisterDevice(space.SpaceId, space.EnrollmentSecret, "pc");
        Assert.True(registered.Success);

        return (service, store, spaceKey, space.SpaceId, registered.Value!.DeviceId);
    }



    [Fact]
    public void CreateSpace_PersistsHashesOnly_AndNeverTheSecrets()
    {
        var service = NewService();
        var store = new FileSpaceStore(_dir);

        var result = service.CreateSpace("my space");

        Assert.True(result.Success);
        var credentials = result.Value!;
        Assert.True(FileSpaceStore.IsSafeId(credentials.SpaceId));

        Assert.False(string.IsNullOrWhiteSpace(credentials.EnrollmentSecret));

        var space = store.GetSpace(credentials.SpaceId);
        Assert.NotNull(space);
        Assert.Equal("my space", space!.Name);
        Assert.Equal(0, space.CurrentVersion);
        Assert.Equal(0, space.KeyWrapVersion);
        Assert.Equal("", space.KeyWrapJson);
        Assert.NotEqual(credentials.EnrollmentSecret, space.EnrollmentHash);
        Assert.Equal(TokenAuth.Hash(credentials.EnrollmentSecret), space.EnrollmentHash);
        Assert.True(TokenAuth.Verify(credentials.EnrollmentSecret, space.EnrollmentHash));
    }

    [Fact]
    public void RegisterDevice_RequiresTheEnrollmentSecret_AndStoresOnlyTheTokenHash()
    {
        var service = NewService();
        var store = new FileSpaceStore(_dir);
        var created = service.CreateSpace("test");
        var spaceId = created.Value!.SpaceId;

        Assert.False(service.RegisterDevice(spaceId, "wrong-secret", "pc").Success);
        Assert.Equal(SyncErrorCode.Forbidden, service.RegisterDevice(spaceId, "wrong-secret", "pc").Error);
        Assert.Equal(SyncErrorCode.SpaceNotFound, service.RegisterDevice("nosuchspace", created.Value.EnrollmentSecret, "pc").Error);

        var registered = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc");
        Assert.True(registered.Success);

        var device = store.GetDevice(spaceId, registered.Value!.DeviceId);
        Assert.NotNull(device);
        Assert.Equal("pc", device!.Name);
        Assert.NotEqual(registered.Value.DeviceToken, device.TokenHash);
        Assert.DoesNotContain(registered.Value.DeviceToken, device.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticate_RejectsWrongToken_RevokedDevice_AndRateLimitsFailures()
    {
        var (service, _, _, spaceId, deviceId) = NewPairedServer(maxAuthFailures: 3);



        Assert.Equal(SyncErrorCode.Unauthenticated, service.Authorize(spaceId, deviceId, "wrong").Error);
        Assert.Equal(SyncErrorCode.Unauthenticated, service.Authorize(spaceId, deviceId, "wrong").Error);
        Assert.Equal(SyncErrorCode.Unauthenticated, service.Authorize(spaceId, deviceId, "wrong").Error);
        Assert.Equal(SyncErrorCode.RateLimited, service.Authorize(spaceId, deviceId, "wrong").Error);
        Assert.Equal(SyncErrorCode.RateLimited, service.Authorize(spaceId, deviceId, "wrong").Error);
    }

    [Fact]
    public void Authenticate_RejectsRevokedDevice()
    {
        var service = NewService();
        var created = service.CreateSpace("test");
        var spaceId = created.Value!.SpaceId;
        var registered = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc");
        var deviceId = registered.Value!.DeviceId;

        Assert.True(service.RevokeDevice(spaceId, deviceId).Success);
        Assert.Equal(SyncErrorCode.Forbidden, service.Authorize(spaceId, deviceId, registered.Value.DeviceToken).Error);
    }

    [Fact]
    public void ResetToken_RotatesTheToken_AndRefusesARevokedDevice()
    {
        var service = NewService();
        var created = service.CreateSpace("test");
        var spaceId = created.Value!.SpaceId;
        var registered = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc");
        var deviceId = registered.Value!.DeviceId;

        var reset = service.ResetToken(spaceId, deviceId);
        Assert.True(reset.Success);
        Assert.NotEqual(registered.Value.DeviceToken, reset.Value!.DeviceToken);
        Assert.True(service.Authorize(spaceId, deviceId, reset.Value.DeviceToken).Success);
        Assert.False(service.Authorize(spaceId, deviceId, registered.Value.DeviceToken).Success);



        service.RevokeDevice(spaceId, deviceId);
        Assert.False(service.ResetToken(spaceId, deviceId).Success);
        Assert.False(service.Authorize(spaceId, deviceId, reset.Value.DeviceToken).Success);
    }



    [Fact]
    public void PutData_FirstPush_AdvancesToVersionOne_AndGetDataReturnsTheExactJson()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var space = store.GetSpace(spaceId)!;
        var device = store.GetDevice(spaceId, deviceId)!;

        var json = SealedEnvelope(spaceKey, spaceId, deviceId, baseVersion: 0);
        var pushed = service.PutData(space, device, json, baseVersion: 0, force: false);

        Assert.True(pushed.Success);
        Assert.Equal(1, pushed.Value);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);

        var pulled = service.GetData(store.GetSpace(spaceId)!, 0);
        Assert.True(pulled.Success);
        Assert.Equal(json, pulled.Value!.Json);
        Assert.Equal(1, pulled.Value.Version.Version);
        Assert.True(store.ReadBlob(spaceId, pulled.Value.Version) is { Length: > 0 });
    }

    [Fact]
    public void PutData_StaleBase_ReturnsConflictWithTheCurrentVersion_AndLeavesStateUntouched()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false).Success);

        var stale = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);
        Assert.False(stale.Success);
        Assert.Equal(SyncErrorCode.VersionConflict, stale.Error);
        Assert.Equal(1, stale.CurrentVersion);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Single(store.GetVersions(spaceId));
    }

    [Fact]
    public void PutData_FastForward_AdvancesToVersionTwo()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false).Success);
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, false).Success);
        Assert.Equal(2, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(2, store.GetVersions(spaceId).Count);
    }

    [Fact]
    public void PutData_Force_MarksTheReplacedVersionAsAConflictCopy()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);
        var overwritten = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, force: true);

        Assert.True(overwritten.Success);
        var versions = store.GetVersions(spaceId);
        var conflict = Assert.Single(versions, v => v.Version == 1);
        Assert.True(conflict.IsConflict);
        Assert.Equal(2, conflict.ConflictOf);
        Assert.False(Assert.Single(versions, v => v.Version == 2).IsConflict);
    }

    [Fact]
    public void PutData_RejectsEnvelopeVersionMismatch_SpaceMismatch_DeviceMismatch_AndGarbage()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var space = store.GetSpace(spaceId)!;
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.Equal(SyncErrorCode.BadRequest, service.PutData(space, device, "not json", 0, false).Error);

        var wrongVersion = SealedEnvelope(spaceKey, spaceId, deviceId, 0).Replace("\"version\":1", "\"version\":7");
        Assert.Equal(SyncErrorCode.BadRequest, service.PutData(space, device, wrongVersion, 0, false).Error);

        var wrongSpace = SealedEnvelope(spaceKey, "sp_other", deviceId, 0);
        Assert.Equal(SyncErrorCode.BadRequest, service.PutData(space, device, wrongSpace, 0, false).Error);

        var wrongDevice = SealedEnvelope(spaceKey, spaceId, "dev_other", 0);
        Assert.Equal(SyncErrorCode.BadRequest, service.PutData(space, device, wrongDevice, 0, false).Error);
    }

    [Fact]
    public void PutData_RefusesAHugePayloadBeforeParsing()
    {
        var options = new SyncServerOptions { StorageRoot = _dir, MaxPayloadBytes = 64 };
        var service = new SpaceService(new FileSpaceStore(_dir), options);
        var store = new FileSpaceStore(_dir);
        var created = service.CreateSpace("test");
        var spaceId = created.Value!.SpaceId;
        var registered = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pc");

        var space = store.GetSpace(spaceId)!;
        var device = store.GetDevice(spaceId, registered.Value!.DeviceId)!;
        var pushed = service.PutData(space, device, new string('x', 200), 0, false);

        Assert.Equal(SyncErrorCode.PayloadTooLarge, pushed.Error);

        Assert.Equal(64, pushed.LimitBytes);
    }

    [Fact]
    public void GetData_ReturnsHistoricalVersions_AndFailsForUnavailableOnes()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;
        var first = SealedEnvelope(spaceKey, spaceId, deviceId, 0);
        service.PutData(store.GetSpace(spaceId)!, device, first, 0, false);
        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, false);

        Assert.Equal(first, service.GetData(store.GetSpace(spaceId)!, 1).Value!.Json);
        Assert.Equal(SyncErrorCode.NotFound, service.GetData(store.GetSpace(spaceId)!, 99).Error);
    }

    [Fact]
    public void ListVersions_ReportsConflictAndRetentionFlags_NewestFirst()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;
        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);
        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, force: true);

        var list = service.ListVersions(store.GetSpace(spaceId)!).Value!;
        Assert.Equal(new long[] { 2, 1 }, list.Select(v => v.Version).ToArray());
        Assert.True(list[1].IsConflict);
        Assert.All(list, v => Assert.True(v.Retained));
    }



    [Fact]
    public void KeyWrap_PutGet_RoundTrips_AndHonoursIfMatch()
    {
        var (service, store, spaceKey, spaceId, _) = NewPairedServer();
        var record = SyncKeyWrap.Wrap(spaceKey, "lock-password");
        var json = SyncJson.SerializeKeyWrap(record);

        var stored = service.PutKeyWrap(store.GetSpace(spaceId)!, json, ifMatchVersion: 0);
        Assert.True(stored.Success);
        Assert.Equal(1, stored.Value);

        var fetched = service.GetKeyWrap(store.GetSpace(spaceId)!);
        Assert.Equal(json, fetched.Value);
        Assert.Equal(spaceKey, SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(fetched.Value!), "lock-password"));

        Assert.Equal(SyncErrorCode.VersionConflict, service.PutKeyWrap(store.GetSpace(spaceId)!, json, ifMatchVersion: 0).Error);
        Assert.True(service.PutKeyWrap(store.GetSpace(spaceId)!, json, ifMatchVersion: 1).Success);
    }

    [Fact]
    public void KeyWrap_RejectsMalformedRecords_AndMissingRecord()
    {
        var (service, store, spaceKey, spaceId, _) = NewPairedServer();

        Assert.Equal(SyncErrorCode.NotFound, service.GetKeyWrap(store.GetSpace(spaceId)!).Error);

        var badKdf = SyncKeyWrap.Wrap(spaceKey, "pw");
        badKdf.Kdf = "argon2id";
        Assert.Equal(SyncErrorCode.BadRequest, service.PutKeyWrap(store.GetSpace(spaceId)!, SyncJson.SerializeKeyWrap(badKdf), 0).Error);

        var badIter = SyncKeyWrap.Wrap(spaceKey, "pw");
        badIter.Iter = 10;
        Assert.Equal(SyncErrorCode.BadRequest, service.PutKeyWrap(store.GetSpace(spaceId)!, SyncJson.SerializeKeyWrap(badIter), 0).Error);

        Assert.Equal(SyncErrorCode.BadRequest, service.PutKeyWrap(store.GetSpace(spaceId)!, "{}", 0).Error);
    }



    private static VersionRecord Version(long version, long size, bool conflict = false)
        => new() { Version = version, Size = size, IsConflict = conflict, ConflictOf = conflict ? version + 1 : 0, Retained = true };

    [Fact]
    public void Retention_NeverEvictsTheCurrentVersion_AndEvictsOldestFirst()
    {
        var versions = new List<VersionRecord> { Version(1, 10), Version(2, 10), Version(3, 10), Version(4, 10) };
        var victims = RetentionPolicy.PlanEviction(versions, currentVersion: 4, maxVersions: 3, quotaBytes: 0);

        Assert.Equal(new long[] { 1 }, victims.Select(v => v.Version).ToArray());
    }

    [Fact]
    public void Retention_DropsConflictsOnlyAfterOrdinaryHistory()
    {


        var withHistory = new List<VersionRecord>
        {
            Version(1, 10, conflict: true), Version(2, 10), Version(3, 10), Version(4, 10),
        };
        var victims = RetentionPolicy.PlanEviction(withHistory, currentVersion: 4, maxVersions: 2, quotaBytes: 0);
        Assert.Equal(new long[] { 2 }, victims.Select(v => v.Version).ToArray());


        var quotaBound = new List<VersionRecord> { Version(1, 400, conflict: true), Version(2, 400) };
        var quotaVictims = RetentionPolicy.PlanEviction(quotaBound, currentVersion: 2, maxVersions: 10, quotaBytes: 500);
        Assert.Equal(new long[] { 1 }, quotaVictims.Select(v => v.Version).ToArray());
    }

    [Fact]
    public void Retention_EnforcesQuota_ButAlwaysKeepsTheCurrentVersion()
    {
        var versions = new List<VersionRecord> { Version(1, 400), Version(2, 400) };
        var victims = RetentionPolicy.PlanEviction(versions, currentVersion: 2, maxVersions: 10, quotaBytes: 500);
        Assert.Equal(new long[] { 1 }, victims.Select(v => v.Version).ToArray());

        var onlyCurrent = new List<VersionRecord> { Version(1, 4000) };
        Assert.Empty(RetentionPolicy.PlanEviction(onlyCurrent, currentVersion: 1, maxVersions: 10, quotaBytes: 500));
    }

    [Fact]
    public void Retention_IsAppliedOnPush_AndMarksEvictedRowsUnretained()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer(maxVersions: 2);
        var device = store.GetDevice(spaceId, deviceId)!;

        for (long baseVersion = 0; baseVersion < 3; baseVersion++)
        {
            var pushed = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, baseVersion), baseVersion, false);
            Assert.True(pushed.Success);
        }

        var versions = store.GetVersions(spaceId);
        Assert.Equal(3, versions.Count);
        Assert.False(versions.Single(v => v.Version == 1).Retained);
        Assert.True(versions.Single(v => v.Version == 2).Retained);
        Assert.True(versions.Single(v => v.Version == 3).Retained);
        Assert.Null(store.ReadBlob(spaceId, versions.Single(v => v.Version == 1)));
        Assert.Equal(SyncErrorCode.NotFound, service.GetData(store.GetSpace(spaceId)!, 1).Error);
    }

    [Fact]
    public void Quota_RefusesAPushThatWouldExceedIt_WhenNothingCanBeReclaimed()
    {


        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer(quotaBytes: 600);
        var device = store.GetDevice(spaceId, deviceId)!;

        var incompressible = string.Concat(Enumerable.Range(0, 40).Select(_ => Guid.NewGuid().ToString("N")));
        var refused = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0, incompressible), 0, false);

        Assert.Equal(SyncErrorCode.QuotaExceeded, refused.Error);
        Assert.Equal(0, store.GetSpace(spaceId)!.CurrentVersion);
    }



    [Fact]
    public void FileSpaceStore_RejectsUnsafeIds_AndReportsCorruptMetadata()
    {
        var store = new FileSpaceStore(_dir);
        Assert.False(FileSpaceStore.IsSafeId("../escape"));
        Assert.False(FileSpaceStore.IsSafeId("a/b"));
        Assert.False(FileSpaceStore.IsSafeId(""));
        Assert.True(FileSpaceStore.IsSafeId("AbC-123_xyz"));

        Assert.Null(store.GetSpace("../../../etc"));
        Assert.False(store.SpaceExists("bad id"));


        Assert.Empty(store.GetDevices("../../../etc"));
        Assert.Empty(store.GetVersions("../../../etc"));
        Assert.Null(store.GetDevice("../../../etc", "device"));
        Assert.Null(store.GetSpace("../escape"));

        var created = new SpaceService(store, new SyncServerOptions { StorageRoot = _dir }).CreateSpace("t");
        var spaceDir = Path.Combine(_dir, "spaces", created.Value!.SpaceId);
        File.WriteAllText(Path.Combine(spaceDir, "versions.json"), "{ not json");
        Assert.Throws<SpaceStoreException>(() => store.GetVersions(created.Value.SpaceId));
    }

    [Fact]
    public void TokenAuth_GeneratesHighEntropyCredentials_AndVerifiesInConstantTime()
    {
        var token = TokenAuth.NewToken();
        Assert.NotEqual(token, TokenAuth.NewToken());
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);

        var hash = TokenAuth.Hash(token);
        Assert.True(TokenAuth.Verify(token, hash));
        Assert.False(TokenAuth.Verify(token + "x", hash));
        Assert.False(TokenAuth.Verify("", hash));
        Assert.False(TokenAuth.Verify(token, "not-base64!!"));

        Assert.True(FileSpaceStore.IsSafeId(TokenAuth.NewSpaceId()));
        Assert.True(FileSpaceStore.IsSafeId(TokenAuth.NewDeviceId()));
    }

    [Fact]
    public void FailureRateLimiter_BlocksWithinWindow_AndRecoversAfterwards()
    {
        var limiter = new FailureRateLimiter(maxFailures: 2, window: TimeSpan.FromMilliseconds(50));
        Assert.False(limiter.IsBlocked("k"));
        limiter.RecordFailure("k");
        Assert.False(limiter.IsBlocked("k"));
        limiter.RecordFailure("k");
        Assert.True(limiter.IsBlocked("k"));

        limiter.Reset("k");
        Assert.False(limiter.IsBlocked("k"));

        limiter.RecordFailure("k");
        Thread.Sleep(80);
        Assert.False(limiter.IsBlocked("k"));
    }



    [Fact]
    public void FailureRateLimiter_SweepsExpiredWindows_SoRandomKeysCannotGrowItForever()
    {




        var limiter = new FailureRateLimiter(maxFailures: 2, window: TimeSpan.FromMilliseconds(60));
        for (var i = 0; i < 6000; i++) limiter.RecordFailure("read:rand-" + i);
        Assert.True(limiter.TrackedKeyCount > 2048, "阈值未越过，判据失效");

        Thread.Sleep(1200);
        limiter.RecordFailure("read:after-expiry");

        Assert.True(limiter.TrackedKeyCount < 100,
            $"过期窗口未被清扫：仍跟踪 {limiter.TrackedKeyCount} 条");
    }

    [Fact]
    public void FailureRateLimiter_SweepNeverWeakensALiveWindow()
    {



        var limiter = new FailureRateLimiter(maxFailures: 2, window: TimeSpan.FromMinutes(5));
        limiter.RecordFailure("auth:target");
        limiter.RecordFailure("auth:target");
        Assert.True(limiter.IsBlocked("auth:target"));

        for (var i = 0; i < 6000; i++) limiter.RecordFailure("read:rand-" + i);
        Thread.Sleep(1200);
        limiter.RecordFailure("read:another");

        Assert.True(limiter.IsBlocked("auth:target"), "活跃窗口被随机键挤掉了 —— 限速器可被绕过");
    }



    [Fact]
    public async Task FileBackend_ConcurrentRegisterAndRevoke_NeverResurrectsARevokedDevice()
    {







        var store = new FileSpaceStore(_dir);
        var service = new SpaceService(store, new SyncServerOptions
        {
            StorageRoot = _dir,
            DefaultQuotaBytes = 500L * 1024 * 1024,
            DefaultMaxVersions = 10,
            MaxAuthFailures = 10,
        });
        var created = service.CreateSpace("race");
        var spaceId = created.Value!.SpaceId;

        var victims = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var reg = service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "victim" + i);
            Assert.True(reg.Success, reg.Message);
            victims.Add(reg.Value!.DeviceId);
        }

        for (var i = 0; i < 400; i++)
            Assert.True(service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "pad" + i).Success);

        for (var round = 0; round < 12; round++)
        {
            var victim = victims[round];
            var registering = Task.Run(() => service.RegisterDevice(spaceId, created.Value.EnrollmentSecret, "new" + round));
            var revoking = Task.Run(() => service.RevokeDevice(spaceId, victim));
            await Task.WhenAll(registering, revoking);

            var devices = store.GetDevices(spaceId);
            var after = devices.Single(d => string.Equals(d.DeviceId, victim, StringComparison.Ordinal));
            Assert.True(after.Revoked, $"第 {round} 轮：撤销被并发的整表写复活了");
            Assert.Contains(devices, d => string.Equals(d.DeviceId, registering.Result.Value!.DeviceId, StringComparison.Ordinal));
        }
    }









    [Fact]
    public void ForceOverwrite_KeepsThePreservedCopyFetchable_AtItsConflictPath()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = service.DeviceOf(spaceId, deviceId)!;

        var first = SealedEnvelope(spaceKey, spaceId, deviceId, 0, "{\"round\":1}");
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, first, 0, force: false).Success);

        var second = SealedEnvelope(spaceKey, spaceId, deviceId, 1, "{\"round\":2}");
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, second, 1, force: true).Success);

        var preserved = Assert.Single(store.GetVersions(spaceId), v => v.Version == 1);
        Assert.True(preserved.IsConflict);
        Assert.Equal(2, preserved.ConflictOf);


        var blobs = Path.Combine(_dir, "spaces", spaceId, "blobs");
        Assert.True(File.Exists(Path.Combine(blobs, "1.conflict")));
        Assert.False(File.Exists(Path.Combine(blobs, "1")));


        var fetched = service.GetData(store.GetSpace(spaceId)!, 1);
        Assert.True(fetched.Success);
        Assert.Equal(first, fetched.Value!.Json);
    }






    [Fact]
    public void IssueReadToken_DoesNotRollBackAVersionAdvanceInBetween()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = service.DeviceOf(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false).Success);

        var staleSnapshot = store.GetSpace(spaceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, false).Success);

        Assert.True(service.IssueReadToken(staleSnapshot).Success);

        var after = store.GetSpace(spaceId)!;
        Assert.Equal(2, after.CurrentVersion);
        Assert.False(string.IsNullOrEmpty(after.ReadTokenHash));
    }






    [Fact]
    public void TouchDevice_MovesOnlyTheLastSeenStamp()
    {
        var (service, store, _, spaceId, deviceId) = NewPairedServer();
        Assert.True(service.RevokeDevice(spaceId, deviceId).Success);
        var revoked = store.GetDevice(spaceId, deviceId)!;

        store.TouchDevice(spaceId, deviceId, DateTime.UtcNow);

        var after = store.GetDevice(spaceId, deviceId)!;
        Assert.True(after.Revoked);
        Assert.Equal(revoked.TokenHash, after.TokenHash);
        Assert.Equal(deviceId, after.DeviceId);
        Assert.NotNull(after.LastSeenAt);
    }


    [Fact]
    public void Authenticate_AfterRevoke_LeavesTheDeviceRevoked()
    {
        var (service, store, _, spaceId, _) = NewPairedServer();


        var issued = service.IssueEditorDevice(store.GetSpace(spaceId)!);
        Assert.True(issued.Success);
        var editorId = issued.Value!.DeviceId;
        var editorToken = issued.Value.DeviceToken;

        Assert.True(service.Authenticate(spaceId, editorId, editorToken).Success);

        Assert.True(service.RevokeDevice(spaceId, editorId).Success);
        Assert.Equal(SyncErrorCode.Forbidden, service.Authenticate(spaceId, editorId, editorToken).Error);
        Assert.True(store.GetDevice(spaceId, editorId)!.Revoked);
    }









    [Fact]
    public void QuotaGate_RefusesAPayloadThatNoRotationCanFit()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer(quotaBytes: 4096);
        var device = service.DeviceOf(spaceId, deviceId)!;




        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false).Success);
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, false).Success);



        var bulky = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16 * 1024));
        var pushed = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 2, bulky), 2, false);

        Assert.Equal(SyncErrorCode.QuotaExceeded, pushed.Error);


        Assert.Equal(4096, pushed.LimitBytes);
        Assert.NotNull(pushed.UsedBytes);
        Assert.True(pushed.UsedBytes > pushed.LimitBytes);
    }





    [Fact]
    public void Retention_CountsConflictCopiesSeparately()
    {
        var versions = new List<VersionRecord>();
        for (var v = 1; v <= 10; v++) versions.Add(new VersionRecord { Version = v, Size = 10, Retained = true });
        versions.Add(new VersionRecord { Version = 11, Size = 100, Retained = true, IsConflict = true });



        Assert.Empty(RetentionPolicy.PlanEviction(versions, currentVersion: 10, maxVersions: 10, quotaBytes: 0));



        var onlyCurrent = new List<VersionRecord>
        {
            new() { Version = 10, Size = 10, Retained = true },
            new() { Version = 11, Size = 100, Retained = true, IsConflict = true },
        };
        Assert.Contains(
            RetentionPolicy.PlanEviction(onlyCurrent, currentVersion: 10, maxVersions: 10, quotaBytes: 50),
            v => v.IsConflict);
    }





    [Fact]
    public void RegisterDevice_IsRateLimited_AfterRepeatedFailures()
    {
        var service = NewService(maxAuthFailures: 3);
        var spaceId = service.CreateSpace("test").Value!.SpaceId;

        for (var i = 0; i < 3; i++)
            Assert.Equal(SyncErrorCode.Forbidden, service.RegisterDevice(spaceId, "wrong-secret", "pc").Error);

        Assert.Equal(SyncErrorCode.RateLimited, service.RegisterDevice(spaceId, "wrong-secret", "pc").Error);
    }


    [Fact]
    public void ResetToken_RefusesARevokedDevice()
    {
        var (service, store, _, spaceId, deviceId) = NewPairedServer();

        Assert.True(service.RevokeDevice(spaceId, deviceId).Success);

        Assert.Equal(SyncErrorCode.Forbidden, service.ResetToken(spaceId, deviceId).Error);
        Assert.True(store.GetDevice(spaceId, deviceId)!.Revoked);
    }






    [Fact]
    public void PutData_RealignsAPointerLeftBehindByACrash()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = service.DeviceOf(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false).Success);


        var space = store.GetSpace(spaceId)!;
        space.CurrentVersion = 0;
        store.SaveSpace(space);



        var pushed = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);

        Assert.Equal(SyncErrorCode.VersionConflict, pushed.Error);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Single(store.GetVersions(spaceId));
    }
}
