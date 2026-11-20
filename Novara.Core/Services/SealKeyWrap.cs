using System.Security.Cryptography;

namespace Novara.Services;


public enum SealKeyWrapOutcome
{

    Ok = 0,


    OkWeakened,


    VersionUnsupported,


    WrongPasswordOrCorrupt,
}

















public static class SealKeyWrap
{
    public const int VersionOffset = 8;
    public const int KdfIdOffset = 9;
    public const int FlagsOffset = 10;
    public const int FlagsSize = 2;
    public const int KdfParamsOffset = 12;
    public const int SaltOffset = 24;


    public const int HeaderSize = 40;

    public const int NonceOffset = HeaderSize;
    public const int WrappedKeyOffset = NonceOffset + SealFormat.NonceSize;
    public const int TagOffset = WrappedKeyOffset + SealFormat.KeySize;


    public const int FileSize = TagOffset + SealFormat.TagSize;

    public const byte CurrentVersion = 1;
    public const byte KdfIdArgon2id = 1;


    public const int BundleAadExtraSize = 1 + 16;

    private static ReadOnlySpan<byte> Magic => "NOVAKWRP"u8;





    public static byte[] Wrap(ReadOnlySpan<byte> vmk, string password, SealKdfParams? parameters = null)
        => WrapCore(vmk, password, parameters ?? SealKdfParams.Default, 0, ReadOnlySpan<byte>.Empty);


    public static bool TryUnwrap(ReadOnlySpan<byte> wrap, string password,
        out byte[] vmk, out SealKeyWrapOutcome outcome)
        => TryUnwrapCore(wrap, password, 0, ReadOnlySpan<byte>.Empty, out vmk, out outcome);






    internal static bool LooksLikeKeyWrap(ReadOnlySpan<byte> bytes)
        => bytes.Length >= FileSize && bytes[..Magic.Length].SequenceEqual(Magic);




    internal static byte[] WrapForBundle(
        ReadOnlySpan<byte> vmk, string password, byte kind, ReadOnlySpan<byte> bundleId,
        SealKdfParams? parameters = null)
    {
        if (bundleId.Length != 16)
            throw new ArgumentException("bundle id must be 16 bytes", nameof(bundleId));

        return WrapCore(vmk, password, parameters ?? SealKdfParams.Default, kind, bundleId);
    }


    internal static bool TryUnwrapFromBundle(
        ReadOnlySpan<byte> wrap, string password, byte kind, ReadOnlySpan<byte> bundleId,
        out byte[] vmk, out SealKeyWrapOutcome outcome)
    {
        if (bundleId.Length != 16)
            throw new ArgumentException("bundle id must be 16 bytes", nameof(bundleId));

        return TryUnwrapCore(wrap, password, kind, bundleId, out vmk, out outcome);
    }

    private static byte[] WrapCore(
        ReadOnlySpan<byte> vmk, string password, SealKdfParams parameters, byte kind, ReadOnlySpan<byte> bundleId)
    {
        if (vmk.Length != SealFormat.KeySize)
            throw new ArgumentException($"vmk must be {SealFormat.KeySize} bytes", nameof(vmk));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("password is required", nameof(password));

        var wrap = new byte[FileSize];
        var header = wrap.AsSpan(0, HeaderSize);

        Magic.CopyTo(header);
        header[VersionOffset] = CurrentVersion;
        header[KdfIdOffset] = KdfIdArgon2id;

        parameters.WriteTo(header[KdfParamsOffset..]);

        var salt = header.Slice(SaltOffset, SealKdfParams.SaltSize);
        RandomNumberGenerator.Fill(salt);

        var nonce = wrap.AsSpan(NonceOffset, SealFormat.NonceSize);
        RandomNumberGenerator.Fill(nonce);

        var kek = parameters.DeriveKey(password, salt);
        var vmkCopy = vmk.ToArray();
        try
        {
            var aad = BuildAad(header, kind, bundleId);
            var sealedPayload = CryptoService.EncryptGcmWithNonce(vmkCopy, kek, nonce.ToArray(), aad);
            sealedPayload.CopyTo(wrap.AsSpan(WrappedKeyOffset, SealFormat.KeySize + SealFormat.TagSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(vmkCopy);
        }

        return wrap;
    }

    private static bool TryUnwrapCore(
        ReadOnlySpan<byte> wrap, string password, byte kind, ReadOnlySpan<byte> bundleId,
        out byte[] vmk, out SealKeyWrapOutcome outcome)
    {
        vmk = Array.Empty<byte>();
        outcome = SealKeyWrapOutcome.WrongPasswordOrCorrupt;

        if (string.IsNullOrEmpty(password)) return false;
        if (wrap.Length < FileSize) return false;
        if (!wrap[..Magic.Length].SequenceEqual(Magic)) return false;

        var header = wrap[..HeaderSize];
        var version = header[VersionOffset];
        if (version == 0 || version > CurrentVersion)
        {

            outcome = SealKeyWrapOutcome.VersionUnsupported;
            return false;
        }



        if (wrap.Length != FileSize) return false;

        if (header[KdfIdOffset] != KdfIdArgon2id) return false;
        for (var i = 0; i < FlagsSize; i++)
            if (header[FlagsOffset + i] != 0) return false;


        if (!SealKdfParams.TryRead(header.Slice(KdfParamsOffset, SealKdfParams.Size), out var parameters, out _))
            return false;

        var salt = header.Slice(SaltOffset, SealKdfParams.SaltSize);
        var kek = parameters.DeriveKey(password, salt);
        try
        {
            var aad = BuildAad(header, kind, bundleId);
            var nonce = wrap.Slice(NonceOffset, SealFormat.NonceSize).ToArray();
            var payload = wrap.Slice(WrappedKeyOffset, SealFormat.KeySize + SealFormat.TagSize).ToArray();

            vmk = CryptoService.DecryptGcmWithNonce(payload, kek, nonce, aad);
        }
        catch (CryptographicException)
        {

            vmk = Array.Empty<byte>();
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }

        outcome = IsWeakened(parameters) ? SealKeyWrapOutcome.OkWeakened : SealKeyWrapOutcome.Ok;
        return true;
    }





    private static bool IsWeakened(SealKdfParams parameters)
        => parameters.MemoryKiB < SealKdfParams.Default.MemoryKiB
           || parameters.Iterations < SealKdfParams.Default.Iterations;

    private static byte[] BuildAad(ReadOnlySpan<byte> header, byte kind, ReadOnlySpan<byte> bundleId)
    {
        if (bundleId.Length == 0) return header.ToArray();

        var aad = new byte[HeaderSize + BundleAadExtraSize];
        header.CopyTo(aad);
        aad[HeaderSize] = kind;
        bundleId.CopyTo(aad.AsSpan(HeaderSize + 1));
        return aad;
    }
}
