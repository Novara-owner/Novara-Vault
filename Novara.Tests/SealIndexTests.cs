using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealIndexTests
{
    private static readonly byte[] Vmk = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x40 + i)).ToArray();
    private const long SealedAt = 1_790_000_000;

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-index-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string File(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) {  }
        }
    }

    private static string HexId(int seed)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, seed);
        for (var i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i * 31 + 7);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HexDigest(int seed)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, seed);
        for (var i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i * 13 + 3);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static SealedItem Item(int seed, string? name = null, string kind = SealedItemKind.Normal, long? expiry = null)
        => new()
        {
            Id = HexId(seed),
            DisplayName = name ?? $"条目 {seed}.pdf",
            OriginalName = $"原名 {seed}.pdf",
            SourcePath = $@"C:\Users\example\Documents\file{seed}.pdf",
            Size = (ulong)(1000 + seed),
            SealedAtUtc = SealedAt,
            Kind = kind,
            ExpiryUtc = expiry,
            Note = seed % 3 == 0 ? "这是一段备注" : null,
            ContentSha256 = HexDigest(seed),
            FolderEntryCount = seed % 5 == 0 ? 7 : null,
        };



    [Fact]
    public void EmptyIndex_IsAnEmptyFileThatLoadsBackEmpty()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.Save(path, Vmk);
        var loaded = SealIndex.Load(path, Vmk);

        Assert.Empty(loaded.Items);
        Assert.Equal(0, loaded.ObservedMaxUtc);
    }

    [Fact]
    public void ThousandItems_RoundTripExactly()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        var index = SealIndex.Empty;
        for (var i = 0; i < 1000; i++) index = index.WithItem(Item(i));
        index = index.WithObservedMax(SealedAt);

        index.Save(path, Vmk);
        var loaded = SealIndex.Load(path, Vmk);

        Assert.Equal(1000, loaded.Items.Count);
        Assert.Equal(index.SerializePayload(), loaded.SerializePayload());
        Assert.Equal(SealedAt, loaded.ObservedMaxUtc);
    }


    [Fact]
    public void WithItems_AppendsOnceAndRejectsDuplicates()
    {
        var index = SealIndex.Empty.WithItems([Item(1), Item(2)]);

        Assert.Equal(2, index.Items.Count);
        Assert.NotNull(index.Find(HexId(1)));
        Assert.Throws<ArgumentException>(() => index.WithItems([Item(2)]));
        Assert.Throws<ArgumentException>(() => index.WithItems([Item(3), Item(3)]));
    }

    [Fact]
    public void MissingFile_LoadsAsEmpty()
    {
        using var dir = new TempDir();
        Assert.Empty(SealIndex.Load(dir.File("nope.nseal"), Vmk).Items);
    }

    [Fact]
    public void AMissingInitializedIndex_IsCorruptionNotAnEmptySpace()
    {
        using var dir = new TempDir();


        Assert.Throws<SealCorruptException>(() => SealIndex.LoadInitialized(dir.File("nope.nseal"), Vmk));
    }







    [Fact]
    public void PayloadSerialisation_IsStableAndFollowsDeclarationOrder()
    {
        var index = SealIndex.Empty.WithItem(Item(1)).WithItem(Item(2)).WithObservedMax(SealedAt);

        var first = index.SerializePayload();
        var second = index.SerializePayload();
        Assert.Equal(first, second);

        var text = Encoding.UTF8.GetString(first);
        var order = new[] { "\"observedMaxUtc\"", "\"items\"", "\"id\"", "\"displayName\"", "\"originalName\"", "\"sourcePath\"", "\"size\"", "\"sealedAtUtc\"", "\"kind\"", "\"contentSha256\"", "\"flags\"" };

        var previous = -1;
        foreach (var token in order)
        {
            var at = text.IndexOf(token, StringComparison.Ordinal);
            Assert.True(at > previous, $"{token} is out of order or missing");
            previous = at;
        }
    }

    [Fact]
    public void PayloadSerialisation_IgnoresAssignmentOrder()
    {
        var a = new SealedItem
        {
            Id = HexId(3),
            DisplayName = "n",
            OriginalName = "o",
            SourcePath = @"C:\x",
            Size = 1,
            SealedAtUtc = SealedAt,
            ContentSha256 = HexDigest(3),
        };

        var b = new SealedItem
        {
            ContentSha256 = HexDigest(3),
            SealedAtUtc = SealedAt,
            Size = 1,
            SourcePath = @"C:\x",
            OriginalName = "o",
            DisplayName = "n",
            Id = HexId(3),
        };

        Assert.Equal(
            SealIndex.Empty.WithItem(a).SerializePayload(),
            SealIndex.Empty.WithItem(b).SerializePayload());
    }







    [Fact]
    public void TamperedEntryCount_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        var index = SealIndex.Empty.WithItem(Item(1)).WithItem(Item(2)).WithItem(Item(3));
        index.Save(path, Vmk);

        var bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9, 4), 2);
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }

    [Fact]
    public void TamperedCiphertext_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.WithItem(Item(1)).Save(path, Vmk);

        var bytes = File.ReadAllBytes(path);
        bytes[SealIndex.HeaderSize + 3] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }

    [Fact]
    public void WrongVmk_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.WithItem(Item(1)).Save(path, Vmk);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, new byte[SealFormat.KeySize]));
        Assert.Equal(SealCorruptReason.Authentication, error.Reason);
    }

    [Fact]
    public void NewerVersion_IsReportedAsUnsupported()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.Save(path, Vmk);
        var bytes = File.ReadAllBytes(path);
        bytes[8] = 2;
        File.WriteAllBytes(path, bytes);

        Assert.Throws<SealVersionUnsupportedException>(() => SealIndex.Load(path, Vmk));
    }

    [Fact]
    public void TruncatedFile_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.Save(path, Vmk);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..20]);

        Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
    }



    private static void WriteRaw(string path, string json, int itemCount)
    {
        var header = new byte[SealIndex.HeaderSize];
        Encoding.ASCII.GetBytes("NSEALIDX").CopyTo(header, 0);
        header[8] = SealIndex.CurrentVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), itemCount);

        var nonce = RandomNumberGenerator.GetBytes(SealFormat.NonceSize);
        nonce.CopyTo(header.AsSpan(13, SealFormat.NonceSize));

        var aad = header.AsSpan(8, 5).ToArray();
        var sealedBytes = CryptoService.EncryptGcmWithNonce(Encoding.UTF8.GetBytes(json), Vmk, nonce, aad);

        var file = new byte[SealIndex.HeaderSize + sealedBytes.Length];
        header.CopyTo(file, 0);
        sealedBytes.CopyTo(file, SealIndex.HeaderSize);
        File.WriteAllBytes(path, file);
    }

    private static string RawItem(string kind = "normal", string? expiryJson = null, string extra = "", string? id = null)
    {
        var expiry = expiryJson is null ? "" : $",\"expiryUtc\":{expiryJson}";
        return $$"""
            {"id":"{{id ?? new string('b', 32)}}","displayName":"a","originalName":"a","sourcePath":"C:\\\\x","size":1,"sealedAtUtc":{{SealedAt}},"kind":"{{kind}}"{{expiry}},"contentSha256":"{{new string('a', 64)}}","flags":0{{extra}}}
            """;
    }

    [Fact]
    public void DuplicateIdsInTheFile_AreRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        var item = RawItem();
        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{item}},{{item}}]}""", 2);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.HeaderInvalid, error.Reason);
    }

    [Fact]
    public void CapsuleWhoseExpiryPrecedesTheSeal_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem("capsule", SealedAt.ToString())}}]}""", 1);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.HeaderInvalid, error.Reason);
    }

    [Fact]
    public void CapsuleWithoutAnExpiry_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem("capsule")}}]}""", 1);

        Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
    }


    [Fact]
    public void UpperCaseId_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem(id: new string('B', 32))}}]}""", 1);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.HeaderInvalid, error.Reason);
    }

    [Fact]
    public void UnknownKind_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem("weird")}}]}""", 1);

        Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
    }





    [Fact]
    public void UnknownJsonMember_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem(extra: ",\"futureField\":1")}}]}""", 1);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.HeaderInvalid, error.Reason);
    }

    [Fact]
    public void EntryCountThatDoesNotMatchTheArray_IsRejected()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        WriteRaw(path, $$"""{"observedMaxUtc":{{SealedAt}},"items":[{{RawItem()}}]}""", 5);

        var error = Assert.Throws<SealCorruptException>(() => SealIndex.Load(path, Vmk));
        Assert.Equal(SealCorruptReason.LengthMismatch, error.Reason);
    }



    [Fact]
    public void LeftoverTempFile_DoesNotAffectLoad()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.WithItem(Item(1)).Save(path, Vmk);
        File.WriteAllText(path + ".tmp", "half a write that never landed");

        var loaded = SealIndex.Load(path, Vmk);
        Assert.Single(loaded.Items);
    }

    [Fact]
    public void Save_ReplacesThePreviousContentEntirely()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        SealIndex.Empty.WithItem(Item(1)).WithItem(Item(2)).Save(path, Vmk);
        SealIndex.Empty.WithItem(Item(9)).Save(path, Vmk);

        var loaded = SealIndex.Load(path, Vmk);
        Assert.Single(loaded.Items);
        Assert.Equal(HexId(9), loaded.Items[0].Id);
    }






    [Fact]
    public void SavedFile_ContainsNoPlaintextNamePathOrNote()
    {
        using var dir = new TempDir();
        var path = dir.File("index.nseal");

        var item = Item(1, name: "旅行照片.jpg");
        item.OriginalName = "原始文件名.jpeg";
        item.Note = "这是一段只应存在于密文里的备注";
        SealIndex.Empty.WithItem(item).Save(path, Vmk);

        var bytes = File.ReadAllBytes(path);

        foreach (var needle in new[] { "旅行照片", "原始文件名", "这是一段只应存在于密文里的备注", @"C:\Users\example" })
            Assert.DoesNotContain(needle, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);


        Assert.Equal("NSEALIDX", Encoding.ASCII.GetString(bytes, 0, 8));
    }



    [Fact]
    public void WithoutItem_RemovesOnlyThatId()
    {
        var index = SealIndex.Empty.WithItem(Item(1)).WithItem(Item(2)).WithItem(Item(3));

        var after = index.WithoutItem(HexId(2));

        Assert.Equal(2, after.Items.Count);
        Assert.Null(after.Find(HexId(2)));
        Assert.NotNull(after.Find(HexId(1)));
        Assert.Same(index, index.WithoutItem(HexId(42)));
    }

    [Fact]
    public void ObservedMax_NeverDecreases()
    {
        var index = SealIndex.Empty.WithObservedMax(500).WithObservedMax(300).WithObservedMax(900).WithObservedMax(900);
        Assert.Equal(900, index.ObservedMaxUtc);
    }

    [Fact]
    public void ObservedMaxReset_MayLowerTheBaselineButNeverBelowZero()
    {
        var index = SealIndex.Empty.WithObservedMax(900).WithObservedMaxReset(400);
        Assert.Equal(400, index.ObservedMaxUtc);
        Assert.Equal(0, index.WithObservedMaxReset(-5).ObservedMaxUtc);
    }

    [Fact]
    public void FromPayload_ClampsAnObservedMaxBeyondTheTimestampWindow()
    {
        var json = Encoding.UTF8.GetBytes("{\"observedMaxUtc\":9223372036854775807,\"items\":[]}");
        var index = SealIndex.FromPayload(json, 0);
        Assert.Equal(SealFormat.MaxSealedAtUtc, index.ObservedMaxUtc);
    }

    [Fact]
    public void WithItem_RejectsDuplicatesAndMalformedEntries()
    {
        var index = SealIndex.Empty.WithItem(Item(1));

        Assert.Throws<ArgumentException>(() => index.WithItem(Item(1)));

        var badId = Item(2);
        badId.Id = "not-hex";
        Assert.Throws<ArgumentException>(() => index.WithItem(badId));

        var badDigest = Item(3);
        badDigest.ContentSha256 = "too-short";
        Assert.Throws<ArgumentException>(() => index.WithItem(badDigest));

        var badKind = Item(4);
        badKind.Kind = "weird";
        Assert.Throws<ArgumentException>(() => index.WithItem(badKind));

        var capsuleNoExpiry = Item(5, kind: SealedItemKind.Capsule);
        Assert.Throws<ArgumentException>(() => index.WithItem(capsuleNoExpiry));

        var capsuleShortExpiry = Item(6, kind: SealedItemKind.Capsule, expiry: SealedAt);
        Assert.Throws<ArgumentException>(() => index.WithItem(capsuleShortExpiry));

        var normalWithExpiry = Item(7, expiry: SealedAt + 1000);
        Assert.Throws<ArgumentException>(() => index.WithItem(normalWithExpiry));
    }
}
