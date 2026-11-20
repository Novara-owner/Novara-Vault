using System.Buffers.Binary;
using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;


public sealed record SealBundleItem(string ItemId, string ContainerPath);





public sealed record SealBundleOptions(
    byte[] Vmk,
    SealIndex Index,
    IReadOnlyList<SealBundleItem> Items,
    string Password,
    byte Kind,
    long CreatedUtc,
    int AppVersion,
    byte[]? BackupBlock = null,
    byte[]? BundleId = null);


public readonly record struct SealBundleWriteResult(
    byte[] BundleId,
    int ItemCount,
    int IndexLength,
    long BodyLength,
    long FileSize,
    byte[] TailSha256)
{
    public string BundleIdHex => Convert.ToHexString(BundleId).ToLowerInvariant();
}
















public static class SealBundleWriter
{
    public const int HeaderSize = 64;
    public const byte CurrentVersion = 1;


    public const byte KindItems = 1;


    public const byte KindItemsWithBackup = 2;

    public const int MagicOffset = 0;
    public const int VersionOffset = 8;
    public const int KindOffset = 9;
    public const int FlagsOffset = 10;
    public const int BundleIdOffset = 12;
    public const int ItemCountOffset = 28;
    public const int IndexLengthOffset = 32;
    public const int BodyLengthOffset = 36;
    public const int CreatedUtcOffset = 44;
    public const int AppVersionOffset = 52;
    public const int ReservedOffset = 56;
    public const int ReservedSize = 8;

    public const int TailSize = 32;


    public const int BlockLengthPrefixSize = 8;


    public const int MaxIndexBytes = 64 * 1024 * 1024;


    public const int IndexAadSize = 16 + 32 + 4;

    private static ReadOnlySpan<byte> Magic => "NVSEALBK"u8;


    public static Task<SealBundleWriteResult> WriteAsync(
        string path, SealBundleOptions options, CancellationToken cancellationToken = default)
        => WriteAsync(path, options, null, cancellationToken);


