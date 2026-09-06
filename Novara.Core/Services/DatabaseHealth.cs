/* ========== DatabaseHealth - Read-Only Health Checks ==========
Function: Orphan-reference scan + live data-file integrity verification (design doc 9.2#8, 2026-08-29).
          Strictly read-only reporting - no repair, no mutation of memory or disk (9.2: no destructive
          entry points by default; a hanging GroupId is reported, never auto-cleared here).
Corresponding UI: SettingsPage data-overview card (health metric blocks)
Logic Range: Whole file
*/
using System.IO;
using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Services;

public enum DataFileIntegrity { DigestVerified, EncryptedStructured, Failed }

public static class DatabaseHealth
{
    /// <summary>
    /// Hanging MemoEntry.GroupId references (target group no longer exists). Soft-deleted entries
    /// are counted too: a trash-restored item must not come back with a dangling group link.
    /// </summary>
    public static int CountOrphanMemoEntries(NovaraDatabase db)
    {
        if (db == null) return 0;
        var groupIds = new HashSet<Guid>(db.MemoGroups.Select(g => g.Id));
        return db.MemoEntries.Count(e => e.GroupId != null && !groupIds.Contains(e.GroupId.Value));
    }

    /// <summary>
    /// Integrity verification of the live data file. Honest scope per D1 (design 9.2#8):
    
    
    ///   authentication (CBC digest covers plaintext only / GCM tag needs a full decryption pass)
    ///   is deliberately NOT performed here - the report never overstates what was verified.
    
    /// The writer is atomic (tmp+move), so a concurrent read always sees a complete file.
    /// </summary>
    public static DataFileIntegrity VerifyDataFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return DataFileIntegrity.Failed;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < NovaraStore.HeaderSize) return DataFileIntegrity.Failed;
            var header = new byte[NovaraStore.HeaderSize];
            fs.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != NovaraStore.Magic) return DataFileIntegrity.Failed;
            var ver = header[4];
            var flag = header[5];
            // N2-14: v3 (KDF-hardened, 9.2#7) is a healthy current version - listing only Legacy/
            
            if (ver != NovaraStore.FileVersionLegacy && ver != NovaraStore.FileVersionCurrent && ver != NovaraStore.FileVersionKdfHardened)
                return DataFileIntegrity.Failed;

            if (flag == NovaraStore.FlagEncrypted)
            {
                // v1 CBC: minimum CBC block overhead; v2/v3 GCM: nonce(12) + tag(16) minimum body.
                // N3-20: compare explicitly instead of ver >= FileVersionCurrent - the >= form leaned
                // on enum ordering coincidentally matching the version matrix (1<2<3), which would
                // silently misclassify future versions; the explicit whitelist matches :52 above.
                var isGcm = ver == NovaraStore.FileVersionCurrent || ver == NovaraStore.FileVersionKdfHardened;
                // N4-25: GCM minimum body = nonce(12) + tag(16) + at least 1 ciphertext byte (29),
                // matching DecryptGcm's tightened lower bound - a 28-byte body is a truncated file.
                var minBody = isGcm ? 12 + 16 + 1 : 16;
                return fs.Length >= NovaraStore.HeaderSize + minBody
                    ? DataFileIntegrity.EncryptedStructured
                    : DataFileIntegrity.Failed;
            }
            if (flag != NovaraStore.FlagPlain) return DataFileIntegrity.Failed;
            if (ver != NovaraStore.FileVersionLegacy) return DataFileIntegrity.Failed; // E4-19: v2 plaintext does not exist

            
            fs.Position = NovaraStore.HeaderSize;
            var stored = header.AsSpan(6, 16).ToArray();
            return MD5.HashData(fs).AsSpan().SequenceEqual(stored)
                ? DataFileIntegrity.DigestVerified
                : DataFileIntegrity.Failed;
        }
        catch
        {
            return DataFileIntegrity.Failed;
        }
    }
}
