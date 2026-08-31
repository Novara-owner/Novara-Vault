/* ========== PasswordService - Security Credentials ==========
Function: security.dat management - dual-salt SHA256 hashing, constant-time verify, lockout.dat fail-count persistence
Corresponding UI: PasswordService.cs
Logic Range: Whole file business logic of this module
*/
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Novara.Services;

public static class PasswordService
{
    private const int SaltSize = 32;
    private const int HashSize = 32; // SHA256

    
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
        /// <summary>9.2#7 (2026-08-29): 1 = legacy salted-SHA256 (pre-KDF-hardening, field missing on
        /// old files), 2 = PBKDF2-SHA256 with CryptoService.CurrentIterations. Old files verify via
        /// the legacy algorithm and are rewritten to v2 transparently on the first successful verify.</summary>
        public int Version { get; set; } = 1;
    }

    private sealed class LockoutFile
    {

        public bool Enabled { get; set; } = true;
        public DateTime Until { get; set; }
        public int FailCount { get; set; } // 2026-08-06: persist fail count so restart cannot bypass the 5-strike lockout (Bug 15)
        
        
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
            var hash = HashPasswordV2(password, hashSalt); // 9.2#7: hardened hash from day one
            Directory.CreateDirectory(BaseDir);
            AtomicWrite(SecurityPath, JsonSerializer.Serialize(new SecurityFile
            {
                HashSalt = Convert.ToBase64String(hashSalt),
                DeriveSalt = Convert.ToBase64String(deriveSalt),
                Hash = Convert.ToBase64String(hash),
                Version = 2
            }));
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
            // 9.2#7: verify by the file's own version. Legacy files verify via salted SHA-256 and are
            // transparently rewritten to the hardened PBKDF2 format on their first successful verify.
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

    /// <summary>9.2#7: silent in-place hardening - keep hashSalt + deriveSalt, rewrite only the
    /// password hash in the V2 format. Failure is swallowed on purpose: the verify result stands and
    /// the next successful verify retries the upgrade.</summary>
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

    /// <summary>Health of security.dat, independent of any password (N4S-04).</summary>
    public enum SecurityHealth { Ok, NotExists, Damaged, TransientError }

    /// <summary>
    /// N4S-04: classify WHY a Verify would fail before counting it as a wrong password. A damaged
    /// security.dat (half-written JSON / broken base64 - stable corruption) must surface as Corrupted
    /// so the rebuild dialog appears; a transient read failure (AV lock etc.) must stay retryable and
    /// never offer the destructive path; only an intact file with a mismatching hash counts strikes.
    /// </summary>
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
            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.Hash)) return SecurityHealth.Damaged;
            Convert.FromBase64String(sf.HashSalt);
            Convert.FromBase64String(sf.Hash);
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

    public static bool ChangePassword(string oldPassword, string newPassword)
    {
        if (!Verify(oldPassword)) return false;
        try
        {
            // E4-01: keep hashSalt + deriveSalt, rewrite only the password hash. Reencrypt(newPw)
            // (SettingsPage) encrypts data.novadb with the current deriveSalt; regenerating it here
            // would leave security.dat and data.novadb inconsistent until the next write, and a
            // crash in that window makes the DB undecryptable ("corrupted" -> guided full wipe).
            var sf = LoadSecurity();
            if (sf == null || string.IsNullOrEmpty(sf.HashSalt) || string.IsNullOrEmpty(sf.DeriveSalt)) return false;
            var hashSalt = Convert.FromBase64String(sf.HashSalt);
            var deriveSalt = Convert.FromBase64String(sf.DeriveSalt);
            var hash = HashPasswordV2(newPassword, hashSalt); // 9.2#7: hardened hash from day one
            Directory.CreateDirectory(BaseDir);
            AtomicWrite(SecurityPath, JsonSerializer.Serialize(new SecurityFile
            {
                HashSalt = Convert.ToBase64String(hashSalt),
                DeriveSalt = Convert.ToBase64String(deriveSalt),
                Hash = Convert.ToBase64String(hash),
                Version = 2
            }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Delete()
    {
        try { if (File.Exists(SecurityPath)) File.Delete(SecurityPath); } catch { }
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

    /// <summary>9.2#7: PBKDF2-SHA256 verification hash (3M iterations, CryptoService.CurrentIterations)
    /// - a fast salted SHA-256 lets a GPU-holder brute-force the password file itself; the hardened
    /// hash makes security.dat as expensive to attack as the database it unlocks.</summary>
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

    /// <summary>E1-15: atomic write (tmp + move) - a half-written security.dat would lock the user
    /// out of an encrypted database with no way back.</summary>
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var w = new StreamWriter(fs);
            w.Write(content);
            w.Flush();
            fs.Flush(true); // E3-13: flush to disk incl. cache - a half-written security.dat on power loss would lock the data
        }
        File.Move(tmp, path, true);
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
        // E3-04: the expiry branch must ALSO reset FailCount - ClearLockout alone keeps it, so a user
        // who was locked, restarted and waited out the timer would get re-locked after ONE wrong try.
        if (remaining <= TimeSpan.Zero) { ClearLockout(); SetFailCount(0); remaining = TimeSpan.Zero; return false; }
        return true;
    }

    
    public static TimeSpan GetRemainingLockout()
    {
        var lf = LoadLockout();
        if (lf == null || lf.Until == default) return TimeSpan.Zero;
        return CalcRemaining(lf);
    }

    /// <summary>
    
    /// </summary>
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
