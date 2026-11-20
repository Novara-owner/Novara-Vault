using System.Security.Cryptography;

namespace Novara.Services;


















public static class SealKeySlot
{
    public const int NonceOffset = 0;
    public const int WrappedKeyOffset = SealFormat.NonceSize;


    public const int WrappedKeyOffsetEnd = WrappedKeyOffset + SealFormat.KeySize;

    public const int TagOffset = WrappedKeyOffsetEnd;


    public const int ReservedOffset = TagOffset + SealFormat.TagSize;

    public const int ReservedSize = SealFormat.KeySlotSize - ReservedOffset;


    public const int SealedPayloadSize = SealFormat.KeySize + SealFormat.TagSize;





    public static void Wrap(ReadOnlySpan<byte> vmk, ReadOnlySpan<byte> ck, ReadOnlySpan<byte> headerPrefix, Span<byte> destination)
    {
        ValidateWriteArguments(vmk, ck, headerPrefix, destination);

        var slot = destination[..SealFormat.KeySlotSize];
        slot.Clear();

        var nonce = slot.Slice(NonceOffset, SealFormat.NonceSize);
        RandomNumberGenerator.Fill(nonce);


        var vmkCopy = vmk.ToArray();
        var ckCopy = ck.ToArray();
        try
        {
            var payload = CryptoService.EncryptGcmWithNonce(ckCopy, vmkCopy, nonce.ToArray(), headerPrefix.ToArray());
            payload.CopyTo(slot.Slice(WrappedKeyOffset, SealedPayloadSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmkCopy);
            CryptographicOperations.ZeroMemory(ckCopy);
        }
    }





    internal static void WrapWithNonce(
        ReadOnlySpan<byte> vmk, ReadOnlySpan<byte> ck, ReadOnlySpan<byte> headerPrefix,
        ReadOnlySpan<byte> nonce, Span<byte> destination)
    {
        ValidateWriteArguments(vmk, ck, headerPrefix, destination);
        if (nonce.Length != SealFormat.NonceSize)
            throw new ArgumentException($"nonce must be {SealFormat.NonceSize} bytes", nameof(nonce));

        var slot = destination[..SealFormat.KeySlotSize];
        slot.Clear();

        nonce.CopyTo(slot[NonceOffset..]);

        var vmkCopy = vmk.ToArray();
        var ckCopy = ck.ToArray();
        try
        {
            var payload = CryptoService.EncryptGcmWithNonce(ckCopy, vmkCopy, nonce.ToArray(), headerPrefix.ToArray());
            payload.CopyTo(slot.Slice(WrappedKeyOffset, SealedPayloadSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmkCopy);
            CryptographicOperations.ZeroMemory(ckCopy);
        }
    }





    public static bool TryUnwrap(
        ReadOnlySpan<byte> vmk, ReadOnlySpan<byte> slot, ReadOnlySpan<byte> headerPrefix,
        out byte[] ck, out SealFormatError error)
    {
        ck = Array.Empty<byte>();

        if (slot.Length < SealFormat.KeySlotSize)
        {
            error = SealFormatError.TooShort;
            return false;
        }

        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));
        if (headerPrefix.Length != SealFormat.AuthenticatedPrefixSize)
            throw new ArgumentException(
                $"header prefix must be {SealFormat.AuthenticatedPrefixSize} bytes", nameof(headerPrefix));

        var source = slot[..SealFormat.KeySlotSize];

        for (var i = 0; i < ReservedSize; i++)
        {
            if (source[ReservedOffset + i] != 0)
            {
                error = SealFormatError.Reserved;
                return false;
            }
        }

        var nonce = source.Slice(NonceOffset, SealFormat.NonceSize).ToArray();
        var payload = source.Slice(WrappedKeyOffset, SealedPayloadSize).ToArray();
        var vmkCopy = vmk.ToArray();

        try
        {
            ck = CryptoService.DecryptGcmWithNonce(payload, vmkCopy, nonce, headerPrefix.ToArray());
            error = SealFormatError.None;
            return true;
        }
        catch (CryptographicException)
        {


            ck = Array.Empty<byte>();
            error = SealFormatError.Authentication;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vmkCopy);
        }
    }

    private static void ValidateWriteArguments(
        ReadOnlySpan<byte> vmk, ReadOnlySpan<byte> ck, ReadOnlySpan<byte> headerPrefix, Span<byte> destination)
    {
        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));
        if (ck.Length != SealFormat.KeySize)
            throw new ArgumentException($"content key must be {SealFormat.KeySize} bytes", nameof(ck));
        if (headerPrefix.Length != SealFormat.AuthenticatedPrefixSize)
            throw new ArgumentException(
                $"header prefix must be {SealFormat.AuthenticatedPrefixSize} bytes", nameof(headerPrefix));
        if (destination.Length < SealFormat.KeySlotSize)
            throw new ArgumentException(
                $"destination must hold {SealFormat.KeySlotSize} bytes", nameof(destination));
    }
}
