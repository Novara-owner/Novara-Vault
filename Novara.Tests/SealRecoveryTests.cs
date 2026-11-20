using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealRecoveryTests
{
    private static readonly byte[] Vmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x40 + i)).ToArray();

    private const long StartedAt = 1_790_000_000;
    private const long OneDay = 86_400;

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "novara-seal-recovery-" + Guid.NewGuid().ToString("N"));
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

    private static string HexId(int seed)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, seed);
        for (var i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i * 29 + 11);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string WrongDigest(int seed)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, seed);
        for (var i = 4; i < bytes.Length; i++) bytes[i] = (byte)(i * 7 + 1);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static SealJournalDraft Draft(string digest, string kind = SealedItemKind.Normal, int? folderEntries = null)
        => new()
        {
            OriginalName = "旅行照片.jpg",
            DisplayName = "旅行照片.jpg",
            Size = 4096,
            SealedAtUtc = StartedAt,
            Kind = kind,
            ExpiryUtc = string.Equals(kind, SealedItemKind.Capsule, StringComparison.Ordinal) ? StartedAt + 30 * OneDay : null,
            Note = "一段备注",
            ContentSha256 = digest,
            FolderEntryCount = folderEntries,
        };

    private static SealJournalRecord SealRecord(
        int seed, string phase, string? sourcePath = null, SealJournalDraft? draft = null)
        => new()
        {
            OpId = HexId(90_000 + seed),
            Op = SealJournalOp.Seal,
            Phase = phase,
            ItemId = HexId(seed),
            SourcePath = sourcePath ?? $@"{Path.GetTempPath()}no-such-source-{seed}.jpg",
            StartedAtUtc = StartedAt + seed,
            Draft = draft ?? Draft(WrongDigest(seed)),
        };

    private static SealJournalRecord TakeRecord(int seed, string phase, string targetPath)
        => new()
        {
            OpId = HexId(80_000 + seed),
            Op = SealJournalOp.Take,
            Phase = phase,
            ItemId = HexId(seed),
            SourcePath = $@"{Path.GetTempPath()}no-such-source-{seed}.jpg",
            TargetPath = targetPath,
            StartedAtUtc = StartedAt + seed,
        };

    private static SealedItem Item(string id, string digest, string? note = null)
        => new()
        {
            Id = id,
            DisplayName = "旅行照片.jpg",
            OriginalName = "旅行照片.jpg",
            SourcePath = @"D:\photos\旅行照片.jpg",
            Size = 4096,
            SealedAtUtc = StartedAt,
            Kind = SealedItemKind.Normal,
            Note = note,
            ContentSha256 = digest,
        };

    private static SealJournal JournalWith(params SealJournalRecord[] records)
    {
        var journal = SealJournal.Empty;
        foreach (var record in records) journal = journal.Append(record);
        return journal;
    }


    private static string WriteContainer(string path, string itemId, byte[] plaintext)
    {
        var contentKey = RandomNumberGenerator.GetBytes(SealFormat.KeySize);
        var header = SealContainerWriter.PrepareHeader(
            Convert.FromHexString(itemId),
            (ulong)plaintext.Length,
            StartedAt,
            SealFormat.MinChunkSize,
            Vmk,
            contentKey);

        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var result = SealContainerWriter.WriteAsync(
                new MemoryStream(plaintext), header, contentKey, destination, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        CryptographicOperations.ZeroMemory(contentKey);
        return Convert.ToHexString(result.PlaintextSha256).ToLowerInvariant();
    }

    private sealed class CountingProbe : ISealFileProbe
    {
        public int SourceQueries { get; private set; }
        public int ContainerQueries { get; private set; }
        public int TargetQueries { get; private set; }
        public int DeleteCalls { get; private set; }

        public bool SourceExists(string sourcePath)
        {
            SourceQueries++;
            return true;
        }

        public bool ContainerExists(string itemId)
        {
            ContainerQueries++;
            return true;
        }

        public SealTargetState TargetState(string targetPath, string itemId)
        {
            TargetQueries++;
            return SealTargetState.Complete;
        }

        public bool FileExists(string path) => false;

        public void Delete(string path) => DeleteCalls++;
    }



    [Fact]
    public void AnEmptyJournal_PlansNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plan = SealRecovery.Plan(SealJournal.Empty, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        Assert.Empty(plan.Actions);
        Assert.Equal(SealJournalStatus.Ok, plan.JournalStatus);
        Assert.False(plan.JournalNeedsAttention);
    }

    [Fact]
    public void AWriteInProgress_OffersAFragmentCleanupAndARetry()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "original.bin");
        File.WriteAllBytes(source, [1, 2, 3]);

        var id = HexId(1);
        WriteContainer(paths.SealingPath(id), id, [9, 9, 9]);

        var journal = JournalWith(SealRecord(1, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        Assert.Equal(
            [SealRecoveryActionKind.DiscardStaging, SealRecoveryActionKind.OfferRetrySeal],
            plan.Actions.Select(a => a.Kind));
        Assert.Equal(id, plan.Actions[0].ItemId);
        Assert.True(plan.Actions[1].NeedsUserDecision);
    }

    [Fact]
    public void AVerifiedContainerWithTheOriginalIntact_OffersARetry()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "original.bin");
        File.WriteAllBytes(source, [1, 2, 3]);

        var id = HexId(2);
        var digest = WriteContainer(paths.SealingPath(id), id, [7, 7, 7, 7]);

        var journal = JournalWith(
            SealRecord(2, SealJournalPhase.SealBegin, source, Draft("")),
            SealRecord(2, SealJournalPhase.Sealed, source, Draft(digest)));

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        Assert.Equal(
            [SealRecoveryActionKind.DiscardStaging, SealRecoveryActionKind.OfferRetrySeal],
            plan.Actions.Select(a => a.Kind));
        Assert.True(plan.Actions[1].NeedsUserDecision);
    }

    [Fact]
    public void AWriteInProgressWithTheOriginalGone_KeepsTheFragmentVisible()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "gone.bin");

        var id = HexId(26);
        WriteContainer(paths.SealingPath(id), id, [9, 9, 9]);

        var journal = JournalWith(SealRecord(26, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.StagingOnly, action.Kind);
        Assert.True(action.NeedsUserDecision);
    }

    [Fact]
    public void AWriteInProgressWithTheOriginalGoneAndNoFragment_IsAVisibleRecord()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "gone-without-fragment.bin");

        var journal = JournalWith(SealRecord(27, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.SourceLost, action.Kind);
        Assert.True(action.NeedsUserDecision);
    }

    [Fact]
    public async Task AnOrphanFragmentCanBeDiscardedExplicitly()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "gone.bin");

        var id = HexId(28);
        WriteContainer(paths.SealingPath(id), id, [9, 9, 9]);
        var journal = JournalWith(SealRecord(28, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.StagingOnly, action.Kind);

        Assert.True(await SealRecovery.ResolveAsync(
            SealIndex.Empty, journal, paths, Vmk, action, SealRecoveryChoice.DiscardStaging,
            new SealFileProbe(paths, SealIndex.Empty)));

        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
        Assert.False(File.Exists(paths.SealingPath(id)));
    }

    [Fact]
    public async Task DiscardingAFragmentIsRefusedWhenTheOriginalDisappearedAfterPlanning()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "source.bin");
        File.WriteAllText(source, "original");

        var id = HexId(29);
        WriteContainer(paths.SealingPath(id), id, [7, 7, 7]);
        var journal = JournalWith(SealRecord(29, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var action = Assert.Single(plan.Actions, a => a.Kind == SealRecoveryActionKind.OfferRetrySeal);

        File.Delete(source);

        Assert.False(await SealRecovery.ResolveAsync(
            SealIndex.Empty, journal, paths, Vmk, action, SealRecoveryChoice.DiscardStaging,
            new SealFileProbe(paths, SealIndex.Empty)));
        Assert.Single(journal.Records);
        Assert.True(File.Exists(paths.SealingPath(id)));

        var replanned = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        Assert.Equal(SealRecoveryActionKind.StagingOnly, Assert.Single(replanned.Actions).Kind);
    }

    [Fact]
    public void AVerifiedContainerWithTheOriginalGone_CompletesFromTheJournal()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(3);
        var digest = WriteContainer(paths.SealingPath(id), id, [5, 5, 5, 5, 5]);

        var journal = JournalWith(
            SealRecord(3, SealJournalPhase.SealBegin),
            SealRecord(3, SealJournalPhase.Sealed, draft: Draft(digest)));

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.CompleteSealFromJournal, action.Kind);
    }

    [Fact]
    public void AnIndexedEntryWhoseOriginalIsStillThere_IsReportedAsAnOrphan()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "original.bin");
        File.WriteAllBytes(source, [1]);

        var id = HexId(4);
        var index = SealIndex.Empty.WithItem(Item(id, WrongDigest(4)));

        var journal = JournalWith(SealRecord(4, SealJournalPhase.SealIndexed, source));

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.OrphanSourceRemains, action.Kind);
        Assert.True(action.NeedsUserDecision);
    }

    [Fact]
    public void AnIndexedEntryWhoseOriginalIsGone_IsFinalised()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(5);
        var index = SealIndex.Empty.WithItem(Item(id, WrongDigest(5)));

        var journal = JournalWith(SealRecord(5, SealJournalPhase.Deleted));

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.FinalizeSeal, action.Kind);
        Assert.True(action.IsAutomatic);
    }

    [Fact]
    public void AnIndexedEntryLeftAtSealIndexedWithoutItsOriginal_IsFinalised()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);


        var id = HexId(6);
        var index = SealIndex.Empty.WithItem(Item(id, WrongDigest(6)));
        var journal = JournalWith(SealRecord(6, SealJournalPhase.SealIndexed));

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.FinalizeSeal, action.Kind);
    }

    [Fact]
    public void AHalfWrittenTake_AsksBeforeCleaningUp()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(7);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));

        var target = Path.Combine(dir.Root, "target.txt");
        File.WriteAllBytes(target, plaintext[..4]);

        var journal = JournalWith(TakeRecord(7, SealJournalPhase.TakeBegin, target));
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.AskCleanupPartialTarget, action.Kind);
        Assert.True(action.NeedsUserDecision);
    }

    [Fact]
    public void ATakeWithBothSidesIntact_OffersThreeChoices()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(8);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));

        var target = Path.Combine(dir.Root, "target.txt");
        File.WriteAllBytes(target, plaintext);

        var journal = JournalWith(TakeRecord(8, SealJournalPhase.TakeBegin, target));
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.BothSidesExist, action.Kind);
        Assert.True(action.NeedsUserDecision);
        Assert.False(action.IsAutomatic);
    }

    [Fact]
    public void ATakeWhoseEntryIsGone_IsFinalised()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var target = Path.Combine(dir.Root, "target.txt");
        File.WriteAllBytes(target, [1, 2, 3]);

        var journal = JournalWith(TakeRecord(9, SealJournalPhase.Taken, target));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.FinalizeTake, action.Kind);
    }

    [Fact]
    public void ATakeWhoseTargetVanished_PlansNothing()
    {


        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(11);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));

        var journal = JournalWith(TakeRecord(11, SealJournalPhase.TakeBegin, Path.Combine(dir.Root, "vanished.txt")));
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.None, action.Kind);
        Assert.False(action.NeedsUserDecision);
    }

    [Fact]
    public void AFolderRestoreInterruptedBeforeExtraction_SurfacesTheScratchArchive()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(33);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));

        var target = Path.Combine(dir.Root, "restored-folder");
        var scratch = SealTakeOperation.TemporaryArchiveFor(target, id);
        File.WriteAllBytes(scratch, plaintext);

        var journal = JournalWith(TakeRecord(33, SealJournalPhase.TakeBegin, target));
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.AskCleanupPartialTarget, action.Kind);
        Assert.True(action.NeedsUserDecision);
    }

    [Fact]
    public async Task CleaningUpTheScratchArchive_DeletesItAndKeepsTheIndexEntry()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(34);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext))).WithItem(Item(HexId(35), WrongDigest(35)));
        index.Save(paths.Index, Vmk);

        var target = Path.Combine(dir.Root, "restored-folder");
        var scratch = SealTakeOperation.TemporaryArchiveFor(target, id);
        File.WriteAllBytes(scratch, plaintext);

        var journal = JournalWith(TakeRecord(34, SealJournalPhase.TakeBegin, target));
        journal.Save(paths.Journal, Vmk);
        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));
        var action = Assert.Single(plan.Actions);

        Assert.True(await SealRecovery.ResolveAsync(
            index, journal, paths, Vmk, action, SealRecoveryChoice.CleanupPartialTarget,
            new SealFileProbe(paths, index)));

        Assert.False(File.Exists(scratch));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
        Assert.NotNull(SealIndex.Load(paths.Index, Vmk).Find(id));
    }

    [Fact]
    public async Task CleaningUpAfterTheEntryIsGone_KeepsTheExtractedFolder()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(36);
        var target = Path.Combine(dir.Root, "restored-folder");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "kept.txt"), "extracted");
        var scratch = SealTakeOperation.TemporaryArchiveFor(target, id);
        File.WriteAllBytes(scratch, [1, 2, 3]);

        var journal = JournalWith(TakeRecord(36, SealJournalPhase.Taken, target));
        journal.Save(paths.Journal, Vmk);
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var action = Assert.Single(plan.Actions);
        Assert.Equal(SealRecoveryActionKind.AskCleanupPartialTarget, action.Kind);

        Assert.True(await SealRecovery.ResolveAsync(
            SealIndex.Empty, journal, paths, Vmk, action, SealRecoveryChoice.CleanupPartialTarget,
            new SealFileProbe(paths, SealIndex.Empty)));

        Assert.False(File.Exists(scratch));
        Assert.True(File.Exists(Path.Combine(target, "kept.txt")));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task ANullOutcome_DropsItsJournalRecord()
    {


        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("完整的正文内容");
        var id = HexId(12);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));
        index.Save(paths.Index, Vmk);

        var journal = JournalWith(TakeRecord(12, SealJournalPhase.TakeBegin, Path.Combine(dir.Root, "vanished.txt")));
        journal.Save(paths.Journal, Vmk);

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));
        var result = await SealRecovery.ApplyAsync(index, journal, plan, paths, Vmk);

        Assert.Equal(1, result.AppliedCount);
        Assert.Empty(result.Deferred);
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
        Assert.NotNull(SealIndex.Load(paths.Index, Vmk).Find(id));
    }

    [Fact]
    public void PlanningNeverDeletesAnything()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("两边都在");
        var id = HexId(10);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));
        var target = Path.Combine(dir.Root, "target.txt");
        File.WriteAllBytes(target, plaintext);

        var probe = new CountingProbe();
        var plan = SealRecovery.Plan(JournalWith(TakeRecord(10, SealJournalPhase.Taken, target)), index, probe);

        Assert.Equal(SealRecoveryActionKind.BothSidesExist, Assert.Single(plan.Actions).Kind);
        Assert.Equal(0, probe.DeleteCalls);
        Assert.True(probe.TargetQueries > 0);
    }

    [Fact]
    public async Task TheRunnerLeavesBothSidesAlone()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("谁也别删");
        var id = HexId(11);
        var index = SealIndex.Empty.WithItem(Item(id, Digest(plaintext)));
        var target = Path.Combine(dir.Root, "target.txt");
        File.WriteAllBytes(target, plaintext);

        var container = paths.ItemPath(id);
        WriteContainer(container, id, plaintext);
        var containerHash = Digest(File.ReadAllBytes(container));

        var journal = JournalWith(TakeRecord(11, SealJournalPhase.TakeBegin, target));
        journal.Save(paths.Journal, Vmk);

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));
        var result = await SealRecovery.ApplyAsync(index, journal, plan, paths, Vmk);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(SealRecoveryActionKind.BothSidesExist, Assert.Single(result.Deferred).Kind);
        Assert.Equal(plaintext, File.ReadAllBytes(target));
        Assert.Equal(containerHash, Digest(File.ReadAllBytes(container)));
    }



    [Fact]
    public async Task ACompleteSealFromTheJournal_LandsInTheIndexWithTheVerifiedDigest()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("校验通过，但索引没写上");
        var id = HexId(12);
        var digest = WriteContainer(paths.SealingPath(id), id, plaintext);

        var journal = JournalWith(
            SealRecord(12, SealJournalPhase.SealBegin),
            SealRecord(12, SealJournalPhase.Sealed, draft: Draft(digest)));

        journal.Save(paths.Journal, Vmk);
        SealIndex.Empty.Save(paths.Index, Vmk);

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var result = await SealRecovery.ApplyAsync(SealIndex.Empty, journal, plan, paths, Vmk);

        Assert.Equal(1, result.AppliedCount);
        Assert.Empty(result.Deferred);

        var reopened = SealIndex.Load(paths.Index, Vmk);
        var item = Assert.Single(reopened.Items);
        Assert.Equal(digest, item.ContentSha256);
        Assert.Equal(id, item.Id);
        Assert.Equal("旅行照片.jpg", item.OriginalName);
        Assert.Equal("一段备注", item.Note);
        Assert.Null(item.FolderEntryCount);

        Assert.True(File.Exists(paths.ItemPath(id)));
        Assert.False(File.Exists(paths.SealingPath(id)));
        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
    }

    [Fact]
    public async Task ADigestMismatch_IsLeftForReview()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(13);
        WriteContainer(paths.SealingPath(id), id, Encoding.UTF8.GetBytes("被篡改的容器"));

        var journal = JournalWith(
            SealRecord(13, SealJournalPhase.SealBegin),
            SealRecord(13, SealJournalPhase.Sealed, draft: Draft(WrongDigest(13))));

        journal.Save(paths.Journal, Vmk);
        SealIndex.Empty.Save(paths.Index, Vmk);

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var result = await SealRecovery.ApplyAsync(SealIndex.Empty, journal, plan, paths, Vmk);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(SealRecoveryActionKind.CompleteSealFromJournal, Assert.Single(result.Deferred).Kind);
        Assert.Empty(SealIndex.Load(paths.Index, Vmk).Items);
        var stillThere = SealJournal.Load(paths.Journal, Vmk).Records;
        Assert.Equal(2, stillThere.Count);
    }

    [Fact]
    public async Task TheVerificationPath_CanOpenItsOwnContainer()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(40);
        var plaintext = Encoding.UTF8.GetBytes("open me");
        var digest = WriteContainer(paths.SealingPath(id), id, plaintext);

        var bytes = File.ReadAllBytes(paths.SealingPath(id));
        Assert.True(SealContainerHeader.TryRead(bytes, out var header, out var headerError));
        Assert.Equal(SealFormatError.None, headerError);
        Assert.True(SealKeySlot.TryUnwrap(
            Vmk, header.KeySlot, bytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize), out var contentKey, out var slotError));
        Assert.Equal(SealFormatError.None, slotError);

        using var body = new MemoryStream(bytes, writable: false);
        var read = await SealContainerReader.ReadAsync(body, contentKey, static (_, _) => Task.CompletedTask);

        Assert.Equal(digest, Convert.ToHexString(read.PlaintextSha256).ToLowerInvariant());
    }

    [Fact]
    public async Task ACompletedSealIsReachableAfterARestart()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var plaintext = Encoding.UTF8.GetBytes("重启后依然在");
        var id = HexId(14);
        var digest = WriteContainer(paths.SealingPath(id), id, plaintext);

        var journal = JournalWith(SealRecord(14, SealJournalPhase.Sealed, draft: Draft(digest)));
        journal.Save(paths.Journal, Vmk);
        SealIndex.Empty.Save(paths.Index, Vmk);

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        await SealRecovery.ApplyAsync(SealIndex.Empty, journal, plan, paths, Vmk);

        var second = SealRecovery.Plan(
            SealJournal.Load(paths.Journal, Vmk),
            SealIndex.Load(paths.Index, Vmk),
            new SealFileProbe(paths, SealIndex.Load(paths.Index, Vmk)));

        Assert.Empty(second.Actions);
        Assert.False(second.JournalNeedsAttention);
    }




    [Theory]
    [InlineData(SealRecoveryActionKind.OrphanSourceRemains, SealRecoveryChoice.TryDeleteSource, true)]
    [InlineData(SealRecoveryActionKind.OrphanSourceRemains, SealRecoveryChoice.KeepBoth, false)]
    [InlineData(SealRecoveryActionKind.BothSidesExist, SealRecoveryChoice.KeepBoxEntry, true)]
    [InlineData(SealRecoveryActionKind.BothSidesExist, SealRecoveryChoice.DiscardStaging, false)]
    [InlineData(SealRecoveryActionKind.AskCleanupPartialTarget, SealRecoveryChoice.KeepPartialTarget, true)]
    [InlineData(SealRecoveryActionKind.OfferRetrySeal, SealRecoveryChoice.DiscardStaging, true)]
    [InlineData(SealRecoveryActionKind.SourceLost, SealRecoveryChoice.KeepTarget, false)]
    [InlineData(SealRecoveryActionKind.None, SealRecoveryChoice.KeepBoth, false)]
    public void ChoiceMatches_OnlyAcceptsThePairsTheResolveSwitchHandles(
        SealRecoveryActionKind kind, SealRecoveryChoice choice, bool expected)
        => Assert.Equal(expected, SealRecovery.ChoiceMatches(kind, choice));



    [Fact]
    public void SweepKeepsIndexedEntriesAndDeletesOnlyTheRest()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var keep = HexId(20);
        var stray = HexId(21);
        var fragment = HexId(22);

        WriteContainer(paths.ItemPath(keep), keep, [1, 2, 3]);
        WriteContainer(paths.ItemPath(stray), stray, [4, 5, 6]);
        WriteContainer(paths.SealingPath(fragment), fragment, [7, 8, 9]);
        File.WriteAllText(Path.Combine(paths.Items, "notes.txt"), "keep me");

        var keepHash = Digest(File.ReadAllBytes(paths.ItemPath(keep)));
        var index = SealIndex.Empty.WithItem(Item(keep, WrongDigest(20)));

        var result = SealRecovery.Sweep(paths, index, SealJournal.Empty);

        Assert.Equal(2, result.ContainersRemoved);
        Assert.True(File.Exists(paths.ItemPath(keep)));
        Assert.False(File.Exists(paths.ItemPath(stray)));
        Assert.False(File.Exists(paths.SealingPath(fragment)));
        Assert.Equal(keepHash, Digest(File.ReadAllBytes(paths.ItemPath(keep))));
        Assert.True(File.Exists(Path.Combine(paths.Items, "notes.txt")));
    }

    [Fact]
    public async Task AnInterruptedSealCanBeDiscardedExplicitly()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);
        var source = Path.Combine(dir.Root, "original.bin");
        File.WriteAllBytes(source, [1, 2, 3]);

        var id = HexId(25);
        WriteContainer(paths.SealingPath(id), id, [9, 9, 9]);

        var journal = JournalWith(SealRecord(25, SealJournalPhase.SealBegin, source, Draft("")));
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        var retry = plan.Actions.Single(a => a.Kind == SealRecoveryActionKind.OfferRetrySeal);

        Assert.True(await SealRecovery.ResolveAsync(
            SealIndex.Empty, journal, paths, Vmk, retry, SealRecoveryChoice.DiscardStaging,
            new SealFileProbe(paths, SealIndex.Empty)));

        Assert.Empty(SealJournal.Load(paths.Journal, Vmk).Records);
        Assert.False(File.Exists(paths.SealingPath(id)));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void SweepKeepsAFragmentAnUnfinishedJournalStillClaims()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var claimed = HexId(23);
        var orphan = HexId(24);
        WriteContainer(paths.SealingPath(claimed), claimed, [1]);
        WriteContainer(paths.SealingPath(orphan), orphan, [2]);

        var journal = JournalWith(SealRecord(23, SealJournalPhase.SealBegin, draft: Draft("")));
        var result = SealRecovery.Sweep(paths, SealIndex.Empty, journal);

        Assert.Equal(1, result.ContainersRemoved);
        Assert.True(File.Exists(paths.SealingPath(claimed)));
        Assert.False(File.Exists(paths.SealingPath(orphan)));
    }

    [Fact]
    public void SweepClearsStagingArchivesAndHalfImportedBundles()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var packOne = Path.Combine(paths.Staging, "pack-one.zip");
        var packTwo = Path.Combine(paths.Staging, "pack-two.zip");
        var unrelated = Path.Combine(paths.Staging, "keep.txt");
        File.WriteAllBytes(packOne, [1]);
        File.WriteAllBytes(packTwo, [2]);
        File.WriteAllText(unrelated, "keep");

        var bundle = Path.Combine(paths.ImportStaging, HexId(30));
        Directory.CreateDirectory(Path.Combine(bundle, "deep"));
        File.WriteAllBytes(Path.Combine(bundle, "deep", "entry.novaseal"), [3]);

        var otherBundle = Path.Combine(paths.ImportStaging, HexId(31));
        Directory.CreateDirectory(otherBundle);

        var result = SealRecovery.Sweep(paths, SealIndex.Empty, SealJournal.Empty);

        Assert.Equal(2, result.StagingArchivesRemoved);
        Assert.Equal(2, result.ImportBundlesRemoved);
        Assert.False(File.Exists(packOne));
        Assert.False(File.Exists(packTwo));
        Assert.True(File.Exists(unrelated));
        Assert.False(Directory.Exists(bundle));
        Assert.False(Directory.Exists(otherBundle));
        Assert.True(Directory.Exists(paths.ImportStaging));
    }


    [Fact]
    public void SweepClearsRootTempFiles()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var indexTemp = Path.Combine(paths.Root, "index.nseal.tmp");
        var journalTemp = Path.Combine(paths.Root, "journal.nsj.tmp");
        var hint = Path.Combine(paths.Root, "hint.txt");
        File.WriteAllText(indexTemp, "half a write");
        File.WriteAllText(journalTemp, "half a write");
        File.WriteAllText(hint, "keep");

        SealRecovery.Sweep(paths, SealIndex.Empty, SealJournal.Empty);

        Assert.False(File.Exists(indexTemp));
        Assert.False(File.Exists(journalTemp));
        Assert.True(File.Exists(hint));
    }

    [Fact]
    public void AnUnreadableJournalStopsTheContainerSweep()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var stray = HexId(25);
        WriteContainer(paths.ItemPath(stray), stray, [1, 2]);


        var bytes = new byte[SealJournal.HeaderSize + SealFormat.TagSize];
        Encoding.ASCII.GetBytes("NSEALJNL").CopyTo(bytes, 0);
        bytes[8] = 1;
        var journal = SealJournal.Load(WriteBytes(dir, "journal.nsj", bytes), Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, journal.Status);

        var result = SealRecovery.Sweep(paths, SealIndex.Empty, journal);

        Assert.Equal(0, result.ContainersRemoved);
        Assert.True(File.Exists(paths.ItemPath(stray)));
    }

    [Fact]
    public void AnUnreadableJournalStopsTheStagingSweep()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var pack = Path.Combine(paths.Staging, "pack-" + HexId(26) + ".zip");
        File.WriteAllBytes(pack, [1, 2]);


        var bytes = new byte[SealJournal.HeaderSize + SealFormat.TagSize];
        Encoding.ASCII.GetBytes("NSEALJNL").CopyTo(bytes, 0);
        bytes[8] = 1;
        var journal = SealJournal.Load(WriteBytes(dir, "journal.nsj", bytes), Vmk);

        Assert.Equal(SealJournalStatus.Unreadable, journal.Status);

        var result = SealRecovery.Sweep(paths, SealIndex.Empty, journal);

        Assert.Equal(0, result.StagingArchivesRemoved);
        Assert.True(File.Exists(pack));
    }

    [Fact]
    public void AStagingPackClaimedByAnUnfinishedJournalSurvivesTheSweep()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var claimed = HexId(41);
        var orphan = HexId(42);
        var claimedPack = Path.Combine(paths.Staging, $"pack-{claimed}.zip");
        var orphanPack = Path.Combine(paths.Staging, $"pack-{orphan}.zip");
        File.WriteAllBytes(claimedPack, [1, 2, 3]);
        File.WriteAllBytes(orphanPack, [4]);

        var journal = SealJournal.Empty.Append(new SealJournalRecord
        {
            OpId = HexId(10_041),
            Op = SealJournalOp.Seal,
            Phase = "seal_begin",
            ItemId = claimed,
            SourcePath = @"C:\source\original.bin",
            StartedAtUtc = 1_700_000_000,
        });
        journal.Save(paths.Journal, Vmk);
        var loaded = SealJournal.Load(paths.Journal, Vmk);

        var result = SealRecovery.Sweep(paths, SealIndex.Empty, loaded);

        Assert.Equal(1, result.StagingArchivesRemoved);
        Assert.True(File.Exists(claimedPack));
        Assert.False(File.Exists(orphanPack));
    }



    [Fact]
    public void ANewerJournalIsReportedAsAttentionInsteadOfNothing()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var bytes = new byte[SealJournal.HeaderSize + SealFormat.TagSize];
        Encoding.ASCII.GetBytes("NSEALJNL").CopyTo(bytes, 0);
        bytes[8] = 2;

        var journal = SealJournal.Load(WriteBytes(dir, "journal.nsj", bytes), Vmk);
        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));

        Assert.Equal(SealJournalStatus.VersionUnsupported, plan.JournalStatus);
        Assert.True(plan.JournalNeedsAttention);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void AMalformedRecordProducesNoAutomaticAction()
    {
        using var dir = new TempDir();
        var paths = NewPaths(dir);

        var id = HexId(26);
        WriteContainer(paths.SealingPath(id), id, [1]);
        var stray = HexId(27);
        WriteContainer(paths.ItemPath(stray), stray, [2]);

        var json = Encoding.UTF8.GetBytes(
            $$"""[{"opId":"{{HexId(90_026)}}","op":"seal","phase":"seal_begin","sourcePath":"x","startedAtUtc":{{StartedAt}}}]""");

        var header = new byte[SealJournal.HeaderSize];
        Encoding.ASCII.GetBytes("NSEALJNL").CopyTo(header, 0);
        header[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), 1);
        var nonce = RandomNumberGenerator.GetBytes(SealFormat.NonceSize);
        nonce.CopyTo(header.AsSpan(13, SealFormat.NonceSize));

        var sealedBytes = CryptoService.EncryptGcmWithNonce(json, Vmk, nonce, header.AsSpan(8, 5).ToArray());
        var file = new byte[header.Length + sealedBytes.Length];
        header.CopyTo(file, 0);
        sealedBytes.CopyTo(file, header.Length);
        File.WriteAllBytes(paths.Journal, file);

        var journal = SealJournal.Load(paths.Journal, Vmk);
        Assert.True(journal.HasMalformedRecords);

        var plan = SealRecovery.Plan(journal, SealIndex.Empty, new SealFileProbe(paths, SealIndex.Empty));
        Assert.Empty(plan.Actions);

        var sweep = SealRecovery.Sweep(paths, SealIndex.Empty, journal);
        Assert.Equal(0, sweep.ContainersRemoved);
        Assert.True(File.Exists(paths.ItemPath(stray)));
        Assert.True(File.Exists(paths.SealingPath(id)));
    }

    private static string WriteBytes(TempDir dir, string name, byte[] bytes)
    {
        var path = Path.Combine(dir.Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
