using System.Buffers.Binary;
using System.Security.Cryptography;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealBundlePeekTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x70 + i)).ToArray();

    private const long CreatedAt = 1_791_000_000;
    private const int AppVersion = 13_000;

    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-peek-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();



    private static string WriteContainer(string path, string id, byte[] plaintext, byte[] vmk)
    {
        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x31 + i)).ToArray();

        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(id), (ulong)plaintext.Length, CreatedAt, SealFormat.MinChunkSize, vmk, contentKey);

        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SealContainerWriter.WriteAsync(
                new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        return Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
    }

    private static SealedItem Item(string id, string digest, string name)
        => new()
        {
            Id = id,
            DisplayName = name,
            OriginalName = name,
            SourcePath = @"D:\docs\" + name,
            Size = 1,
            SealedAtUtc = CreatedAt,
            Kind = SealedItemKind.Normal,
            ContentSha256 = digest,
        };


    private static async Task<(string Path, byte[] Backup, int ItemCount)> WriteBundleAsync(
        TempDir dir, string fileName, byte kind, int itemCount = 2)
    {
        var items = new List<SealBundleItem>();
        var index = SealIndex.Empty;

        for (var i = 0; i < itemCount; i++)
        {
            var id = NewId();
            var container = dir.PathOf(id + ".novaseal");
            var plaintext = System.Text.Encoding.UTF8.GetBytes($"peek payload {i}");
            var digest = WriteContainer(container, id, plaintext, Vmk);
            index = index.WithItem(Item(id, digest, $"f{i}.bin"));
            items.Add(new SealBundleItem(id, container));
        }

        byte[]? backup = null;
        if (kind == SealBundleWriter.KindItemsWithBackup)
        {
            var store = new NovaraStore(dir.PathOf("source.novadb"));
            store.Load();
            store.Database.MemoEntries.Add(new MemoEntry { Name = "peek backup", Type = "自定义" });
            var backupPath = dir.PathOf("backup.novaenc");
            Assert.True(store.ExportBackupEncrypted(backupPath, "backup-pw", includeFilePathEntries: false));
            backup = File.ReadAllBytes(backupPath);
        }

        var path = dir.PathOf(fileName);
        await SealBundleWriter.WriteAsync(
            path,
            new SealBundleOptions(Vmk, index, items, "bundle-pw", kind, CreatedAt, AppVersion, backup),
            FastKdf,
            default);

        return (path, backup ?? [], itemCount);
    }

    [Fact]
    public async Task ThePeekAgreesWithTheFullOpenOnBothKinds()
    {
        using var dir = new TempDir();

        foreach (var kind in new[] { SealBundleWriter.KindItems, SealBundleWriter.KindItemsWithBackup })
        {
            var (path, _, count) = await WriteBundleAsync(dir, $"k{kind}.novabundle", kind);

            var peek = SealBundleReader.Peek(path);
            Assert.Equal(SealBundleReadStatus.Ok, peek.Status);
            Assert.NotNull(peek.Peek);
            Assert.Equal(kind, peek.Peek!.Kind);
            Assert.Equal(count, peek.Peek.ItemCount);
            Assert.Equal(CreatedAt, peek.Peek.CreatedUtc);
            Assert.Equal(AppVersion, peek.Peek.AppVersion);
            Assert.Equal(kind == SealBundleWriter.KindItemsWithBackup, peek.Peek.HasBackup);


            var opened = await SealBundleReader.OpenAsync(path, "bundle-pw");
            Assert.Equal(SealBundleReadStatus.Ok, opened.Status);
            Assert.Equal(peek.Peek.Kind, opened.Bundle!.Kind);
        }
    }

    [Fact]
    public async Task APeekNeedsNoPasswordAndLeaksNoKey()
    {
        using var dir = new TempDir();
        var (path, _, _) = await WriteBundleAsync(dir, "nopw.novabundle", SealBundleWriter.KindItemsWithBackup);


        var peek = SealBundleReader.Peek(path);
        Assert.Equal(SealBundleReadStatus.Ok, peek.Status);


        var opened = await SealBundleReader.OpenAsync(path, "not-the-password");
        Assert.Equal(SealBundleReadStatus.WrongPassword, opened.Status);
    }

    [Fact]
    public void AFileThatIsNotAPackageIsRefusedByThePeek()
    {
        using var dir = new TempDir();
        var path = dir.PathOf("random.bin");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(200));

        Assert.Equal(SealBundleReadStatus.Corrupt, SealBundleReader.Peek(path).Status);


        var tiny = dir.PathOf("tiny.bin");
        File.WriteAllBytes(tiny, new byte[10]);
        Assert.Equal(SealBundleReadStatus.Corrupt, SealBundleReader.Peek(tiny).Status);

        Assert.Equal(SealBundleReadStatus.IoError, SealBundleReader.Peek(dir.PathOf("missing.bin")).Status);
    }

    [Fact]
    public async Task ThePeekRefusesTheSameTamperedHeadersTheReaderRefuses()
    {
        using var dir = new TempDir();
        var (path, _, _) = await WriteBundleAsync(dir, "tamper.novabundle", SealBundleWriter.KindItems, itemCount: 1);
        var original = File.ReadAllBytes(path);


        var future = (byte[])original.Clone();
        future[SealBundleWriter.VersionOffset] = SealBundleWriter.CurrentVersion + 1;
        var futurePath = dir.PathOf("future.novabundle");
        File.WriteAllBytes(futurePath, future);
        Assert.Equal(SealBundleReadStatus.VersionUnsupported, SealBundleReader.Peek(futurePath).Status);


        var badKind = (byte[])original.Clone();
        badKind[SealBundleWriter.KindOffset] = 7;
        var badKindPath = dir.PathOf("badkind.novabundle");
        File.WriteAllBytes(badKindPath, badKind);
        Assert.Equal(SealBundleReadStatus.Corrupt, SealBundleReader.Peek(badKindPath).Status);


        var reserved = (byte[])original.Clone();
        reserved[SealBundleWriter.ReservedOffset] = 1;
        var reservedPath = dir.PathOf("reserved.novabundle");
        File.WriteAllBytes(reservedPath, reserved);
        Assert.Equal(SealBundleReadStatus.Corrupt, SealBundleReader.Peek(reservedPath).Status);


        var shortBody = (byte[])original.Clone();
        var bodyLength = BinaryPrimitives.ReadInt64LittleEndian(shortBody.AsSpan(SealBundleWriter.BodyLengthOffset, 8));
        BinaryPrimitives.WriteInt64LittleEndian(
            shortBody.AsSpan(SealBundleWriter.BodyLengthOffset, 8), bodyLength + 1);
        var shortBodyPath = dir.PathOf("shortbody.novabundle");
        File.WriteAllBytes(shortBodyPath, shortBody);
        Assert.Equal(SealBundleReadStatus.Corrupt, SealBundleReader.Peek(shortBodyPath).Status);
    }

    [Fact]
    public async Task TheBackupBlockComesOutAsAWorkingEncryptedBackup()
    {
        using var dir = new TempDir();
        var (path, backup, _) = await WriteBundleAsync(
            dir, "with-backup.novabundle", SealBundleWriter.KindItemsWithBackup);

        var extracted = dir.PathOf("extracted.novaenc");
        var status = await SealBundleReader.ExtractBackupAsync(path, "bundle-pw", extracted);

        Assert.Equal(SealBundleReadStatus.Ok, status);
        Assert.Equal(backup, File.ReadAllBytes(extracted));


        var target = new NovaraStore(dir.PathOf("target.novadb"));
        var loaded = target.ImportBackup(extracted, "backup-pw");
        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Contains(target.Database.MemoEntries, m => m.Name == "peek backup");
    }

    [Fact]
    public async Task AKindOnePackageHasNoBackupToExtract()
    {
        using var dir = new TempDir();
        var (path, _, _) = await WriteBundleAsync(dir, "kind1.novabundle", SealBundleWriter.KindItems, itemCount: 1);

        var extracted = dir.PathOf("nothing.novaenc");
        var status = await SealBundleReader.ExtractBackupAsync(path, "bundle-pw", extracted);

        Assert.Equal(SealBundleReadStatus.Corrupt, status);
        Assert.False(File.Exists(extracted));
    }

    [Fact]
    public async Task ExtractionRefusesAWrongPasswordAndLeavesNoFile()
    {
        using var dir = new TempDir();
        var (path, _, _) = await WriteBundleAsync(dir, "wrongpw.novabundle", SealBundleWriter.KindItemsWithBackup);

        var extracted = dir.PathOf("never.novaenc");
        var status = await SealBundleReader.ExtractBackupAsync(path, "wrong", extracted);

        Assert.Equal(SealBundleReadStatus.WrongPassword, status);
        Assert.False(File.Exists(extracted));
    }

    [Fact]
    public async Task ATamperedPackageNeverHandsOutABackupBlock()
    {
        using var dir = new TempDir();
        var (path, _, _) = await WriteBundleAsync(dir, "tailed.novabundle", SealBundleWriter.KindItemsWithBackup);


        var bytes = File.ReadAllBytes(path);
        bytes[SealBundleWriter.HeaderSize + SealKeyWrap.FileSize + 4] ^= 0xFF;
        var tampered = dir.PathOf("tailed-bad.novabundle");
        File.WriteAllBytes(tampered, bytes);

        var extracted = dir.PathOf("refused.novaenc");
        var status = await SealBundleReader.ExtractBackupAsync(tampered, "bundle-pw", extracted);

        Assert.Equal(SealBundleReadStatus.Corrupt, status);
        Assert.False(File.Exists(extracted));
    }
}
