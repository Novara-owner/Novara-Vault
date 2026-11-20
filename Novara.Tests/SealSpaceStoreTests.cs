using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealSpaceStoreTests
{
    private const string SpacePassword = "Ab3kLm9Qz7Rt";
    private const string NewSpacePassword = "Xy7#nQ2$wL9@";

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-space-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) {  }
        }
    }


    private static SealSpaceStore Store(TempDir dir)
        => new(new SealSpacePaths(dir.Root), SealKdfParams.Create(SealKdfParams.MemoryMinKiB, 1));



    [Fact]
    public void Initialize_CreatesTheSixEntries()
    {
        using var dir = new TempDir();
        var store = Store(dir);

        var outcome = store.Initialize(SpacePassword, "一句提示", out var vmk);

        Assert.Equal(SealStoreOutcome.Ok, outcome);
        Assert.Equal(SealFormat.KeySize, vmk.Length);

        Assert.True(File.Exists(store.Paths.KeyWrap));
        Assert.True(File.Exists(store.Paths.Index));
        Assert.True(File.Exists(store.Paths.Hint));
        Assert.True(Directory.Exists(store.Paths.Items));
        Assert.True(Directory.Exists(store.Paths.ImportStaging));
        Assert.True(Directory.Exists(store.Paths.Staging));
    }





    [Fact]
    public void SecondInitialize_IsRefusedAndChangesNothing()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, "hint", out _);

        var before = SnapshotOf(dir.Root);

        var outcome = store.Initialize("Zq4!mN8@vB2#", "other", out var vmk);

        Assert.Equal(SealStoreOutcome.AlreadyInitialized, outcome);
        Assert.Empty(vmk);
        Assert.Equal(before, SnapshotOf(dir.Root));
    }





    [Fact]
    public void AFailedInitializeRollsBackTheKeyWrap()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        Directory.CreateDirectory(store.Paths.Root);
        Directory.CreateDirectory(store.Paths.Index);

        var outcome = store.Initialize(SpacePassword, null, out var vmk);

        Assert.Equal(SealStoreOutcome.IoError, outcome);
        Assert.Empty(vmk);
        Assert.False(store.Exists());

        Directory.Delete(store.Paths.Index, recursive: true);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize(SpacePassword, null, out var retry));
        Assert.Equal(SealFormat.KeySize, retry.Length);
    }

    [Fact]
    public void Initialize_RejectsAWeakPassword()
    {
        using var dir = new TempDir();
        var store = Store(dir);

        Assert.Equal(SealStoreOutcome.WeakPassword, store.Initialize("short", null, out _));
        Assert.False(store.Exists());
    }

    [Fact]
    public void Initialize_RefusesTheMainPassword()
    {
        using var dir = new TempDir();
        var mainDir = Path.Combine(Path.GetTempPath(), "novara-main-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mainDir);
        PasswordService.SetBaseDir(mainDir);

        try
        {
            Assert.True(PasswordService.Create(SpacePassword));

            var store = Store(dir);
            var outcome = store.Initialize(SpacePassword, "hint", out var vmk);

            Assert.Equal(SealStoreOutcome.SameAsMainPassword, outcome);
            Assert.Empty(vmk);
            Assert.False(store.Exists());
        }
        finally
        {
            PasswordService.SetBaseDir(null);
            try { Directory.Delete(mainDir, recursive: true); } catch (IOException) {  }
        }
    }



    [Theory]
    [InlineData(255, 255)]
    [InlineData(256, 256)]
    [InlineData(257, 256)]
    public void Hint_IsCutAtTheByteLimit(int characters, int expectedBytes)
    {
        using var dir = new TempDir();
        var store = Store(dir);

        store.WriteHint(new string('a', characters));

        var bytes = File.ReadAllBytes(store.Paths.Hint);
        Assert.Equal(expectedBytes, bytes.Length);
        Assert.Equal(expectedBytes, store.ReadHint()!.Length);
    }





    [Fact]
    public void Hint_NeverSplitsAMultibyteCharacter()
    {
        using var dir = new TempDir();
        var store = Store(dir);

        store.WriteHint(new string('中', 300));

        var bytes = File.ReadAllBytes(store.Paths.Hint);
        Assert.True(bytes.Length <= SealSpaceStore.MaxHintBytes, $"hint is {bytes.Length} bytes");

        var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        Assert.Equal(85, decoded.Length);
        Assert.All(decoded, c => Assert.Equal('中', c));
    }

    [Fact]
    public void EmptyHint_RemovesTheFile()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.WriteHint("temporary");
        Assert.True(File.Exists(store.Paths.Hint));

        store.WriteHint(null);

        Assert.False(File.Exists(store.Paths.Hint));
        Assert.Null(store.ReadHint());
    }

    [Fact]
    public void MissingHint_ReadsAsNull()
    {
        using var dir = new TempDir();
        Assert.Null(Store(dir).ReadHint());
    }



    [Fact]
    public void Unlock_SucceedsWithTheRightPasswordAndFailsWithAWrongOne()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out var created);

        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(SpacePassword, out var opened, out _));
        Assert.Equal(created, opened);

        Assert.Equal(SealStoreOutcome.WrongPassword, store.TryUnlock("Wr0ng!Passw0rd#9", out var rejected, out _));
        Assert.Empty(rejected);
    }

    [Fact]
    public void Unlock_OnAMissingSpaceSaysNotInitialized()
    {
        using var dir = new TempDir();
        Assert.Equal(SealStoreOutcome.NotInitialized, Store(dir).TryUnlock(SpacePassword, out _, out _));
    }





    [Fact]
    public void ChangePassword_TouchesOnlyTheKeyWrap()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, "hint", out _);

        for (var i = 0; i < 100; i++)
            File.WriteAllBytes(store.Paths.ItemPath(i.ToString("x32")), RandomNumberGenerator.GetBytes(512));

        var itemsBefore = SnapshotOf(store.Paths.Items);
        var wrapBefore = SHA256.HashData(File.ReadAllBytes(store.Paths.KeyWrap));

        var stopwatch = Stopwatch.StartNew();
        var outcome = store.ChangePassword(SpacePassword, NewSpacePassword);
        stopwatch.Stop();

        Assert.Equal(SealStoreOutcome.Ok, outcome);
        Assert.Equal(itemsBefore, SnapshotOf(store.Paths.Items));
        Assert.NotEqual(wrapBefore, SHA256.HashData(File.ReadAllBytes(store.Paths.KeyWrap)));
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"change took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void AfterChangePassword_TheOldOneIsDeadAndTheNewOneWorks()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out var original);

        Assert.Equal(SealStoreOutcome.Ok, store.ChangePassword(SpacePassword, NewSpacePassword));

        Assert.Equal(SealStoreOutcome.WrongPassword, store.TryUnlock(SpacePassword, out _, out _));
        Assert.False(store.MatchesPassword(SpacePassword));

        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(NewSpacePassword, out var reopened, out _));
        Assert.Equal(original, reopened);
        Assert.True(store.MatchesPassword(NewSpacePassword));
    }

    [Fact]
    public void ChangePassword_RejectsAWrongOldPasswordWithoutTouchingAnything()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out _);

        var before = SnapshotOf(dir.Root);
        Assert.Equal(SealStoreOutcome.WrongPassword, store.ChangePassword("N0t!ThePassw0rd", NewSpacePassword));
        Assert.Equal(before, SnapshotOf(dir.Root));
    }

    [Fact]
    public void ChangePassword_RejectsAWeakNewPassword()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out _);

        Assert.Equal(SealStoreOutcome.WeakPassword, store.ChangePassword(SpacePassword, "short"));
        Assert.True(store.MatchesPassword(SpacePassword));
    }



    [Fact]
    public void TruncatedKeyWrap_IsReportedAsCorruptNotAsAWrongPassword()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out _);

        var bytes = File.ReadAllBytes(store.Paths.KeyWrap);
        File.WriteAllBytes(store.Paths.KeyWrap, bytes[..50]);

        Assert.Equal(SealStoreOutcome.Corrupt, store.TryUnlock(SpacePassword, out var vmk, out _));
        Assert.Empty(vmk);
    }

    [Fact]
    public void ForeignFileAsKeyWrap_IsReportedAsCorrupt()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, null, out _);

        File.WriteAllBytes(store.Paths.KeyWrap, RandomNumberGenerator.GetBytes(100));

        Assert.Equal(SealStoreOutcome.Corrupt, store.TryUnlock(SpacePassword, out _, out _));
    }



    [Fact]
    public void Contains_RecognisesEverythingUnderTheRoot()
    {
        using var dir = new TempDir();
        var paths = new SealSpacePaths(dir.Root);

        Assert.True(paths.Contains(paths.Root));
        Assert.True(paths.Contains(paths.ItemPath("00ff")));
        Assert.True(paths.Contains(Path.Combine(paths.Items, "..", "index.nseal")));
        Assert.False(paths.Contains(@"C:\Windows\System32\notepad.exe"));
    }

    [Fact]
    public void DeleteAll_RemovesTheWholeTree()
    {
        using var dir = new TempDir();
        var store = Store(dir);
        store.Initialize(SpacePassword, "hint", out _);
        File.WriteAllBytes(store.Paths.ItemPath("aa"), new byte[10]);

        Assert.Equal(SealStoreOutcome.Ok, store.DeleteAll());

        Assert.False(Directory.Exists(store.Paths.Root));
        Assert.Equal(SealStoreOutcome.NotInitialized, store.TryUnlock(SpacePassword, out _, out _));
    }




    private static string SnapshotOf(string root)
    {
        if (!Directory.Exists(root)) return "<missing>";

        var builder = new StringBuilder();
        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            builder.Append("D ").Append(Path.GetRelativePath(root, directory)).Append('\n');

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            builder.Append("F ").Append(Path.GetRelativePath(root, file)).Append(' ')
                .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))).Append('\n');
        }

        return builder.ToString();
    }
}
