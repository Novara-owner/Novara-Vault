using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SyncEngineTests : IAsyncLifetime
{
    private const string LockPassword = "lock-pw-123";


    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "novara-engine-" + Guid.NewGuid().ToString("N"));

    private HostedSyncServer _server = null!;

    public SyncEngineTests() => Directory.CreateDirectory(_root);

    public async Task InitializeAsync() => _server = await HostedSyncServer.StartAsync();

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        PasswordService.SetBaseDir(null);
        try { Directory.Delete(_root, true); } catch { }
    }




    private sealed record Device(SyncApiClient Client, string SpaceId, string SpaceKey, string DeviceId, string Enrollment);

    private async Task<Device> NewDeviceAsync(string spaceName = "engine", string deviceName = "pc")
    {
        var created = await _server.CreateSpaceAsync(spaceName, deviceName);
        return new Device(created.Client, created.SpaceId, created.SpaceKey, created.DeviceId, created.EnrollmentSecret);
    }

    private async Task<Device> JoinAsync(Device space, string deviceName)
    {
        var joined = await _server.PairDeviceAsync(space.SpaceId, space.Enrollment, deviceName);
        return new Device(joined.Client, space.SpaceId, space.SpaceKey, joined.DeviceId, space.Enrollment);
    }


    private NovaraStore NewStore(string fileName)
    {
        PasswordService.SetBaseDir(_root);
        if (!File.Exists(Path.Combine(_root, "security.dat"))) Assert.True(PasswordService.Create(LockPassword));

        var store = new NovaraStore(Path.Combine(_root, fileName));
        store.Load();
        if (!store.IsEncrypted) Assert.True(store.EnableEncryption(LockPassword));
        return store;
    }


    private static SyncState NewState(Device device, bool enabled = true) => new()
    {
        Enabled = enabled,
        ServerUrl = device.Client.BaseUrl,
        SpaceId = device.SpaceId,
        DeviceId = device.DeviceId,
        DeviceName = "pc",
        FrequencyMinutes = 5,
    };

    private static void AddMemo(NovaraStore store, string name)
    {
        store.Database.MemoEntries.Add(new MemoEntry { Name = name, Type = "自定义" });
        Assert.True(store.SaveSync());
    }

    private static Task<SyncRoundOutcome> RoundAsync(NovaraStore store, SyncState state, Device device)
        => SyncEngine.RunRoundAsync(store, state, device.SpaceKey, device.Client, Now);



    [Fact]
    public async Task FirstRound_PublishesTheInitialState_AndSecondRoundIsANoOp()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        AddMemo(store, "first");
        var state = NewState(device);

        var pushed = await RoundAsync(store, state, device);
        Assert.Equal(SyncRoundStatus.Push, pushed.Status);
        Assert.Equal(1, pushed.RemoteVersion);
        Assert.Equal(1, state.BaseVersion);
        Assert.Equal(1, state.LastSeenVersion);
        Assert.Equal(Now, state.LastSyncAt);
        Assert.NotEqual("", state.LastPushedSha256);


        Assert.Equal(SyncRoundStatus.NoOp, (await RoundAsync(store, state, device)).Status);
        Assert.Equal(1, (await device.Client.GetInfoAsync()).Value!.Version);


        AddMemo(store, "second");
        var again = await RoundAsync(store, state, device);
        Assert.Equal(SyncRoundStatus.Push, again.Status);
        Assert.Equal(2, state.BaseVersion);
    }

    [Fact]
    public async Task RoundTrip_TransfersTheCiphertextVerbatim()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        AddMemo(store, "verbatim");
        var state = NewState(device);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(store, state, device)).Status);

        var stored = await device.Client.GetDataAsync(1);
        Assert.True(stored.Success, stored.Message);



        var envelope = SyncEnvelopeCodec.Deserialize(stored.Value!.Json);
        Assert.True(SyncEnvelopeCodec.Verify(envelope, device.SpaceKey));
        var manifestContainer = SyncEnvelopeCodec.DecodePayload(envelope);
        Assert.Equal(5, manifestContainer[4]);
        var manifestJson = SyncContainer.OpenManifest(manifestContainer, device.SpaceKey);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<SyncEngine.SyncManifestV5>(manifestJson,
            SyncPayloadApplier.SyncJsonOptions)!;
        Assert.Equal(5, manifest.Format);

        var entriesPid = manifest.Partitions["memoEntries"];
        var blob = await device.Client.GetChunkAsync(entriesPid, default);
        Assert.True(blob.Success, blob.Message);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<MemoEntry>>(
            SyncContainer.OpenChunk(blob.Value!, device.SpaceKey, Convert.FromHexString(entriesPid)),
            SyncPayloadApplier.SyncJsonOptions)!;
        Assert.Contains(entries, e => !e.IsDeleted && e.Name == "verbatim");
    }

    [Fact]
    public async Task SecondDevice_WithAnEmptyVault_AdoptsTheRemoteCopy()
    {
        var publisher = await NewDeviceAsync();
        var publisherStore = NewStore("publisher.novadb");
        AddMemo(publisherStore, "from-publisher");
        var publisherState = NewState(publisher);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(publisherStore, publisherState, publisher)).Status);


        var joiner = await JoinAsync(publisher, "laptop");
        var joinerStore = NewStore("joiner.novadb");
        Assert.Empty(joinerStore.Database.MemoEntries);
        var joinerState = NewState(joiner);

        var adopted = await RoundAsync(joinerStore, joinerState, joiner);
        Assert.Equal(SyncRoundStatus.Pull, adopted.Status);
        Assert.Equal("from-publisher", joinerStore.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
        Assert.Equal(1, joinerState.BaseVersion);




        var reopened = new NovaraStore(Path.Combine(_root, "joiner.novadb"));
        Assert.Equal(LoadStatus.Ok, reopened.LoadWithPassword(LockPassword).Status);
        Assert.Equal("from-publisher", reopened.Database.MemoEntries.Single(e => !e.IsDeleted).Name);


        Assert.Equal(SyncRoundStatus.NoOp, (await RoundAsync(joinerStore, joinerState, joiner)).Status);
    }

    [Fact]
    public async Task SecondDevice_WithOnlyAGroup_IsAskedInsteadOfBeingOverwritten()
    {



        var publisher = await NewDeviceAsync();
        var publisherStore = NewStore("publisher.novadb");
        AddMemo(publisherStore, "remote");
        var publisherState = NewState(publisher);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(publisherStore, publisherState, publisher)).Status);

        var joiner = await JoinAsync(publisher, "laptop");
        var joinerStore = NewStore("joiner.novadb");
        Assert.Empty(joinerStore.Database.MemoGroups);
        joinerStore.Database.MemoGroups.Add(new MemoGroup { Name = "本机分组" });
        Assert.True(joinerStore.SaveSync());
        Assert.Empty(joinerStore.Database.MemoEntries);
        var joinerState = NewState(joiner);

        var outcome = await RoundAsync(joinerStore, joinerState, joiner);
        Assert.Equal(SyncRoundStatus.Conflict, outcome.Status);
        Assert.True(outcome.NeedsDecision);
        Assert.Equal("本机分组", joinerStore.Database.MemoGroups.Single().Name);
        Assert.Empty(joinerStore.Database.MemoEntries);
    }

    [Fact]
    public async Task SecondDevice_WithLocalData_IsAskedInsteadOfBeingOverwritten()
    {
        var publisher = await NewDeviceAsync();
        var publisherStore = NewStore("publisher.novadb");
        AddMemo(publisherStore, "remote");
        var publisherState = NewState(publisher);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(publisherStore, publisherState, publisher)).Status);

        var joiner = await JoinAsync(publisher, "laptop");
        var joinerStore = NewStore("joiner.novadb");
        AddMemo(joinerStore, "mine");
        var joinerState = NewState(joiner);

        var outcome = await RoundAsync(joinerStore, joinerState, joiner);
        Assert.Equal(SyncRoundStatus.Conflict, outcome.Status);
        Assert.True(outcome.NeedsDecision);
        Assert.Equal(0, outcome.LocalVersion);
        Assert.Equal(1, outcome.RemoteVersion);
        Assert.Equal("mine", joinerStore.Database.MemoEntries.Single(e => !e.IsDeleted).Name);


        Assert.Equal(1, joinerState.LastSeenVersion);
    }

    [Fact]
    public async Task PushAgainstAStaleBase_ReportsAConflictWithTheServerVersion_AndNeverOverwrites()
    {
        var a = await NewDeviceAsync();
        var b = await JoinAsync(a, "laptop");
        var storeA = NewStore("a.novadb");
        var storeB = NewStore("b.novadb");
        var stateA = NewState(a);
        var stateB = NewState(b);


        AddMemo(storeB, "b-first");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);
        Assert.Equal(SyncRoundStatus.Pull, (await RoundAsync(storeA, stateA, a)).Status);


        AddMemo(storeA, "a-edit");
        AddMemo(storeB, "b-edit");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);

        var conflict = await RoundAsync(storeA, stateA, a);
        Assert.Equal(SyncRoundStatus.Conflict, conflict.Status);
        Assert.Equal(2, conflict.RemoteVersion);


        Assert.Equal(2, (await a.Client.GetInfoAsync()).Value!.Version);
    }

    [Fact]
    public async Task ConflictWithStructurallyIdenticalPayloads_AutoRebases_InsteadOfAlarmingTheUser()
    {
        var a = await NewDeviceAsync();
        var b = await JoinAsync(a, "laptop");
        var storeA = NewStore("a.novadb");
        var storeB = NewStore("b.novadb");
        var stateA = NewState(a);
        var stateB = NewState(b);


        AddMemo(storeB, "shared");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);
        Assert.Equal(SyncRoundStatus.Pull, (await RoundAsync(storeA, stateA, a)).Status);
        var v1Sha = SyncEnvelopeCodec.HashPayload(SyncPayloadApplier.BuildPayloadJson(storeA.Database));




        AddMemo(storeB, "second");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);
        Assert.Equal(SyncRoundStatus.Pull,
            (await SyncEngine.PullAsync(storeA, stateA, a.SpaceKey, a.Client, Now)).Status);
        stateA.BaseVersion = 1;
        stateA.LastSeenVersion = 1;
        stateA.LastPushedSha256 = v1Sha;




        var outcome = await RoundAsync(storeA, stateA, a);
        Assert.Equal(SyncRoundStatus.AutoRebased, outcome.Status);
        Assert.True(outcome.Changed);
        Assert.False(outcome.NeedsDecision);
        Assert.Equal(2, stateA.BaseVersion);
        Assert.Equal(2, (await a.Client.GetInfoAsync()).Value!.Version);
    }




    [Fact]
    public async Task Pull_WithAManifestMissingAPartition_FailsClosedInsteadOfApplyingPartially()
    {
        var publisher = await NewDeviceAsync();
        var publisherStore = NewStore("publisher.novadb");
        AddMemo(publisherStore, "from-publisher");
        var publisherState = NewState(publisher);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(publisherStore, publisherState, publisher)).Status);



        var partitions = SyncPartitions.ExtractPartitions(
            StableJson.SerializeToUtf8BytesStable(
                SyncFieldPolicy.CloneForSync(publisherStore.Database), SyncPayloadApplier.SyncJsonOptions)!);
        var pids = partitions.ToDictionary(k => k.Key, k => SyncPartitions.PidOf(k.Value), StringComparer.Ordinal);
        foreach (var key in SyncPartitions.Keys)
        {
            var pid = pids[key];
            var put = await publisher.Client.PutChunkAsync(pid,
                SyncContainer.SealChunk(partitions[key], publisher.SpaceKey, Convert.FromHexString(pid)), default);
            Assert.True(put.Success, put.Message);
        }
        Assert.True(pids.Remove("noteCards"));

        var manifestJson = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new SyncEngine.SyncManifestV5(5, pids), SyncEngine.ManifestOptions);
        var envelope = new SyncEnvelope
        {
            Space = publisher.SpaceId,
            Crypto = SyncContainer.FileVersionManifest,
            Version = 2,
            Base = 1,
            Device = publisher.DeviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(Now),
            Payload = Convert.ToBase64String(SyncContainer.SealManifest(
                manifestJson, publisher.SpaceKey, SyncContainer.NewVersionSalt())),
        };
        SyncEnvelopeCodec.Seal(envelope, publisher.SpaceKey);
        var published = await publisher.Client.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), 1,
            chunkIds: pids.Values.ToList());
        Assert.True(published.Success, published.Message);


        var joiner = await JoinAsync(publisher, "laptop");
        var joinerStore = NewStore("joiner.novadb");
        var joinerState = NewState(joiner);
        var outcome = await SyncEngine.PullAsync(joinerStore, joinerState, joiner.SpaceKey, joiner.Client, Now);
        Assert.Equal(SyncRoundStatus.Error, outcome.Status);
        Assert.Equal(0, joinerState.BaseVersion);
        Assert.Equal(0, joinerState.LastSeenVersion);
    }

    [Fact]
    public async Task ForcePush_RebasesOntoTheLiveVersion_AndKeepsTheOverwrittenOneAsAConflict()
    {
        var a = await NewDeviceAsync();
        var b = await JoinAsync(a, "laptop");
        var storeA = NewStore("a.novadb");
        var storeB = NewStore("b.novadb");
        var stateA = NewState(a);
        var stateB = NewState(b);

        AddMemo(storeB, "b-first");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);
        Assert.Equal(SyncRoundStatus.Pull, (await RoundAsync(storeA, stateA, a)).Status);

        AddMemo(storeA, "a-wins");
        AddMemo(storeB, "b-edit");
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(storeB, stateB, b)).Status);

        var forced = await SyncEngine.ForcePushAsync(storeA, stateA, a.SpaceKey, a.Client, Now);
        Assert.Equal(SyncRoundStatus.Push, forced.Status);
        Assert.Equal(3, forced.RemoteVersion);
        Assert.Equal(3, stateA.BaseVersion);


        var versions = await a.Client.ListVersionsAsync();
        Assert.True(versions.Success, versions.Message);
        var preserved = Assert.Single(versions.Value!, v => v.Version == 2);
        Assert.True(preserved.IsConflict);
        Assert.Equal(3, preserved.ConflictOf);
    }

    [Fact]
    public async Task ConflictAgainstALegacyV4Version_FallsBackToTheV4Comparison_InsteadOfErroring()
    {





        var a = await NewDeviceAsync();
        var b = await JoinAsync(a, "laptop");
        var storeA = NewStore("a.novadb");
        var storeB = NewStore("b.novadb");
        var stateA = NewState(a);
        var stateB = NewState(b);



        AddMemo(storeA, "legacy-a1");
        var aV1Sha = SyncEnvelopeCodec.HashPayload(SyncPayloadApplier.BuildPayloadJson(storeA.Database));
        await PushLegacyV4Async(a, storeA, baseVersion: 0);
        stateA.BaseVersion = 1;
        stateA.LastSeenVersion = 1;
        stateA.LastPushedSha256 = aV1Sha;


        Assert.Equal(SyncRoundStatus.Pull, (await RoundAsync(storeB, stateB, b)).Status);
        AddMemo(storeB, "legacy-b2");
        var bV2Sha = SyncEnvelopeCodec.HashPayload(SyncPayloadApplier.BuildPayloadJson(storeB.Database));
        await PushLegacyV4Async(b, storeB, baseVersion: 1);
        stateB.BaseVersion = 2;
        stateB.LastSeenVersion = 2;
        stateB.LastPushedSha256 = bV2Sha;


        AddMemo(storeA, "legacy-a-edit");
        var outcome = await RoundAsync(storeA, stateA, a);

        Assert.Equal(SyncRoundStatus.Conflict, outcome.Status);
        Assert.True(outcome.NeedsDecision);
        Assert.Equal(2, outcome.RemoteVersion);
    }


    private static async Task PushLegacyV4Async(Device device, NovaraStore store, long baseVersion)
    {
        var envelope = new SyncEnvelope
        {
            Space = device.SpaceId,
            Version = baseVersion + 1,
            Base = baseVersion,
            Device = device.DeviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(Now),
            Payload = Convert.ToBase64String(SyncContainer.Seal(
                SyncPayloadApplier.BuildPayloadJson(store.Database), device.SpaceKey, SyncContainer.NewVersionSalt())),
        };
        SyncEnvelopeCodec.Seal(envelope, device.SpaceKey);
        var put = await device.Client.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), baseVersion, false, default);
        Assert.True(put.Success, put.Message);
    }



    [Fact]
    public async Task LockedVault_WaitsWithoutAnyNetworkRound()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        AddMemo(store, "x");
        var state = NewState(device);


        var relocked = new NovaraStore(Path.Combine(_root, "pc.novadb"));
        Assert.Equal(SyncRoundStatus.WaitUnlocked, (await RoundAsync(relocked, state, device)).Status);
        Assert.Equal(0, state.BaseVersion);
        Assert.Equal(0, (await device.Client.GetInfoAsync()).Value!.Version);


        Assert.Equal(SyncRoundStatus.WaitUnlocked,
            (await SyncEngine.RunRoundAsync(store, state, "", device.Client, Now)).Status);
    }

    [Fact]
    public async Task PendingRestore_StandsDown_AndTheRestoredBytesAreNeverPublished()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        AddMemo(store, "restored");
        store.SetSuppressSave(true);
        var state = NewState(device);

        Assert.Equal(SyncRoundStatus.RestorePending, (await RoundAsync(store, state, device)).Status);
        Assert.Equal(0, (await device.Client.GetInfoAsync()).Value!.Version);
    }

    [Fact]
    public async Task DisabledSync_AndRollback_DoNothing()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        var state = NewState(device, enabled: false);

        Assert.Equal(SyncRoundStatus.Disabled, (await RoundAsync(store, state, device)).Status);


        state.Enabled = true;
        state.LastSeenVersion = 5;
        Assert.Equal(SyncRoundStatus.RollbackRejected, (await RoundAsync(store, state, device)).Status);
    }

    [Fact]
    public async Task UnreachableServer_IsATransportFailure_NotAnError()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("pc.novadb");
        var state = NewState(device);

        using var offline = new SyncApiClient("http://127.0.0.1:1", device.SpaceId, device.DeviceId, "token",
            timeout: TimeSpan.FromSeconds(2));
        var outcome = await SyncEngine.RunRoundAsync(store, state, device.SpaceKey, offline, Now);
        Assert.Equal(SyncRoundStatus.TransportFailure, outcome.Status);
        Assert.False(outcome.Ok);
        Assert.False(outcome.Changed);
    }



    private static SyncApiClient DeadClient()
        => new("http://127.0.0.1:1", "space", "device", "token", timeout: TimeSpan.FromSeconds(2));

    [Fact]
    public async Task TransportFailure_IsRetriedSameRound_AndRecoversOnTheNextAttempt()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("retry-recover.novadb");
        AddMemo(store, "retry-recover");
        var state = NewState(device);

        var calls = 0;
        var delays = new List<TimeSpan>();
        var outcome = await SyncEngine.RunRoundWithTransportRetryAsync(
            store, state, device.SpaceKey,
            _ => Task.FromResult(calls++ == 0 ? DeadClient() : device.Client),
            Now, delay: (span, _) => { delays.Add(span); return Task.CompletedTask; });

        Assert.True(outcome.Ok);
        Assert.Equal(2, calls);
        Assert.Single(delays);
        Assert.Equal(SyncEngine.TransportRetryBaseDelay, delays[0]);
    }

    [Fact]
    public async Task TransportFailure_ExhaustsRetries_AndStaysHonest()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("retry-exhaust.novadb");
        AddMemo(store, "retry-exhaust");
        var state = NewState(device);

        var delays = new List<TimeSpan>();
        var outcome = await SyncEngine.RunRoundWithTransportRetryAsync(
            store, state, device.SpaceKey,
            _ => Task.FromResult(DeadClient()),
            Now, delay: (span, _) => { delays.Add(span); return Task.CompletedTask; });

        Assert.Equal(SyncRoundStatus.TransportFailure, outcome.Status);
        Assert.Equal(2, delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(20), delays[1]);
    }

    [Fact]
    public async Task NonTransportOutcomes_AreNotRetried()
    {
        var device = await NewDeviceAsync();
        var store = NewStore("retry-disabled.novadb");
        AddMemo(store, "retry-disabled");
        var state = NewState(device, enabled: false);

        var delays = new List<TimeSpan>();
        var calls = 0;
        var outcome = await SyncEngine.RunRoundWithTransportRetryAsync(
            store, state, device.SpaceKey,
            _ => Task.FromResult(calls++ == 0 ? DeadClient() : device.Client),
            Now, delay: (span, _) => { delays.Add(span); return Task.CompletedTask; });

        Assert.Equal(SyncRoundStatus.Disabled, outcome.Status);
        Assert.Empty(delays);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Pull_KeepsThisDevicesOwnCredentials_AndTakesTheRemoteReplicaData()
    {
        var publisher = await NewDeviceAsync();
        var publisherStore = NewStore("publisher.novadb");
        AddMemo(publisherStore, "shared");
        publisherStore.Database.AppSettings.AppLanguage = "en-US";
        Assert.True(publisherStore.SaveSync());
        var publisherState = NewState(publisher);
        Assert.Equal(SyncRoundStatus.Push, (await RoundAsync(publisherStore, publisherState, publisher)).Status);

        var joiner = await JoinAsync(publisher, "laptop");
        var joinerStore = NewStore("joiner.novadb");
        joinerStore.Database.AppSettings.McpToken = "joiner-secret";
        joinerStore.Database.AppSettings.AutoStart = true;
        joinerStore.Database.AppSettings.QuickCaptureHotkey = "AltN";
        Assert.True(joinerStore.SaveSync());
        var joinerState = NewState(joiner);

        Assert.Equal(SyncRoundStatus.Pull, (await RoundAsync(joinerStore, joinerState, joiner)).Status);

        var settings = joinerStore.Database.AppSettings;
        Assert.Equal("shared", joinerStore.Database.MemoEntries.Single(e => !e.IsDeleted).Name);
        Assert.Equal("en-US", settings.AppLanguage);
        Assert.Equal("joiner-secret", settings.McpToken);
        Assert.True(settings.AutoStart);
        Assert.Equal("AltN", settings.QuickCaptureHotkey);
    }
}
