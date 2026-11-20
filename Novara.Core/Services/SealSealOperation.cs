using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;






public interface ISealSealPorts
{
    SealIndex Index { get; }

    SealJournal Journal { get; }


    void AppendJournal(SealJournalRecord record);


    void RemoveJournal(string opId);


    void SaveIndex(SealIndex index);






    void MarkVerified(string itemId);


    bool DeleteSource(string path, bool isDirectory);
}










public class SealSealPorts : ISealSealPorts
{
    private readonly byte[] _vmk;

    public SealSealPorts(SealSpacePaths paths, byte[] vmk)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        Paths = paths;
        _vmk = vmk;

        Index = SealIndex.LoadInitialized(paths.Index, vmk);


        Journal = SealJournal.LoadRequiredIfPresent(paths.Journal, vmk);

        if (Journal.Status != SealJournalStatus.Ok || Journal.HasMalformedRecords)
            throw new InvalidOperationException("the journal must be read and recovered before anything is sealed");
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


    public void MarkVerified(string itemId)
    {


    }





    public virtual bool DeleteSource(string path, bool isDirectory)
    {
        try
        {
            ClearReadOnlyAttributes(path);

            if (isDirectory) Directory.Delete(path, recursive: true);
            else File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }


    internal static void ClearReadOnlyAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            ClearReadOnly(path);
            return;
        }

        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            ClearReadOnly(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else ClearReadOnly(entry);
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0) return;

        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
















public sealed class SealSealOperation
{
    private enum Stage
    {
        Preparing,
        Packing,
        Writing,
        Verifying,
        Indexing,
        Deleting,
    }

    private readonly SealSpacePaths _paths;
    private readonly byte[] _vmk;
    private readonly ISealSealPorts _ports;
    private readonly Func<string> _newId;

    public SealSealOperation(SealSpacePaths paths, byte[] vmk, ISealSealPorts? ports = null, Func<string>? newId = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(vmk);

        _paths = paths;
        _vmk = vmk;
        _ports = ports ?? new SealSealPorts(paths, vmk);
        _newId = newId ?? NewId;
    }


    public SealIndex Index => _ports.Index;


    public SealJournal Journal => _ports.Journal;





    public async Task<IReadOnlyList<SealResult>> RunAsync(
        IReadOnlyList<SealRequest> requests,
        IProgress<SealProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new List<SealResult>(requests.Count);

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            progress?.Report(new SealProgress(i + 1, requests.Count, ItemNameOf(request.SourcePath)));

            var result = await SealOneAsync(request, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (result.Error == SealSealError.Cancelled) break;
        }

        return results;
    }

