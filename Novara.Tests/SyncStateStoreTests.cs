using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SyncStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "novara-state-" + Guid.NewGuid().ToString("N"));

    public SyncStateStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string StatePath => Path.Combine(_dir, SyncStateStore.FileName);

    [Fact]
    public void Save_ThenLoad_RoundTripsTheState()
    {
        var store = new SyncStateStore(StatePath);
        store.Save(new SyncState { SpaceId = "sp", DeviceId = "dev", BaseVersion = 7, LastSeenVersion = 9 });

        var back = store.Load();
        Assert.Equal("sp", back.SpaceId);
        Assert.Equal("dev", back.DeviceId);
        Assert.Equal(7, back.BaseVersion);
        Assert.Equal(9, back.LastSeenVersion);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_MissingOrCorruptFile_DegradesToAFreshStateInsteadOfThrowing()
    {
        var store = new SyncStateStore(StatePath);
        Assert.Equal("", store.Load().SpaceId);
        File.WriteAllText(StatePath, "{ not json at all");
        Assert.Equal("", store.Load().SpaceId);
    }

    [Fact]
    public async Task Save_ConcurrentWriters_NeverThrowAndNeverInterleave()
    {
        var store = new SyncStateStore(StatePath);
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var writers = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            for (int i = 0; i < 40; i++)
            {
                try { store.Save(new SyncState { SpaceId = "sp-" + w, BaseVersion = i }); }
                catch (Exception ex) { failures.Add(ex); }
            }
        })).ToArray();
        await Task.WhenAll(writers);

        Assert.Empty(failures);

        var back = store.Load();
        Assert.StartsWith("sp-", back.SpaceId);
        Assert.True(back.BaseVersion >= 0 && back.BaseVersion < 40);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
