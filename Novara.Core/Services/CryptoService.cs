/* ========== CryptoService - Encryption Utility ==========
Function: AES-GCM encryption/decryption with password-derived keys and salt
Corresponding UI: CryptoService.cs
Logic Range: Whole file business logic of this module
*/
using System.IO.Compression;
using System.Security.Cryptography;

namespace Novara.Services;

public static class CryptoService
{
    private const int KeySize = 32;   // AES-256
    private const int IvSize = 16;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int Iterations = 100000;

    public static byte[] Encrypt(byte[] plainData, string password, byte[] deriveSalt)
    {
        var key = DeriveKey(password, deriveSalt);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var compressed = Compress(plainData);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(compressed, 0, compressed.Length);

        var result = new byte[IvSize + cipher.Length];
        iv.CopyTo(result, 0);
        cipher.CopyTo(result, IvSize);
        return result;
    }

    public static byte[] Decrypt(byte[] cipherData, string password, byte[] deriveSalt)
    {
        if (cipherData.Length < IvSize + 16) throw new InvalidDataException(Loc.T("Crypto_Err_ShortCipher"));
        var key = DeriveKey(password, deriveSalt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = cipherData.AsSpan(0, IvSize).ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var compressed = decryptor.TransformFinalBlock(cipherData, IvSize, cipherData.Length - IvSize);
        return Decompress(compressed);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations = Iterations)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    /// <summary>
    /// AES-256-GCM (authenticated encryption, format v2, 2026-08-10, see 4.7):
    /// PBKDF2 key + 12-byte nonce + 16-byte tag. Wrong password / tampered data throw
    /// AuthenticationTagMismatchException (explicit corruption detection - replaces the CBC+MD5 combo).
    /// Layout: nonce(12) + tag(16) + ciphertext.
    /// Optional overrides (2026-08-29, encrypted export backup 9.2#6): a custom iteration count and
    /// associated data. The main-database paths keep the defaults (const Iterations, no AAD).
    /// </summary>
    public static byte[] EncryptGcm(byte[] plainData, string password, byte[] deriveSalt, int iterations = Iterations, byte[]? associatedData = null)
    {
        var key = DeriveKey(password, deriveSalt, iterations);
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var compressed = Compress(plainData);

        var cipher = new byte[compressed.Length];
        var tag = new byte[GcmTagSize];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Encrypt(nonce, compressed, cipher, tag, associatedData);

        var result = new byte[GcmNonceSize + GcmTagSize + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, GcmNonceSize);
        cipher.CopyTo(result, GcmNonceSize + GcmTagSize);
        return result;
    }

    public static byte[] DecryptGcm(byte[] data, string password, byte[] deriveSalt, int iterations = Iterations, byte[]? associatedData = null)
    {
        if (data.Length < GcmNonceSize + GcmTagSize) throw new InvalidDataException(Loc.T("Crypto_Err_ShortCipher"));
        var key = DeriveKey(password, deriveSalt, iterations);
        var nonce = data.AsSpan(0, GcmNonceSize);
        var tag = data.AsSpan(GcmNonceSize, GcmTagSize);
        var cipher = data.AsSpan(GcmNonceSize + GcmTagSize);

        var compressed = new byte[cipher.Length];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, compressed, associatedData); // wrong password / tamper -> AuthenticationTagMismatchException
        return Decompress(compressed);
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(data);
        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }
}
