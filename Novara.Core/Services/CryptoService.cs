using System.IO.Compression;
using System.Security.Cryptography;

namespace Novara.Services;

public static class CryptoService
{
    private const int KeySize = 32;
    private const int IvSize = 16;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    internal const int LegacyIterations = 100_000;


    public const int CurrentIterations = 3_000_000;

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























    private static readonly object KeyCacheLock = new();
    private const int KeyCacheCapacity = 4;
    private static readonly string[] CacheKeys = new string[KeyCacheCapacity];
    private static readonly byte[]?[] CacheValues = new byte[]?[KeyCacheCapacity];
    private static int CacheCount;
    private static readonly string[] HitLog = new string[256];
    private static readonly string[] MissLog = new string[64];
    private static int HitCount, MissCount;


    public static (string[] Hits, string[] Misses) CacheCounters()
    {
        lock (KeyCacheLock)
        {
            var hits = new string[HitCount];
            for (var i = 0; i < HitCount; i++) hits[i] = HitLog[i];
            var misses = new string[MissCount];
            for (var i = 0; i < MissCount; i++) misses[i] = MissLog[i];
            return (hits, misses);
        }
    }



    public static void ClearKeyCache()
    {        lock (KeyCacheLock)
        {

            for (var i = 0; i < KeyCacheCapacity; i++)
            {
                if (CacheValues[i] is { } v) CryptographicOperations.ZeroMemory(v);
                CacheKeys[i] = null!;
                CacheValues[i] = null;
            }
            CacheCount = 0;
        }
    }

    private static string CacheKey(string password, byte[] salt, int iterations)
        => string.Concat(iterations.ToString(), ":", Convert.ToBase64String(salt), ":", password);


    internal static byte[] DeriveKey(string password, byte[] salt, int iterations = LegacyIterations)
    {
        var cacheKey = CacheKey(password, salt, iterations);
        lock (KeyCacheLock)
        {
            for (var i = 0; i < CacheCount; i++)
            {
                if (string.Equals(CacheKeys[i], cacheKey, StringComparison.Ordinal))
                {
                    if (HitCount < HitLog.Length) HitLog[HitCount++] = iterations + ":" + DateTime.UtcNow.ToString("HH:mm:ss.fff");
                    return (byte[])CacheValues[i]!.Clone();
                }
            }
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
        lock (KeyCacheLock)
        {
            if (MissCount < MissLog.Length) MissLog[MissCount++] = iterations + ":" + DateTime.UtcNow.ToString("HH:mm:ss.fff");

            for (var i = 0; i < CacheCount; i++)
                if (string.Equals(CacheKeys[i], cacheKey, StringComparison.Ordinal)) return (byte[])CacheValues[i]!.Clone();
            if (CacheCount >= KeyCacheCapacity)
            {

                if (CacheValues[0] is { } evicted) CryptographicOperations.ZeroMemory(evicted);
                for (var i = 1; i < KeyCacheCapacity; i++) { CacheKeys[i - 1] = CacheKeys[i]; CacheValues[i - 1] = CacheValues[i]; }
                CacheCount = KeyCacheCapacity - 1;
            }
            CacheKeys[CacheCount] = cacheKey;
            CacheValues[CacheCount] = (byte[])derived.Clone();
            CacheCount++;
        }
        return derived;
    }




    public const int Argon2idDefaultIterations = 3;


    public const int Argon2idDefaultMemoryKiB = 64 * 1024;


    public const byte V5KdfPbkdf2Sha256 = 0x00;
    public const byte V5KdfArgon2id = 0x01;

    private static byte[] DeriveKeyArgon2idCore(string password, byte[] salt, int iterations, int memoryKiB, int lanes)
    {
        using var argon2 = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memoryKiB,
            DegreeOfParallelism = lanes,
        };
        return argon2.GetBytes(KeySize);
    }


