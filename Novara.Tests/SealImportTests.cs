using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

[Collection("CoreSequential")]
public class SealImportTests
{
    private const long SealedAtBase = 1_790_000_000;
    private const string BundlePassword = "Bundle-9-tunnel-42";
    private const int AppVersion = 13_000;

    private static readonly SealKdfParams FastKdf = SealKdfParams.Create(8 * 1024, 1);

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {  }
        }
    }

    private static SealSpacePaths NewSpace(TempDir dir, string name)
    {
        var paths = new SealSpacePaths(dir.PathOf(name));
        paths.EnsureCreated();
        return paths;
    }

    private static byte[] VmkOf(byte seed)
        => Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(seed + i)).ToArray();

    private static string HexId(string seed)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32].ToLowerInvariant();

    private static string WriteContainer(string path, string id, byte[] plaintext, long sealedAt, byte[] vmk)
    {
        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x70 + i)).ToArray();

        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(id), (ulong)plaintext.Length, sealedAt, SealFormat.MinChunkSize, vmk, contentKey);

        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SealContainerWriter.WriteAsync(
                new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        return Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();
    }

    private sealed record BundleFixture(string Path, SealIndex Index, List<(string Id, byte[] Plain)> Items, byte[] Vmk);

    private static BundleFixture BuildBundle(
        TempDir dir,
        string name,
        int count,
        byte seed,
        long sealedAtBase = SealedAtBase,
        long? capsuleExpiry = null,
        byte kind = SealBundleWriter.KindItems,
        byte[]? backup = null,
        string? displayName = null,
        byte[]? bundleId = null)
    {
        var vmk = VmkOf(seed);
        var index = SealIndex.Empty;
        var refs = new List<SealBundleItem>();
        var items = new List<(string Id, byte[] Plain)>();

        for (var i = 0; i < count; i++)
        {
            var id = HexId($"{name}-{i}");
            var plaintext = Encoding.UTF8.GetBytes($"{name} payload {i}" + new string('x', 40));
            var container = dir.PathOf($"{name}-{i}.novaseal");

            var digest = WriteContainer(container, id, plaintext, sealedAtBase + i, vmk);

            index = index.WithItem(new SealedItem
            {
                Id = id,
                DisplayName = displayName ?? $"{name}-{i}.bin",
                OriginalName = displayName ?? $"{name}-{i}.bin",
                SourcePath = $@"D:\src\{name}-{i}.bin",
                Size = (ulong)plaintext.Length,
                SealedAtUtc = sealedAtBase + i,
                Kind = capsuleExpiry is null ? SealedItemKind.Normal : SealedItemKind.Capsule,
                ExpiryUtc = capsuleExpiry,
                ContentSha256 = digest,
            });

            refs.Add(new SealBundleItem(id, container));
            items.Add((id, plaintext));
        }

        var path = dir.PathOf(name + ".novabundle");
        SealBundleWriter.WriteAsync(
                path,
                new SealBundleOptions(vmk, index, refs, BundlePassword, kind, SealedAtBase, AppVersion, backup, bundleId),
                FastKdf,
                default)
            .GetAwaiter().GetResult();

        return new BundleFixture(path, index, items, vmk);
    }

    private static byte[] Unwrap(string containerPath, byte[] vmk)
    {
        var bytes = File.ReadAllBytes(containerPath);
        Assert.True(SealContainerHeader.TryRead(bytes, out var header, out _));
        Assert.True(SealKeySlot.TryUnwrap(
            vmk, header.KeySlot, bytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize), out var contentKey, out _));

        var plain = new MemoryStream();
        SealContainerReader.ReadAsync(
                new MemoryStream(bytes, writable: false),
                contentKey,
                (chunk, _) =>
                {
                    plain.Write(chunk.Span);
                    return Task.CompletedTask;
                })
            .GetAwaiter().GetResult();

        return plain.ToArray();
    }

    [Fact]
    public async Task ImportingIntoAFreshSpaceCreatesItAndKeepsTheBytes()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "fresh", 3, 0x10);
        var target = NewSpace(dir, "target");

        var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target);

        Assert.Equal(SealImportStatus.Ok, result.Status);
        Assert.Equal(3, result.Counts.Imported);
        Assert.Empty(result.FailedItems);

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(BundlePassword, out var vmk, out _));

        try
        {
            var index = SealIndex.Load(target.Index, vmk);
            Assert.Equal(3, index.Items.Count);

            foreach (var (id, plaintext) in bundle.Items)
            {
                Assert.Equal(plaintext, Unwrap(target.ItemPath(id), vmk));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ImportingIntoAnExistingSpaceRewrapsOnlyTheKeySlot()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "rewrap", 2, 0x20);
        var target = NewSpace(dir, "target");

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize("Local-9-tunnel-42", null, out var localVmk));

        try
        {
            var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target, localVmk);
            Assert.Equal(SealImportStatus.Ok, result.Status);
            Assert.Equal(2, result.Counts.Imported);

            foreach (var (id, plaintext) in bundle.Items)
            {
                var container = File.ReadAllBytes(target.ItemPath(id));
                var original = File.ReadAllBytes(dir.PathOf($"rewrap-{bundle.Items.FindIndex(x => x.Id == id)}.novaseal"));

                Assert.NotEqual(original, container);
                Assert.Equal(original.Length, container.Length);


                Assert.Equal(original.AsSpan(0, SealFormat.AuthenticatedPrefixSize).ToArray(),
                    container.AsSpan(0, SealFormat.AuthenticatedPrefixSize).ToArray());
                Assert.Equal(original.AsSpan(112).ToArray(), container.AsSpan(112).ToArray());

                Assert.Equal(plaintext, Unwrap(target.ItemPath(id), localVmk));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localVmk);
        }
    }

    [Fact]
    public async Task TheFiveTamperShapesAreRefusedAndNothingIsStaged()
    {
        using var dir = new TempDir();

        var clean = BuildBundle(dir, "clean", 2, 0x30);
        var other = BuildBundle(dir, "other", 2, 0x40);

        var cleanBytes = File.ReadAllBytes(clean.Path);
        var otherBytes = File.ReadAllBytes(other.Path);

        var cases = new List<(string Name, Func<byte[]> Mutate, SealImportStatus Expected)>
        {
            ("swapped keywrap", () =>
            {
                var bytes = (byte[])cleanBytes.Clone();
                Array.Copy(otherBytes, 64, bytes, 64, SealKeyWrap.FileSize);
                return bytes;
            }, SealImportStatus.WrongPassword),

            ("swapped index", () =>
            {
                var bytes = (byte[])cleanBytes.Clone();
                Array.Copy(otherBytes, 64 + SealKeyWrap.FileSize, bytes, 64 + SealKeyWrap.FileSize,
                    BinaryPrimitives.ReadInt32LittleEndian(cleanBytes.AsSpan(32, 4)));
                return bytes;
            }, SealImportStatus.Corrupt),

            ("kind flipped to 2", () =>
            {
                var bytes = (byte[])cleanBytes.Clone();
                bytes[9] = SealBundleWriter.KindItemsWithBackup;
                return bytes;
            }, SealImportStatus.WrongPassword),

            ("itemCount claims 99", () =>
            {
                var bytes = (byte[])cleanBytes.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), 99);
                return bytes;
            }, SealImportStatus.Corrupt),

            ("indexLen over the ceiling", () =>
            {
                var bytes = (byte[])cleanBytes.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32, 4), SealBundleReader.MaxIndexBytes + 1);
                return bytes;
            }, SealImportStatus.Corrupt),
        };

        foreach (var (name, mutate, expected) in cases)
        {
            var target = NewSpace(dir, "target-" + name.Replace(' ', '-'));
            var tampered = dir.PathOf("tampered-" + name.Replace(' ', '-') + ".novabundle");
            File.WriteAllBytes(tampered, mutate());

            var result = await SealImport.RunAsync(tampered, BundlePassword, target);

            Assert.Equal(expected, result.Status);


            Assert.False(File.Exists(target.Index), $"{name}: nothing may be imported");
            Assert.Empty(Directory.EnumerateFileSystemEntries(target.ImportStaging));
        }
    }

    [Fact]
    public async Task ATamperedBodyFailsTheTailHash()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "tail", 2, 0x50);
        var target = NewSpace(dir, "target");

        var bytes = File.ReadAllBytes(bundle.Path);
        bytes[bytes.Length - 40] ^= 0xFF;
        var tampered = dir.PathOf("tail-tampered.novabundle");
        File.WriteAllBytes(tampered, bytes);

        var result = await SealImport.RunAsync(tampered, BundlePassword, target);

        Assert.Equal(SealImportStatus.Corrupt, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target.ImportStaging));
    }

    [Fact]
    public async Task AnItemAlreadyPresentIsSkipped()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "skip", 2, 0x60);
        var target = NewSpace(dir, "target");

        var first = await SealImport.RunAsync(bundle.Path, BundlePassword, target);
        Assert.Equal(2, first.Counts.Imported);

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(BundlePassword, out var vmk, out _));

        try
        {
            var before = File.ReadAllBytes(target.ItemPath(bundle.Items[0].Id));

            var second = await SealImport.RunAsync(bundle.Path, BundlePassword, target, vmk);
            Assert.Equal(2, second.Counts.Skipped);
            Assert.Equal(0, second.Counts.Imported);

            Assert.Equal(before, File.ReadAllBytes(target.ItemPath(bundle.Items[0].Id)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ADifferentContainerUnderTheSameIdIsAConflict()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "conflict", 1, 0x62);
        var target = NewSpace(dir, "target");

        await SealImport.RunAsync(bundle.Path, BundlePassword, target);

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(BundlePassword, out var vmk, out _));

        try
        {


            var before = File.ReadAllBytes(target.ItemPath(bundle.Items[0].Id));
            var index = SealIndex.Load(target.Index, vmk);
            var existing = index.Find(bundle.Items[0].Id)!;

            index.WithoutItem(existing.Id).WithItem(CloneWithDigest(existing, new string('a', 64))).Save(target.Index, vmk);

            var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target, vmk);

            Assert.Equal(1, result.Counts.Conflict);
            Assert.Equal(0, result.Counts.Imported);
            Assert.Equal(before, File.ReadAllBytes(target.ItemPath(bundle.Items[0].Id)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    private static SealedItem CloneWithDigest(SealedItem item, string digest)
        => new()
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            OriginalName = item.OriginalName,
            SourcePath = item.SourcePath,
            Size = item.Size,
            SealedAtUtc = item.SealedAtUtc,
            Kind = item.Kind,
            ExpiryUtc = item.ExpiryUtc,
            Note = item.Note,
            ContentSha256 = digest,
            FolderEntryCount = item.FolderEntryCount,
            Flags = item.Flags,
        };

    [Fact]
    public async Task ASameNameDifferentIdIsImportedAndCounted()
    {
        using var dir = new TempDir();
        var target = NewSpace(dir, "target");

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize("Local-9-tunnel-42", null, out var localVmk));

        try
        {

            var localId = HexId("local-same-name");
            var localPlain = Encoding.UTF8.GetBytes("local payload");
            var localDigest = WriteContainer(target.ItemPath(localId), localId, localPlain, SealedAtBase, localVmk);
            SealIndex.Empty.WithItem(new SealedItem
            {
                Id = localId,
                DisplayName = "same-name.bin",
                OriginalName = "same-name.bin",
                SourcePath = @"D:\src\local.bin",
                Size = (ulong)localPlain.Length,
                SealedAtUtc = SealedAtBase,
                Kind = SealedItemKind.Normal,
                ContentSha256 = localDigest,
            }).Save(target.Index, localVmk);

            var bundle = BuildBundle(dir, "same-name", 1, 0x64, displayName: "same-name.bin");

            var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target, localVmk);

            Assert.Equal(SealImportStatus.Ok, result.Status);
            Assert.Equal(1, result.Counts.Imported);
            Assert.Equal(1, result.Counts.SameName);

            var index = SealIndex.Load(target.Index, localVmk);
            Assert.Equal(2, index.Items.Count);
            Assert.Equal(2, index.Items.Count(i => i.DisplayName == "same-name.bin"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localVmk);
        }
    }

    [Fact]
    public async Task AnExpiredCapsuleArrivesExpired()
    {
        using var dir = new TempDir();
        var expiredAt = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var bundle = BuildBundle(dir, "expired", 1, 0x66, capsuleExpiry: expiredAt);
        var target = NewSpace(dir, "target");

        var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target);

        Assert.Equal(SealImportStatus.Ok, result.Status);
        Assert.Equal(1, result.Counts.Expired);

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(BundlePassword, out var vmk, out _));

        try
        {
            var index = SealIndex.Load(target.Index, vmk);
            var item = Assert.Single(index.Items);
            Assert.Equal(expiredAt, item.ExpiryUtc);

            var verdict = SealTimeLock.Evaluate(item, DateTimeOffset.UtcNow, index.ObservedMaxUtc);
            Assert.Equal(SealTakeVerdict.Expired, verdict);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }

    [Fact]
    public async Task ObservedMaxOnlyRises()
    {
        using var dir = new TempDir();
        var target = NewSpace(dir, "target");

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize("Local-9-tunnel-42", null, out var localVmk));

        try
        {
            var high = BuildBundle(dir, "high", 1, 0x68, sealedAtBase: 1_795_000_000);
            var low = BuildBundle(dir, "low", 1, 0x6A, sealedAtBase: 1_790_000_100);

            var first = await SealImport.RunAsync(high.Path, BundlePassword, target, localVmk);
            Assert.Equal(SealImportStatus.Ok, first.Status);
            Assert.Equal(1_795_000_000, SealIndex.Load(target.Index, localVmk).ObservedMaxUtc);

            var second = await SealImport.RunAsync(low.Path, BundlePassword, target, localVmk);
            Assert.Equal(SealImportStatus.Ok, second.Status);
            Assert.Equal(1_795_000_000, SealIndex.Load(target.Index, localVmk).ObservedMaxUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localVmk);
        }
    }

    [Fact]
    public async Task AnEmptyBundleImportsNothing()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "empty", 0, 0x6C);
        var target = NewSpace(dir, "target");

        var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target);

        Assert.Equal(SealImportStatus.Ok, result.Status);
        Assert.Equal(0, result.Counts.Imported);
        Assert.Empty(result.FailedItems);
    }

    [Fact]
    public async Task AHundredItemsKeepTheirContents()
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "bulk", 100, 0x6E);
        var target = NewSpace(dir, "target");

        var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target);

        Assert.Equal(SealImportStatus.Ok, result.Status);
        Assert.Equal(100, result.Counts.Imported);

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.TryUnlock(BundlePassword, out var vmk, out _));

        try
        {
            var index = SealIndex.Load(target.Index, vmk);
            Assert.Equal(100, index.Items.Count);

            foreach (var (id, plaintext) in bundle.Items)
            {
                Assert.Equal(plaintext, Unwrap(target.ItemPath(id), vmk));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }





    [Theory]
    [InlineData("corrupt", SealImportStatus.Corrupt)]
    [InlineData("unsupported", SealImportStatus.VersionUnsupported)]
    public async Task ACorruptLocalIndexFailsTheImportWithTheMappedStatus(string mode, SealImportStatus expected)
    {
        using var dir = new TempDir();
        var bundle = BuildBundle(dir, "mapped", 1, 0x40);
        var target = NewSpace(dir, "target");

        var store = new SealSpaceStore(target, FastKdf);
        Assert.Equal(SealStoreOutcome.Ok, store.Initialize("Local-9-tunnel-42", null, out var localVmk));

        try
        {

            var bytes = File.ReadAllBytes(target.Index);
            if (mode == "corrupt") bytes[0] = 0x58;
            else bytes[8] = 2;
            File.WriteAllBytes(target.Index, bytes);

            var result = await SealImport.RunAsync(bundle.Path, BundlePassword, target, localVmk);
            Assert.Equal(expected, result.Status);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(localVmk);
        }
    }
}
