using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public interface ISealTakePorts
{
    SealIndex Index { get; }

    SealJournal Journal { get; }

    void AppendJournal(SealJournalRecord record);

    void RemoveJournal(string opId);

    void SaveIndex(SealIndex index);


    bool DeleteContainer(string path);





    void MarkVerified(string itemId);
}


public class SealTakePorts : ISealTakePorts
{
    private readonly byte[] _vmk;

    public SealTakePorts(SealSpacePaths paths, byte[] vmk)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        Paths = paths;
        _vmk = vmk;

        Index = SealIndex.LoadInitialized(paths.Index, vmk);


        Journal = SealJournal.LoadRequiredIfPresent(paths.Journal, vmk);

        if (Journal.Status != SealJournalStatus.Ok || Journal.HasMalformedRecords)
            throw new InvalidOperationException("the journal must be read and recovered before anything is taken");
    }

    public SealSpacePaths Paths { get; }

    public SealIndex Index { get; private set; }

    public SealJournal Journal { get; private set; }

    public void AppendJournal(SealJournalRecord record)
    {
        Journal = Journal.Append(record);
        Journal.Save(Paths.Journal, _vmk);
    }

    public void RemoveJournal(string opId)
    {
        Journal = Journal.Remove(opId);
        Journal.Save(Paths.Journal, _vmk);
    }

    public void SaveIndex(SealIndex index)
    {
        Index = index;
        Index.Save(Paths.Index, _vmk);
    }

    public virtual bool DeleteContainer(string path)
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


    public void MarkVerified(string itemId)
    {

    }
}

















public sealed class SealTakeOperation
{

    public const int MaxAutoNumberAttempts = 10_000;

    private readonly SealSpacePaths _paths;
    private readonly byte[] _vmk;
    private readonly ISealTakePorts _ports;
    private readonly Func<DateTimeOffset> _clock;