    public static byte[] DeriveKeyArgon2id(string password, byte[] salt, int iterations, int memoryKiB)
    {
        var cacheKey = "argon2id:" + iterations + ":" + memoryKiB + ":" + Convert.ToBase64String(salt) + ":" + password;
        lock (KeyCacheLock)
        {
            for (var i = 0; i < CacheCount; i++)
            {
                if (string.Equals(CacheKeys[i], cacheKey, StringComparison.Ordinal))
                {
                    if (HitCount < HitLog.Length) HitLog[HitCount++] = "a" + iterations + ":" + DateTime.UtcNow.ToString("HH:mm:ss.fff");
                    return (byte[])CacheValues[i]!.Clone();
                }
            }
        }

        var derived = DeriveKeyArgon2idCore(password, salt, iterations, memoryKiB, 1);
        lock (KeyCacheLock)
        {
            if (MissCount < MissLog.Length) MissLog[MissCount++] = "a" + iterations + ":" + DateTime.UtcNow.ToString("HH:mm:ss.fff");
            for (var i = 0; i < CacheCount; i++)
                if (string.Equals(CacheKeys[i], cacheKey, StringComparison.Ordinal)) return (byte[])CacheValues[i]!.Clone();
            if (CacheCount >= KeyCacheCapacity)
            {
                if (CacheValues[0] is { } evicted) CryptographicOperations.ZeroMemory(evicted);
                for (var i = 1; i < KeyCacheCapacity; i++) { CacheKeys[i - 1] = CacheKeys[i]; CacheValues[i - 1] = CacheValues[i]; }
                CacheCount = KeyCacheCapacity - 1;
            }
            CacheKeys[CacheCount] = cacheKey;
            CacheValues[CacheCount] = (byte[])derived.Clone();
            CacheCount++;
        }
        return derived;
    }


    internal static byte[] DeriveKeyArgon2idForVectorTest(string password, byte[] salt, int iterations, int memoryKiB, int lanes)
        => DeriveKeyArgon2idCore(password, salt, iterations, memoryKiB, lanes);









    public static byte[] EncryptGcm(byte[] plainData, string password, byte[] deriveSalt, int iterations = LegacyIterations, byte[]? associatedData = null)
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

    public static byte[] DecryptGcm(byte[] data, string password, byte[] deriveSalt, int iterations = LegacyIterations, byte[]? associatedData = null)
    {



        if (data.Length < GcmNonceSize + GcmTagSize + 1) throw new InvalidDataException(Loc.T("Crypto_Err_ShortCipher"));
        var key = DeriveKey(password, deriveSalt, iterations);
        var nonce = data.AsSpan(0, GcmNonceSize);
        var tag = data.AsSpan(GcmNonceSize, GcmTagSize);
        var cipher = data.AsSpan(GcmNonceSize + GcmTagSize);

        var compressed = new byte[cipher.Length];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, compressed, associatedData);
        return Decompress(compressed);
    }








    public static byte[] EncryptGcmWithKey(byte[] plainData, byte[] key, byte[]? associatedData = null)
    {
        if (key is null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));

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








    public static byte[] EncryptGcmRaw(byte[] plainData, byte[] key, byte[]? associatedData = null)
    {
        if (key is null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);

        var cipher = new byte[plainData.Length];
        var tag = new byte[GcmTagSize];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Encrypt(nonce, plainData, cipher, tag, associatedData);

        var result = new byte[GcmNonceSize + GcmTagSize + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, GcmNonceSize);
        cipher.CopyTo(result, GcmNonceSize + GcmTagSize);
        return result;
    }


    public static byte[] DecryptGcmRaw(byte[] data, byte[] key, byte[]? associatedData = null)
    {
        if (key is null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));
        if (data.Length < GcmNonceSize + GcmTagSize + 1) throw new InvalidDataException(Loc.T("Crypto_Err_ShortCipher"));

        var nonce = data.AsSpan(0, GcmNonceSize);
        var tag = data.AsSpan(GcmNonceSize, GcmTagSize);
        var cipher = data.AsSpan(GcmNonceSize + GcmTagSize);

        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, plain, associatedData);
        return plain;
    }


    public static byte[] DecryptGcmWithKey(byte[] data, byte[] key, byte[]? associatedData = null)
    {
        if (key is null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));
        if (data.Length < GcmNonceSize + GcmTagSize + 1) throw new InvalidDataException(Loc.T("Crypto_Err_ShortCipher"));

        var nonce = data.AsSpan(0, GcmNonceSize);
        var tag = data.AsSpan(GcmNonceSize, GcmTagSize);
        var cipher = data.AsSpan(GcmNonceSize + GcmTagSize);

        var compressed = new byte[cipher.Length];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, compressed, associatedData);
        return Decompress(compressed);
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(data);
        return ms.ToArray();
    }

    internal const long MaxDecompressedBytes = 256L * 1024 * 1024;



    internal static byte[] Decompress(byte[] data)
    {

        const long MaxDecompressed = MaxDecompressedBytes;
        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        long total = 0;
        while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxDecompressed) throw new InvalidOperationException("decompressed payload exceeds the safety limit");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
