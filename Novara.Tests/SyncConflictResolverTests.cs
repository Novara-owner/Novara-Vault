using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SyncConflictResolverTests : IAsyncLifetime
{
    private const string LockPassword = "lock-pw-123";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "novara-conflict-" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "novara-conflict-out-" + Guid.NewGuid().ToString("N"));

    private HostedSyncServer _server = null!;

    public SyncConflictResolverTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_out);
    }

    public async Task InitializeAsync() => _server = await HostedSyncServer.StartAsync();

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        PasswordService.SetBaseDir(null);
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_out, true); } catch { }
    }



    private sealed record Device(SyncApiClient Client, string SpaceId, string SpaceKey, string DeviceId, string Enrollment);

    private async Task<Device> NewDeviceAsync(string deviceName = "pc")
    {
        var created = await _server.CreateSpaceAsync("conflict", deviceName);
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

    private static SyncState NewState(Device d) => new()
    {
        Enabled = true,
        ServerUrl = d.Client.BaseUrl,
        SpaceId = d.SpaceId,
        DeviceId = d.DeviceId,
        DeviceName = "pc",
        FrequencyMinutes = 5,
    };

    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private static void Save(NovaraStore store) => Assert.True(store.SaveSync());

    private static void AddMemo(NovaraStore store, string name)
        => store.Database.MemoEntries.Add(new MemoEntry { Name = name, Type = "自定义" });

    private static string[] LiveMemoNames(NovaraStore store)
        => store.Database.MemoEntries.Where(e => !e.IsDeleted).Select(e => e.Name).OrderBy(n => n).ToArray();





    private async Task<(Device Left, NovaraStore Store, SyncState State)> SetUpConflictAsync()
    {
        var a = await NewDeviceAsync("pc-a");
        var b = await JoinAsync(a, "pc-b");
        var storeA = NewStore("a.novadb");
        var storeB = NewStore("b.novadb");
        var stateA = NewState(a);
        var stateB = NewState(b);


        AddMemo(storeB, "untouched");
        AddMemo(storeB, "shared");
        Save(storeB);
        Assert.Equal(SyncRoundStatus.Push, (await SyncEngine.RunRoundAsync(storeB, stateB, b.SpaceKey, b.Client, Now)).Status);
        Assert.Equal(SyncRoundStatus.Pull, (await SyncEngine.RunRoundAsync(storeA, stateA, a.SpaceKey, a.Client, Now)).Status);
        Assert.Equal(new[] { "shared", "untouched" }, LiveMemoNames(storeA));


        storeA.Database.MemoEntries.Single(e => e.Name == "shared").Name = "shared-A";
        AddMemo(storeA, "a-only");
        Save(storeA);

        AddMemo(storeB, "b-only");
        Save(storeB);
        Assert.Equal(SyncRoundStatus.Push, (await SyncEngine.RunRoundAsync(storeB, stateB, b.SpaceKey, b.Client, Now)).Status);


        Assert.Equal(SyncRoundStatus.Conflict, (await SyncEngine.RunRoundAsync(storeA, stateA, a.SpaceKey, a.Client, Now)).Status);
        return (a, storeA, stateA);
    }



    [Fact]
    public async Task Inspect_ReportsExactlyWhatEachSideHas()
    {
        var (left, store, state) = await SetUpConflictAsync();

        var inspection = await SyncConflictResolver.InspectAsync(store, state, left.SpaceKey, left.Client);

        Assert.True(inspection.Success, inspection.Message);
        Assert.Equal(1, inspection.LocalVersion);
        Assert.Equal(2, inspection.RemoteVersion);

        var diff = inspection.Diff!;
        Assert.Equal(1, diff.OnlyLocal);
        Assert.Equal(1, diff.OnlyRemote);
        Assert.Equal(1, diff.BothChanged);
        Assert.Equal(1, Assert.Single(diff.Partitions, p => p.Partition == SyncPartition.Memo).Same);
        Assert.Equal(new[] { SyncPartition.Memo }, diff.Dirty.Select(p => p.Partition).ToArray());
    }

    [Fact]
    public async Task Inspect_KeepsTheServerPayloadVerbatim_SoExportingIsAByteCopy()
    {
        var (left, store, state) = await SetUpConflictAsync();

        var inspection = await SyncConflictResolver.InspectAsync(store, state, left.SpaceKey, left.Client);
        var stored = await left.Client.GetDataAsync(2);

        Assert.True(stored.Success, stored.Message);
        var serverContainer = SyncEnvelopeCodec.DecodePayload(SyncEnvelopeCodec.Deserialize(stored.Value!.Json));
        Assert.Equal(serverContainer, inspection.RemoteContainer);
    }

    [Fact]
    public async Task Inspect_FailsClosed_WhenTheDownloadedPayloadCannotBeTrusted()
    {
        var (left, store, state) = await SetUpConflictAsync();


        var otherKey = SyncKeyWrap.CreateSpaceKey();
        var inspection = await SyncConflictResolver.InspectAsync(store, state, otherKey, left.Client);

        Assert.False(inspection.Success);
        Assert.Null(inspection.Diff);
        Assert.Empty(inspection.RemoteContainer);
    }

    [Fact]
    public async Task Inspect_FailsClosed_OnTheEnvironmentGates()
    {
        var (left, store, state) = await SetUpConflictAsync();


        var relocked = new NovaraStore(Path.Combine(_root, "a.novadb"));
        Assert.False((await SyncConflictResolver.InspectAsync(relocked, state, left.SpaceKey, left.Client)).Success);


        store.SetSuppressSave(true);
        Assert.False((await SyncConflictResolver.InspectAsync(store, state, left.SpaceKey, left.Client)).Success);
        store.SetSuppressSave(false);


        Assert.False((await SyncConflictResolver.InspectAsync(store, state, "", left.Client)).Success);
    }



    [Fact]
    public async Task ExportedServerVersion_IsAV4Container_ThatOpensWithTheSpaceKey()
    {
        var (left, store, state) = await SetUpConflictAsync();
        var inspection = await SyncConflictResolver.InspectAsync(store, state, left.SpaceKey, left.Client);

        var path = Path.Combine(_out, "remote.novaenc");
        Assert.True(SyncConflictResolver.ExportRemoteVersion(path, inspection.RemoteContainer).Success);


        Assert.Equal(inspection.RemoteContainer, await File.ReadAllBytesAsync(path));
        var payload = SyncJson.DeserializeDatabase(System.Text.Encoding.UTF8.GetString(
            SyncContainer.Open(await File.ReadAllBytesAsync(path), left.SpaceKey)));
        Assert.Equal(new[] { "b-only", "shared", "untouched" },
            payload.MemoEntries.Where(e => !e.IsDeleted).Select(e => e.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task ExportedLocalVersion_RestoresThroughTheReleasedImportChain()
    {
        var (left, store, state) = await SetUpConflictAsync();

        var path = Path.Combine(_out, "local.novaenc");
        Assert.True(SyncConflictResolver.ExportLocalVersion(path, store, left.SpaceKey).Success);


        var restored = NewStore("restored.novadb");
        var result = restored.ImportBackup(path, left.SpaceKey);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(new[] { "a-only", "shared-A", "untouched" }, LiveMemoNames(restored));
    }

    [Fact]
    public async Task Exports_OverwriteAnExistingFile_AndRejectEmptyInput()
    {
        var (left, store, _) = await SetUpConflictAsync();
        var path = Path.Combine(_out, "same.novaenc");

        Assert.True(SyncConflictResolver.ExportLocalVersion(path, store, left.SpaceKey).Success);
        var first = await File.ReadAllBytesAsync(path);
        Assert.True(SyncConflictResolver.ExportLocalVersion(path, store, left.SpaceKey).Success);

        var second = await File.ReadAllBytesAsync(path);
        Assert.NotEmpty(first);
        Assert.NotEqual(first, second);
        Assert.False(SyncConflictResolver.ExportRemoteVersion(path, Array.Empty<byte>()).Success);
        Assert.False(SyncConflictResolver.ExportRemoteVersion("", new byte[] { 1 }).Success);
        Assert.False(SyncConflictResolver.ExportLocalVersion(path, store, "").Success);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SuggestedFileNames_AreAscii_AndSayWhichSideTheyAre()
    {
        var stamp = new DateTime(2026, 9, 12, 10, 35, 20, DateTimeKind.Utc);

        var remote = SyncConflictResolver.SuggestFileName(remote: true, version: 42, stamp);
        var local = SyncConflictResolver.SuggestFileName(remote: false, version: -3, stamp);

        Assert.Equal("novara-sync-remote-v42-20260912-103520.novaenc", remote);
        Assert.Equal("novara-sync-local-v0-20260912-103520.novaenc", local);
        Assert.All(remote.Concat(local), c => Assert.True(c < 128, $"non-ascii char in '{remote}' / '{local}'"));
    }
}
