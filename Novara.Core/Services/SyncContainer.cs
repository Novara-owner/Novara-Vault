using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Novara.Services;










public static class SyncContainer
{
    internal const uint Magic = 0x41564F4E;
    internal const byte FileVersionV4 = 4;
    internal const byte FlagEncrypted = 0x01;
    internal const byte AlgoIdAes256Gcm = 0x00;
    internal const byte KdfIdPbkdf2Sha256 = 0x00;
    internal const int HeaderSize = 44;


    internal const int IterationsMin = 1_000;
    internal const int IterationsMax = 5_000_000;


    public static byte[] NewVersionSalt() => RandomNumberGenerator.GetBytes(SyncKeyWrap.SaltSize);







    public static byte[] Seal(byte[] plainUtf8, string spaceKeyBase64, byte[] versionSalt)
    {
        if (versionSalt is null || versionSalt.Length != SyncKeyWrap.SaltSize)
            throw new ArgumentException($"version salt must be {SyncKeyWrap.SaltSize} bytes", nameof(versionSalt));

        var blobKey = SyncKeyWrap.DeriveBlobKey(spaceKeyBase64, versionSalt);
        var header = BuildHeader(SyncKeyWrap.BlobIterations, versionSalt);
        try
        {
            var body = CryptoService.EncryptGcmWithKey(plainUtf8, blobKey, header);
            var result = new byte[header.Length + body.Length];
            header.CopyTo(result, 0);
            body.CopyTo(result, header.Length);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(blobKey); }
    }








    internal static byte[] SealBody(byte[] bodyBytes, string spaceKeyBase64, byte[] versionSalt, byte[] nonce)
    {
        if (bodyBytes is null || bodyBytes.Length == 0) throw new ArgumentException("body is required", nameof(bodyBytes));
        if (versionSalt is null || versionSalt.Length != SyncKeyWrap.SaltSize)
            throw new ArgumentException($"version salt must be {SyncKeyWrap.SaltSize} bytes", nameof(versionSalt));
        if (nonce is null || nonce.Length != SyncKeyWrap.NonceSize)
            throw new ArgumentException($"nonce must be {SyncKeyWrap.NonceSize} bytes", nameof(nonce));

        var blobKey = SyncKeyWrap.DeriveBlobKey(spaceKeyBase64, versionSalt);
        var header = BuildHeader(SyncKeyWrap.BlobIterations, versionSalt);
        try
        {
            var cipher = new byte[bodyBytes.Length];
            var tag = new byte[SyncKeyWrap.TagSize];
            using var gcm = new AesGcm(blobKey, SyncKeyWrap.TagSize);
            gcm.Encrypt(nonce, bodyBytes, cipher, tag, header);

            var body = new byte[SyncKeyWrap.NonceSize + SyncKeyWrap.TagSize + cipher.Length];
            nonce.CopyTo(body, 0);
            tag.CopyTo(body, SyncKeyWrap.NonceSize);
            cipher.CopyTo(body, SyncKeyWrap.NonceSize + SyncKeyWrap.TagSize);

            var result = new byte[header.Length + body.Length];
            header.CopyTo(result, 0);
            body.CopyTo(result, header.Length);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(blobKey); }
    }






    public static byte[] Open(byte[] container, string spaceKeyBase64)
    {
        if (container is null || container.Length <= HeaderSize)
            throw new InvalidDataException("sync container is truncated");

        var header = container.AsSpan(0, HeaderSize).ToArray();
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
            throw new InvalidDataException("sync container magic mismatch");
        if (header[4] != FileVersionV4)
            throw new InvalidDataException($"unsupported sync container version: {header[4]}");
        if (header[5] != FlagEncrypted)
            throw new InvalidDataException("sync container must be encrypted");
        if (header[6] != AlgoIdAes256Gcm)
            throw new InvalidDataException($"unsupported sync container algorithm: {header[6]}");
        if (header[7] != KdfIdPbkdf2Sha256)
            throw new InvalidDataException($"unsupported sync container kdf: {header[7]}");



        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        if (iterations < IterationsMin || iterations > IterationsMax)
            throw new InvalidDataException($"sync container iteration count out of range: {iterations}");

        var versionSalt = header.AsSpan(12, SyncKeyWrap.SaltSize).ToArray();
        var body = container.AsSpan(HeaderSize).ToArray();
        var blobKey = SyncKeyWrap.DeriveBlobKey(spaceKeyBase64, versionSalt, iterations);
        try
        {
            try
            {
                return CryptoService.DecryptGcmWithKey(body, blobKey, header);
            }
            catch (AuthenticationTagMismatchException)
            {


                throw new InvalidDataException("sync container authentication failed");
            }
        }
        finally { CryptographicOperations.ZeroMemory(blobKey); }
    }


    public static bool TryReadHeader(byte[] container, out int version, out int iterations, out byte[] salt)
    {
        version = 0;
        iterations = 0;
        salt = Array.Empty<byte>();
        if (container is null || container.Length < HeaderSize) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(container) != Magic) return false;

        version = container[4];
        iterations = BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(8, 4));
        salt = container.AsSpan(12, SyncKeyWrap.SaltSize).ToArray();
        return true;
    }

    private static byte[] BuildHeader(int iterations, byte[] salt)
    {
        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        header[4] = FileVersionV4;
        header[5] = FlagEncrypted;
        header[6] = AlgoIdAes256Gcm;
        header[7] = KdfIdPbkdf2Sha256;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), iterations);
        salt.CopyTo(header, 12);
        return header;
    }
}
