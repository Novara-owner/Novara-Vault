using Novara.Services;
using Xunit;

namespace Novara.Tests;



[Collection("CoreSequential")]
public class StoreSavedEventTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public StoreSavedEventTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "novara-saved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        PasswordService.SetBaseDir(_dir);
        _file = Path.Combine(_dir, "novara.dat");
    }

    public void Dispose()
    {
        try { foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
        PasswordService.SetBaseDir(null);
    }

    [Fact]
    public async Task SaveAsync_RaisesSavedOncePerBatch()
    {
        var store = new NovaraStore(_file);
        store.Load();
        Assert.True(store.IsLoaded);

        var count = 0;
        store.Saved += () => Interlocked.Increment(ref count);

        await store.SaveAsync();
        Assert.Equal(1, count);
    }





    [Fact]
    public void SaveSync_DoesNotRaiseSaved()
    {
        var store = new NovaraStore(_file);
        store.Load();
        Assert.True(store.IsLoaded);

        var count = 0;
        store.Saved += () => Interlocked.Increment(ref count);

        Assert.True(store.SaveSync());
        Assert.Equal(0, count);
        Assert.True(File.Exists(_file), "the write itself must still have happened");
    }

    [Fact]
    public async Task SaveAsync_WhileSuppressed_DoesNotRaiseSaved()
    {
        var store = new NovaraStore(_file);
        store.Load();
        Assert.True(store.IsLoaded);

        var count = 0;
        store.Saved += () => Interlocked.Increment(ref count);

        store.SetSuppressSave(true);
        await store.SaveAsync();
        Assert.Equal(0, count);
    }


    [Fact]
    public async Task SaveAsync_CoalescesABurstIntoOneSaved()
    {
        var store = new NovaraStore(_file);
        store.Load();
        Assert.True(store.IsLoaded);

        var count = 0;
        store.Saved += () => Interlocked.Increment(ref count);

        var pending = new[] { store.SaveAsync(), store.SaveAsync(), store.SaveAsync() };
        await Task.WhenAll(pending);

        Assert.Equal(1, count);
    }


    [Fact]
    public async Task SaveAsync_SubscriberThrowing_StillWrites()
    {
        var store = new NovaraStore(_file);
        store.Load();
        Assert.True(store.IsLoaded);

        store.Saved += () => throw new InvalidOperationException("subscriber blew up");
        await store.SaveAsync();

        Assert.True(File.Exists(_file));
    }
}
