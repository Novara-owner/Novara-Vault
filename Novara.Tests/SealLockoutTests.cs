using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealLockoutTests
{
    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-lockout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string File(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) {  }
        }
    }

    private sealed class FakeClock : ISealClock
    {
        public long TickCount64 { get; set; }

        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    }

    private static (SealLockout Lockout, FakeClock Clock) Create(TempDir dir)
    {
        var clock = new FakeClock { TickCount64 = 1_000_000 };
        return (new SealLockout(dir.File("lockout-seal.dat"), clock), clock);
    }



    [Fact]
    public void FiveFailures_LockForThirtyMinutes()
    {
        using var dir = new TempDir();
        var (lockout, _) = Create(dir);

        for (var i = 0; i < SealLockout.MaxFailures - 1; i++) lockout.RegisterFailure();
        Assert.False(lockout.IsLocked);

        lockout.RegisterFailure();

        Assert.True(lockout.IsLocked);
        Assert.True(lockout.Remaining > TimeSpan.FromMinutes(29));
        Assert.True(lockout.Remaining <= SealLockout.LockDuration);
    }

    [Fact]
    public void TheLockExpiresAfterThirtyMinutes()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        clock.TickCount64 += (long)SealLockout.LockDuration.TotalMilliseconds;

        Assert.False(lockout.IsLocked);
        Assert.Equal(0, lockout.FailCount);
    }

    [Fact]
    public void AFailureDuringTheLock_DoesNotExtendIt()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        var before = lockout.Remaining;
        clock.TickCount64 += 60_000;
        lockout.RegisterFailure();

        Assert.True(lockout.Remaining < before, "the lock must keep counting down, not reset");
        Assert.True(lockout.Remaining > TimeSpan.FromMinutes(28));
    }

    [Fact]
    public void FourFailuresThenASuccess_ClearTheCounter()
    {
        using var dir = new TempDir();
        var (lockout, _) = Create(dir);

        for (var i = 0; i < SealLockout.MaxFailures - 1; i++) lockout.RegisterFailure();
        lockout.RegisterSuccess();

        Assert.Equal(0, lockout.FailCount);
        Assert.False(lockout.IsLocked);
    }







    [Fact]
    public void MovingTheSystemClockBack_DoesNotShortenTheLock()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        var before = lockout.Remaining;
        clock.UtcNow = clock.UtcNow.AddHours(-1);

        Assert.Equal(before, lockout.Remaining);
        Assert.True(lockout.IsLocked);
    }

    [Fact]
    public void MovingTheSystemClockForward_DoesNotShortenItEither()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        var before = lockout.Remaining;
        clock.UtcNow = clock.UtcNow.AddHours(5);

        Assert.Equal(before, lockout.Remaining);
    }





    [Fact]
    public void AfterAReboot_TheWallClockCarriesTheLock()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        clock.TickCount64 = 5_000;
        clock.UtcNow = clock.UtcNow.AddMinutes(10);

        Assert.True(lockout.IsLocked);
        Assert.True(lockout.Remaining > TimeSpan.FromMinutes(19));
        Assert.True(lockout.Remaining <= TimeSpan.FromMinutes(20));
    }

    [Fact]
    public void AfterARebootAndAFullWait_TheLockIsGone()
    {
        using var dir = new TempDir();
        var (lockout, clock) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        clock.TickCount64 = 5_000;
        clock.UtcNow = clock.UtcNow.AddMinutes(31);

        Assert.False(lockout.IsLocked);
    }



    [Fact]
    public void AMissingFile_MeansNoLock()
    {
        using var dir = new TempDir();
        var (lockout, _) = Create(dir);

        Assert.False(lockout.IsLocked);
        Assert.Equal(0, lockout.FailCount);
    }





    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"failCount\":\"five\"}")]
    public void AnUnreadableFile_MeansNoLockAndIsKept(string contents)
    {
        using var dir = new TempDir();
        var path = dir.File("lockout-seal.dat");
        File.WriteAllText(path, contents);

        var lockout = new SealLockout(path, new FakeClock());

        Assert.False(lockout.IsLocked);
        Assert.True(File.Exists(path), "the damaged file must be left for inspection");
    }

    [Fact]
    public void Clear_RemovesTheFile()
    {
        using var dir = new TempDir();
        var (lockout, _) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        lockout.Clear();

        Assert.False(File.Exists(dir.File("lockout-seal.dat")));
        Assert.False(lockout.IsLocked);
    }







    [Fact]
    public void Registering_DoesNotTouchAnyOtherFile()
    {
        using var dir = new TempDir();
        var neighbour = dir.File("keywrap.nskw");
        File.WriteAllBytes(neighbour, [1, 2, 3, 4]);
        var before = File.ReadAllBytes(neighbour);

        var (lockout, _) = Create(dir);
        for (var i = 0; i < SealLockout.MaxFailures; i++) lockout.RegisterFailure();

        Assert.Equal(before, File.ReadAllBytes(neighbour));

        var names = Directory.GetFiles(dir.Root).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["keywrap.nskw", "lockout-seal.dat"], names);
    }
}
