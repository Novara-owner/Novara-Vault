using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealTakeOperationTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x80 + i)).ToArray();

    private const long StartedAt = 1_790_000_000;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(StartedAt);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-take-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) {  }
        }
    }

    private static SealSpacePaths NewPaths(TempDir dir)
    {
        var paths = new SealSpacePaths(Path.Combine(dir.Root, "seal"));
        paths.EnsureCreated();
        return paths;
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();


    private static string WriteContainer(SealSpacePaths paths, string id, byte[] plaintext, bool folderArchive = false)
    {
        var contentKey = RandomNumberGenerator.GetBytes(SealFormat.KeySize);
        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(id),
            (ulong)plaintext.Length,
            StartedAt,
            SealFormat.MinChunkSize,
            Vmk,
            contentKey,
            folderArchive ? SealFormat.FlagFolderArchive : (byte)0);

        using var destination = new FileStream(
            paths.ItemPath(id), FileMode.Create, FileAccess.Write, FileShare.None);

        var result = SealContainerWriter.WriteAsync(
                new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        CryptographicOperations.ZeroMemory(contentKey);
        return Convert.ToHexString(result.PlaintextSha256).ToLowerInvariant();
    }

    private static SealedItem MakeItem(
        SealSpacePaths paths,
        string name,
        byte[] plaintext,
        bool folderArchive = false,
        int? folderEntryCount = null,
        long? expiryUtc = null)
    {
        var id = NewId();
        var digest = WriteContainer(paths, id, plaintext, folderArchive);

        return new SealedItem
        {
            Id = id,
            DisplayName = name,
            OriginalName = name,
            SourcePath = @"D:\originals\" + name,
            Size = (ulong)plaintext.Length,
            SealedAtUtc = StartedAt,
            Kind = expiryUtc is null ? SealedItemKind.Normal : SealedItemKind.Capsule,
            ExpiryUtc = expiryUtc,
            ContentSha256 = digest,
            FolderEntryCount = folderEntryCount,
        };
    }

    private static void SeedIndex(SealSpacePaths paths, params SealedItem[] items)
    {
        var index = SealIndex.Empty;
        foreach (var item in items) index = index.WithItem(item);
        index.Save(paths.Index, Vmk);
    }

    private sealed class RecordingPorts : ISealTakePorts
    {
        public List<string> Calls { get; } = [];

        public SealIndex Index { get; private set; } = SealIndex.Empty;

        public SealJournal Journal { get; private set; } = SealJournal.Empty;

        public void Seed(SealIndex index)
        {
            Index = index;
        }

        public void AppendJournal(SealJournalRecord record)
        {
            Journal = Journal.Append(record);
            Calls.Add("journal:" + record.Phase);
        }

        public void RemoveJournal(string opId)
        {
            Journal = Journal.Remove(opId);
            Calls.Add("journal:remove");
        }

        public void SaveIndex(SealIndex index)
        {
            Index = index;
            Calls.Add("index:" + index.Items.Count);
        }

        public bool DeleteContainer(string path)
        {
            Calls.Add("delete");
            return true;
        }

        public void MarkVerified(string itemId) => Calls.Add("verify");
    }

    [Fact]
    public async Task ANormalTakeRestoresTheBytesUnderTheOriginalName()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var content = Encoding.UTF8.GetBytes("还给我的内容");
        var item = MakeItem(paths, "笔记.txt", content);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        Assert.True(result.Taken);
        Assert.Equal(Path.Combine(outDir, "笔记.txt"), result.TargetPath);
        Assert.Equal(content, File.ReadAllBytes(result.TargetPath!));
        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.False(File.Exists(paths.ItemPath(item.Id)));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
        Assert.Equal(StartedAt, SealIndex.Load(paths.Index, Vmk).ObservedMaxUtc);
    }

    [Fact]
    public async Task ADamagedContainerThrowsAndKeepsTheEntry()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "损坏.bin", [1, 2, 3, 4, 5, 6, 7, 8]);
        SeedIndex(paths, item);


        var container = paths.ItemPath(item.Id);
        var bytes = File.ReadAllBytes(container);
        bytes[SealFormat.HeaderSize + SealFormat.BlockLengthPrefixSize + SealFormat.NonceSize + 1] ^= 0xFF;
        File.WriteAllBytes(container, bytes);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var error = await Assert.ThrowsAsync<SealCorruptException>(
            () => operation.RunAsync(new SealTakeRequest(item.Id, outDir)));

        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.True(File.Exists(container));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task AnEmptyContainerComesBackAsAnEmptyFile()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "空.txt", []);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        Assert.True(result.Taken);
        Assert.Empty(File.ReadAllBytes(result.TargetPath!));
        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);
    }

    [Fact]
    public async Task AutoNumberPicksTheNextFreeName()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var first = MakeItem(paths, "a.txt", [1]);
        var second = MakeItem(paths, "a.txt", [2]);
        SeedIndex(paths, first, second);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);

        var one = await operation.RunAsync(new SealTakeRequest(first.Id, outDir));
        var two = await operation.RunAsync(new SealTakeRequest(second.Id, outDir));

        Assert.Equal(Path.Combine(outDir, "a.txt"), one.TargetPath);
        Assert.Equal(Path.Combine(outDir, "a (1).txt"), two.TargetPath);
        Assert.True(File.Exists(Path.Combine(outDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "a (1).txt")));
    }

    [Fact]
    public async Task CancelRefusesToOverwriteAnything()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [1, 2, 3]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "a.txt"), [9]);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, SealTakeConflictPolicy.Cancel));

        Assert.Equal(SealTakeStatus.Cancelled, result.Status);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(outDir, "a.txt")));
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.True(File.Exists(paths.ItemPath(item.Id)));
    }

    [Fact]
    public async Task CancelOnAFreeNameAlsoWritesNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [1, 2, 3]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, SealTakeConflictPolicy.Cancel));


        Assert.Equal(SealTakeStatus.Cancelled, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.True(File.Exists(paths.ItemPath(item.Id)));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task RenameUsesTheNameTheCallerChose()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [4, 5]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "a.txt"), [9]);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, SealTakeConflictPolicy.Rename, TargetName: "改名.txt"));

        Assert.True(result.Taken);
        Assert.Equal(Path.Combine(outDir, "改名.txt"), result.TargetPath);
        Assert.Equal([4, 5], File.ReadAllBytes(result.TargetPath!));
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(outDir, "a.txt")));
    }

    [Fact]
    public async Task ARelativeNameFromTheCallerIsRefused()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [4, 5]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, SealTakeConflictPolicy.Rename, TargetName: Path.Combine("..", "escape.txt")));


        Assert.Equal(SealTakeStatus.TargetUnavailable, result.Status);
        Assert.Null(result.TargetPath);
        Assert.False(File.Exists(Path.Combine(dir.Root, "escape.txt")));
        Assert.True(Directory.Exists(outDir));
    }





    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("name.")]
    [InlineData("a:b.txt")]
    public async Task ANonPortableNameFromTheCallerIsRefused(string targetName)
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [4, 5]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, SealTakeConflictPolicy.Rename, TargetName: targetName));

        Assert.Equal(SealTakeStatus.TargetUnavailable, result.Status);
        Assert.Null(result.TargetPath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
    }

    [Fact]
    public async Task AnUnavailableTargetDirectoryWritesNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "a.txt", [1]);
        SeedIndex(paths, item);


        var notADirectory = Path.Combine(dir.Root, "not-a-directory");
        File.WriteAllText(notADirectory, "x");

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, notADirectory));

        Assert.Equal(SealTakeStatus.TargetUnavailable, result.Status);
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.True(File.Exists(paths.ItemPath(item.Id)));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task ALockedCapsuleTouchesNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var expiry = StartedAt + 30 * 86_400;
        var item = MakeItem(paths, "胶囊.txt", [1], expiryUtc: expiry);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        Assert.Equal(SealTakeStatus.Locked, result.Status);
        Assert.Equal(expiry, result.LockedUntilUtc);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
        Assert.True(File.Exists(paths.ItemPath(item.Id)));
    }

    [Fact]
    public async Task ASuspiciousClockRefusesTheCapsule()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var expiry = StartedAt + 30 * 86_400;
        var item = MakeItem(paths, "胶囊.txt", [1], expiryUtc: expiry);


        SealIndex.Empty.WithItem(item).WithObservedMax(StartedAt + 48 * 3600).Save(paths.Index, Vmk);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        Assert.Equal(SealTakeStatus.ClockSuspected, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
        Assert.Single(SealIndex.Load(paths.Index, Vmk).Items);
    }

    [Fact]
    public async Task AFolderRestoresItsStructure()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var (item, entryCount) = MakeFolderItem(dir, paths);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        Assert.True(result.Taken);
        var restored = Path.Combine(outDir, "项目资料");
        Assert.True(Directory.Exists(restored));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(restored, "a.txt")));
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(Path.Combine(restored, "子目录", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(restored, "空目录")));
        Assert.Equal(4, entryCount);
        Assert.False(File.Exists(Path.Combine(outDir, $".novara-restore-{item.Id}.zip")));
        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);

        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task AFolderCanComeOutAsAnArchive()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var (item, entryCount) = MakeFolderItem(dir, paths);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);
        var result = await operation.RunAsync(
            new SealTakeRequest(item.Id, outDir, ExtractAsArchive: true));

        Assert.True(result.Taken);
        Assert.Equal(Path.Combine(outDir, "项目资料.zip"), result.TargetPath);

        using var archive = ZipFile.OpenRead(result.TargetPath!);
        Assert.Equal(entryCount, archive.Entries.Count);
    }

    [Fact]
    public async Task ADamagedArchiveKeepsTheTemporaryZipAndReportsExtractFailed()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);



        var item = MakeItem(paths, "项目资料", Encoding.UTF8.GetBytes("这不是一个 zip"), folderArchive: true, folderEntryCount: 3);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var operation = new SealTakeOperation(paths, Vmk, clock: () => Now);

        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));

        var temporary = Path.Combine(outDir, $".novara-restore-{item.Id}.zip");
        Assert.Equal(SealTakeStatus.ExtractFailed, result.Status);
        Assert.Equal(temporary, result.TargetPath);
        Assert.True(File.Exists(temporary));
        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);



        var journal = SealJournal.Load(paths.Journal, Vmk);
        Assert.NotEmpty(journal.Records);
        var plan = SealRecovery.Plan(journal, SealIndex.Load(paths.Index, Vmk),
            new SealFileProbe(paths, SealIndex.Empty));
        Assert.Equal(SealRecoveryActionKind.AskCleanupPartialTarget, Assert.Single(plan.Actions).Kind);
    }

    [Fact]
    public async Task TheSequenceIsTakeBeginVerifyTakenIndexDelete()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var item = MakeItem(paths, "order.bin", [7, 7, 7]);
        SeedIndex(paths, item);

        var outDir = Path.Combine(dir.Root, "out");
        Directory.CreateDirectory(outDir);

        var ports = new RecordingPorts();
        ports.Seed(SealIndex.Load(paths.Index, Vmk));

        var operation = new SealTakeOperation(paths, Vmk, ports, () => Now);
        var result = await operation.RunAsync(new SealTakeRequest(item.Id, outDir));
        Assert.True(result.Taken);

        Assert.Equal(
            [
                "journal:take_begin",
                "verify",
                "journal:taken",
                "index:0",
                "delete",
                "journal:remove",
            ],
            ports.Calls);

        var verify = ports.Calls.IndexOf("verify");
        var taken = ports.Calls.IndexOf("journal:taken");
        var index = ports.Calls.IndexOf("index:0");
        var delete = ports.Calls.IndexOf("delete");

        Assert.True(verify < taken);
        Assert.True(index < delete);
    }

    private static (SealedItem Item, int EntryCount) MakeFolderItem(TempDir dir, SealSpacePaths paths)
    {
        var source = Path.Combine(dir.Root, "项目资料");
        Directory.CreateDirectory(Path.Combine(source, "子目录"));
        Directory.CreateDirectory(Path.Combine(source, "空目录"));
        File.WriteAllBytes(Path.Combine(source, "a.txt"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(source, "子目录", "b.txt"), [4, 5, 6, 7]);

        var zip = Path.Combine(dir.Root, "pack.zip");
        var pack = SealFolderPack.Pack(source, zip);
        var bytes = File.ReadAllBytes(zip);
        File.Delete(zip);

        var item = MakeItem(paths, "项目资料", bytes, folderArchive: true, folderEntryCount: pack.EntryCount);
        return (item, pack.EntryCount);
    }
}
