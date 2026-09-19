using System.Text;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;

public class SqliteSpaceStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-sqlite-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SpaceService NewService(int maxVersions = 10)
        => new(new SqliteSpaceStore(_dir), new SyncServerOptions { StorageRoot = _dir, DefaultMaxVersions = maxVersions });

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



    [Fact]
    public void Sqlite_Store_CreatesTheDatabaseAndSeparatesSpaces()
    {
        var store = new SqliteSpaceStore(_dir);
        Assert.True(File.Exists(Path.Combine(_dir, "novara-sync.db")));

        Assert.False(store.SpaceExists("AbC-123_xyz"));
        Assert.Null(store.GetSpace("AbC-123_xyz"));
        Assert.Empty(store.GetDevices("AbC-123_xyz"));
        Assert.Empty(store.GetVersions("AbC-123_xyz"));

        store.SaveSpace(new SpaceRecord { SpaceId = "AbC-123_xyz", Name = "one", QuotaBytes = 10, MaxVersions = 3 });
        Assert.True(store.SpaceExists("AbC-123_xyz"));

        var read = store.GetSpace("AbC-123_xyz");
        Assert.NotNull(read);
        Assert.Equal("one", read!.Name);
        Assert.Equal(10, read.QuotaBytes);
        Assert.Equal(3, read.MaxVersions);
        Assert.Equal(0, read.CurrentVersion);


        read.Name = "renamed";
        read.CurrentVersion = 7;
        read.KeyWrapJson = "{\"wrap\":1}";
        read.KeyWrapVersion = 2;
        store.SaveSpace(read);
        Assert.Equal("renamed", store.GetSpace("AbC-123_xyz")!.Name);
        Assert.Equal(7, store.GetSpace("AbC-123_xyz")!.CurrentVersion);
        Assert.Equal("{\"wrap\":1}", store.GetSpace("AbC-123_xyz")!.KeyWrapJson);
    }

    [Fact]
    public void Sqlite_Store_PersistsDevices_AndReplacesTheVersionList()
    {
        var store = new SqliteSpaceStore(_dir);
        store.SaveSpace(new SpaceRecord { SpaceId = "sp1", Name = "s", QuotaBytes = 1, MaxVersions = 2 });

        store.SaveDevice("sp1", new DeviceRecord { DeviceId = "d1", Name = "pc", TokenHash = "h1" });
        store.SaveDevice("sp1", new DeviceRecord { DeviceId = "d2", Name = "phone", TokenHash = "h2" });
        store.SaveDevice("sp1", new DeviceRecord { DeviceId = "d1", Name = "pc-renamed", TokenHash = "h1b", Revoked = true });

        var devices = store.GetDevices("sp1");
        Assert.Equal(2, devices.Count);
        Assert.Equal("pc-renamed", store.GetDevice("sp1", "d1")!.Name);
        Assert.True(store.GetDevice("sp1", "d1")!.Revoked);
        Assert.Null(store.GetDevice("sp1", "nope"));

        store.SaveVersions("sp1", new[]
        {
            new VersionRecord { Version = 2, DeviceId = "d1", Sha256 = "b", Size = 20 },
            new VersionRecord { Version = 1, DeviceId = "d1", Sha256 = "a", Size = 10, Retained = false, IsConflict = true, ConflictOf = 2 },
        });
        var versions = store.GetVersions("sp1");
        Assert.Equal(new long[] { 1, 2 }, versions.Select(v => v.Version).ToArray());
        Assert.True(versions[0].IsConflict);
        Assert.False(versions[0].Retained);
        Assert.Equal(2, versions[0].ConflictOf);


        store.SaveVersions("sp1", new[] { new VersionRecord { Version = 2, DeviceId = "d1", Sha256 = "b", Size = 20 } });
        Assert.Single(store.GetVersions("sp1"));
    }

    [Fact]
    public void Sqlite_Store_RejectsUnsafeIds_AndReportsADamagedDatabase()
    {
        var store = new SqliteSpaceStore(_dir);
        Assert.False(SqliteSpaceStore.IsSafeId("../escape"));
        Assert.False(SqliteSpaceStore.IsSafeId("a/b"));
        Assert.Null(store.GetSpace("../../etc"));
        Assert.False(store.SpaceExists("bad id"));
        Assert.Throws<SpaceStoreException>(() => store.SaveSpace(new SpaceRecord { SpaceId = "bad id" }));
        Assert.Throws<SpaceStoreException>(() => store.SaveVersions("bad/id", Array.Empty<VersionRecord>()));

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = Path.Combine(_dir, "novara-sync.db" + suffix);
            if (File.Exists(path)) File.Delete(path);
        }
        File.WriteAllBytes(Path.Combine(_dir, "novara-sync.db"), Enumerable.Repeat((byte)0x41, 4096).ToArray());

        Assert.Throws<SpaceStoreException>(() => store.SpaceExists("AbC-123"));
        Assert.Throws<SpaceStoreException>(() => store.GetSpace("AbC-123"));
    }

    [Fact]
    public void Sqlite_Store_Blobs_UseTheSameOnDiskLayoutAsTheFileStore()
    {
        var sqlite = new SqliteSpaceStore(_dir);
        var file = new FileSpaceStore(_dir);
        sqlite.SaveSpace(new SpaceRecord { SpaceId = "sp1", Name = "s", QuotaBytes = 1, MaxVersions = 2 });

        var version = new VersionRecord { Version = 3, Size = 4, Retained = true };
        sqlite.WriteBlob("sp1", version, new byte[] { 1, 2, 3, 4 });
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, file.ReadBlob("sp1", version));

        var conflict = new VersionRecord { Version = 3, IsConflict = true };
        sqlite.WriteBlob("sp1", conflict, new byte[] { 9 });
        Assert.True(File.Exists(Path.Combine(_dir, "spaces", "sp1", "blobs", "3.conflict")));
        Assert.Single(file.ReadBlob("sp1", conflict)!);

        sqlite.DeleteBlob("sp1", conflict);
        Assert.Null(sqlite.ReadBlob("sp1", conflict));
    }



    [Fact]
    public void Sqlite_EndToEnd_PairPushPull_AndConflict()
    {
        var service = NewService();
        var store = new SqliteSpaceStore(_dir);

        var created = service.CreateSpace("sqlite");
        Assert.True(created.Success);
        var space = created.Value!;
        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var spaceId = space.SpaceId;

        Assert.Equal(SyncErrorCode.Forbidden, service.RegisterDevice(spaceId, "wrong", "pc").Error);
        var registered = service.RegisterDevice(spaceId, space.EnrollmentSecret, "pc");
        Assert.True(registered.Success);
        var deviceId = registered.Value!.DeviceId;


        var reopened = new SqliteSpaceStore(_dir);
        Assert.True(reopened.SpaceExists(spaceId));
        Assert.NotNull(reopened.GetDevice(spaceId, deviceId));

        var device = store.GetDevice(spaceId, deviceId)!;
        var first = SealedEnvelope(spaceKey, spaceId, deviceId, 0);
        Assert.Equal(1, service.PutData(store.GetSpace(spaceId)!, device, first, 0, false).Value);

        var pulled = service.GetData(store.GetSpace(spaceId)!, 0);
        Assert.Equal(first, pulled.Value!.Json);

        Assert.Equal(2, service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, false).Value);

        var stale = service.PutData(store.GetSpace(spaceId)!, device, first, 0, false);
        Assert.Equal(SyncErrorCode.VersionConflict, stale.Error);
        Assert.Equal(2, stale.CurrentVersion);

        Assert.Equal(new long[] { 2, 1 }, service.ListVersions(store.GetSpace(spaceId)!).Value!.Select(v => v.Version).ToArray());
    }

    [Fact]
    public void Sqlite_EndToEnd_ForceKeepsAConflictCopy_AndRetentionEvictsTheOldest()
    {
        var service = NewService(maxVersions: 2);
        var store = new SqliteSpaceStore(_dir);

        var created = service.CreateSpace("sqlite");
        var space = created.Value!;
        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var spaceId = space.SpaceId;
        var deviceId = service.RegisterDevice(spaceId, space.EnrollmentSecret, "pc").Value!.DeviceId;
        var device = store.GetDevice(spaceId, deviceId)!;

        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 0), 0, false);
        var forced = service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 1), 1, force: true);
        Assert.True(forced.Success);

        var versions = store.GetVersions(spaceId);
        Assert.True(Assert.Single(versions, v => v.Version == 1).IsConflict);



        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 2), 2, false);
        var afterThird = store.GetVersions(spaceId);
        Assert.True(Assert.Single(afterThird, v => v.Version == 2).Retained);
        Assert.True(Assert.Single(afterThird, v => v.Version == 1).Retained);



        service.PutData(store.GetSpace(spaceId)!, device, SealedEnvelope(spaceKey, spaceId, deviceId, 3), 3, false);
        var after = store.GetVersions(spaceId);
        Assert.False(Assert.Single(after, v => v.Version == 2).Retained);
        Assert.Null(store.ReadBlob(spaceId, Assert.Single(after, v => v.Version == 2)));
        Assert.True(Assert.Single(after, v => v.Version == 3).Retained);
        Assert.True(Assert.Single(after, v => v.Version == 1).Retained);
        Assert.Equal(SyncErrorCode.NotFound, service.GetData(store.GetSpace(spaceId)!, 2).Error);
    }

    [Fact]
    public void Sqlite_EndToEnd_KeyWrapRoundTrip_AndDeviceRevocation()
    {
        var service = NewService();
        var store = new SqliteSpaceStore(_dir);

        var created = service.CreateSpace("sqlite");
        var space = created.Value!;
        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var spaceId = space.SpaceId;
        var registered = service.RegisterDevice(spaceId, space.EnrollmentSecret, "pc");
        var deviceId = registered.Value!.DeviceId;
        var token = registered.Value.DeviceToken;

        var json = SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(spaceKey, "lock-pw"));
        Assert.Equal(SyncErrorCode.NotFound, service.GetKeyWrap(store.GetSpace(spaceId)!).Error);
        Assert.Equal(1, service.PutKeyWrap(store.GetSpace(spaceId)!, json, 0).Value);
        Assert.Equal(SyncErrorCode.VersionConflict, service.PutKeyWrap(store.GetSpace(spaceId)!, json, 0).Error);

        var fetched = service.GetKeyWrap(store.GetSpace(spaceId)!).Value!;
        Assert.Equal(spaceKey, SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(fetched), "lock-pw"));

        Assert.True(service.Authorize(spaceId, deviceId, token).Success);
        Assert.True(service.RevokeDevice(spaceId, deviceId).Success);
        Assert.Equal(SyncErrorCode.Forbidden, service.Authorize(spaceId, deviceId, token).Error);

        Assert.Equal(SyncErrorCode.Forbidden, service.ResetToken(spaceId, deviceId).Error);
        Assert.Equal(SyncErrorCode.Forbidden, service.Authorize(spaceId, deviceId, token).Error);
    }
}