    public SealTakeOperation(
        SealSpacePaths paths, byte[] vmk, ISealTakePorts? ports = null, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        _paths = paths;
        _vmk = vmk;
        _ports = ports ?? new SealTakePorts(paths, vmk);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public SealIndex Index => _ports.Index;

    public SealJournal Journal => _ports.Journal;





    public static (string Path, SealTakeStatus Status) ResolveConflict(
        string targetDirectory,
        string originalName,
        SealTakeConflictPolicy policy,
        string? targetName = null,
        string suffix = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        ArgumentException.ThrowIfNullOrEmpty(originalName);

        var name = policy == SealTakeConflictPolicy.Rename && !string.IsNullOrWhiteSpace(targetName)
            ? targetName!.Trim()
            : originalName;






        if (name is "." or ".."
            || Path.GetFileName(name) != name
            || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || SealEligibility.IsReservedName(name))
            return (Path.Combine(targetDirectory, name), SealTakeStatus.TargetUnavailable);

        if (suffix.Length > 0 && !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            name += suffix;

        var candidate = Path.Combine(targetDirectory, name);
        if (!Occupied(candidate)) return (candidate, SealTakeStatus.Taken);



        if (policy != SealTakeConflictPolicy.AutoNumber) return (candidate, SealTakeStatus.Conflict);

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var n = 1; n <= MaxAutoNumberAttempts; n++)
        {
            var numbered = Path.Combine(targetDirectory, $"{stem} ({n}){extension}");
            if (!Occupied(numbered)) return (numbered, SealTakeStatus.Taken);
        }

        return (candidate, SealTakeStatus.Conflict);
    }

    public async Task<SealTakeResult> RunAsync(SealTakeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = _ports.Index.Find(request.ItemId);
        if (item is null) return new SealTakeResult(request.ItemId, SealTakeStatus.ItemMissing);




        if (request.Policy == SealTakeConflictPolicy.Cancel)
            return new SealTakeResult(request.ItemId, SealTakeStatus.Cancelled);

        var now = _clock();
        var verdict = SealTimeLock.Evaluate(item, now, _ports.Index.ObservedMaxUtc);


        if (verdict == SealTakeVerdict.RollbackSuspected)
            return new SealTakeResult(request.ItemId, SealTakeStatus.ClockSuspected);
        if (verdict == SealTakeVerdict.Locked)
            return new SealTakeResult(request.ItemId, SealTakeStatus.Locked, LockedUntilUtc: item.ExpiryUtc);

        if (string.IsNullOrWhiteSpace(request.TargetDirectory) || !Directory.Exists(request.TargetDirectory))
            return new SealTakeResult(request.ItemId, SealTakeStatus.TargetUnavailable);

        var archive = item.IsFolder && request.ExtractAsArchive;
        var (target, resolveStatus) = ResolveConflict(
            request.TargetDirectory,
            item.OriginalName,
            request.Policy,
            request.TargetName,
            archive ? ".zip" : "");

        if (resolveStatus != SealTakeStatus.Taken) return new SealTakeResult(request.ItemId, resolveStatus);



        var restoreFolder = item.IsFolder && !request.ExtractAsArchive;
        var temporaryArchive = restoreFolder ? TemporaryArchiveFor(target, item.Id) : null;
        var writeTarget = temporaryArchive ?? target;

        var opId = NewId();
        var startedAt = now.ToUnixTimeSeconds();

        _ports.AppendJournal(Record(opId, SealJournalPhase.TakeBegin, item, target, startedAt));

        try
        {
            await WritePlaintextAsync(item, writeTarget, overwrite: restoreFolder, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SealCorruptException)
        {





            if (TryDeleteFile(writeTarget)) _ports.RemoveJournal(opId);
            throw;
        }
        catch (OperationCanceledException)
        {

            var cleared = TryDeleteFile(writeTarget);
            if (cleared) _ports.RemoveJournal(opId);
            return new SealTakeResult(request.ItemId, SealTakeStatus.Cancelled, cleared ? null : writeTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {


            var cleared = TryDeleteFile(writeTarget);
            if (cleared) _ports.RemoveJournal(opId);
            return new SealTakeResult(request.ItemId, SealTakeStatus.TargetUnavailable,
                cleared ? null : writeTarget);
        }


        _ports.MarkVerified(item.Id);
        _ports.AppendJournal(Record(opId, SealJournalPhase.Taken, item, target, startedAt));
        _ports.SaveIndex(_ports.Index.WithoutItem(item.Id).WithObservedMax(now.ToUnixTimeSeconds()));





        var containerGone = _ports.DeleteContainer(_paths.ItemPath(item.Id));



        if (temporaryArchive is not null)
        {
            try
            {
                SealFolderPack.Extract(temporaryArchive, target, cancellationToken);
            }
            catch (Exception ex) when (ex is SealExtractFailedException or IOException or UnauthorizedAccessException
                                       or OperationCanceledException)
            {






                return new SealTakeResult(request.ItemId, SealTakeStatus.ExtractFailed, temporaryArchive);
            }
        }

        var archiveGone = temporaryArchive is null || TryDeleteFile(temporaryArchive);
        if (containerGone && archiveGone)
            _ports.RemoveJournal(opId);




        var leftoverPath = temporaryArchive is not null && !archiveGone ? temporaryArchive
            : !containerGone ? _paths.ItemPath(item.Id) : null;

        return new SealTakeResult(request.ItemId, SealTakeStatus.Taken, target, null, leftoverPath);
    }



    private async Task WritePlaintextAsync(
        SealedItem item, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        var containerPath = _paths.ItemPath(item.Id);
        if (!File.Exists(containerPath)) throw new SealCorruptException(SealCorruptReason.Truncated);

        await using var container = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var headerBytes = new byte[SealFormat.HeaderSize];
        await container.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        if (!SealContainerHeader.TryRead(headerBytes, out var header, out var formatError))
        {
            if (formatError == SealFormatError.VersionUnsupported)
                throw new SealVersionUnsupportedException(headerBytes[SealFormat.VersionOffset]);

            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
        }

        if (!SealKeySlot.TryUnwrap(
                _vmk, header.KeySlot, headerBytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize),
                out var contentKey, out _))
        {
            throw new SealCorruptException(SealCorruptReason.Authentication);
        }

        try
        {
            await using var output = new FileStream(
                destination,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);


            container.Position = 0;

            var read = await SealContainerReader.ReadAsync(
                    container,
                    contentKey,
                    async (chunk, token) => await output.WriteAsync(chunk, token).ConfigureAwait(false),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            var digest = Convert.ToHexString(read.PlaintextSha256).ToLowerInvariant();
            if (!string.Equals(digest, item.ContentSha256, StringComparison.Ordinal))
                throw new SealCorruptException(SealCorruptReason.ContentDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }



    private static SealJournalRecord Record(
        string opId, string phase, SealedItem item, string targetPath, long startedAtUtc)
        => new()
        {
            OpId = opId,
            Op = SealJournalOp.Take,
            Phase = phase,
            ItemId = item.Id,

            SourcePath = string.IsNullOrEmpty(item.SourcePath) ? item.OriginalName : item.SourcePath,
            TargetPath = targetPath,
            StartedAtUtc = startedAtUtc,
            Draft = null,
        };



    internal static string TemporaryArchiveFor(string targetPath, string itemId)
        => Path.Combine(Path.GetDirectoryName(targetPath) ?? string.Empty, $".novara-restore-{itemId}.zip");

    private static bool Occupied(string path) => File.Exists(path) || Directory.Exists(path);



    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

            return false;
        }
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
