using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public readonly record struct SealRecoveryResult(int AppliedCount, IReadOnlyList<SealRecoveryAction> Deferred);


public readonly record struct SealSweepResult(
    int ContainersRemoved,
    int StagingArchivesRemoved,
    int ImportBundlesRemoved);

















public static class SealRecovery
{




    public static SealRecoveryPlan Plan(SealJournal journal, SealIndex index, ISealFileProbe probe)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(probe);

        var actions = new List<SealRecoveryAction>();

        if (journal.Status != SealJournalStatus.Ok)
            return new SealRecoveryPlan(journal.Status, journal.HasMalformedRecords, actions);

        foreach (var operation in GroupByOperation(journal))
        {


            if (operation.Any(r => r.IsMalformed)) continue;

            var last = operation.OrderByDescending(r => PhaseRank(r.Phase)).First();

            if (string.Equals(last.Op, SealJournalOp.Seal, StringComparison.Ordinal))
                PlanSeal(last, index, probe, actions);
            else
                PlanTake(last, index, probe, actions);
        }

        return new SealRecoveryPlan(journal.Status, journal.HasMalformedRecords, actions);
    }

    private static void PlanSeal(
        SealJournalRecord last, SealIndex index, ISealFileProbe probe, List<SealRecoveryAction> actions)
    {
        var inIndex = index.Find(last.ItemId) is not null;

        if (string.Equals(last.Phase, SealJournalPhase.Deleted, StringComparison.Ordinal))
        {


            if (inIndex) Add(SealRecoveryActionKind.FinalizeSeal);
            else if (probe.ContainerExists(last.ItemId)) Add(SealRecoveryActionKind.CompleteSealFromJournal);




            else Add(SealRecoveryActionKind.SourceLost);
        }
        else if (inIndex)
        {

            Add(probe.SourceExists(last.SourcePath)
                ? SealRecoveryActionKind.OrphanSourceRemains
                : SealRecoveryActionKind.FinalizeSeal);
        }
        else if (string.Equals(last.Phase, SealJournalPhase.SealBegin, StringComparison.Ordinal))
        {




            if (probe.SourceExists(last.SourcePath))
            {
                if (probe.ContainerExists(last.ItemId)) Add(SealRecoveryActionKind.DiscardStaging);
                Add(SealRecoveryActionKind.OfferRetrySeal);
            }
            else
            {


                Add(probe.ContainerExists(last.ItemId)
                    ? SealRecoveryActionKind.StagingOnly
                    : SealRecoveryActionKind.SourceLost);
            }
        }
        else if (!probe.SourceExists(last.SourcePath))
        {

            Add(SealRecoveryActionKind.CompleteSealFromJournal);
        }
        else
        {


            if (probe.ContainerExists(last.ItemId)) Add(SealRecoveryActionKind.DiscardStaging);
            Add(SealRecoveryActionKind.OfferRetrySeal);
        }

        void Add(SealRecoveryActionKind kind)
            => actions.Add(new SealRecoveryAction(kind, last.OpId, last.ItemId));
    }

    private static void PlanTake(
        SealJournalRecord last, SealIndex index, ISealFileProbe probe, List<SealRecoveryAction> actions)
    {
        if (string.IsNullOrEmpty(last.TargetPath))
        {

            if (index.Find(last.ItemId) is null)
            {
                actions.Add(new SealRecoveryAction(SealRecoveryActionKind.FinalizeTake, last.OpId, last.ItemId));
                return;
            }
            actions.Add(new SealRecoveryAction(SealRecoveryActionKind.None, last.OpId, last.ItemId));
            return;
        }




        var targetState = probe.TargetState(last.TargetPath!, last.ItemId);
        var scratch = SealTakeOperation.TemporaryArchiveFor(last.TargetPath!, last.ItemId);
        var scratchExists = probe.FileExists(scratch);

        if (index.Find(last.ItemId) is null)
        {



            actions.Add(scratchExists
                ? new SealRecoveryAction(SealRecoveryActionKind.AskCleanupPartialTarget, last.OpId, last.ItemId)
                : new SealRecoveryAction(SealRecoveryActionKind.FinalizeTake, last.OpId, last.ItemId));
            return;
        }

        var kind = targetState switch
        {
            SealTargetState.Missing when scratchExists => SealRecoveryActionKind.AskCleanupPartialTarget,
            SealTargetState.Missing => SealRecoveryActionKind.None,
            SealTargetState.Partial => SealRecoveryActionKind.AskCleanupPartialTarget,
            _ => SealRecoveryActionKind.BothSidesExist,
        };

        actions.Add(new SealRecoveryAction(kind, last.OpId, last.ItemId));
    }






    public static async Task<SealRecoveryResult> ApplyAsync(
        SealIndex index,
        SealJournal journal,
        SealRecoveryPlan plan,
        SealSpacePaths paths,
        byte[] vmk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        var currentIndex = index;
        var currentJournal = journal;
        var applied = 0;
        var deferred = new List<SealRecoveryAction>();

        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (action.Kind)
            {
                case SealRecoveryActionKind.DiscardStaging:

                    if (!DeleteUnindexedContainers(paths, currentIndex, action.ItemId))
                    {
                        deferred.Add(action);
                        break;
                    }


                    var retryPending = plan.Actions.Any(a =>
                        string.Equals(a.OpId, action.OpId, StringComparison.Ordinal)
                        && a.Kind == SealRecoveryActionKind.OfferRetrySeal);
                    if (!retryPending) currentJournal = currentJournal.Remove(action.OpId);
                    applied++;
                    break;

                case SealRecoveryActionKind.CompleteSealFromJournal:
                {
                    var record = currentJournal.Records.LastOrDefault(
                        r => string.Equals(r.OpId, action.OpId, StringComparison.Ordinal) && r.Draft is not null);

                    if (record?.Draft is null
                        || !await TryMaterialiseSealAsync(record, paths, vmk, cancellationToken).ConfigureAwait(false))
                    {
                        deferred.Add(action);
                        break;
                    }



                    currentIndex = currentIndex.WithItem(BuildItem(record));
                    currentIndex.Save(paths.Index, vmk);
                    currentJournal = currentJournal.Remove(action.OpId);
                    applied++;
                    break;
                }

                case SealRecoveryActionKind.FinalizeSeal:
                case SealRecoveryActionKind.FinalizeTake:
                    currentJournal = currentJournal.Remove(action.OpId);
                    applied++;
                    break;

                case SealRecoveryActionKind.None:




                    currentJournal = currentJournal.Remove(action.OpId);
                    applied++;
                    break;

                default:
                    deferred.Add(action);
                    break;
            }
        }

        if (applied > 0) currentJournal.Save(paths.Journal, vmk);

        return new SealRecoveryResult(applied, deferred);
    }











    public static async Task<bool> ResolveAsync(
        SealIndex index,
        SealJournal journal,
        SealSpacePaths paths,
        byte[] vmk,
        SealRecoveryAction action,
        SealRecoveryChoice choice,
        ISealFileProbe probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(probe);

        cancellationToken.ThrowIfCancellationRequested();

        switch (action.Kind)
        {
            case SealRecoveryActionKind.OrphanSourceRemains:
            {
                if (choice is SealRecoveryChoice.TryDeleteSource)
                {
                    if (!TryDeleteOriginal(index, action.ItemId, out var removed)) return false;
                    if (!removed) return false;
                }
                else if (choice is SealRecoveryChoice.SourceAlreadyGone)
                {


                    if (OriginalStillExists(index, action.ItemId)) return false;
                }
                else
                {
                    return false;
                }

                journal.Remove(action.OpId).Save(paths.Journal, vmk);
                return true;
            }

            case SealRecoveryActionKind.BothSidesExist:
            {
                if (choice is SealRecoveryChoice.KeepTarget)
                {


                    index.WithoutItem(action.ItemId).Save(paths.Index, vmk);
                    if (!TryDeleteIfPresent(paths.ItemPath(action.ItemId))) return false;
                    journal.Remove(action.OpId).Save(paths.Journal, vmk);
                    return true;
                }

                if (choice is SealRecoveryChoice.KeepBoxEntry)
                {
                    var target = TargetOf(journal, action.OpId);
                    if (target is null || !TryDeleteTarget(target)) return false;
                    journal.Remove(action.OpId).Save(paths.Journal, vmk);
                    return true;
                }

                if (choice is SealRecoveryChoice.KeepBoth)
                {

                    journal.Remove(action.OpId).Save(paths.Journal, vmk);
                    return true;
                }

                return false;
            }

            case SealRecoveryActionKind.AskCleanupPartialTarget:
            {
                if (choice is SealRecoveryChoice.CleanupPartialTarget)
                {
                    var target = TargetOf(journal, action.OpId);
                    if (target is null) return false;



                    var scratch = SealTakeOperation.TemporaryArchiveFor(target, action.ItemId);
                    if (!TryDeleteIfPresent(scratch)) return false;


                    if (index.Find(action.ItemId) is not null && !TryDeleteTarget(target)) return false;
                    journal.Remove(action.OpId).Save(paths.Journal, vmk);
                    return true;
                }

                if (choice is SealRecoveryChoice.KeepPartialTarget)
                {
                    journal.Remove(action.OpId).Save(paths.Journal, vmk);
                    return true;
                }

                return false;
            }

            case SealRecoveryActionKind.OfferRetrySeal:
            case SealRecoveryActionKind.StagingOnly:
            case SealRecoveryActionKind.SourceLost:
            {



                if (choice is not SealRecoveryChoice.DiscardStaging) return false;


                if (action.Kind == SealRecoveryActionKind.OfferRetrySeal)
                {
                    var record = journal.Records.FirstOrDefault(r => r.OpId == action.OpId);
                    if (record is null || !probe.SourceExists(record.SourcePath)) return false;
                }
                if (!DeleteUnindexedContainers(paths, index, action.ItemId)) return false;
                if (!TryDeleteIfPresent(Path.Combine(paths.Staging, $"pack-{action.ItemId}.zip"))) return false;
                journal.Remove(action.OpId).Save(paths.Journal, vmk);
                return true;
            }

            default:
                return false;
        }
    }






    internal static bool ChoiceMatches(SealRecoveryActionKind kind, SealRecoveryChoice choice) => kind switch
    {
        SealRecoveryActionKind.OrphanSourceRemains
            => choice is SealRecoveryChoice.TryDeleteSource or SealRecoveryChoice.SourceAlreadyGone,
        SealRecoveryActionKind.BothSidesExist
            => choice is SealRecoveryChoice.KeepTarget or SealRecoveryChoice.KeepBoxEntry or SealRecoveryChoice.KeepBoth,
        SealRecoveryActionKind.AskCleanupPartialTarget
            => choice is SealRecoveryChoice.CleanupPartialTarget or SealRecoveryChoice.KeepPartialTarget,
        SealRecoveryActionKind.OfferRetrySeal or SealRecoveryActionKind.StagingOnly or SealRecoveryActionKind.SourceLost
            => choice is SealRecoveryChoice.DiscardStaging,
        _ => false,
    };




    private static bool OriginalStillExists(SealIndex index, string itemId)
    {
        var item = index.Find(itemId);
        if (item is null || string.IsNullOrEmpty(item.SourcePath)) return false;
        return item.IsFolder ? Directory.Exists(item.SourcePath) : File.Exists(item.SourcePath);
    }

    private static bool TryDeleteOriginal(SealIndex index, string itemId, out bool removed)
    {
        removed = false;
        var item = index.Find(itemId);
        if (item is null || string.IsNullOrEmpty(item.SourcePath)) return true;

        try
        {
            if (item.IsFolder)
            {
                if (!Directory.Exists(item.SourcePath)) return true;
                Directory.Delete(item.SourcePath, recursive: true);
            }
            else
            {
                if (!File.Exists(item.SourcePath)) return true;
                File.Delete(item.SourcePath);
            }

            removed = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? TargetOf(SealJournal journal, string opId)
        => journal.Records.LastOrDefault(r => string.Equals(r.OpId, opId, StringComparison.Ordinal))?.TargetPath;

    private static bool TryDeleteTarget(string target)
    {
        try
        {
            if (File.Exists(target)) File.Delete(target);
            else if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }














    public static SealSweepResult Sweep(SealSpacePaths paths, SealIndex index, SealJournal journal)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(journal);

        var containers = 0;
        var journalTrusted = journal.Status == SealJournalStatus.Ok && !journal.HasMalformedRecords;
        var pending = new HashSet<string>(
            journalTrusted
                ? journal.Records.Where(r => !r.IsMalformed).Select(r => r.ItemId)
                : Enumerable.Empty<string>(),
            StringComparer.Ordinal);

        if (journalTrusted && Directory.Exists(paths.Items))
        {
            var indexed = new HashSet<string>(index.Items.Select(i => i.Id), StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(paths.Items))
            {
                var extension = Path.GetExtension(file);
                if (!IsContainerExtension(extension)) continue;

                var id = Path.GetFileNameWithoutExtension(file);
                if (indexed.Contains(id) || pending.Contains(id)) continue;

                if (TryDeleteFile(file)) containers++;
            }
        }

        var archives = 0;
        if (journalTrusted && Directory.Exists(paths.Staging))
        {
            foreach (var file in Directory.EnumerateFiles(paths.Staging, "pack-*.zip"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                var owner = stem.StartsWith("pack-", StringComparison.Ordinal) ? stem["pack-".Length..] : string.Empty;
                if (owner.Length > 0 && pending.Contains(owner)) continue;
                if (TryDeleteFile(file)) archives++;
            }
        }



        if (Directory.Exists(paths.Root))
        {
            foreach (var file in Directory.EnumerateFiles(paths.Root, "*.tmp"))
                TryDeleteFile(file);
        }

        var bundles = 0;
        if (Directory.Exists(paths.ImportStaging))
        {
            foreach (var directory in Directory.EnumerateDirectories(paths.ImportStaging))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    bundles++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {


                }
            }
        }

        return new SealSweepResult(containers, archives, bundles);
    }



    private static bool DeleteUnindexedContainers(SealSpacePaths paths, SealIndex index, string itemId)
    {
        var ok = TryDeleteIfPresent(paths.SealingPath(itemId));



        if (index.Find(itemId) is null) ok &= TryDeleteIfPresent(paths.ItemPath(itemId));
        return ok;
    }






    private static bool TryDeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }





    private static async Task<bool> TryMaterialiseSealAsync(
        SealJournalRecord record, SealSpacePaths paths, byte[] vmk, CancellationToken cancellationToken)
    {
        var staging = paths.SealingPath(record.ItemId);
        var committed = paths.ItemPath(record.ItemId);

        var source = File.Exists(staging) ? staging : File.Exists(committed) ? committed : null;
        if (source is null) return false;

        try
        {
            await using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var headerBytes = new byte[SealFormat.HeaderSize];
                await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);

                if (!SealContainerHeader.TryRead(headerBytes, out var header, out _)) return false;
                if (!SealKeySlot.TryUnwrap(
                        vmk,
                        header.KeySlot,
                        headerBytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize),
                        out var contentKey,
                        out _))
                {
                    return false;
                }

                try
                {


                    stream.Position = 0;

                    var read = await SealContainerReader.ReadAsync(
                            stream, contentKey, static (_, _) => Task.CompletedTask, null, cancellationToken)
                        .ConfigureAwait(false);

                    var digest = Convert.ToHexString(read.PlaintextSha256).ToLowerInvariant();
                    if (!string.Equals(digest, record.Draft!.ContentSha256, StringComparison.Ordinal))
                        return false;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(contentKey);
                }
            }

            if (string.Equals(source, committed, StringComparison.Ordinal)) return true;
            if (File.Exists(committed)) return false;

            File.Move(source, committed);
            return true;
        }
        catch (Exception ex) when (ex is SealCorruptException
                                      or SealVersionUnsupportedException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static SealedItem BuildItem(SealJournalRecord record)
    {
        var draft = record.Draft!;

        return new SealedItem
        {
            Id = record.ItemId,
            DisplayName = draft.DisplayName,
            OriginalName = draft.OriginalName,
            SourcePath = record.SourcePath,
            Size = draft.Size,
            SealedAtUtc = draft.SealedAtUtc,
            Kind = draft.Kind,
            ExpiryUtc = draft.ExpiryUtc,
            Note = draft.Note,
            ContentSha256 = draft.ContentSha256,
            FolderEntryCount = draft.FolderEntryCount,
            Flags = 0,
        };
    }

    private static IEnumerable<List<SealJournalRecord>> GroupByOperation(SealJournal journal)
    {
        var order = new List<List<SealJournalRecord>>();
        var byOp = new Dictionary<string, List<SealJournalRecord>>(StringComparer.Ordinal);

        foreach (var record in journal.Records)
        {
            if (!byOp.TryGetValue(record.OpId, out var operation))
            {
                operation = [];
                byOp[record.OpId] = operation;
                order.Add(operation);
            }

            operation.Add(record);
        }

        return order;
    }

    private static int PhaseRank(string phase) => phase switch
    {
        SealJournalPhase.SealBegin or SealJournalPhase.TakeBegin => 0,
        SealJournalPhase.Sealed or SealJournalPhase.Taken => 1,
        SealJournalPhase.SealIndexed => 2,
        SealJournalPhase.Deleted => 3,
        _ => -1,
    };

    private static bool IsContainerExtension(string extension)
        => extension.Equals(".novaseal", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".sealing", StringComparison.OrdinalIgnoreCase);

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}





public sealed class SealFileProbe(SealSpacePaths paths, SealIndex index) : ISealFileProbe
{
    public bool SourceExists(string sourcePath)
        => !string.IsNullOrEmpty(sourcePath)
           && (File.Exists(sourcePath) || Directory.Exists(sourcePath));

    public bool ContainerExists(string itemId)
        => File.Exists(paths.SealingPath(itemId)) || File.Exists(paths.ItemPath(itemId));

    public bool FileExists(string path)
        => !string.IsNullOrEmpty(path) && File.Exists(path);






    public SealTargetState TargetState(string targetPath, string itemId)
    {
        if (string.IsNullOrEmpty(targetPath)) return SealTargetState.Missing;

        if (Directory.Exists(targetPath)) return SealTargetState.Partial;
        if (!File.Exists(targetPath)) return SealTargetState.Missing;

        var expected = index.Find(itemId)?.ContentSha256;
        if (string.IsNullOrEmpty(expected)) return SealTargetState.Partial;

        try
        {
            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(digest, expected, StringComparison.Ordinal)
                ? SealTargetState.Complete
                : SealTargetState.Partial;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealTargetState.Partial;
        }
    }
}