    private async Task<SealResult> SealOneAsync(SealRequest request, CancellationToken cancellationToken)
    {
        var source = FullPathOrNull(request.SourcePath);
        if (source is null) return Failure(SealSealError.SourceMissing);

        var (eligible, reject) = SealEligibility.Evaluate(source, _paths);
        if (!eligible) return Failure(SealSealError.Rejected, reject);

        var isDirectory = Directory.Exists(source);
        var originalName = NameOf(source);
        if (string.IsNullOrEmpty(originalName)) return Failure(SealSealError.InvalidRequest);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var itemId = _newId();
        var opId = NewId();





        long? expiryUtc = null;
        if (request.ExpirySeconds is { } expirySeconds)
        {
            if (expirySeconds <= 0 || expirySeconds > SealFormat.MaxSealedAtUtc - now)
                return Failure(SealSealError.InvalidRequest);
            expiryUtc = now + expirySeconds;
        }

        var draft = new SealJournalDraft
        {
            OriginalName = originalName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? originalName : request.DisplayName!.Trim(),
            SealedAtUtc = now,
            Kind = request.Kind,
            ExpiryUtc = expiryUtc,
            Note = request.Note,
            ContentSha256 = "",
            FolderEntryCount = null,
        };

        if (!SealJournalRecord.IsWellFormed(Record(opId, SealJournalPhase.SealBegin, itemId, source, now, draft), out _))
            return Failure(SealSealError.InvalidRequest);

        string? packPath = null;
        var keepPackForRecovery = false;
        var stage = Stage.Preparing;
        var indexUpdated = false;
        var journalStarted = false;

        try
        {
            long plainBytes;

            if (isDirectory)
            {
                stage = Stage.Packing;
                packPath = Path.Combine(_paths.Staging, $"pack-{itemId}.zip");



                var pack = await Task.Run(
                    () => SealFolderPack.Pack(source, packPath, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                draft.Size = (ulong)pack.OriginalTotalBytes;
                draft.FolderEntryCount = pack.EntryCount;
                plainBytes = new FileInfo(packPath).Length;
            }
            else
            {
                var info = new FileInfo(source);
                if (!info.Exists) return Failure(SealSealError.SourceMissing);
                plainBytes = info.Length;
                draft.Size = (ulong)plainBytes;
            }




            var needed = SealPreflight.Estimate(plainBytes);
            var room = SealPreflight.Check(_paths.Root, needed);
            if (room.Status == SealPreflightStatus.Insufficient) return Failure(SealSealError.NotEnoughSpace);
            if (room.Status == SealPreflightStatus.Unreachable) return Failure(SealSealError.VolumeUnreachable);

            _ports.AppendJournal(Record(opId, SealJournalPhase.SealBegin, itemId, source, now, draft));
            journalStarted = true;

            stage = Stage.Writing;
            var plaintextPath = isDirectory ? packPath! : source;
            var write = await WriteContainerAsync(
                    itemId, plaintextPath, plainBytes, now, isDirectory, cancellationToken)
                .ConfigureAwait(false);

            var digest = Convert.ToHexString(write.PlaintextSha256).ToLowerInvariant();
            draft.ContentSha256 = digest;

            stage = Stage.Verifying;
            var sealingPath = _paths.SealingPath(itemId);
            if (!await VerifiesAsync(sealingPath, digest, cancellationToken).ConfigureAwait(false))
            {
                Discard(itemId);
                _ports.RemoveJournal(opId);
                return Failure(SealSealError.VerifyFailed);
            }



            _ports.MarkVerified(itemId);
            _ports.AppendJournal(Record(opId, SealJournalPhase.Sealed, itemId, source, now, draft));

            var containerPath = _paths.ItemPath(itemId);
            if (File.Exists(containerPath))
            {



                TryDeleteFile(_paths.SealingPath(itemId));
                _ports.RemoveJournal(opId);
                return Failure(SealSealError.ItemIdCollision);
            }

            File.Move(sealingPath, containerPath);

            stage = Stage.Indexing;



            _ports.SaveIndex(_ports.Index.WithItem(BuildItem(itemId, source, draft))
                .WithObservedMax(now));
            indexUpdated = true;

            _ports.AppendJournal(Record(opId, SealJournalPhase.SealIndexed, itemId, source, now, draft));

            stage = Stage.Deleting;
            if (!_ports.DeleteSource(source, isDirectory))
            {


                return new SealResult(itemId, SourceDeleted: false, IndexUpdated: true, SealSealError.SourceDeleteFailed);
            }



            try
            {
                _ports.AppendJournal(Record(opId, SealJournalPhase.Deleted, itemId, source, now, draft));
                _ports.RemoveJournal(opId);
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {

            }

            return new SealResult(itemId, SourceDeleted: true, IndexUpdated: true);
        }
        catch (OperationCanceledException)
        {
            Discard(itemId);
            TryRemoveJournal(opId, journalStarted && !indexUpdated);
            return Failure(SealSealError.Cancelled, itemId: itemId);
        }
        catch (Exception ex) when (IsIoFailure(ex) || ex is SealPackException)
        {
            Discard(itemId);



            keepPackForRecovery = isDirectory && packPath is not null && File.Exists(packPath)
                && journalStarted && !indexUpdated;
            if (!keepPackForRecovery) TryRemoveJournal(opId, journalStarted && !indexUpdated);
            return Failure(MapError(stage), itemId: itemId);
        }
        finally
        {
            if (packPath is not null && !keepPackForRecovery) TryDeleteFile(packPath);
        }
    }



    private async Task<SealWriteResult> WriteContainerAsync(
        string itemId, string plaintextPath, long plainBytes, long sealedAtUtc, bool isDirectory,
        CancellationToken cancellationToken)
    {
        var contentKey = RandomNumberGenerator.GetBytes(SealFormat.KeySize);

        try
        {
            var header = SealContainerWriter.PrepareHeader(
                Convert.FromHexString(itemId),
                (ulong)plainBytes,
                sealedAtUtc,
                SealFormat.DefaultChunkSize,
                _vmk,
                contentKey,
                isDirectory ? SealFormat.FlagFolderArchive : (byte)0);

            await using var destination = new FileStream(
                _paths.SealingPath(itemId), FileMode.Create, FileAccess.Write, FileShare.None,
                1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var plaintext = new FileStream(
                plaintextPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);

            var result = await SealContainerWriter.WriteAsync(
                    plaintext, header, contentKey, destination, null, cancellationToken)
                .ConfigureAwait(false);

            destination.Flush(flushToDisk: true);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    private async Task<bool> VerifiesAsync(string containerPath, string expectedDigest, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            containerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var headerBytes = new byte[SealFormat.HeaderSize];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        if (!SealContainerHeader.TryRead(headerBytes, out var header, out _)) return false;
        if (!SealKeySlot.TryUnwrap(
                _vmk, header.KeySlot, headerBytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize),
                out var contentKey, out _))
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
            return string.Equals(digest, expectedDigest, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }



    private static SealJournalRecord Record(
        string opId, string phase, string itemId, string sourcePath, long startedAtUtc, SealJournalDraft draft)
        => new()
        {
            OpId = opId,
            Op = SealJournalOp.Seal,
            Phase = phase,
            ItemId = itemId,
            SourcePath = sourcePath,
            StartedAtUtc = startedAtUtc,
            Draft = new SealJournalDraft
            {
                OriginalName = draft.OriginalName,
                DisplayName = draft.DisplayName,
                Size = draft.Size,
                SealedAtUtc = draft.SealedAtUtc,
                Kind = draft.Kind,
                ExpiryUtc = draft.ExpiryUtc,
                Note = draft.Note,
                ContentSha256 = draft.ContentSha256,
                FolderEntryCount = draft.FolderEntryCount,
            },
        };

    private static SealedItem BuildItem(string itemId, string sourcePath, SealJournalDraft draft)
        => new()
        {
            Id = itemId,
            DisplayName = draft.DisplayName,
            OriginalName = draft.OriginalName,
            SourcePath = sourcePath,
            Size = draft.Size,
            SealedAtUtc = draft.SealedAtUtc,
            Kind = draft.Kind,
            ExpiryUtc = draft.ExpiryUtc,
            Note = draft.Note,
            ContentSha256 = draft.ContentSha256,
            FolderEntryCount = draft.FolderEntryCount,
            Flags = 0,
        };






    private void Discard(string itemId)
    {
        TryDeleteFile(_paths.SealingPath(itemId));
    }

    private void TryRemoveJournal(string opId, bool journalStarted)
    {
        if (!journalStarted) return;

        try
        {
            _ports.RemoveJournal(opId);
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {

        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }

    private static SealResult Failure(SealSealError error, SealRejectReason? reject = null, string itemId = "")
    {
        var result = new SealResult(itemId, SourceDeleted: false, IndexUpdated: false, error);
        return reject is null ? result : result with { Reject = reject };
    }

    private static SealSealError MapError(Stage stage) => stage switch
    {
        Stage.Packing => SealSealError.PackFailed,
        Stage.Writing => SealSealError.WriteFailed,
        Stage.Verifying => SealSealError.VerifyFailed,
        Stage.Indexing => SealSealError.IndexFailed,
        Stage.Deleting => SealSealError.SourceDeleteFailed,
        _ => SealSealError.WriteFailed,
    };

    private static bool IsIoFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException
           or SealCorruptException or SealVersionUnsupportedException;

    private static string? FullPathOrNull(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string NameOf(string source)
    {
        var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    private static string ItemNameOf(string path) => NameOf(path) is { Length: > 0 } name ? name : "?";

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
