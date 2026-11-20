using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealJournalTests
{
    private static readonly byte[] Vmk = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x20 + i)).ToArray();
    private const long StartedAt = 1_790_000_000;
    private const long OneDay = 86_400;

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-journal-" + Guid.NewGuid().ToString("N"));
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

    private static SealJournalDraft Draft(int seed, string kind = SealedItemKind.Normal, int? folderEntries = null)
        => new()
        {
            OriginalName = $"原名 {seed}.pdf",
            DisplayName = $"显示名 {seed}.pdf",
            Size = (ulong)(2048 + seed),
            SealedAtUtc = StartedAt,
            Kind = kind,
            ExpiryUtc = string.Equals(kind, SealedItemKind.Capsule, StringComparison.Ordinal) ? StartedAt + 365 * OneDay : null,
            Note = seed % 2 == 0 ? "这是一段备注" : null,
            ContentSha256 = HexDigest(seed),
            FolderEntryCount = folderEntries,
        };

    private static SealJournalRecord SealRecord(int seed, string phase, bool withDraft = true, bool withHash = true)
    {
        var draft = Draft(seed);
        if (!withHash) draft.ContentSha256 = "";

        return new SealJournalRecord
        {
            OpId = HexId(10_000 + seed),
            Op = SealJournalOp.Seal,
            Phase = phase,
            ItemId = HexId(seed),
            SourcePath = $@"C:\Users\example\Documents\旅行照片 {seed}.jpg",
            StartedAtUtc = StartedAt + seed,
            Draft = withDraft ? draft : null,
        };
    }

    private static SealJournalRecord TakeRecord(int seed, string phase)
        => new()
        {
            OpId = HexId(20_000 + seed),
            Op = SealJournalOp.Take,
            Phase = phase,
            ItemId = HexId(seed),
            SourcePath = $@"C:\Users\example\Documents\taken {seed}.jpg",
            TargetPath = $@"D:\restore\taken {seed}.jpg",
            StartedAtUtc = StartedAt + seed,
        };


    private static void WriteRaw(string path, string json, int recordCount, byte version = SealJournal.CurrentVersion)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = new byte[SealJournal.HeaderSize];
        "NSEALJNL"u8.CopyTo(header);
        header[8] = version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), recordCount);

        var nonce = RandomNumberGenerator.GetBytes(SealFormat.NonceSize);
        var aad = header.AsSpan(8, 5).ToArray();
        var sealedBytes = CryptoService.EncryptGcmWithNonce(payload, Vmk, nonce, aad);

        var file = new byte[SealJournal.HeaderSize + sealedBytes.Length];
        header.AsSpan(0, 13).CopyTo(file);
        nonce.CopyTo(file.AsSpan(13, SealFormat.NonceSize));
        sealedBytes.CopyTo(file.AsSpan(SealJournal.HeaderSize));

        System.IO.File.WriteAllBytes(path, file);
    }

    private static string Sha256Of(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path)));



    [Fact]
    public void TheSixPhaseLiterals_AreTheContractedStrings()
    {


        Assert.Equal("seal_begin", SealJournalPhase.SealBegin);
        Assert.Equal("sealed", SealJournalPhase.Sealed);
        Assert.Equal("seal_indexed", SealJournalPhase.SealIndexed);
        Assert.Equal("deleted", SealJournalPhase.Deleted);
        Assert.Equal("take_begin", SealJournalPhase.TakeBegin);
        Assert.Equal("taken", SealJournalPhase.Taken);

        Assert.Equal("seal", SealJournalOp.Seal);
        Assert.Equal("take", SealJournalOp.Take);
    }

    [Fact]
    public void TheHeader_IsTwentyFiveBytesAndCarriesTheRecordCount()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        SealJournal.Empty.Append(SealRecord(1, SealJournalPhase.SealBegin)).Save(path, Vmk);

        var bytes = System.IO.File.ReadAllBytes(path);
        Assert.Equal(25, SealJournal.HeaderSize);
        Assert.True(bytes.Length > SealJournal.HeaderSize + SealFormat.TagSize);

        Assert.Equal("NSEALJNL"u8.ToArray(), bytes.AsSpan(0, 8).ToArray());
        Assert.Equal(SealJournal.CurrentVersion, bytes[8]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(9, 4)));
    }



    [Fact]
    public void AMissingFile_IsAnEmptyJournalRatherThanAnError()
    {
        using var dir = new TempDir();

        var journal = SealJournal.Load(dir.File("journal.nsj"), Vmk);

        Assert.Equal(SealJournalStatus.Ok, journal.Status);
        Assert.Empty(journal.Records);
        Assert.False(journal.HasMalformedRecords);
    }

    [Fact]
    public void AnEmptyJournal_RoundTripsToZeroRecords()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        SealJournal.Empty.Save(path, Vmk);
        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Ok, loaded.Status);
        Assert.Empty(loaded.Records);
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public void SixRecords_RoundTripFieldByField()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var journal = SealJournal.Empty
            .Append(SealRecord(1, SealJournalPhase.SealBegin))
            .Append(SealRecord(1, SealJournalPhase.Sealed))
            .Append(SealRecord(1, SealJournalPhase.SealIndexed))
            .Append(SealRecord(1, SealJournalPhase.Deleted))
            .Append(TakeRecord(2, SealJournalPhase.TakeBegin))
            .Append(TakeRecord(2, SealJournalPhase.Taken));

        journal.Save(path, Vmk);
        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Ok, loaded.Status);
        Assert.Equal(6, loaded.Records.Count);
        Assert.False(loaded.HasMalformedRecords);

        for (var i = 0; i < 6; i++)
        {
            var expected = journal.Records[i];
            var actual = loaded.Records[i];

            Assert.Equal(expected.OpId, actual.OpId);
            Assert.Equal(expected.Op, actual.Op);
            Assert.Equal(expected.Phase, actual.Phase);
            Assert.Equal(expected.ItemId, actual.ItemId);
            Assert.Equal(expected.SourcePath, actual.SourcePath);
            Assert.Equal(expected.TargetPath, actual.TargetPath);
            Assert.Equal(expected.StartedAtUtc, actual.StartedAtUtc);
        }

        var sealDraft = loaded.Records[0].Draft!;
        Assert.Equal("原名 1.pdf", sealDraft.OriginalName);
        Assert.Equal("显示名 1.pdf", sealDraft.DisplayName);
        Assert.Equal(2049UL, sealDraft.Size);
        Assert.Equal(StartedAt, sealDraft.SealedAtUtc);
        Assert.Equal(SealedItemKind.Normal, sealDraft.Kind);
        Assert.Null(sealDraft.ExpiryUtc);
        Assert.Null(sealDraft.Note);
        Assert.Equal(HexDigest(1), sealDraft.ContentSha256);
    }

    [Fact]
    public void ADraft_KeepsTheCapsuleFieldsAndTheFolderCount()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var record = new SealJournalRecord
        {
            OpId = HexId(77),
            Op = SealJournalOp.Seal,
            Phase = SealJournalPhase.Sealed,
            ItemId = HexId(78),
            SourcePath = @"C:\资料\项目",
            StartedAtUtc = StartedAt,
            Draft = Draft(78, SealedItemKind.Capsule, folderEntries: 42),
        };

        SealJournal.Empty.Append(record).Save(path, Vmk);
        var loaded = SealJournal.Load(path, Vmk).Records.Single().Draft!;

        Assert.Equal(SealedItemKind.Capsule, loaded.Kind);
        Assert.Equal(StartedAt + 365 * OneDay, loaded.ExpiryUtc);
        Assert.Equal(42, loaded.FolderEntryCount);
        Assert.Equal("这是一段备注", loaded.Note);
    }

    [Fact]
    public void TwoSerialisationsOfTheSameJournal_AreByteIdentical()
    {
        var journal = SealJournal.Empty
            .Append(SealRecord(5, SealJournalPhase.SealBegin))
            .Append(TakeRecord(6, SealJournalPhase.TakeBegin));

        Assert.Equal(journal.SerializePayload(), journal.SerializePayload());
    }



    [Fact]
    public void AnyFlippedByte_MakesTheJournalUnreadableAndLeavesTheFileUntouched()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        SealJournal.Empty.Append(SealRecord(3, SealJournalPhase.SealBegin)).Save(path, Vmk);
        var before = Sha256Of(path);

        var bytes = System.IO.File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        System.IO.File.WriteAllBytes(path, bytes);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, loaded.Status);
        Assert.Equal(SealCorruptReason.Authentication, loaded.Reason);
        Assert.Empty(loaded.Records);
        Assert.True(System.IO.File.Exists(path), "a load must never delete the journal");
        Assert.Equal(Sha256Of(path), Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.NotEqual(before, Sha256Of(path));
    }

    [Fact]
    public void TheRecordCount_IsBoundByTheAad()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");



        var json = RecordJson(HexId(1), SealJournalOp.Seal, SealJournalPhase.SealBegin, HexId(2), withDraft: false);
        WriteRaw(path, "[" + json + "]", recordCount: 1);

        var bytes = System.IO.File.ReadAllBytes(path);
        bytes[9] = 2;
        System.IO.File.WriteAllBytes(path, bytes);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, loaded.Status);
        Assert.Equal(SealCorruptReason.Authentication, loaded.Reason);
    }

    [Fact]
    public void ACountThatDisagreesWithTheArray_IsCorrupt()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var json = RecordJson(HexId(1), SealJournalOp.Seal, SealJournalPhase.SealBegin, HexId(2), withDraft: false);
        WriteRaw(path, "[" + json + "]", recordCount: 2);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, loaded.Status);
        Assert.Equal(SealCorruptReason.LengthMismatch, loaded.Reason);
    }

    [Fact]
    public void AnUnknownMember_IsRefusedRatherThanIgnored()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var json = RecordJson(HexId(1), SealJournalOp.Seal, SealJournalPhase.SealBegin, HexId(2), withDraft: false)
            .TrimEnd('}') + ",\"futureField\":1}";
        WriteRaw(path, "[" + json + "]", recordCount: 1);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, loaded.Status);
        Assert.Equal(SealCorruptReason.HeaderInvalid, loaded.Reason);
    }

    [Fact]
    public void ANewerVersion_IsReportedAsUnsupportedNotAsCorruption()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var header = new byte[SealJournal.HeaderSize];
        "NSEALJNL"u8.CopyTo(header);
        header[8] = 2;

        var file = new byte[SealJournal.HeaderSize + SealFormat.TagSize];
        header.CopyTo(file, 0);
        System.IO.File.WriteAllBytes(path, file);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.VersionUnsupported, loaded.Status);
        Assert.Null(loaded.Reason);
    }

    [Fact]
    public void AnEmptyFile_IsTruncated()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");
        System.IO.File.WriteAllBytes(path, []);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, loaded.Status);
        Assert.Equal(SealCorruptReason.Truncated, loaded.Reason);
    }



    [Fact]
    public void ARecordMissingItsItemId_IsMarkedMalformedAndTheOthersSurvive()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var records = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var phase = i switch
            {
                0 => SealJournalPhase.SealBegin,
                1 => SealJournalPhase.Sealed,
                2 => SealJournalPhase.SealIndexed,
                3 => SealJournalPhase.Deleted,
                4 => SealJournalPhase.TakeBegin,
                _ => SealJournalPhase.Taken,
            };
            var isTake = i >= 4;
            var op = isTake ? SealJournalOp.Take : SealJournalOp.Seal;


            var itemId = i == 4 ? "" : HexId(100 + i);
            records.Add(RecordJson(HexId(200 + i / 4), op, phase, itemId, withDraft: !isTake, targetForTake: isTake));
        }

        WriteRaw(path, "[" + string.Join(",", records) + "]", recordCount: 6);

        var loaded = SealJournal.Load(path, Vmk);

        Assert.Equal(SealJournalStatus.Ok, loaded.Status);
        Assert.Equal(6, loaded.Records.Count);
        Assert.True(loaded.HasMalformedRecords);
        Assert.Equal(5, loaded.Records.Count(r => !r.IsMalformed));
        Assert.True(loaded.Records[4].IsMalformed);
        Assert.Contains("itemId", loaded.Records[4].MalformedReason);
    }

    [Fact]
    public void SavingWithAMalformedRecord_IsRefused()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var broken = RecordJson(HexId(1), SealJournalOp.Seal, SealJournalPhase.SealBegin, "", withDraft: false);
        var good = RecordJson(HexId(2), SealJournalOp.Take, SealJournalPhase.TakeBegin, HexId(3), withDraft: false, targetForTake: true);
        WriteRaw(path, "[" + broken + "," + good + "]", recordCount: 2);

        var loaded = SealJournal.Load(path, Vmk);
        var before = Sha256Of(path);

        var refused = Assert.Throws<InvalidOperationException>(() => loaded.Save(path, Vmk));

        Assert.Contains("failed validation", refused.Message);
        Assert.Equal(before, Sha256Of(path));
    }

    [Fact]
    public void RemovingMalformedRecords_MakesTheJournalSavableAgain()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var broken = RecordJson(HexId(1), SealJournalOp.Seal, SealJournalPhase.SealBegin, "", withDraft: false);
        var good = RecordJson(HexId(2), SealJournalOp.Take, SealJournalPhase.TakeBegin, HexId(3), withDraft: false, targetForTake: true);
        WriteRaw(path, "[" + broken + "," + good + "]", recordCount: 2);

        var cleaned = SealJournal.Load(path, Vmk).RemoveMalformedRecords();

        Assert.False(cleaned.HasMalformedRecords);
        Assert.Single(cleaned.Records);
        cleaned.Save(path, Vmk);

        var reloaded = SealJournal.Load(path, Vmk);
        Assert.Equal(SealJournalStatus.Ok, reloaded.Status);
        Assert.False(reloaded.HasMalformedRecords);
        Assert.Single(reloaded.Records);
        Assert.Equal(SealJournalOp.Take, reloaded.Records[0].Op);
    }

    [Fact]
    public void AnUnreadableJournal_IsNeverOverwritten()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        SealJournal.Empty.Append(SealRecord(4, SealJournalPhase.SealBegin)).Save(path, Vmk);
        var bytes = System.IO.File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        System.IO.File.WriteAllBytes(path, bytes);
        var before = Sha256Of(path);

        var loaded = SealJournal.Load(path, Vmk);
        var refused = Assert.Throws<InvalidOperationException>(() => loaded.Save(path, Vmk));

        Assert.Contains("could not be read", refused.Message);
        Assert.Equal(before, Sha256Of(path));
    }

    [Fact]
    public void ARecordLeavesTheJournalOnlyWhenRemovedOnPurpose()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var broken = RecordJson(HexId(9), SealJournalOp.Seal, SealJournalPhase.SealBegin, "", withDraft: false);
        var good = RecordJson(HexId(8), SealJournalOp.Take, SealJournalPhase.TakeBegin, HexId(7), withDraft: false, targetForTake: true);
        WriteRaw(path, "[" + broken + "," + good + "]", recordCount: 2);

        var loaded = SealJournal.Load(path, Vmk);
        var cleaned = loaded.Remove(HexId(9));

        Assert.Single(cleaned.Records);
        Assert.False(cleaned.HasMalformedRecords);

        cleaned.Save(path, Vmk);
        var reloaded = SealJournal.Load(path, Vmk);

        Assert.Single(reloaded.Records);
        Assert.Equal(HexId(8), reloaded.Records[0].OpId);
    }



    [Fact]
    public void APhaseFromTheOtherOperation_IsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = HexId(1),
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.TakeBegin,
                ItemId = HexId(2),
                SourcePath = @"C:\a.jpg",
                StartedAtUtc = StartedAt,
            }));

        Assert.Contains("phase does not belong to op", ex.Message);
    }

    [Fact]
    public void ATakeNeedsATargetAndASealMustNotHaveOne()
    {
        var noTarget = Assert.Throws<ArgumentException>(() =>
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = HexId(1),
                Op = SealJournalOp.Take,
                Phase = SealJournalPhase.TakeBegin,
                ItemId = HexId(2),
                SourcePath = @"C:\a.jpg",
                StartedAtUtc = StartedAt,
            }));
        Assert.Contains("targetPath", noTarget.Message);

        var sealWithTarget = Assert.Throws<ArgumentException>(() =>
            SealJournal.Empty.Append(new SealJournalRecord
            {
                OpId = HexId(3),
                Op = SealJournalOp.Seal,
                Phase = SealJournalPhase.SealBegin,
                ItemId = HexId(4),
                SourcePath = @"C:\a.jpg",
                TargetPath = @"D:\a.jpg",
                StartedAtUtc = StartedAt,
            }));
        Assert.Contains("must not carry a targetPath", sealWithTarget.Message);
    }

    [Fact]
    public void TheFirstSealPhaseMayOmitTheDraftButLaterPhasesMayNot()
    {
        var bare = new SealJournalRecord
        {
            OpId = HexId(1),
            Op = SealJournalOp.Seal,
            Phase = SealJournalPhase.SealBegin,
            ItemId = HexId(2),
            SourcePath = @"C:\a.jpg",
            StartedAtUtc = StartedAt,
        };

        Assert.Single(SealJournal.Empty.Append(bare).Records);

        var later = new SealJournalRecord
        {
            OpId = HexId(1),
            Op = SealJournalOp.Seal,
            Phase = SealJournalPhase.Sealed,
            ItemId = HexId(2),
            SourcePath = @"C:\a.jpg",
            StartedAtUtc = StartedAt,
        };

        var ex = Assert.Throws<ArgumentException>(() => SealJournal.Empty.Append(later));
        Assert.Contains("draft", ex.Message);
    }

    [Fact]
    public void OnceTheContainerIsWritten_TheDraftMustCarryTheHash()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SealJournal.Empty.Append(SealRecord(11, SealJournalPhase.Sealed, withHash: false)));

        Assert.Contains("contentSha256", ex.Message);
    }

    [Fact]
    public void TheSamePhaseTwice_IsRefused()
    {
        var journal = SealJournal.Empty.Append(SealRecord(12, SealJournalPhase.SealBegin));

        var ex = Assert.Throws<ArgumentException>(() => journal.Append(SealRecord(12, SealJournalPhase.SealBegin)));

        Assert.Contains("already has a record for that phase", ex.Message);
    }

    [Fact]
    public void ATakeRecordMustNotCarryADraft()
    {
        var record = TakeRecord(13, SealJournalPhase.TakeBegin);
        record.Draft = Draft(13);

        var ex = Assert.Throws<ArgumentException>(() => SealJournal.Empty.Append(record));

        Assert.Contains("must not carry a draft", ex.Message);
    }



    [Fact]
    public void TheFileNeverContainsTheSourcePath()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        SealJournal.Empty
            .Append(SealRecord(14, SealJournalPhase.SealBegin))
            .Append(TakeRecord(15, SealJournalPhase.TakeBegin))
            .Save(path, Vmk);

        var bytes = System.IO.File.ReadAllBytes(path);

        Assert.False(ContainsBytes(bytes, @"C:\Users\example\Documents\旅行照片 14.jpg"u8.ToArray()), "the source path leaked");
        Assert.False(ContainsBytes(bytes, "旅行照片"u8.ToArray()), "part of the file name leaked");
        Assert.False(ContainsBytes(bytes, @"D:\restore\taken 15.jpg"u8.ToArray()), "the take target leaked");



        Assert.Equal("NSEALJNL"u8.ToArray(), bytes.AsSpan(0, 8).ToArray());
    }


    private static bool ContainsBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;

        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                match = false;
                break;
            }

            if (match) return true;
        }

        return false;
    }

    [Fact]
    public void AThousandRecords_RoundTripInsideTheBudget()
    {
        using var dir = new TempDir();
        var path = dir.File("journal.nsj");

        var journal = SealJournal.Empty;
        for (var i = 0; i < 1000; i++)
            journal = journal.Append(SealRecord(i, SealJournalPhase.SealBegin));

        var watch = Stopwatch.StartNew();
        journal.Save(path, Vmk);
        var loaded = SealJournal.Load(path, Vmk);
        watch.Stop();

        Assert.Equal(1000, loaded.Records.Count);
        Assert.True(watch.ElapsedMilliseconds < 200, $"a full rewrite of 1000 records took {watch.ElapsedMilliseconds} ms");
    }


    private static string RecordJson(
        string opId, string op, string phase, string itemId, bool withDraft, bool targetForTake = false)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"opId\":\"{opId}\",");
        sb.Append($"\"op\":\"{op}\",");
        sb.Append($"\"phase\":\"{phase}\",");
        sb.Append($"\"itemId\":\"{itemId}\",");
        sb.Append($"\"sourcePath\":\"C:\\\\Users\\\\example\\\\a.jpg\",");
        if (targetForTake) sb.Append("\"targetPath\":\"D:\\\\restore\\\\a.jpg\",");
        sb.Append($"\"startedAtUtc\":{StartedAt}");
        if (withDraft)
        {
            sb.Append(",\"draft\":{");
            sb.Append("\"originalName\":\"a.jpg\",");
            sb.Append("\"displayName\":\"a.jpg\",");
            sb.Append("\"size\":1234,");
            sb.Append($"\"sealedAtUtc\":{StartedAt},");
            sb.Append("\"kind\":\"normal\",");
            sb.Append($"\"contentSha256\":\"{HexDigest(1)}\"");
            sb.Append('}');
        }

        sb.Append('}');
        return sb.ToString();
    }

    [Fact]
    public void LoadRequiredIfPresent_AbsentIsEmpty_PresentButUnreadableIsNot()
    {


        var dir = Path.Combine(Path.GetTempPath(), "novara-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "journal.nseal");
            var vmk = new byte[SealFormat.KeySize];

            var absent = SealJournal.LoadRequiredIfPresent(path, vmk);
            Assert.Equal(SealJournalStatus.Ok, absent.Status);
            Assert.Empty(absent.Records);

            File.WriteAllBytes(path, new byte[10]);
            var unreadable = SealJournal.LoadRequiredIfPresent(path, vmk);
            Assert.Equal(SealJournalStatus.Unreadable, unreadable.Status);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
