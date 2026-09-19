using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novara.Services;

public static class PasswordService
{
    private const int SaltSize = 32;
    private const int HashSize = 32;


    private static string? _baseDirOverride;
    public static void SetBaseDir(string? dir) => _baseDirOverride = dir;

    private static string BaseDir => _baseDirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreEnv.DataDirName);
    private static string SecurityPath => Path.Combine(BaseDir, "security.dat");
    private static string LockoutPath => Path.Combine(BaseDir, "lockout.dat");

    private sealed class SecurityFile
    {
        public string HashSalt { get; set; } = "";
        public string DeriveSalt { get; set; } = "";
        public string Hash { get; set; } = "";



        public int Version { get; set; } = 1;
    }

    private sealed class LockoutFile
    {

        public bool Enabled { get; set; } = true;
        public DateTime Until { get; set; }
        public int FailCount { get; set; }


        public long RemainingMs { get; set; }
        public long TickAtLock { get; set; }
        public DateTime WallAtLockUtc { get; set; }
    }

    public static bool Exists() => File.Exists(SecurityPath);


    public static string SecurityFilePath => SecurityPath;

    public static bool Create(string password)
    {
        try
        {
            var hashSalt = RandomNumberGenerator.GetBytes(SaltSize);
            var deriveSalt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = HashPasswordV2(password, hashSalt);
            Directory.CreateDirectory(BaseDir);
            AtomicWrite(SecurityPath, JsonSerializer.Serialize(new SecurityFile
            {
                HashSalt = Convert.ToBase64String(hashSalt),
                DeriveSalt = Convert.ToBase64String(deriveSalt),
                Hash = Convert.ToBase64String(hash),
                Version = 2
            }));




            DiscardStaged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Verify(string password)
    {
        try
        {
            var sf = LoadSecurity();
            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.Hash)) return false;
            var hashSalt = Convert.FromBase64String(sf.HashSalt);
            var expected = Convert.FromBase64String(sf.Hash);


            var actual = sf.Version >= 2 ? HashPasswordV2(password, hashSalt) : HashPassword(password, hashSalt);
            var ok = CryptographicOperations.FixedTimeEquals(expected, actual);
            if (ok && sf.Version < 2) UpgradeSecurityFile(sf, password, hashSalt);
            return ok;
        }
        catch
        {
            return false;
        }
    }




    private static void UpgradeSecurityFile(SecurityFile sf, string password, byte[] hashSalt)
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            AtomicWrite(SecurityPath, JsonSerializer.Serialize(new SecurityFile
            {
                HashSalt = sf.HashSalt,
                DeriveSalt = sf.DeriveSalt,
                Hash = Convert.ToBase64String(HashPasswordV2(password, hashSalt)),
                Version = 2
            }));
        }
        catch { }
    }


    public enum SecurityHealth { Ok, NotExists, Damaged, TransientError }







    public static SecurityHealth CheckSecurityHealth()
    {
        try
        {
            if (!File.Exists(SecurityPath)) return SecurityHealth.NotExists;
            string text;
            using (var fs = new FileStream(SecurityPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
            var sf = JsonSerializer.Deserialize<SecurityFile>(text);


            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.DeriveSalt) || string.IsNullOrEmpty(sf.Hash)) return SecurityHealth.Damaged;

            if (Convert.FromBase64String(sf.HashSalt).Length != SaltSize) return SecurityHealth.Damaged;
            if (Convert.FromBase64String(sf.DeriveSalt).Length != SaltSize) return SecurityHealth.Damaged;
            if (Convert.FromBase64String(sf.Hash).Length != HashSize) return SecurityHealth.Damaged;
            return SecurityHealth.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SecurityHealth.TransientError;
        }
        catch
        {
            return SecurityHealth.Damaged;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(SecurityPath)) File.Delete(SecurityPath); } catch { }
        DiscardStaged();
    }













    private static string StagedPath => Path.Combine(BaseDir, "security.dat.new");



    public static bool StageChange(string newPassword)
    {
        try
        {
            var sf = LoadSecurity();
            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.DeriveSalt)) return false;
            var hashSalt = Convert.FromBase64String(sf.HashSalt);
            Directory.CreateDirectory(BaseDir);
            AtomicWrite(StagedPath, JsonSerializer.Serialize(new SecurityFile
            {
                HashSalt = sf.HashSalt,
                DeriveSalt = sf.DeriveSalt,
                Hash = Convert.ToBase64String(HashPasswordV2(newPassword, hashSalt)),
                Version = 2
            }));
            return true;
        }
        catch { return false; }
    }

    public static bool HasStagedChange()
    {
        try { return File.Exists(StagedPath); }
        catch { return false; }
    }


    public static bool VerifyStaged(string password)
    {
        try
        {
            if (!File.Exists(StagedPath)) return false;
            var sf = JsonSerializer.Deserialize<SecurityFile>(File.ReadAllText(StagedPath));
            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.Hash) || sf.Version < 2) return false;
            var expected = Convert.FromBase64String(sf.Hash);
            var actual = HashPasswordV2(password, Convert.FromBase64String(sf.HashSalt));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }




    public static bool PromoteStaged()
    {
        try
        {
            if (!File.Exists(StagedPath)) return false;
            var content = File.ReadAllText(StagedPath);
            AtomicWrite(SecurityPath, content);
            File.Delete(StagedPath);
            return true;
        }
        catch { return false; }
    }



    public static void DiscardStaged()
    {
        try { if (File.Exists(StagedPath)) File.Delete(StagedPath); } catch { }
    }

    public static byte[]? GetDeriveSalt()
    {
        var sf = LoadSecurity();
        if (sf == null || string.IsNullOrEmpty(sf.DeriveSalt)) return null;
        try { return Convert.FromBase64String(sf.DeriveSalt); } catch { return null; }
    }

    private static SecurityFile? LoadSecurity()
    {



        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(SecurityPath)) return null;
                return JsonSerializer.Deserialize<SecurityFile>(File.ReadAllText(SecurityPath));
            }
            catch (Exception ex) when (attempt < 2 && (ex is IOException || ex is UnauthorizedAccessException))
            {
                Thread.Sleep(attempt == 0 ? 25 : 75);
            }
        }
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        using var sha = SHA256.Create();
        var input = new byte[salt.Length + System.Text.Encoding.UTF8.GetByteCount(password)];
        salt.CopyTo(input, 0);
        System.Text.Encoding.UTF8.GetBytes(password, input.AsSpan(salt.Length));
        return sha.ComputeHash(input);
    }




    private static byte[] HashPasswordV2(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, CryptoService.CurrentIterations, HashAlgorithmName.SHA256, 32);

    private static LockoutFile? LoadLockout()
    {
        try
        {
            if (!File.Exists(LockoutPath)) return null;
            return JsonSerializer.Deserialize<LockoutFile>(File.ReadAllText(LockoutPath));
        }
        catch { return null; }
    }

    private static void WriteLockout(LockoutFile lf)
    {
        try { Directory.CreateDirectory(BaseDir); AtomicWrite(LockoutPath, JsonSerializer.Serialize(lf)); } catch { }
    }



    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var w = new StreamWriter(fs);
                w.Write(content);
                w.Flush();
                fs.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    public static void SetLockoutUntil(DateTime until)
    {
        var lf = LoadLockout() ?? new LockoutFile();
        lf.Until = until;

        lf.RemainingMs = (long)Math.Max(0, (until - DateTime.Now).TotalMilliseconds);
        lf.TickAtLock = Environment.TickCount64;
        lf.WallAtLockUtc = DateTime.UtcNow;
        WriteLockout(lf);
    }

    public static void ClearLockout()
    {
        var lf = LoadLockout();
        if (lf == null) return;
        lf.Until = default;
        lf.RemainingMs = 0;
        lf.TickAtLock = 0;
        lf.WallAtLockUtc = default;
        WriteLockout(lf);
    }

    public static void DeleteLockout()
    {
        try { if (File.Exists(LockoutPath)) File.Delete(LockoutPath); } catch { }
    }

    public static bool IsLockedOut(out TimeSpan remaining)
    {
        var lf = LoadLockout();
        if (lf == null || lf.Until == default) { remaining = TimeSpan.Zero; return false; }
        remaining = CalcRemaining(lf);


        if (remaining <= TimeSpan.Zero) { ClearLockout(); SetFailCount(0); remaining = TimeSpan.Zero; return false; }
        return true;
    }


    public static TimeSpan GetRemainingLockout()
    {
        var lf = LoadLockout();
        if (lf == null || lf.Until == default) return TimeSpan.Zero;
        return CalcRemaining(lf);
    }




    private static TimeSpan CalcRemaining(LockoutFile lf)
    {
        if (lf.RemainingMs > 0)
        {
            long elapsed = Environment.TickCount64 - lf.TickAtLock;
            if (elapsed >= 0)
                return TimeSpan.FromMilliseconds(Math.Max(0, lf.RemainingMs - elapsed));
            double wallElapsed = (DateTime.UtcNow - lf.WallAtLockUtc).TotalMilliseconds;
            return TimeSpan.FromMilliseconds(Math.Max(0, lf.RemainingMs - wallElapsed));
        }

        var r = lf.Until - DateTime.Now;
        return r > TimeSpan.Zero ? r : TimeSpan.Zero;
    }

    public static bool IsLockoutEnabled()
    {
        return LoadLockout()?.Enabled ?? true;
    }

    public static void SetLockoutEnabled(bool enabled)
    {
        var lf = LoadLockout() ?? new LockoutFile();
        lf.Enabled = enabled;
        if (!enabled)
        {

            lf.FailCount = 0;
            lf.Until = default;
            lf.RemainingMs = 0;
            lf.TickAtLock = 0;
            lf.WallAtLockUtc = default;
        }
        WriteLockout(lf);
    }

    public static int GetFailCount()
    {
        return LoadLockout()?.FailCount ?? 0;
    }

    public static void SetFailCount(int count)
    {
        var lf = LoadLockout() ?? new LockoutFile();
        lf.FailCount = count;
        WriteLockout(lf);
    }
}
