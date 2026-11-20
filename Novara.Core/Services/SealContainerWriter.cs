using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Novara.Services;


public readonly record struct SealWriteResult(int BlockCount, byte[] PlaintextSha256);






internal interface ISealNonceSource
{
    void Fill(Span<byte> destination);
}


internal sealed class RandomSealNonceSource : ISealNonceSource
{
    public static readonly RandomSealNonceSource Instance = new();

    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}


























public static class SealContainerWriter
{





    public static SealContainerHeader PrepareHeader(
        ReadOnlySpan<byte> itemId,
        ulong plaintextLength,
        long sealedAtUtc,
        int chunkSize,
        ReadOnlySpan<byte> vmk,
        ReadOnlySpan<byte> contentKey,
        byte flags = 0)
        => PrepareHeaderCore(itemId, plaintextLength, sealedAtUtc, chunkSize, vmk, contentKey, flags, null);





    internal static SealContainerHeader PrepareHeaderCore(
        ReadOnlySpan<byte> itemId,
        ulong plaintextLength,
        long sealedAtUtc,
        int chunkSize,
        ReadOnlySpan<byte> vmk,
        ReadOnlySpan<byte> contentKey,
        byte flags,
        byte[]? keySlotNonce)
    {
        var draft = SealContainerHeader.Create(
            itemId, plaintextLength, sealedAtUtc, chunkSize, new byte[SealFormat.KeySlotSize], flags);

        var prefix = new byte[SealFormat.AuthenticatedPrefixSize];
        var full = new byte[SealFormat.HeaderSize];
        draft.WriteTo(full);
        full.AsSpan(0, SealFormat.AuthenticatedPrefixSize).CopyTo(prefix);

        var slot = new byte[SealFormat.KeySlotSize];
        if (keySlotNonce is null)
            SealKeySlot.Wrap(vmk, contentKey, prefix, slot);
        else
            SealKeySlot.WrapWithNonce(vmk, contentKey, prefix, keySlotNonce, slot);

        return SealContainerHeader.Create(itemId, plaintextLength, sealedAtUtc, chunkSize, slot, flags);
    }







    public static Task<SealWriteResult> WriteAsync(
        Stream plaintext,
        SealContainerHeader header,
        byte[] contentKey,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
        => WriteCoreAsync(plaintext, header, contentKey, destination, progress, cancellationToken,
            RandomSealNonceSource.Instance);





    internal static Task<SealWriteResult> WriteAsync(
        Stream plaintext,
        SealContainerHeader header,
        byte[] contentKey,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken,
        ISealNonceSource nonceSource)
        => WriteCoreAsync(plaintext, header, contentKey, destination, progress, cancellationToken, nonceSource);

    private static async Task<SealWriteResult> WriteCoreAsync(
        Stream plaintext,
        SealContainerHeader header,
        byte[] contentKey,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken,
        ISealNonceSource nonceSource)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(nonceSource);
        ArgumentNullException.ThrowIfNull(contentKey);
        ArgumentNullException.ThrowIfNull(destination);

        if (!header.IsInitialised)
            throw new ArgumentException("header was never initialised", nameof(header));
        if (contentKey.Length != SealFormat.KeySize)
            throw new ArgumentException($"content key must be {SealFormat.KeySize} bytes", nameof(contentKey));

        var headerBytes = new byte[SealFormat.HeaderSize];
        header.WriteTo(headerBytes);
        await destination.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var remaining = header.PlaintextLength;
        var blockIndex = 0;
        long processed = 0;

        if (remaining > 0)
        {



            var chunk = header.ChunkSize;
            var plain = new byte[chunk];
            var payload = new byte[chunk + SealFormat.TagSize];
            var nonce = new byte[SealFormat.NonceSize];
            var aad = new byte[SealFormat.BlockAadSize];
            var blockHeader = new byte[SealFormat.BlockLengthPrefixSize + SealFormat.NonceSize];

            using var gcm = new AesGcm(contentKey, SealFormat.TagSize);

            try
            {
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var take = (int)Math.Min((ulong)chunk, remaining);
                    await plaintext.ReadExactlyAsync(plain.AsMemory(0, take), cancellationToken).ConfigureAwait(false);

                    sha.AppendData(plain.AsSpan(0, take));

                    var isFinal = (ulong)take == remaining;
                    FillBlockAad(header.ItemId, blockIndex, isFinal, aad);
                    nonceSource.Fill(nonce);
                    gcm.Encrypt(
                        nonce,
                        plain.AsSpan(0, take),
                        payload.AsSpan(0, take),
                        payload.AsSpan(take, SealFormat.TagSize),
                        aad);



                    BinaryPrimitives.WriteInt32LittleEndian(
                        blockHeader, SealFormat.NonceSize + take + SealFormat.TagSize);
                    nonce.CopyTo(blockHeader.AsSpan(SealFormat.BlockLengthPrefixSize));

                    await destination.WriteAsync(blockHeader, cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(
                        payload.AsMemory(0, take + SealFormat.TagSize), cancellationToken).ConfigureAwait(false);

                    remaining -= (ulong)take;
                    processed += take;
                    blockIndex++;
                    progress?.Report(processed);
                }
            }
            finally
            {

                CryptographicOperations.ZeroMemory(plain);
            }
        }

        if (plaintext.CanSeek && plaintext.Position < plaintext.Length)



            throw new IOException(
                "source stream holds more bytes than the header declares; the container would be truncated");

        return new SealWriteResult(blockIndex, sha.GetHashAndReset());
    }


    internal static byte[] BuildBlockAad(ReadOnlySpan<byte> itemId, int blockIndex, bool isFinal)
    {
        var aad = new byte[SealFormat.BlockAadSize];
        FillBlockAad(itemId, blockIndex, isFinal, aad);
        return aad;
    }


    internal static void FillBlockAad(ReadOnlySpan<byte> itemId, int blockIndex, bool isFinal, Span<byte> aad)
    {
        if (itemId.Length != SealFormat.ItemIdSize)
            throw new ArgumentException($"item id must be {SealFormat.ItemIdSize} bytes", nameof(itemId));
        if (aad.Length < SealFormat.BlockAadSize)
            throw new ArgumentException($"aad buffer must be at least {SealFormat.BlockAadSize} bytes", nameof(aad));

        itemId.CopyTo(aad);
        BinaryPrimitives.WriteInt32LittleEndian(aad.Slice(SealFormat.ItemIdSize), blockIndex);
        aad[SealFormat.ItemIdSize + 4] = isFinal ? (byte)1 : (byte)0;
    }
}