    internal static async Task<SealBundleWriteResult> WriteAsync(
        string path, SealBundleOptions options, SealKdfParams? kdfParameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(options);

        var temporary = path + ".tmp";

        try
        {
            SealBundleWriteResult result;
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                result = await WriteToAsync(stream, options, kdfParameters, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }



            File.Move(temporary, path);
            return result;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }




    internal static async Task<SealBundleWriteResult> WriteToAsync(
        Stream output, SealBundleOptions options, SealKdfParams? kdfParameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var bundleId = options.BundleId ?? RandomNumberGenerator.GetBytes(16);
        if (bundleId.Length != 16) throw new ArgumentException("bundle id must be 16 bytes", nameof(options));

        var keywrap = SealKeyWrap.WrapForBundle(
            options.Vmk, options.Password, options.Kind, bundleId, kdfParameters);

        var indexJson = options.Index.SerializePayload();
        var indexNonce = RandomNumberGenerator.GetBytes(SealFormat.NonceSize);
        var indexAad = BuildIndexAad(bundleId, SHA256.HashData(keywrap), options.Items.Count);
        byte[] indexCipher;
        try
        {
            indexCipher = CryptoService.EncryptGcmWithNonce(indexJson, options.Vmk, indexNonce, indexAad);
        }
        finally
        {

            CryptographicOperations.ZeroMemory(indexJson);
        }

        var indexBlock = new byte[SealFormat.NonceSize + indexCipher.Length];
        indexNonce.CopyTo(indexBlock, 0);
        indexCipher.CopyTo(indexBlock, SealFormat.NonceSize);

        if (indexBlock.Length > MaxIndexBytes)
            throw new ArgumentException($"the index exceeds {MaxIndexBytes} bytes", nameof(options));

        var lengths = options.Items.Select(item => new FileInfo(item.ContainerPath).Length).ToArray();
        var itemsBytes = lengths.Sum(length => (long)BlockLengthPrefixSize + length);
        var backupBytes = options.Kind == KindItemsWithBackup
            ? (long)BlockLengthPrefixSize + options.BackupBlock!.Length
            : 0L;

        var bodyLength = SealKeyWrap.FileSize + indexBlock.Length + backupBytes + itemsBytes;

        var header = new byte[HeaderSize];
        Magic.CopyTo(header.AsSpan(MagicOffset));
        header[VersionOffset] = CurrentVersion;
        header[KindOffset] = options.Kind;
        bundleId.CopyTo(header.AsSpan(BundleIdOffset, 16));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(ItemCountOffset, 4), options.Items.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(IndexLengthOffset, 4), indexBlock.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(BodyLengthOffset, 8), bodyLength);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(CreatedUtcOffset, 8), options.CreatedUtc);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(AppVersionOffset, 4), options.AppVersion);


        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        sha.AppendData(header);

        await output.WriteAsync(keywrap, cancellationToken).ConfigureAwait(false);
        sha.AppendData(keywrap);

        await output.WriteAsync(indexBlock, cancellationToken).ConfigureAwait(false);
        sha.AppendData(indexBlock);

        if (options.Kind == KindItemsWithBackup)
        {
            var backupLength = new byte[BlockLengthPrefixSize];
            BinaryPrimitives.WriteUInt64LittleEndian(backupLength, (ulong)options.BackupBlock!.Length);

            await output.WriteAsync(backupLength, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(options.BackupBlock, cancellationToken).ConfigureAwait(false);
            sha.AppendData(backupLength);
            sha.AppendData(options.BackupBlock);
        }

        for (var i = 0; i < options.Items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lengthPrefix = new byte[BlockLengthPrefixSize];
            BinaryPrimitives.WriteUInt64LittleEndian(lengthPrefix, (ulong)lengths[i]);

            await output.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
            sha.AppendData(lengthPrefix);

            await using var container = new FileStream(
                options.Items[i].ContainerPath, FileMode.Open, FileAccess.Read, FileShare.Read);




            if (container.Length != lengths[i])
                throw new IOException("container length changed while the bundle was being written");

            await CopyAndHashAsync(container, output, sha, lengths[i], cancellationToken).ConfigureAwait(false);
        }

        var tail = sha.GetHashAndReset();
        await output.WriteAsync(tail, cancellationToken).ConfigureAwait(false);

        return new SealBundleWriteResult(
            bundleId, options.Items.Count, indexBlock.Length, bodyLength,
            HeaderSize + bodyLength + TailSize, tail);
    }


    internal static byte[] BuildIndexAad(ReadOnlySpan<byte> bundleId, ReadOnlySpan<byte> keywrapSha256, int itemCount)
    {
        if (bundleId.Length != 16) throw new ArgumentException("bundle id must be 16 bytes", nameof(bundleId));
        if (keywrapSha256.Length != 32) throw new ArgumentException("keywrap digest must be 32 bytes", nameof(keywrapSha256));

        var aad = new byte[IndexAadSize];
        bundleId.CopyTo(aad);
        keywrapSha256.CopyTo(aad.AsSpan(16));
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(48, 4), itemCount);
        return aad;
    }

    private static void Validate(SealBundleOptions options)
    {
        if (options.Vmk is null || options.Vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(options));
        if (string.IsNullOrEmpty(options.Password))
            throw new ArgumentException("a bundle must be wrapped under the space password", nameof(options));

        if (options.Kind is not (KindItems or KindItemsWithBackup))
            throw new ArgumentException("kind must be 1 or 2", nameof(options));

        if (options.Kind == KindItemsWithBackup && options.BackupBlock is null)
            throw new ArgumentException("kind 2 needs the backup block", nameof(options));
        if (options.Kind == KindItems && options.BackupBlock is not null)
            throw new ArgumentException("kind 1 must not carry a backup block", nameof(options));

        if (options.CreatedUtc <= 0)
            throw new ArgumentException("createdUtc must be a positive epoch second", nameof(options));

        if (options.Items.Count != options.Index.Items.Count)
            throw new ArgumentException("the index and the item list must describe the same entries", nameof(options));

        var indexIds = new HashSet<string>(options.Index.Items.Select(i => i.Id), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in options.Items)
        {
            if (!seen.Add(item.ItemId))
                throw new ArgumentException("the same item appears twice", nameof(options));
            if (!indexIds.Contains(item.ItemId))
                throw new ArgumentException("an item is not described by the index", nameof(options));
            if (!File.Exists(item.ContainerPath))
                throw new ArgumentException("a container is missing", nameof(options));
        }
    }

    private static async Task CopyAndHashAsync(
        Stream source, Stream destination, IncrementalHash sha, long expectedLength,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            copied += read;
            sha.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        if (copied != expectedLength)
            throw new IOException("container changed while the bundle was being written");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }
}
