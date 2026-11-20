using System.Buffers.Binary;
using System.Security.Cryptography;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealBundleWriterTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0xA0 + i)).ToArray();

    private const long CreatedAt = 1_790_000_000;
    private const int AppVersion = 13_000;

    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-bundle-" + Guid.NewGuid().ToString("N"));
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

    private static string WriteContainer(string path, int size, int seed)
    {
        var content = new byte[size];
        new Random(seed).NextBytes(content);
        File.WriteAllBytes(path, content);
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private static SealedItem Item(string id, string digest, string name)
        => new()
        {
            Id = id,
            DisplayName = name,
            OriginalName = name,
            SourcePath = @"D:\photos\" + name,
            Size = 1,
            SealedAtUtc = CreatedAt,
            Kind = SealedItemKind.Normal,
            ContentSha256 = digest,
        };

    private static SealBundleOptions Options(
        SealIndex index,
        IReadOnlyList<SealBundleItem> items,
        byte kind = SealBundleWriter.KindItems,
        byte[]? backup = null,
        byte[]? bundleId = null,
        long createdUtc = CreatedAt,
        int appVersion = AppVersion)
        => new(Vmk, index, items, "bundle password", kind, createdUtc, appVersion, backup, bundleId);

    [Fact]
    public async Task TheHeaderCarriesTheDocumentedOffsets()
    {
        using var dir = new TempDir();
        var id = NewId();
        var container = dir.PathOf(id + ".novaseal");
        var digest = WriteContainer(container, 128, 1);

        var index = SealIndex.Empty.WithItem(Item(id, digest, "a.txt"));
        var result = await SealBundleWriter.WriteAsync(
            dir.PathOf("a.novabundle"), Options(index, [new SealBundleItem(id, container)]), FastKdf, default);

        var file = File.ReadAllBytes(dir.PathOf("a.novabundle"));
        Assert.Equal(SealBundleWriter.HeaderSize, 64);

        Assert.Equal("NVSEALBK", System.Text.Encoding.ASCII.GetString(file, 0, 8));
        Assert.Equal(SealBundleWriter.CurrentVersion, file[8]);
        Assert.Equal(SealBundleWriter.KindItems, file[9]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(10, 2)));
        Assert.Equal(result.BundleId, file.AsSpan(12, 16).ToArray());
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(28, 4)));
        Assert.Equal(result.IndexLength, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(32, 4)));
        Assert.Equal(result.BodyLength, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(36, 8)));
        Assert.Equal(CreatedAt, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(44, 8)));
        Assert.Equal(AppVersion, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(52, 4)));
        Assert.Equal(0, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(56, 8)));

        Assert.Equal(1, result.ItemCount);
        Assert.Equal(file.Length, result.FileSize);
    }

    [Fact]
    public async Task KindOneWithThreeItemsMatchesTheSizeAndEveryByte()
    {
        using var dir = new TempDir();
        var ids = new List<string>();
        var items = new List<SealBundleItem>();
        var parts = new List<(string Id, byte[] Bytes)>();
        var index = SealIndex.Empty;

        for (var i = 0; i < 3; i++)
        {
            var id = NewId();
            ids.Add(id);
            var container = dir.PathOf(id + ".novaseal");
            var digest = WriteContainer(container, 100 + i * 37, 10 + i);
            var bytes = File.ReadAllBytes(container);

            parts.Add((id, bytes));
            items.Add(new SealBundleItem(id, container));
            index = index.WithItem(Item(id, digest, $"item-{i}.bin"));
        }

        var path = dir.PathOf("three.novabundle");
        var result = await SealBundleWriter.WriteAsync(path, Options(index, items), FastKdf, default);
        var file = File.ReadAllBytes(path);

        var expected = 64 + SealKeyWrap.FileSize + result.IndexLength
                       + parts.Sum(p => SealBundleWriter.BlockLengthPrefixSize + (long)p.Bytes.Length)
                       + SealBundleWriter.TailSize;

        Assert.Equal(expected, file.Length);
        Assert.Equal(3, result.ItemCount);


        Assert.Equal(
            SHA256.HashData(file.AsSpan(0, file.Length - SealBundleWriter.TailSize)),
            file.AsSpan(file.Length - SealBundleWriter.TailSize).ToArray());


        var keywrap = file.AsSpan(64, SealKeyWrap.FileSize).ToArray();
        Assert.True(SealKeyWrap.LooksLikeKeyWrap(keywrap));
        Assert.True(SealKeyWrap.TryUnwrapFromBundle(
            keywrap, "bundle password", SealBundleWriter.KindItems, result.BundleId, out var vmk, out _));


        var indexBlock = file.AsSpan(64 + SealKeyWrap.FileSize, result.IndexLength).ToArray();
        var indexNonce = indexBlock.AsSpan(0, SealFormat.NonceSize).ToArray();


        var expectedAad = new byte[16 + 32 + 4];
        result.BundleId.CopyTo(expectedAad, 0);
        SHA256.HashData(keywrap).CopyTo(expectedAad, 16);
        BinaryPrimitives.WriteInt32LittleEndian(expectedAad.AsSpan(48, 4), 3);

        var indexJson = CryptoService.DecryptGcmWithNonce(
            indexBlock.AsSpan(SealFormat.NonceSize).ToArray(), vmk, indexNonce, expectedAad);

        Assert.Contains("\"items\"", System.Text.Encoding.UTF8.GetString(indexJson), StringComparison.Ordinal);
        Assert.Contains(ids[0], System.Text.Encoding.UTF8.GetString(indexJson), StringComparison.Ordinal);


        var offset = 64 + SealKeyWrap.FileSize + result.IndexLength;
        foreach (var (id, bytes) in parts)
        {
            var length = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(offset, 8));
            Assert.Equal((ulong)bytes.Length, length);
            offset += 8;

            Assert.Equal(bytes, file.AsSpan(offset, bytes.Length).ToArray());
            offset += bytes.Length;
            Assert.Equal(32, id.Length);
        }

        Assert.Equal(file.Length - SealBundleWriter.TailSize, offset);
    }

    [Fact]
    public async Task KindTwoPutsTheBackupBlockFirstAndItStillImports()
    {
        using var dir = new TempDir();

        var store = new NovaraStore(dir.PathOf("source.novadb"));
        store.Load();
        store.Database.MemoEntries.Add(new Novara.Models.MemoEntry { Name = "bundle backup", Type = "自定义" });
        var backupPath = dir.PathOf("backup.novaenc");
        Assert.True(store.ExportBackupEncrypted(backupPath, "backup-pw", includeFilePathEntries: false));
        var backup = File.ReadAllBytes(backupPath);

        var id = NewId();
        var container = dir.PathOf(id + ".novaseal");
        var digest = WriteContainer(container, 256, 7);
        var index = SealIndex.Empty.WithItem(Item(id, digest, "b.txt"));

        var path = dir.PathOf("with-backup.novabundle");
        var result = await SealBundleWriter.WriteAsync(
            path,
            Options(index, [new SealBundleItem(id, container)], SealBundleWriter.KindItemsWithBackup, backup),
            FastKdf,
            default);

        var file = File.ReadAllBytes(path);
        Assert.Equal(SealBundleWriter.KindItemsWithBackup, file[9]);

        var offset = 64 + SealKeyWrap.FileSize + result.IndexLength;
        var length = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(offset, 8));
        Assert.Equal((ulong)backup.Length, length);

        var embedded = file.AsSpan(offset + 8, (int)length).ToArray();
        Assert.Equal(backup, embedded);


        var extracted = dir.PathOf("extracted.novaenc");
        File.WriteAllBytes(extracted, embedded);

        var target = new NovaraStore(dir.PathOf("target.novadb"));
        var loaded = target.ImportBackup(extracted, "backup-pw");
        Assert.Equal(LoadStatus.Ok, loaded.Status);
    }

    [Fact]
    public async Task AnEmptyBundleIsLegal()
    {
        using var dir = new TempDir();
        var path = dir.PathOf("empty.novabundle");

        var result = await SealBundleWriter.WriteAsync(
            path, Options(SealIndex.Empty, []), FastKdf, default);

        var file = File.ReadAllBytes(path);
        Assert.Equal(0, result.ItemCount);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(28, 4)));
        Assert.Equal(64 + SealKeyWrap.FileSize + result.IndexLength + SealBundleWriter.TailSize, file.Length);
        Assert.Equal(
            SHA256.HashData(file.AsSpan(0, file.Length - SealBundleWriter.TailSize)),
            file.AsSpan(file.Length - SealBundleWriter.TailSize).ToArray());
    }

    [Fact]
    public async Task CancellingLeavesNeitherOutputNorTempFile()
    {
        using var dir = new TempDir();
        var id = NewId();
        var container = dir.PathOf(id + ".novaseal");
        var digest = WriteContainer(container, 64, 3);
        var index = SealIndex.Empty.WithItem(Item(id, digest, "c.txt"));

        var path = dir.PathOf("cancelled.novabundle");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SealBundleWriter.WriteAsync(
            path, Options(index, [new SealBundleItem(id, container)]), FastKdf, cts.Token));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task TheCreatedStampAndAppVersionRoundTrip()
    {
        using var dir = new TempDir();
        var id = NewId();
        var container = dir.PathOf(id + ".novaseal");
        var digest = WriteContainer(container, 32, 5);
        var index = SealIndex.Empty.WithItem(Item(id, digest, "d.txt"));

        var path = dir.PathOf("stamped.novabundle");
        await SealBundleWriter.WriteAsync(
            path,
            Options(index, [new SealBundleItem(id, container)], createdUtc: 1_800_000_123, appVersion: 13_000),
            FastKdf,
            default);

        var file = File.ReadAllBytes(path);
        Assert.Equal(1_800_000_123, BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(44, 8)));
        Assert.Equal(13_000, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(52, 4)));
    }

    [Fact]
    public async Task AnExistingTargetIsNeverOverwritten()
    {
        using var dir = new TempDir();
        var id = NewId();
        var container = dir.PathOf(id + ".novaseal");
        var digest = WriteContainer(container, 16, 6);
        var index = SealIndex.Empty.WithItem(Item(id, digest, "e.txt"));

        var path = dir.PathOf("taken.novabundle");
        File.WriteAllText(path, "do not replace me");

        await Assert.ThrowsAsync<IOException>(() => SealBundleWriter.WriteAsync(
            path, Options(index, [new SealBundleItem(id, container)]), FastKdf, default));

        Assert.Equal("do not replace me", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
