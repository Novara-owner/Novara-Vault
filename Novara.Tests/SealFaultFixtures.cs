using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novara.Models;
using Novara.Services;

namespace Novara.Tests;









internal static class SealFaultFixtures
{

    internal static readonly byte[] TestVmk =
        Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x31 + i)).ToArray();

    internal const long SealedAt = 1_790_000_000;

    internal static readonly byte[] Plain = Encoding.UTF8.GetBytes("fault-injection fixture payload");

    internal static readonly byte[] FixedNonce = [0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B];


    internal static string DefaultRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Novara.csproj")))
            directory = directory.Parent;

        if (directory is null) throw new InvalidOperationException("repository root not found");

        return Path.Combine(directory.FullName, "Novara.Tests", "Fixtures", "seal-fault");
    }


    internal static IReadOnlyList<string> Emit(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        var names = new List<string>();

        foreach (var faultCase in Cases())
        {
            var caseRoot = Path.Combine(root, faultCase.Name);
            Directory.CreateDirectory(caseRoot);

            faultCase.Arrange(caseRoot);

            File.WriteAllText(
                Path.Combine(caseRoot, "expected.json"),
                JsonSerializer.Serialize(new { @case = faultCase.Name, setup = faultCase.Setup, actions = faultCase.Expected },
                    new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            names.Add(faultCase.Name);
        }

        return names;
    }

    private sealed record FaultCase(string Name, string Setup, string[] Expected, Action<string> Arrange);

    private static IReadOnlyList<FaultCase> Cases() =>
    [
        new("journal_before_write", "empty journal and index, nothing on disk",
            [], root => Arrange(root, Layout.Nothing)),

        new("staging_write_in_progress", "seal_begin, original on disk, half-written container",
            [nameof(SealRecoveryActionKind.DiscardStaging), nameof(SealRecoveryActionKind.OfferRetrySeal)],
            root => Arrange(root, Layout.StagingOnly)),

        new("verified_source_present", "sealed record, original still on disk, verified container",
            [nameof(SealRecoveryActionKind.DiscardStaging), nameof(SealRecoveryActionKind.OfferRetrySeal)],
            root => Arrange(root, Layout.VerifiedOriginalPresent)),

        new("verified_source_gone", "sealed record, original gone, verified container",
            [nameof(SealRecoveryActionKind.CompleteSealFromJournal)],
            root => Arrange(root, Layout.VerifiedOriginalGone)),

        new("seal_index_before_delete_source", "entry indexed, original still on disk",
            [nameof(SealRecoveryActionKind.OrphanSourceRemains)],
            root => Arrange(root, Layout.IndexedOriginalPresent)),

        new("deleted_before_cleanup", "entry indexed, original deleted, deleted phase recorded",
            [nameof(SealRecoveryActionKind.FinalizeSeal)],
            root => Arrange(root, Layout.DeletedOriginalGone)),

        new("take_target_in_progress", "take_begin, entry indexed, half-written target",
            [nameof(SealRecoveryActionKind.AskCleanupPartialTarget)],
            root => Arrange(root, Layout.TakePartialTarget)),

        new("take_both_sides_exist", "taken phase, entry indexed, complete target",
            [nameof(SealRecoveryActionKind.BothSidesExist)],
            root => Arrange(root, Layout.TakeBothSides)),

        new("take_after_remove", "taken phase, entry already gone, complete target",
            [nameof(SealRecoveryActionKind.FinalizeTake)],
            root => Arrange(root, Layout.TakeAfterRemove)),
    ];

    private enum Layout
    {
        Nothing,
        StagingOnly,
        VerifiedOriginalPresent,
        VerifiedOriginalGone,
        IndexedOriginalPresent,
        DeletedOriginalGone,
        TakePartialTarget,
        TakeBothSides,
        TakeAfterRemove,
    }

    private static void Arrange(string caseRoot, Layout layout)
    {
        var paths = new SealSpacePaths(Path.Combine(caseRoot, "seal"));
        paths.EnsureCreated();

        var id = ItemIdOf(layout);

        var index = SealIndex.Empty;
        var journal = SealJournal.Empty;

        if (layout is not Layout.Nothing)
        {
            var digest = Convert.ToHexString(SHA256.HashData(Plain)).ToLowerInvariant();

            var sourceExists = layout is Layout.StagingOnly or Layout.VerifiedOriginalPresent
                or Layout.IndexedOriginalPresent;

            if (sourceExists) WriteSource(caseRoot);

            var inIndex = layout is Layout.IndexedOriginalPresent or Layout.DeletedOriginalGone
                or Layout.TakePartialTarget or Layout.TakeBothSides;

            if (inIndex) index = index.WithItem(Item(id, digest));


            var needsContainer = layout is Layout.StagingOnly or Layout.VerifiedOriginalPresent
                or Layout.VerifiedOriginalGone;

            if (needsContainer) WriteContainer(paths.SealingPath(id), id);

            if (layout is Layout.IndexedOriginalPresent or Layout.DeletedOriginalGone
                or Layout.TakePartialTarget or Layout.TakeBothSides or Layout.TakeAfterRemove)
            {
                WriteContainer(paths.ItemPath(id), id);
            }

            var targetExists = layout is Layout.TakePartialTarget or Layout.TakeBothSides or Layout.TakeAfterRemove;
            if (targetExists) WriteTarget(caseRoot, partial: layout == Layout.TakePartialTarget);

            journal = layout switch
            {
                Layout.StagingOnly => JournalWith(id, SealJournalPhase.SealBegin, draftHash: null),
                Layout.VerifiedOriginalPresent => JournalWith(id, SealJournalPhase.Sealed, draftHash: digest),
                Layout.VerifiedOriginalGone => JournalWith(id, SealJournalPhase.Sealed, draftHash: digest),
                Layout.IndexedOriginalPresent => JournalWith(id, SealJournalPhase.SealIndexed, draftHash: digest),
                Layout.DeletedOriginalGone => JournalWith(id, SealJournalPhase.Deleted, draftHash: digest),
                Layout.TakePartialTarget => TakeJournal(id, SealJournalPhase.TakeBegin),
                Layout.TakeBothSides => TakeJournal(id, SealJournalPhase.Taken),
                Layout.TakeAfterRemove => TakeJournal(id, SealJournalPhase.Taken),
                _ => SealJournal.Empty,
            };

            journal.SaveWithNonce(paths.Journal, TestVmk, FixedNonce);
        }
        else
        {
            journal.SaveWithNonce(paths.Journal, TestVmk, FixedNonce);
        }

        index.SaveWithNonce(paths.Index, TestVmk, FixedNonce);
    }

    private static void WriteSource(string caseRoot)
    {
        var directory = Path.Combine(caseRoot, "source");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "original.bin"), Plain);
    }

    private static void WriteTarget(string caseRoot, bool partial)
    {
        var directory = Path.Combine(caseRoot, "target");
        Directory.CreateDirectory(directory);

        var bytes = partial ? Plain[..(Plain.Length / 2)] : Plain;
        File.WriteAllBytes(Path.Combine(directory, "plain.txt"), bytes);
    }


    private static void WriteContainer(string path, string id)
    {
        var contentKey = Enumerable.Range(0, SealFormat.KeySize).Select(i => (byte)(0x90 + i)).ToArray();

        var header = SealContainerWriter.PrepareHeaderCore(
            Convert.FromHexString(id),
            (ulong)Plain.Length,
            SealedAt,
            SealFormat.MinChunkSize,
            TestVmk,
            contentKey,
            flags: 0,
            keySlotNonce: FixedNonce);

        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        SealContainerWriter.WriteAsync(
                new MemoryStream(Plain), header, contentKey, destination, null, CancellationToken.None,
                new ConstantNonceSource(FixedNonce))
            .GetAwaiter().GetResult();
    }

    private static SealJournal JournalWith(string id, string phase, string? draftHash)
    {
        var draft = new SealJournalDraft
        {
            OriginalName = "original.bin",
            DisplayName = "original.bin",
            Size = (ulong)Plain.Length,
            SealedAtUtc = SealedAt,
            Kind = SealedItemKind.Normal,
            ContentSha256 = draftHash ?? "",
            FolderEntryCount = null,
        };

        return SealJournal.Empty.Append(new SealJournalRecord
        {
            OpId = OpIdOf(id),
            Op = SealJournalOp.Seal,
            Phase = phase,
            ItemId = id,
            SourcePath = "%ROOT%/source/original.bin",
            StartedAtUtc = SealedAt,
            Draft = draft,
        });
    }

    private static SealJournal TakeJournal(string id, string phase)
        => SealJournal.Empty.Append(new SealJournalRecord
        {
            OpId = OpIdOf(id),
            Op = SealJournalOp.Take,
            Phase = phase,
            ItemId = id,
            SourcePath = "%ROOT%/source/original.bin",
            TargetPath = "%ROOT%/target/plain.txt",
            StartedAtUtc = SealedAt,
        });

    private static SealedItem Item(string id, string digest)
        => new()
        {
            Id = id,
            DisplayName = "original.bin",
            OriginalName = "original.bin",
            SourcePath = "source/original.bin",
            Size = (ulong)Plain.Length,
            SealedAtUtc = SealedAt,
            Kind = SealedItemKind.Normal,
            ContentSha256 = digest,
        };

    private static string ItemIdOf(Layout layout) => HashId(layout.ToString());

    private static string OpIdOf(string itemId) => HashId("op-" + itemId);

    private static string HashId(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant();


    private sealed class ConstantNonceSource(byte[] nonce) : ISealNonceSource
    {
        public void Fill(Span<byte> destination) => nonce.CopyTo(destination);
    }
}
