using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;



public enum SealBundleReadStatus
{
    Ok = 0,
    Corrupt,
    WrongPassword,
    VersionUnsupported,
    IoError,
}


public sealed record SealBundleItemRef(string ItemId, long Offset, long Length);


public sealed record SealBundleInfo(
    byte[] BundleId,
    byte Kind,
    long CreatedUtc,
    int AppVersion,
    SealIndex Index,
    IReadOnlyList<SealBundleItemRef> Items)
{
    public string BundleIdHex => Convert.ToHexString(BundleId).ToLowerInvariant();


    public long BackupOffset { get; init; } = -1;


    public long BackupLength { get; init; }
}

public sealed record SealBundleReadResult(SealBundleReadStatus Status, SealBundleInfo? Bundle);






public sealed record SealBundlePeek(byte Kind, int ItemCount, long CreatedUtc, int AppVersion, int IndexLength)
{

    public bool HasBackup => Kind == SealBundleWriter.KindItemsWithBackup;
}


public sealed record SealBundlePeekResult(SealBundleReadStatus Status, SealBundlePeek? Peek);
















public static class SealBundleReader
{
    public const int MaxItems = 1_000_000;
    public const int MaxIndexBytes = 64 * 1024 * 1024;


    public static async Task<SealBundleReadResult> OpenAsync(
        string path, string password, CancellationToken cancellationToken = default)
    {
        var (status, info, vmk) = await OpenCoreAsync(path, password, cancellationToken).ConfigureAwait(false);

        try
        {
            return new SealBundleReadResult(status, info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmk);
        }
    }


    internal static async Task<(SealBundleReadStatus Status, SealBundleInfo? Info, byte[] Vmk)> OpenCoreAsync(
        string path, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (SealBundleReadStatus.IoError, null, []);
        }

        await using (stream)
        {
            try
            {
                return await ReadAsync(stream, password, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (SealBundleReadStatus.IoError, null, []);
            }
        }
    }







