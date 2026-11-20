using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public enum SealImportStatus
{
    Ok = 0,
    Corrupt,
    WrongPassword,
    VersionUnsupported,
    NotUnlocked,
    WeakPassword,
    SameAsMainPassword,
    IoError,
}



public readonly record struct SealImportCounts(int Imported, int Skipped, int Conflict, int SameName, int Expired);


public sealed record SealImportResult(
    SealImportStatus Status,
    SealImportCounts Counts,
    IReadOnlyList<string> FailedItems)
{




    public bool HasBackup { get; init; }
}






















public static class SealImport
{





    public static async Task<SealImportResult> RunAsync(
        string bundlePath,
        string bundlePassword,
        SealSpacePaths paths,
        byte[]? localVmk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundlePath);
        ArgumentNullException.ThrowIfNull(paths);

        var opened = await SealBundleReader.OpenCoreAsync(bundlePath, bundlePassword, cancellationToken)
            .ConfigureAwait(false);

        if (opened.Status != SealBundleReadStatus.Ok || opened.Info is null)
            return Failed(MapReadStatus(opened.Status), []);

        var info = opened.Info;
        var bundleVmk = opened.Vmk;

        var createdHere = false;
        var vmk = localVmk;

        try
        {
            if (vmk is null)
            {
                var store = new SealSpaceStore(paths);
                if (store.Exists()) return Failed(SealImportStatus.NotUnlocked, []);

                var outcome = store.Initialize(bundlePassword, null, out var created);
                if (outcome != SealStoreOutcome.Ok) return Failed(MapStoreStatus(outcome), []);

                vmk = created;
                createdHere = true;
            }

            return await MergeAsync(bundlePath, info, bundleVmk, vmk, paths, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bundleVmk);
            if (createdHere && vmk is not null) CryptographicOperations.ZeroMemory(vmk);
        }
    }

    private static async Task<SealImportResult> MergeAsync(
        string bundlePath,
        SealBundleInfo info,
        byte[] bundleVmk,
        byte[] vmk,
        SealSpacePaths paths,
        CancellationToken cancellationToken)
    {
        var staging = Path.Combine(paths.ImportStaging, info.BundleIdHex);

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(SealImportStatus.IoError, []);
        }

        var imported = 0;
        var skipped = 0;
        var conflict = 0;
        var sameName = 0;
        var expired = 0;
        var failed = new List<string>();
        var pending = new List<(string Id, string StagingPath)>();
        var importedItems = new List<SealedItem>();

        SealIndex index;
        try
        {
            index = SealIndex.LoadInitialized(paths.Index, vmk);
        }
        catch (SealCorruptException)
        {

            return Failed(SealImportStatus.Corrupt, []);
        }
        catch (SealVersionUnsupportedException)
        {
            return Failed(SealImportStatus.VersionUnsupported, []);
        }

        var observedMax = index.ObservedMaxUtc;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var names = new HashSet<string>(index.Items.Select(i => i.DisplayName), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var reference in info.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = info.Index.Find(reference.ItemId);
                if (item is null)
                {
                    failed.Add(reference.ItemId);
                    break;
                }

                observedMax = Math.Max(observedMax, item.SealedAtUtc);

                var existing = index.Find(item.Id);

                if (existing is not null)
                {



                    if (string.Equals(existing.ContentSha256, item.ContentSha256, StringComparison.Ordinal)) skipped++;
                    else conflict++;

                    continue;
                }

                if (File.Exists(paths.ItemPath(item.Id)))
                {

                    conflict++;
                    continue;
                }

                var stagingPath = Path.Combine(staging, item.Id + ".novaseal");

                if (!await ReWrapAsync(bundlePath, reference, bundleVmk, vmk, stagingPath, cancellationToken)
                        .ConfigureAwait(false))
                {
                    failed.Add(item.Id);
                    break;
                }

                pending.Add((item.Id, stagingPath));
                importedItems.Add(item);
                imported++;

                if (!names.Add(item.DisplayName)) sameName++;

                if (item.IsCapsule && item.ExpiryUtc <= now) expired++;
            }

            if (failed.Count > 0)
            {
                TryDeleteDirectory(staging);
                return Failed(SealImportStatus.Corrupt, failed);
            }

            index = index.WithItems(importedItems);






            var moved = new List<string>();
            try
            {
                foreach (var (id, stagingPath) in pending)
                {
                    var destination = paths.ItemPath(id);
                    File.Move(stagingPath, destination);
                    moved.Add(destination);
                }

                index.WithObservedMax(observedMax).Save(paths.Index, vmk);
            }
            catch
            {
                foreach (var destination in moved) TryDeleteFile(destination);
                throw;
            }
            TryDeleteDirectory(staging);

            return new SealImportResult(
                SealImportStatus.Ok, new SealImportCounts(imported, skipped, conflict, sameName, expired), [])
            {
                HasBackup = info.BackupOffset >= 0,
            };        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(staging);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SealCorruptException)
        {
            TryDeleteDirectory(staging);
            return Failed(SealImportStatus.IoError, failed);
        }
    }





    private static async Task<bool> ReWrapAsync(
        string bundlePath,
        SealBundleItemRef reference,
        byte[] bundleVmk,
        byte[] localVmk,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        source.Position = reference.Offset;

        var headerBytes = new byte[SealFormat.HeaderSize];
        if (!await ReadExactlyAsync(source, headerBytes, cancellationToken).ConfigureAwait(false)) return false;

        if (!SealContainerHeader.TryRead(headerBytes, out var header, out _)) return false;

        var prefix = headerBytes.AsSpan(0, SealFormat.AuthenticatedPrefixSize).ToArray();

        if (!SealKeySlot.TryUnwrap(bundleVmk, header.KeySlot, prefix, out var contentKey, out _)) return false;

        try
        {
            var slot = new byte[SealFormat.KeySlotSize];
            SealKeySlot.Wrap(localVmk, contentKey, prefix, slot);

            var rewrapped = SealContainerHeader.Create(
                header.ItemId,
                header.PlaintextLength,
                header.SealedAtUtc,
                header.ChunkSize,
                slot,
                header.Flags);

            var newHeader = new byte[SealFormat.HeaderSize];
            rewrapped.WriteTo(newHeader);

            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await target.WriteAsync(newHeader, cancellationToken).ConfigureAwait(false);

            source.Position = reference.Offset + SealFormat.HeaderSize;
            await CopyAsync(source, target, reference.Length - SealFormat.HeaderSize, cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    private static async Task CopyAsync(Stream source, Stream destination, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var remaining = count;

        while (remaining > 0)
        {
            var take = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, take), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new IOException("a bundle container ended early");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;
            total += read;
        }

        return true;
    }

    private static SealImportResult Failed(SealImportStatus status, IReadOnlyList<string> failedItems)
        => new(status, default, failedItems);

    private static SealImportStatus MapReadStatus(SealBundleReadStatus status) => status switch
    {
        SealBundleReadStatus.Corrupt => SealImportStatus.Corrupt,
        SealBundleReadStatus.WrongPassword => SealImportStatus.WrongPassword,
        SealBundleReadStatus.VersionUnsupported => SealImportStatus.VersionUnsupported,
        _ => SealImportStatus.IoError,
    };

    private static SealImportStatus MapStoreStatus(SealStoreOutcome outcome) => outcome switch
    {
        SealStoreOutcome.WeakPassword => SealImportStatus.WeakPassword,
        SealStoreOutcome.SameAsMainPassword => SealImportStatus.SameAsMainPassword,
        SealStoreOutcome.Corrupt => SealImportStatus.Corrupt,
        SealStoreOutcome.IoError => SealImportStatus.IoError,
        _ => SealImportStatus.IoError,
    };

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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }
}
