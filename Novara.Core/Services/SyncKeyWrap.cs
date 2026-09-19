using System.Security.Cryptography;
using System.Text;

namespace Novara.Services;





public sealed class SyncKeyWrapRecord
{
    public int Wrap { get; set; } = SyncKeyWrap.WrapVersion;
    public string Kdf { get; set; } = SyncKeyWrap.KdfName;


    public int Iter { get; set; } = SyncKeyWrap.WrapIterations;


    public string Salt { get; set; } = "";


    public string Nonce { get; set; } = "";


    public string Ct { get; set; } = "";
}




















public static class SyncKeyWrap
{
    public const int WrapVersion = 1;
    public const string KdfName = "pbkdf2-sha256";


    public const int WrapIterations = CryptoService.CurrentIterations;





    public const int BlobIterations = 1_000;

    public const int SpaceKeySize = 32;
    public const int SaltSize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;


    public const int SpaceKeyTextLength = 44;


    private static byte[] WrapAad(int iterations)
        => Encoding.UTF8.GetBytes($"novara-keywrap-v{WrapVersion}|{KdfName}|{iterations}");


    public static string CreateSpaceKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SpaceKeySize));


    public static bool IsWellFormedSpaceKey(string? spaceKeyBase64)
    {
        if (string.IsNullOrEmpty(spaceKeyBase64)) return false;
        try { return Convert.FromBase64String(spaceKeyBase64).Length == SpaceKeySize; }
        catch (FormatException) { return false; }
    }


    public static SyncKeyWrapRecord Wrap(string spaceKeyBase64, string lockPassword)
        => WrapWith(spaceKeyBase64, lockPassword, RandomNumberGenerator.GetBytes(SaltSize), RandomNumberGenerator.GetBytes(NonceSize));






    internal static SyncKeyWrapRecord WrapWith(string spaceKeyBase64, string lockPassword, byte[] salt, byte[] nonce)
    {
        if (!IsWellFormedSpaceKey(spaceKeyBase64))
            throw new ArgumentException($"space key must be the base64 text of {SpaceKeySize} bytes", nameof(spaceKeyBase64));
        if (string.IsNullOrEmpty(lockPassword)) throw new ArgumentException("lock password is required", nameof(lockPassword));
        if (salt is null || salt.Length != SaltSize) throw new ArgumentException($"salt must be {SaltSize} bytes", nameof(salt));
        if (nonce is null || nonce.Length != NonceSize) throw new ArgumentException($"nonce must be {NonceSize} bytes", nameof(nonce));

        var plain = Encoding.UTF8.GetBytes(spaceKeyBase64);
        var kek = Rfc2898DeriveBytes.Pbkdf2(lockPassword, salt, WrapIterations, HashAlgorithmName.SHA256, SpaceKeySize);
        var ct = new byte[plain.Length];
        var tag = new byte[TagSize];
        try
        {
            using var gcm = new AesGcm(kek, TagSize);
            gcm.Encrypt(nonce, plain, ct, tag, WrapAad(WrapIterations));
        }
        finally { CryptographicOperations.ZeroMemory(kek); }

        var blob = new byte[ct.Length + TagSize];
        ct.CopyTo(blob, 0);
        tag.CopyTo(blob, ct.Length);

        return new SyncKeyWrapRecord
        {
            Wrap = WrapVersion,
            Kdf = KdfName,
            Iter = WrapIterations,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Ct = Convert.ToBase64String(blob),
        };
    }





    public static string Unwrap(SyncKeyWrapRecord record, string lockPassword)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrEmpty(lockPassword)) throw new ArgumentException("lock password is required", nameof(lockPassword));
        if (record.Wrap != WrapVersion) throw new InvalidDataException($"unsupported keywrap version: {record.Wrap}");
        if (!string.Equals(record.Kdf, KdfName, StringComparison.Ordinal))
            throw new InvalidDataException($"unsupported keywrap kdf: {record.Kdf}");
        if (record.Iter < 1000 || record.Iter > 5_000_000) throw new InvalidDataException("keywrap iteration count out of range");

        byte[] salt, nonce, blob;
        try
        {
            salt = Convert.FromBase64String(record.Salt);
            nonce = Convert.FromBase64String(record.Nonce);
            blob = Convert.FromBase64String(record.Ct);
        }
        catch (FormatException) { throw new InvalidDataException("keywrap record contains invalid base64"); }

        if (salt.Length != SaltSize) throw new InvalidDataException("keywrap salt must be 32 bytes");
        if (nonce.Length != NonceSize) throw new InvalidDataException("keywrap nonce must be 12 bytes");
        if (blob.Length != SpaceKeyTextLength + TagSize) throw new InvalidDataException("keywrap ciphertext has an unexpected length");

        var kek = Rfc2898DeriveBytes.Pbkdf2(lockPassword, salt, record.Iter, HashAlgorithmName.SHA256, SpaceKeySize);
        var plain = new byte[SpaceKeyTextLength];
        try
        {
            using var gcm = new AesGcm(kek, TagSize);
            gcm.Decrypt(nonce, blob.AsSpan(0, SpaceKeyTextLength), blob.AsSpan(SpaceKeyTextLength, TagSize), plain, WrapAad(record.Iter));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidDataException("keywrap could not be opened (wrong lock password or tampered record)");
        }
        finally { CryptographicOperations.ZeroMemory(kek); }

        var text = Encoding.UTF8.GetString(plain);
        if (!IsWellFormedSpaceKey(text)) throw new InvalidDataException("keywrap payload is not a valid space key");
        return text;
    }











    public static string? Rewrap(string wrapJson, string oldPassword, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword)) return null;

        string spaceKey;
        try { spaceKey = Unwrap(SyncJson.DeserializeKeyWrap(wrapJson), oldPassword); }
        catch (InvalidDataException) { return null; }
        catch (ArgumentException) { return null; }

        var rewrapped = SyncJson.SerializeKeyWrap(Wrap(spaceKey, newPassword));

        string? reopened;
        try { reopened = Unwrap(SyncJson.DeserializeKeyWrap(rewrapped), newPassword); }
        catch (InvalidDataException e) { throw new InvalidDataException("the re-wrapped keywrap did not open with the new password", e); }
        catch (ArgumentException e) { throw new InvalidDataException("the re-wrapped keywrap did not open with the new password", e); }

        if (!string.Equals(reopened, spaceKey, StringComparison.Ordinal))
            throw new InvalidDataException("the re-wrapped keywrap opened to a different space key");
        return rewrapped;
    }





    public static byte[] DeriveBlobKey(string spaceKeyBase64, byte[] versionSalt, int iterations = BlobIterations)
    {
        if (!IsWellFormedSpaceKey(spaceKeyBase64))
            throw new ArgumentException($"space key must be the base64 text of {SpaceKeySize} bytes", nameof(spaceKeyBase64));
        if (versionSalt is null || versionSalt.Length != SaltSize)
            throw new ArgumentException($"version salt must be {SaltSize} bytes", nameof(versionSalt));

        return Rfc2898DeriveBytes.Pbkdf2(spaceKeyBase64, versionSalt, iterations, HashAlgorithmName.SHA256, SpaceKeySize);
    }
}