    private static (SealBundleReadStatus Status, SealBundlePeek? Peek) ReadHeader(byte[] header, long fileLength)
    {
        if (!header.AsSpan(SealBundleWriter.MagicOffset, 8).SequenceEqual("NVSEALBK"u8))
            return (SealBundleReadStatus.Corrupt, null);

        var version = header[SealBundleWriter.VersionOffset];
        if (version == 0 || version > SealBundleWriter.CurrentVersion)
            return (SealBundleReadStatus.VersionUnsupported, null);

        var kind = header[SealBundleWriter.KindOffset];
        if (kind is not (SealBundleWriter.KindItems or SealBundleWriter.KindItemsWithBackup))
            return (SealBundleReadStatus.Corrupt, null);

        if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(SealBundleWriter.FlagsOffset, 2)) != 0)
            return (SealBundleReadStatus.Corrupt, null);

        for (var i = 0; i < SealBundleWriter.ReservedSize; i++)
        {
            if (header[SealBundleWriter.ReservedOffset + i] != 0)
                return (SealBundleReadStatus.Corrupt, null);
        }

        var itemCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(SealBundleWriter.ItemCountOffset, 4));
        if (itemCount < 0 || itemCount > MaxItems) return (SealBundleReadStatus.Corrupt, null);

        var indexLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(SealBundleWriter.IndexLengthOffset, 4));
        if (indexLength < SealFormat.NonceSize + SealFormat.TagSize || indexLength > MaxIndexBytes)
            return (SealBundleReadStatus.Corrupt, null);

        var bodyLength = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(SealBundleWriter.BodyLengthOffset, 8));
        if (bodyLength < SealKeyWrap.FileSize + indexLength)
            return (SealBundleReadStatus.Corrupt, null);

        if (fileLength != SealBundleWriter.HeaderSize + bodyLength + SealBundleWriter.TailSize)
            return (SealBundleReadStatus.Corrupt, null);

        var createdUtc = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(SealBundleWriter.CreatedUtcOffset, 8));
        var appVersion = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(SealBundleWriter.AppVersionOffset, 4));

        return (SealBundleReadStatus.Ok, new SealBundlePeek(kind, itemCount, createdUtc, appVersion, indexLength));
    }






    public static SealBundlePeekResult Peek(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[SealBundleWriter.HeaderSize];
            var total = 0;
            while (total < header.Length)
            {
                var read = stream.Read(header, total, header.Length - total);
                if (read == 0) return new SealBundlePeekResult(SealBundleReadStatus.Corrupt, null);
                total += read;
            }

            var (status, peek) = ReadHeader(header, stream.Length);
            return new SealBundlePeekResult(status, peek);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SealBundlePeekResult(SealBundleReadStatus.IoError, null);
        }
    }

    private static async Task<(SealBundleReadStatus, SealBundleInfo?, byte[])> ReadAsync(
        FileStream stream, string password, CancellationToken cancellationToken)
    {
        var fileLength = stream.Length;

        var header = new byte[SealBundleWriter.HeaderSize];
        if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return (SealBundleReadStatus.Corrupt, null, []);

        var (headerStatus, peek) = ReadHeader(header, fileLength);
        if (peek is null) return (headerStatus, null, []);

        var kind = peek.Kind;
        var itemCount = peek.ItemCount;
        var createdUtc = peek.CreatedUtc;
        var appVersion = peek.AppVersion;
        var bundleId = header.AsSpan(SealBundleWriter.BundleIdOffset, 16).ToArray();



        var backupOffset = -1L;
        var backupLengthValue = 0L;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(header);

        var keywrap = new byte[SealKeyWrap.FileSize];
        if (!await ReadExactlyAsync(stream, keywrap, cancellationToken).ConfigureAwait(false))
            return (SealBundleReadStatus.Corrupt, null, []);

        sha.AppendData(keywrap);


        if (!SealKeyWrap.TryUnwrapFromBundle(keywrap, password, kind, bundleId, out var vmk, out var wrapOutcome))
        {
            return wrapOutcome == SealKeyWrapOutcome.VersionUnsupported
                ? (SealBundleReadStatus.VersionUnsupported, null, [])
                : (SealBundleReadStatus.WrongPassword, null, []);
        }

        var indexBlock = new byte[peek.IndexLength];
        if (!await ReadExactlyAsync(stream, indexBlock, cancellationToken).ConfigureAwait(false))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        sha.AppendData(indexBlock);

        SealIndex index;
        try
        {
            var aad = SealBundleWriter.BuildIndexAad(bundleId, SHA256.HashData(keywrap), itemCount);

            var indexNonce = indexBlock.AsSpan(0, SealFormat.NonceSize).ToArray();
            var indexCipher = indexBlock.AsSpan(SealFormat.NonceSize).ToArray();
            var indexJson = CryptoService.DecryptGcmWithNonce(indexCipher, vmk, indexNonce, aad);

            index = SealIndex.FromPayload(indexJson, itemCount);
        }
        catch (Exception ex) when (ex is CryptographicException or SealCorruptException or JsonException)
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        if (kind == SealBundleWriter.KindItemsWithBackup)
        {
            var backupLength = new byte[SealBundleWriter.BlockLengthPrefixSize];            if (!await ReadExactlyAsync(stream, backupLength, cancellationToken).ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            var length = BinaryPrimitives.ReadUInt64LittleEndian(backupLength);
            if (length > (ulong)(fileLength - stream.Position - SealBundleWriter.TailSize))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            sha.AppendData(backupLength);




            backupOffset = stream.Position;

            if (!await SkipAndHashAsync(stream, sha, (long)length, cancellationToken).ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            backupLengthValue = (long)length;
        }

        if (index.Items.Count != itemCount)
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        var items = new List<SealBundleItemRef>(itemCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < itemCount; i++)
        {
            var lengthBytes = new byte[SealBundleWriter.BlockLengthPrefixSize];
            if (!await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            var length = BinaryPrimitives.ReadUInt64LittleEndian(lengthBytes);
            var remaining = fileLength - stream.Position - SealBundleWriter.TailSize;

            if (length < SealFormat.HeaderSize || length > (ulong)remaining)
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            sha.AppendData(lengthBytes);

            var containerHeader = new byte[SealFormat.HeaderSize];
            if (!await ReadExactlyAsync(stream, containerHeader, cancellationToken).ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            sha.AppendData(containerHeader);

            if (!SealContainerHeader.TryRead(containerHeader, out var parsed, out _))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            var itemId = Convert.ToHexString(parsed.ItemId).ToLowerInvariant();
            if (index.Find(itemId) is null || !seen.Add(itemId))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            var offset = stream.Position;
            if (!await SkipAndHashAsync(stream, sha, (long)length - SealFormat.HeaderSize, cancellationToken)
                    .ConfigureAwait(false))
            {
                CryptographicOperations.ZeroMemory(vmk);
                return (SealBundleReadStatus.Corrupt, null, []);
            }

            items.Add(new SealBundleItemRef(itemId, offset - SealFormat.HeaderSize, (long)length));
        }

        if (stream.Position != fileLength - SealBundleWriter.TailSize)
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        var tail = new byte[SealBundleWriter.TailSize];
        if (!await ReadExactlyAsync(stream, tail, cancellationToken).ConfigureAwait(false))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        var computed = sha.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(computed, tail))
        {
            CryptographicOperations.ZeroMemory(vmk);
            return (SealBundleReadStatus.Corrupt, null, []);
        }

        return (SealBundleReadStatus.Ok,
            new SealBundleInfo(bundleId, kind, createdUtc, appVersion, index, items)
            {
                BackupOffset = backupOffset,
                BackupLength = backupLengthValue,
            },
            vmk);
    }












    public static async Task<SealBundleReadStatus> ExtractBackupAsync(
        string path, string password, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(destination);




        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SealBundleReadStatus.IoError;
        }

        await using (stream)
        {
            byte[] vmk = [];
            try
            {
                (var status, var info, vmk) = await ReadAsync(stream, password, cancellationToken)
                    .ConfigureAwait(false);

                if (status != SealBundleReadStatus.Ok || info is null)
                    return status;
                if (info.BackupOffset < 0 || info.BackupLength <= 0) return SealBundleReadStatus.Corrupt;

                stream.Position = info.BackupOffset;




                var temporary = destination + ".tmp";
                TryDeleteFile(temporary);
                try
                {
                    await using (var target = new FileStream(
                        temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[64 * 1024];
                        var remaining = info.BackupLength;

                        while (remaining > 0)
                        {
                            var take = (int)Math.Min(buffer.Length, remaining);
                            var read = await stream.ReadAsync(buffer.AsMemory(0, take), cancellationToken).ConfigureAwait(false);
                            if (read == 0) return SealBundleReadStatus.Corrupt;

                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            remaining -= read;
                        }
                    }

                    File.Move(temporary, destination, overwrite: true);
                    return SealBundleReadStatus.Ok;
                }
                finally
                {
                    TryDeleteFile(temporary);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return SealBundleReadStatus.IoError;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vmk);
            }
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

    private static async Task<bool> SkipAndHashAsync(Stream stream, IncrementalHash sha, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var remaining = count;

        while (remaining > 0)
        {
            var take = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, take), cancellationToken).ConfigureAwait(false);
            if (read == 0) return false;

            sha.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
