using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;

public class SyncV5ServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-v5srv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SpaceService NewService(long quotaBytes = 0, int maxVersions = 10)
    {
        var options = new SyncServerOptions
        {
            StorageRoot = _dir,
            DefaultQuotaBytes = quotaBytes > 0 ? quotaBytes : 500L * 1024 * 1024,
            DefaultMaxVersions = maxVersions,
        };
        return new SpaceService(new FileSpaceStore(_dir), options);
    }

    private (SpaceService Service, FileSpaceStore Store, string SpaceKey, string SpaceId, string DeviceId) NewPairedServer(
        long quotaBytes = 0, int maxVersions = 10)
    {
        var service = NewService(quotaBytes, maxVersions);
        var store = new FileSpaceStore(_dir);
        var created = service.CreateSpace("v5");
        Assert.True(created.Success);
        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var registered = service.RegisterDevice(created.Value!.SpaceId, created.Value.EnrollmentSecret, "pc");
        Assert.True(registered.Success);
        return (service, store, spaceKey, created.Value.SpaceId, registered.Value!.DeviceId);
    }

    private static string SealedV4Envelope(string spaceKey, string spaceId, string deviceId, long baseVersion)
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes("{\"k\":\"v\"}"), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = spaceId,
            Device = deviceId,
            Base = baseVersion,
            Version = baseVersion + 1,
            Crypto = SyncContainer.FileVersionV4,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return SyncEnvelopeCodec.Serialize(envelope);
    }

    private static string SealedManifestEnvelope(string spaceKey, string spaceId, string deviceId, long baseVersion,
        IReadOnlyDictionary<string, string> partitions)
    {
        var manifest = JsonSerializer.SerializeToUtf8Bytes(
            new { format = 5, partitions },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var container = SyncContainer.SealManifest(manifest, spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = spaceId,
            Device = deviceId,
            Base = baseVersion,
            Version = baseVersion + 1,
            Crypto = SyncContainer.FileVersionManifest,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return SyncEnvelopeCodec.Serialize(envelope);
    }

    private static string PushChunk(SpaceService service, ISpaceStore store, string spaceId, byte[] body)
    {


        var chunkId = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var put = service.PutChunk(store.GetSpace(spaceId)!, chunkId, body);
        Assert.True(put.Success, put.Message);
        return chunkId;
    }



    [Fact]
    public void ManifestPush_SwitchesGeneration_AndAV4PushIsRefusedForever()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;


        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedV4Envelope(spaceKey, spaceId, deviceId, 0),
            0, false, containerV5Capable: true).Success);
        Assert.Equal(4, store.GetSpace(spaceId)!.ContainerGeneration);



        var pids = new Dictionary<string, string> { ["memoEntries"] = new string('a', 64) };
        Assert.True(service.PutChunk(store.GetSpace(spaceId)!, pids["memoEntries"], new byte[] { 1 }).Success);
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 1, pids),
            1, false, containerV5Capable: true, chunkIds: pids.Values.ToList()).Success);
        Assert.Equal(5, store.GetSpace(spaceId)!.ContainerGeneration);



        var refused = service.PutData(store.GetSpace(spaceId)!, device, SealedV4Envelope(spaceKey, spaceId, deviceId, 2),
            2, false, containerV5Capable: true);
        Assert.False(refused.Success);
        Assert.Equal(SyncErrorCode.UpgradeRequired, refused.Error);
        Assert.Equal(2, store.GetSpace(spaceId)!.CurrentVersion);
    }

    [Fact]
    public void ManifestPush_WithAStaleBase_IsRefused_AndDoesNotSwitchTheGeneration()
    {




        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedV4Envelope(spaceKey, spaceId, deviceId, 0),
            0, false, containerV5Capable: true).Success);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(4, store.GetSpace(spaceId)!.ContainerGeneration);


        var pids = new Dictionary<string, string> { ["memoEntries"] = new string('c', 64) };
        var stale = service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 0, pids),
            0, false, containerV5Capable: true, chunkIds: pids.Values.ToList());

        Assert.False(stale.Success);
        Assert.Equal(SyncErrorCode.VersionConflict, stale.Error);
        Assert.Equal(1, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(4, store.GetSpace(spaceId)!.ContainerGeneration);


        Assert.True(service.PutChunk(store.GetSpace(spaceId)!, pids["memoEntries"], new byte[] { 1 }).Success);
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 1, pids),
            1, false, containerV5Capable: true, chunkIds: pids.Values.ToList()).Success);
        Assert.Equal(5, store.GetSpace(spaceId)!.ContainerGeneration);
    }

    [Fact]
    public void ManifestPush_WithoutTheCapabilityHeader_IsRefusedAsAForgery()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        var pids = new Dictionary<string, string> { ["memoEntries"] = new string('b', 64) };
        var forged = service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 0, pids),
            0, false, containerV5Capable: false);

        Assert.False(forged.Success);
        Assert.Equal(SyncErrorCode.UpgradeRequired, forged.Error);
        Assert.Equal(0, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(4, store.GetSpace(spaceId)!.ContainerGeneration);
    }



    [Fact]
    public void DataAndChunkReads_InV5Generation_InterceptLegacyDeviceTokens_ButNotReadTokens()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        var chunkId = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("partition body"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 0, new Dictionary<string, string> { ["memoEntries"] = chunkId }),
            0, false, containerV5Capable: true, chunkIds: new List<string> { chunkId }).Success);
        Assert.Equal(5, store.GetSpace(spaceId)!.ContainerGeneration);

        var v5Space = store.GetSpace(spaceId)!;


        Assert.Equal(SyncErrorCode.UpgradeRequired, service.GetData(v5Space, 0, containerV5Capable: false, deviceAuth: true).Error);
        Assert.Equal(SyncErrorCode.UpgradeRequired, service.GetChunk(v5Space, chunkId, containerV5Capable: false, deviceAuth: true).Error);


        Assert.True(service.GetData(v5Space, 0, containerV5Capable: true, deviceAuth: true).Success);
        Assert.True(service.GetChunk(v5Space, chunkId, containerV5Capable: true, deviceAuth: true).Success);



        Assert.True(service.GetData(v5Space, 0, containerV5Capable: false, deviceAuth: false).Success);
        Assert.True(service.GetChunk(v5Space, chunkId, containerV5Capable: false, deviceAuth: false).Success);



        Assert.Equal(SyncErrorCode.UpgradeRequired, service.GetData(v5Space, 1, containerV5Capable: false, deviceAuth: true).Error);
        Assert.True(service.GetData(v5Space, 1, containerV5Capable: true, deviceAuth: true).Success);
    }

    [Fact]
    public void V4GenerationReads_AreUnaffected_ByTheCapabilityHeader()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedV4Envelope(spaceKey, spaceId, deviceId, 0),
            0, false, containerV5Capable: true).Success);
        var v4Space = store.GetSpace(spaceId)!;


        Assert.True(service.GetData(v4Space, 0, containerV5Capable: false, deviceAuth: true).Success);
    }



    [Fact]
    public void ChunkQuota_CountsPhysicalChunkBytes_AndSkipsASameLengthOverwrite()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer(quotaBytes: 1000);
        var space = store.GetSpace(spaceId)!;

        var first = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("chunk-one"))).ToLowerInvariant();
        Assert.True(service.PutChunk(space, first, new byte[600]).Success);

        var second = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("chunk-two"))).ToLowerInvariant();
        var over = service.PutChunk(space, second, new byte[500]);
        Assert.False(over.Success);
        Assert.Equal(SyncErrorCode.QuotaExceeded, over.Error);
        Assert.Equal(1000, over.LimitBytes);


        Assert.Equal(1100, over.UsedBytes);


        Assert.True(service.PutChunk(space, first, new byte[600]).Success);
    }



    [Fact]
    public void Retention_ReclaimsChunksNoLongerReferenced_ByAnyRetainedVersion()
    {
        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer(maxVersions: 2);
        var device = store.GetDevice(spaceId, deviceId)!;


        var chunk1 = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("partition v1"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 0, new Dictionary<string, string> { ["memoEntries"] = chunk1 }),
            0, false, containerV5Capable: true, chunkIds: new List<string> { chunk1 }).Success);

        var chunk2 = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("partition v2"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 1, new Dictionary<string, string> { ["memoEntries"] = chunk2 }),
            1, false, containerV5Capable: true, chunkIds: new List<string> { chunk2 }).Success);

        var chunk3 = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("partition v3"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 2, new Dictionary<string, string> { ["memoEntries"] = chunk3 }),
            2, false, containerV5Capable: true, chunkIds: new List<string> { chunk3 }).Success);


        Assert.Equal(SyncErrorCode.NotFound, service.GetChunk(store.GetSpace(spaceId)!, chunk1, true, true).Error);
        Assert.True(service.GetChunk(store.GetSpace(spaceId)!, chunk2, true, true).Success);
        Assert.True(service.GetChunk(store.GetSpace(spaceId)!, chunk3, true, true).Success);


        var live = store.GetVersions(spaceId).Where(v => v.Retained)
            .SelectMany(v => v.ChunkIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(new HashSet<string> { chunk2, chunk3 }, live);
    }



    [Fact]
    public void ManifestPush_WithoutItsChunkIds_IsRefused_AndPrunesNothing()
    {




        var (service, store, spaceKey, spaceId, deviceId) = NewPairedServer();
        var device = store.GetDevice(spaceId, deviceId)!;

        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedV4Envelope(spaceKey, spaceId, deviceId, 0),
            0, false, containerV5Capable: true).Success);
        Assert.Equal(4, store.GetSpace(spaceId)!.ContainerGeneration);


        var liveChunk = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("referenced by v2"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 1,
                new Dictionary<string, string> { ["memoEntries"] = liveChunk }),
            1, false, containerV5Capable: true, chunkIds: new List<string> { liveChunk }).Success);
        Assert.Equal(2, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(5, store.GetSpace(spaceId)!.ContainerGeneration);

        var pids = new Dictionary<string, string> { ["memoEntries"] = new string('e', 64) };


        var noHeader = service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 2, pids),
            2, false, containerV5Capable: true);
        Assert.False(noHeader.Success);
        Assert.Equal(SyncErrorCode.BadRequest, noHeader.Error);


        var allMalformed = service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, deviceId, 2, pids),
            2, false, containerV5Capable: true, chunkIds: new List<string> { "not-a-chunk-id" });
        Assert.False(allMalformed.Success);
        Assert.Equal(SyncErrorCode.BadRequest, allMalformed.Error);


        Assert.Equal(2, store.GetSpace(spaceId)!.CurrentVersion);
        Assert.Equal(1, store.GetVersions(spaceId).Count(v => v.Version == 2));
        Assert.True(service.GetChunk(store.GetSpace(spaceId)!, liveChunk, containerV5Capable: true, deviceAuth: true).Success);


        var nextChunk = PushChunk(service, store, spaceId, Encoding.UTF8.GetBytes("referenced by v3"));
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device,
            SealedManifestEnvelope(spaceKey, spaceId, deviceId, 2,
                new Dictionary<string, string> { ["memoEntries"] = nextChunk }),
            2, false, containerV5Capable: true, chunkIds: new List<string> { nextChunk }).Success);
        Assert.Equal(3, store.GetSpace(spaceId)!.CurrentVersion);
    }



    [Theory]
    [InlineData("file")]
    [InlineData("sqlite")]
    public void ChunkIds_SurviveTheMetadataRoundTrip_InBothBackends(string backend)
    {
        ISpaceStore store = backend == "file" ? (ISpaceStore)new FileSpaceStore(_dir) : new SqliteSpaceStore(_dir);
        var service = new SpaceService(store, new SyncServerOptions
        {
            StorageRoot = _dir,
            DefaultQuotaBytes = 500L * 1024 * 1024,
            DefaultMaxVersions = 10,
        });
        var created = service.CreateSpace("backend-" + backend);
        Assert.True(created.Success);
        var spaceKey = SyncKeyWrap.CreateSpaceKey();
        var registered = service.RegisterDevice(created.Value!.SpaceId, created.Value.EnrollmentSecret, "pc");
        Assert.True(registered.Success);
        var spaceId = created.Value.SpaceId;
        var device = store.GetDevice(spaceId, registered.Value!.DeviceId)!;

        var ids = new List<string>
        {
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("one"))).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("two"))).ToLowerInvariant(),
        };
        var pids = new Dictionary<string, string> { ["memoEntries"] = ids[0], ["diaryItems"] = ids[1] };


        foreach (var cid in ids) Assert.True(service.PutChunk(store.GetSpace(spaceId)!, cid, new byte[] { 1 }).Success);
        Assert.True(service.PutData(store.GetSpace(spaceId)!, device, SealedManifestEnvelope(spaceKey, spaceId, registered.Value.DeviceId, 0, pids),
            0, false, containerV5Capable: true, chunkIds: ids.Concat(new List<string> { new string('Z', 64)  }).ToList()).Success);

        var row = store.GetVersions(spaceId).Single(v => v.Version == 1);
        Assert.Equal(ids, row.ChunkIds);
    }
}
