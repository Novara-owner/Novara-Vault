using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Novara.Services;


public readonly record struct SealReadResult(int BlockCount, ulong PlaintextLength, byte[] PlaintextSha256);



















public static class SealContainerReader
{





    public static async Task<SealReadResult> ReadAsync(
        Stream input,
        byte[] contentKey,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sink,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(contentKey);
        ArgumentNullException.ThrowIfNull(sink);

        if (contentKey.Length != SealFormat.KeySize)
            throw new ArgumentException($"content key must be {SealFormat.KeySize} bytes", nameof(contentKey));

        var headerBytes = new byte[SealFormat.HeaderSize];
        if (await ReadAtMostAsync(input, headerBytes, cancellationToken).ConfigureAwait(false) < SealFormat.HeaderSize)
            throw new SealCorruptException(SealCorruptReason.Truncated);

        if (!SealContainerHeader.TryRead(headerBytes, out var header, out var formatError))
        {
            if (formatError == SealFormatError.VersionUnsupported)
                throw new SealVersionUnsupportedException(headerBytes[SealFormat.VersionOffset]);

            throw new SealCorruptException(SealCorruptReason.HeaderInvalid);
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var remaining = header.PlaintextLength;
        var blockIndex = 0;
        long produced = 0;

        if (remaining > 0)
        {
            var minPayload = SealFormat.NonceSize + 1 + SealFormat.TagSize;
            var maxPayload = SealFormat.NonceSize + header.ChunkSize + SealFormat.TagSize;



            var prefix = new byte[SealFormat.BlockLengthPrefixSize];
            var nonce = new byte[SealFormat.NonceSize];
            var payload = new byte[maxPayload - SealFormat.NonceSize];
            var plain = new byte[header.ChunkSize];
            var aad = new byte[SealFormat.BlockAadSize];

            using var gcm = new AesGcm(contentKey, SealFormat.TagSize);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await ReadAtMostAsync(input, prefix, cancellationToken).ConfigureAwait(false) < SealFormat.BlockLengthPrefixSize)
                        throw new SealCorruptException(SealCorruptReason.Truncated);

                    var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(prefix);
                    if (payloadLength < minPayload || payloadLength > maxPayload)
                        throw new SealCorruptException(SealCorruptReason.BlockLength);

                    var payloadBytes = payloadLength - SealFormat.NonceSize;
                    try
                    {
                        await input.ReadExactlyAsync(nonce, cancellationToken).ConfigureAwait(false);
                        await input.ReadExactlyAsync(payload.AsMemory(0, payloadBytes), cancellationToken).ConfigureAwait(false);
                    }
                    catch (EndOfStreamException)
                    {
                        throw new SealCorruptException(SealCorruptReason.Truncated);
                    }

                    var plainLength = payloadBytes - SealFormat.TagSize;
                    if ((ulong)plainLength > remaining)
                        throw new SealCorruptException(SealCorruptReason.LengthMismatch);



                    var isFinal = (ulong)plainLength == remaining;
                    SealContainerWriter.FillBlockAad(header.ItemId, blockIndex, isFinal, aad);

                    try
                    {
                        gcm.Decrypt(
                            nonce,
                            payload.AsSpan(0, plainLength),
                            payload.AsSpan(plainLength, SealFormat.TagSize),
                            plain.AsSpan(0, plainLength),
                            aad);
                    }
                    catch (CryptographicException)
                    {
                        throw new SealCorruptException(SealCorruptReason.Authentication);
                    }


                    await sink(plain.AsMemory(0, plainLength), cancellationToken).ConfigureAwait(false);
                    sha.AppendData(plain.AsSpan(0, plainLength));

                    remaining -= (ulong)plainLength;
                    produced += plainLength;
                    blockIndex++;
                    progress?.Report(produced);

                    if (isFinal) break;
                }
            }
            finally
            {

                CryptographicOperations.ZeroMemory(plain);
            }
        }

        if (remaining != 0)
            throw new SealCorruptException(SealCorruptReason.Truncated);



        var probe = new byte[1];
        if (await ReadAtMostAsync(input, probe, cancellationToken).ConfigureAwait(false) > 0)
            throw new SealCorruptException(SealCorruptReason.TrailingData);

        return new SealReadResult(blockIndex, (ulong)produced, sha.GetHashAndReset());
    }


    private static async Task<int> ReadAtMostAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
