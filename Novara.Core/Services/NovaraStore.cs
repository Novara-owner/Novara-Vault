/* ========== NovaraStore - Persistence Layer ==========
Function: Single-file database (data.novadb) - magic/version/MD5 header validation, AES encryption, atomic write, async save merging, import/export, reset
Corresponding UI: NovaraStore.cs
Logic Range: Whole file business logic of this module
*/
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

public class NovaraStore
{

    private const uint Magic = 0x41564F4E; // "NOVA"
    private const byte FileVersionLegacy = 1;   // 2.0: plaintext or CBC-encrypted (see 4.7)
    private const byte FileVersionCurrent = 2;  // 3.0: AES-256-GCM encrypted
    private const byte FlagPlain = 0x00;
    private const byte FlagEncrypted = 0x01;
    private const int HeaderSize = 4 + 1 + 1 + 16;
    // 2026-08-28 integrity upgrade (design doc 9.1#3): BACKUP FILES ONLY - version 3 carries a full
    // SHA-256 digest in a 38B header. data.novadb never writes v3 (2.1 contract: its header matrix
    // stays v1-plain/v1-CBC/v2-GCM, encrypted integrity via GCM tag).
    private const byte FileVersionSha256Backup = 3;
    private const int HeaderSizeSha256 = 4 + 1 + 1 + 32;

    // 2026-08-29 encrypted export backup (design doc 9.2#6): version 4 - the header carries the
    // algorithm/KDF ids, the iteration count and a per-backup random salt, so the file is fully
    // self-contained and decryptable on any machine without security.dat. Layout: 44B header +
    // CryptoService GCM block (nonce 12 + tag 16 + ciphertext). The 44 header bytes double as the
    // GCM associated data (the tag itself cannot be part of it; the nonce is authenticated by the
    // tag). Iterations live in the file so the 9.2#7 KDF upgrade can raise them per-export while
    // old backups keep importing. data.novadb never writes v4 (2.1 contract untouched).
    private const byte FileVersionEncryptedBackup = 4;
    private const int HeaderSizeEncryptedBackup = 4 + 1 + 1 + 1 + 1 + 4 + 32; // 44
    private const int GcmBlockOverhead = 12 + 16; // nonce + tag prefix of the CryptoService GCM output
    private const byte AlgoIdAes256Gcm = 0x00;
    private const byte KdfIdPbkdf2Sha256 = 0x00;
    private const int BackupKdfIterations = 300000;
    // Import-side sanity window for the header-declared iteration count: rejects attacker-crafted
    // headers (DoS via a multi-second derivation on the UI thread) and zero/garbage values.
    private const int BackupKdfIterationsMin = 1000;
    private const int BackupKdfIterationsMax = 2000000;

    
    private const int SaveDebounceMs = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private volatile bool _savePending;
    private volatile bool _saveRunning;
    private volatile bool _loaded;
    private volatile bool _suppressSave; // N4S-01: disk was overwritten by a snapshot restore - the in-memory DB is stale, every save path must no-op until the state is re-established
    private byte _encryptionFlag = FlagPlain;
    private int _fileVersion = FileVersionLegacy; // read from the file header; plaintext stays v1 forever (2.0-compatible)
    private bool _needsFormatMigration; // v1 CBC loaded -> offer the one-time GCM migration (4.7)
    private string? _password;

    public NovaraDatabase Database { get; private set; } = new();

    public byte EncryptionFlag => _encryptionFlag;

    public bool IsLoaded => _loaded;

    /// <summary>
    /// N4S-01: AutoBackupService flips this on after a successful restore (disk now holds the restored
    /// snapshot while memory still holds the pre-restore DB) and off after a successful rollback.
    /// While on, SaveAsync/SaveSync are success-no-ops: exit paths may proceed, but nothing may write
    /// the stale in-memory state back over the restored file (which would silently roll back the
    /// restore, or crash on a missing derive salt when the snapshot's encryption state differs).
    /// Cleared by ResetDatabase/ImportBackup - those rebuild an authoritative memory state themselves.
    /// </summary>
    public void SetSuppressSave(bool on) => _suppressSave = on;

    /// <summary>N5S-02: true while a restored snapshot awaits the restart barrier - UI layers use this
    /// to refuse navigation/write entries instead of relying on the overlay alone.</summary>
    public bool IsSaveSuppressed => _suppressSave;

    public bool IsEncrypted => _encryptionFlag == FlagEncrypted;

    
    
    public void Invalidate()
    {
        _suppressSave = true; 
        _loaded = false;
        _password = null;
        Database = null!; 
    }

    /// <summary>Current in-memory password (null when plaintext/not loaded). 5.0 Windows Hello
    /// enable flow reads it to store into PasswordVault.</summary>
    public string? Password => _password;

    /// <summary>True when the loaded database is v1 CBC-encrypted and can be migrated to GCM (4.7).</summary>
    public bool NeedsFormatMigration => _needsFormatMigration;

    public event Action<string>? SaveFailed;

    public NovaraStore(string filePath)
    {
        _filePath = filePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreEnv.DataDirName, "data.novadb");

    public LoadResult Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {

                Database = new NovaraDatabase();
                _encryptionFlag = FlagPlain;
                _loaded = true;
                SaveSync();
                return new LoadResult(LoadStatus.EmptyCreated);
            }

            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < HeaderSize) return Corrupted(Loc.T("Store_Err_HeaderIncomplete"));

            var header = new byte[HeaderSize];
            fs.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != Magic) return Corrupted(Loc.T("Store_Err_BadMagic"));
            if (header[4] != FileVersionLegacy && header[4] != FileVersionCurrent) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]));
            _fileVersion = header[4]; // v1 (legacy CBC / plaintext) or v2 (GCM); see 4.7
            byte flag = header[5];
            var md5Stored = header.AsSpan(6, 16).ToArray();

            var body = new byte[fs.Length - HeaderSize];
            fs.ReadExactly(body);

            if (flag == FlagEncrypted)
            {

                _encryptionFlag = FlagEncrypted;
                _loaded = false;
                return new LoadResult(LoadStatus.Encrypted);
            }
            if (flag != FlagPlain) return Corrupted(string.Format(Loc.T("Store_Err_UnknownEnc"), flag));
            if (header[4] != FileVersionLegacy) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4])); // E1-26: a v2 plaintext file does not exist (4.7 matrix) - defensive corruption
            _fileVersion = FileVersionLegacy; // plaintext is always v1 (2.0-compatible)

            var md5Actual = MD5.HashData(body);
            if (!md5Stored.AsSpan().SequenceEqual(md5Actual))
                return Corrupted(Loc.T("Store_Err_Md5Fail"));

            var db = JsonSerializer.Deserialize<NovaraDatabase>(body, JsonOptions);
            if (db == null) return Corrupted(Loc.T("Store_Err_Deserialize"));

            // D23: sanitize null collection props on every load (old DBs / external files may carry
            // checkedStates:null / subTexts:null which NRE BuildTodoCard and SearchPage string.Join).
            if (db.TodoCards != null) foreach (var td in db.TodoCards) { td.CheckedStates ??= new(); td.SubTexts ??= new(); }

            // E4-10: partition-level null protection - a hand-crafted/external file with "DiaryItems": null
            // would NRE every page's .Where(...) (symmetric with ImportBackup normalization).
            db.AppSettings ??= new AppSettings();
            db.AppSettings.McpAllowedProcesses ??= new();
            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            foreach (var en in db.MemoEntries) en.Fields ??= new(); // E5-07: field-level protection (symmetric with ImportBackup) - "fields":null entries would NRE BasicMemoPage/SearchPage e.Fields.Select
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();

            Database = db;
            _encryptionFlag = FlagPlain;
            _loaded = true;
            return new LoadResult(LoadStatus.Ok);
        }
        catch (Exception ex)
        {
            // D8-1 (Round 5): MD5 ok but JSON deserialize failed -> corrupted (same as #38)
            return ex is System.Text.Json.JsonException
                ? Corrupted(Loc.T("Store_Err_Deserialize"))
                : new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    public LoadResult LoadWithPassword(string password)
    {
        try
        {
            if (!File.Exists(_filePath))
            {

                Database = new NovaraDatabase();
                _encryptionFlag = FlagPlain;
                _password = null;
                _loaded = true;
                return new LoadResult(LoadStatus.EmptyCreated);
            }
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < HeaderSize) return Corrupted(Loc.T("Store_Err_HeaderIncomplete"));
            var header = new byte[HeaderSize];
            fs.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != Magic) return Corrupted(Loc.T("Store_Err_BadMagic"));
            if (header[4] != FileVersionLegacy && header[4] != FileVersionCurrent) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]));
            _fileVersion = header[4];
            if (header[5] == FlagPlain)
                return header[4] == FileVersionCurrent
                    ? Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4])) // E5-16: a v2 plaintext file does not exist (4.7 matrix) - defensive corruption, symmetric with Load() E1-26
                    : new LoadResult(LoadStatus.WrongPassword, Loc.T("Store_Err_NotEncrypted"));
            if (header[5] != FlagEncrypted) return Corrupted(string.Format(Loc.T("Store_Err_UnknownEnc"), header[5])); // D8-2 (Round 5): unknown encryption flag -> corrupted (aligned with Load())
            var md5Stored = header.AsSpan(6, 16).ToArray();
            var body = new byte[fs.Length - HeaderSize];
            fs.ReadExactly(body);

            if (!PasswordService.Verify(password))
            {
                // N4S-04: classify the failure instead of always counting a strike. A damaged security.dat
                // previously locked the user into the 5-strike loop with no path to the rebuild dialog.
                return PasswordService.CheckSecurityHealth() switch
                {
                    PasswordService.SecurityHealth.Ok => new LoadResult(LoadStatus.WrongPassword, Loc.T("Store_Err_WrongPassword")), // intact file, hash mismatch - genuine wrong password
                    PasswordService.SecurityHealth.TransientError => new LoadResult(LoadStatus.IoError, "security.dat read failed"), // AV/lock transient - retry-only, never destructive
                    _ => Corrupted(Loc.T("Store_Err_SecurityMissing")), // NotExists / Damaged - stable corruption
                };
            }
            var salt = PasswordService.GetDeriveSalt();
            if (salt == null) return Corrupted(Loc.T("Store_Err_SecurityMissing"));

            byte[] plain;
            if (_fileVersion == FileVersionCurrent)
            {
                // v2 GCM: authenticated by the 16-byte tag - no MD5; wrong password / tamper throw.
                // E1-02: Verify() already passed above, so a decrypt failure is corruption/tampering,
                // NOT a wrong password - report it as Corrupted (the lock screen shows the corrupt dialog
                // instead of counting strikes and locking the user out of a damaged database).
                try { plain = CryptoService.DecryptGcm(body, password, salt); }
                catch { return Corrupted(Loc.T("Store_Err_DecryptFail")); } 
                _needsFormatMigration = false;
            }
            else
            {
                try { plain = CryptoService.Decrypt(body, password, salt); }
                catch { return Corrupted(Loc.T("Store_Err_DecryptFail")); } // E2-02: generic corruption copy (the MD5 check below keeps Store_Err_Md5Fail)
                var md5Actual = MD5.HashData(plain);
                if (!md5Stored.AsSpan().SequenceEqual(md5Actual)) return Corrupted(Loc.T("Store_Err_Md5Fail"));
                _needsFormatMigration = true; // v1 CBC loaded - the one-time GCM migration is offered (4.7)
            }

            var db = JsonSerializer.Deserialize<NovaraDatabase>(plain, JsonOptions);
            if (db == null) return Corrupted(Loc.T("Store_Err_Deserialize"));

            // D23: same sanitize as the plain load above (see that comment).
            if (db.TodoCards != null) foreach (var td in db.TodoCards) { td.CheckedStates ??= new(); td.SubTexts ??= new(); }

            // E4-10: partition-level null protection - symmetric with the plaintext load path.
            db.AppSettings ??= new AppSettings();
            db.AppSettings.McpAllowedProcesses ??= new();
            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            foreach (var en in db.MemoEntries) en.Fields ??= new(); // E5-07: field-level protection (symmetric with ImportBackup) - "fields":null entries would NRE BasicMemoPage/SearchPage e.Fields.Select
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();

            Database = db;
            _encryptionFlag = FlagEncrypted;
            _password = password;
            _loaded = true;
            return new LoadResult(LoadStatus.Ok);
        }
        catch (Exception ex)
        {
            // D8-1 (Round 5): decrypted but JSON deserialize failed -> corrupted (same as #38)
            return ex is System.Text.Json.JsonException
                ? Corrupted(Loc.T("Store_Err_Deserialize"))
                : new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    public bool EnableEncryption(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        if (_suppressSave) return false; // N5S-05: a suppressed session must not mint a security.dat the restored db can never pair with
        _password = password;
        _encryptionFlag = FlagEncrypted;
        _fileVersion = FileVersionCurrent; // 3.0 new encryption is GCM (v2) from day one (4.7)
        _loaded = true;
        if (!SaveSync())
        {

            _encryptionFlag = FlagPlain;
            _fileVersion = FileVersionLegacy;
            _password = null;
            return false;
        }
        return true;
    }

    /// <summary>
    /// One-time v1 CBC -&gt; v2 GCM migration (4.7): re-encrypt the in-memory database with the same
    /// password and write atomically. On ANY failure the old file stays intact and the app keeps
    /// running on v1 (the migration is offered again on the next launch).
    /// </summary>
    public bool MigrateFormat()
    {
        if (!_needsFormatMigration || _fileVersion != FileVersionLegacy || string.IsNullOrEmpty(_password))
            return false;
        var oldVersion = _fileVersion;
        _fileVersion = FileVersionCurrent;
        if (!SaveSync())
        {
            _fileVersion = oldVersion; // roll back to v1 - keep the legacy file untouched
            return false;
        }
        _needsFormatMigration = false;
        return true;
    }

    public bool DisableEncryption()
    {
        if (_suppressSave) return false; // N5S-03: refusing here keeps security.dat paired with the restored db - the SaveSync below would fake-succeed and the caller would delete the password file
        _encryptionFlag = FlagPlain;
        if (!SaveSync())
        {

            _encryptionFlag = FlagEncrypted;
            return false;
        }
        _password = null;
        _needsFormatMigration = false; // E5-15: the DB is now plaintext - a stale v1-CBC migration offer must not resurface (E4-18 only covered ResetDatabase)
        return true;
    }

    public bool Reencrypt(string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword)) return false;
        if (_suppressSave) return false; // N5S-04: a fake-success here desyncs security.dat (new hash) from data.novadb (old key) - the exact lock-out N4S-04 family exists to prevent
        var oldPassword = _password; // #25: roll back old password on write failure
        _password = newPassword;
        _encryptionFlag = FlagEncrypted;
        if (!SaveSync()) { _password = oldPassword; return false; }
        return true;
    }

    public bool ExportBackup(string backupPath, bool includeFilePathEntries)
    {
        try
        {
            if (_suppressSave) return false; // N5S-07: memory is stale vs the restored snapshot - exporting it would silently hand the user the wrong data
            if (!_loaded) return false; 
            var export = CloneForExport(includeFilePathEntries);
            if (export == null) return false;
            var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);
            // 2026-08-28 (design doc 9.1#3): backups leave the machine, so integrity moves MD5 -> SHA-256
            // as backup format v3 (38B header). SHA-256 detects corruption only - it is NOT tamper
            // protection on a plaintext file (that lands with the encrypted-backup format, doc 9.2#6).
            var sha = SHA256.HashData(json);
            var header = new byte[HeaderSizeSha256];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
            header[4] = FileVersionSha256Backup; // backup files only - data.novadb never writes v3
            header[5] = FlagPlain;
            sha.CopyTo(header, 6);
            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // E2-03: atomic write (tmp + move) - an interrupted export must never leave a half-written backup file
            var tmpPath = backupPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(header);
                fs.Write(json);
                fs.Flush(true); // D8-3 (Round 5): flush to disk (incl. cache) against power loss
            }
            File.Move(tmpPath, backupPath, true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NovaraStore 导出备份失败: {ex}");
            return false;
        }
    }

    /// <summary>
    /// D21: trashed items (IsDeleted) must not leave the machine with a backup - deep-clone the
    /// DB, drop them and return the clean copy; the live in-memory DB is never mutated.
    /// 4.4: includeFilePathEntries=false excludes path backups entirely (device-migration use case).
    /// Shared by ExportBackup (plaintext v3) and ExportBackupEncrypted (v4).
    /// </summary>
    private NovaraDatabase? CloneForExport(bool includeFilePathEntries)
    {
        try
        {
            
            
            
            var copy = JsonSerializer.Deserialize<NovaraDatabase>(JsonSerializer.SerializeToUtf8Bytes(Database, JsonOptions), JsonOptions);
            // E4-20: a clone that fails to deserialize must NOT fall back to the live db (that
            // would leak trashed items / excluded paths into the backup). Fail the export instead,
            // consistent with the catch block below.
            if (copy == null) return null;
            copy.MemoEntries?.RemoveAll(x => x.IsDeleted);
            copy.PathBackupItems?.RemoveAll(x => x.IsDeleted);
            copy.TodoCards?.RemoveAll(x => x.IsDeleted);
            copy.NoteCards?.RemoveAll(x => x.IsDeleted);
            copy.DiaryItems?.RemoveAll(x => x.IsDeleted);
            if (!includeFilePathEntries) copy.PathBackupItems?.Clear();
            return copy;
        }
        catch
        {
            // E1-26: clone failure (concurrent UI mutation) must NOT leak trashed items / excluded
            // paths, and must NOT mutate the live db - fail the export instead; the user retries.
            return null;
        }
    }

    /* ========== NovaraStore Encrypted Export Backup ==========
Function: Encrypted export backup (format v4, design doc 9.2#6, 2026-08-29): 44B self-contained
    header (magic/version=4/flag=encrypted/algoId/kdfId/iterations/random per-backup salt) +
    GCM block (nonce+tag+cipher of GZip'd JSON). Header bytes double as the GCM AAD.
Corresponding UI: SettingsPage encrypted-backup dialogs
Logic Range: ExportBackupEncrypted + the v4 branch in ImportBackup
*/
    public bool ExportBackupEncrypted(string backupPath, string password, bool includeFilePathEntries)
    {
        try
        {
            if (_suppressSave) return false; // N5S-07: same stale-memory guard as the plaintext export
            if (!_loaded) return false; // N6-16: never export a "valid header + empty db" clone
            if (string.IsNullOrEmpty(password)) return false;
            var export = CloneForExport(includeFilePathEntries);
            if (export == null) return false;
            var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);

            // Per-backup random salt: independent from security.dat on purpose - the backup
            // password is a separate secret and must stay decryptable without this machine.
            var salt = RandomNumberGenerator.GetBytes(32);
            var header = new byte[HeaderSizeEncryptedBackup];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
            header[4] = FileVersionEncryptedBackup;
            header[5] = FlagEncrypted;
            header[6] = AlgoIdAes256Gcm;
            header[7] = KdfIdPbkdf2Sha256;
            BitConverter.TryWriteBytes(header.AsSpan(8, 4), BackupKdfIterations);
            salt.CopyTo(header, 12);
            // The raw header bytes are the AAD - parsed and passed as-is, never reassembled
            // field by field (a byte-order/padding mismatch would break every decryption).
            var cipher = CryptoService.EncryptGcm(json, password, salt, BackupKdfIterations, header);

            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // E2-03: atomic write (tmp + move) - same guarantee as the plaintext export
            var tmpPath = backupPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(header);
                fs.Write(cipher); // CryptoService layout: nonce(12) + tag(16) + ciphertext
                fs.Flush(true);
            }
            File.Move(tmpPath, backupPath, true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NovaraStore 加密导出失败: {ex}");
            return false;
        }
    }

    /* ========== NovaraStore Import Backup ==========
Function: Backup import: header/version/MD5 validation, plaintext only, in-memory rollback on failure (#1/#22), null-normalize & dedupe (#23/#24)
Corresponding UI: NovaraStore.cs
Logic Range: Below methods in this region
*/
    public LoadResult ImportBackup(string backupPath, string? backupPassword = null)
    {
        // N5S-06: refuse while suppressed - the old code cleared suppression before validation, so a
        // failed import silently un-protected the restored snapshot, and an "encrypted" success path
        // re-encrypted imported data with the stale session password + snapshot derive salt.
        if (_suppressSave) return new LoadResult(LoadStatus.IoError, "restore pending - restart first");
        var oldDb = Database; // L31: rollback anchor - every failure path below restores this reference
        try
        {
            if (!File.Exists(backupPath)) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupMissing"));
            byte[] body;
            using (var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // 2026-08-28 dual-format import: v3 = SHA-256 over a 38B header (current exports);
                // v1 = legacy MD5 over the 22B header - 2.0/3.0/4.x/5.0 exports stay importable forever.
                // 2026-08-29: v4 = encrypted export backup (9.2#6). Format detection is header-only -
                // the file extension is never consulted (audit 2026-08-29: renamed/stripped suffixes
                // must still import).
                if (fs.Length < HeaderSize) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupHeader"));
                var prefix = new byte[6];
                fs.ReadExactly(prefix);
                if (BitConverter.ToUInt32(prefix, 0) != Magic) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupMagic"));
                var ver = prefix[4];
                if (ver != FileVersionLegacy && ver != FileVersionCurrent && ver != FileVersionSha256Backup && ver != FileVersionEncryptedBackup)
                    return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));

                if (ver == FileVersionEncryptedBackup)
                {
                    // v4 layout: 44B header (magic/ver/flag/algoId/kdfId/iterations/salt) + GCM block
                    // (nonce 12 + tag 16 + cipher). The raw 44 header bytes are the AAD exactly as
                    // they were written - parsed here and passed as-is, never reassembled field by
                    // field (a byte-order mismatch would break every decryption).
                    if (prefix[5] != FlagEncrypted) return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver)); // not in the format matrix (E4-19 symmetry)
                    if (fs.Length < HeaderSizeEncryptedBackup + GcmBlockOverhead)
                        return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupHeader"));
                    if (string.IsNullOrEmpty(backupPassword))
                        return new LoadResult(LoadStatus.NeedPassword); // UI re-enters with the backup password
                    var header44 = new byte[HeaderSizeEncryptedBackup];
                    fs.Position = 0;
                    fs.ReadExactly(header44);
                    var algoId = header44[6];
                    var kdfId = header44[7];
                    var iter32 = BitConverter.ToUInt32(header44, 8);
                    if (algoId != AlgoIdAes256Gcm || kdfId != KdfIdPbkdf2Sha256 || iter32 < BackupKdfIterationsMin || iter32 > BackupKdfIterationsMax)
                        return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver)); // unknown algo/kdf or DoS-grade iteration count
                    var salt = header44[12..44];
                    var encBody = new byte[fs.Length - HeaderSizeEncryptedBackup];
                    fs.ReadExactly(encBody);
                    try
                    {
                        // Auth failure (wrong password OR tampering) is deliberately one
                        // indistinguishable outcome - never split the two error messages.
                        body = CryptoService.DecryptGcm(encBody, backupPassword, salt, (int)iter32, header44);
                    }
                    catch
                    {
                        return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupAuthFail"));
                    }
                }
                else
                {
                    if (prefix[5] != FlagPlain) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupNotPlain"));
                    // E4-19: a v2 plaintext file does not exist in the format matrix (4.7) - reject it,
                    // symmetric with Load's defensive corruption handling (E1-26).
                    if (ver == FileVersionCurrent) return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));

                    var headerSize = ver == FileVersionSha256Backup ? HeaderSizeSha256 : HeaderSize;
                    var digestLen = headerSize - 6;
                    var storedDigest = new byte[digestLen];
                    if (digestLen > 0)
                    {
                        var rest = new byte[digestLen];
                        fs.ReadExactly(rest);
                        rest.CopyTo(storedDigest, 0);
                    }
                    body = new byte[fs.Length - headerSize];
                    fs.ReadExactly(body);
                    var actual = ver == FileVersionSha256Backup ? SHA256.HashData(body) : MD5.HashData(body);
                    if (!storedDigest.AsSpan().SequenceEqual(actual))
                        return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupMd5"));
                }
            }

            var db = JsonSerializer.Deserialize<NovaraDatabase>(body, JsonOptions);
            if (db == null) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupDeserialize"));

            db.AppSettings ??= new AppSettings();
            db.AppSettings.McpAllowedProcesses ??= new();
            db.AppSettings.PrivacyLockEnabled = _encryptionFlag == FlagEncrypted;

            // #23/#24: normalize null partitions, dedupe Ids, clear dangling group refs (prevent ToDictionary/NRE crash)
            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();
            foreach (var en in db.MemoEntries) en.Fields ??= new();
            foreach (var td in db.TodoCards) td.SubTexts ??= new();
            foreach (var td in db.TodoCards) td.CheckedStates ??= new(); 
            var gseen = new HashSet<Guid>();
            foreach (var g in db.MemoGroups) if (!gseen.Add(g.Id)) g.Id = Guid.NewGuid();
            var eseen = new HashSet<Guid>();
            foreach (var e in db.MemoEntries)
            {
                if (!eseen.Add(e.Id)) e.Id = Guid.NewGuid();
                if (e.GroupId != null && !gseen.Contains(e.GroupId.Value)) e.GroupId = null;
            }
            var tseen = new HashSet<Guid>();
            foreach (var t in db.TodoCards) if (!tseen.Add(t.Id)) t.Id = Guid.NewGuid();
            var nseen = new HashSet<Guid>();
            foreach (var n in db.NoteCards) if (!nseen.Add(n.Id)) n.Id = Guid.NewGuid();
            var pseen = new HashSet<Guid>();
            foreach (var p in db.PathBackupItems) if (!pseen.Add(p.Id)) p.Id = Guid.NewGuid();
            var dseen = new HashSet<string>();
            foreach (var d in db.DiaryItems) if (!dseen.Add(d.Id)) d.Id = Guid.NewGuid().ToString();

            if (File.Exists(_filePath)) File.SetAttributes(_filePath, FileAttributes.Normal);
            Database = db;
            
            
            var oldLoaded = _loaded;
            var oldSuppress = _suppressSave;
            _loaded = true;
            _suppressSave = false; // N4S-01: the imported db is about to be written to disk - memory and disk become consistent again
            if (_encryptionFlag == FlagEncrypted)
            {

                if (string.IsNullOrEmpty(_password) || PasswordService.GetDeriveSalt() == null)
                {
                    Database = oldDb; // E1-05: roll back the in-memory DB before bailing out (L31) - else a later SaveAsync silently overwrites the local db with the imported data
                    _loaded = oldLoaded;
                    _suppressSave = oldSuppress;
                    return new LoadResult(LoadStatus.IoError, Loc.T("Store_Err_ImportNoKey"));
                }
                if (!SaveSync()) { Database = oldDb; _loaded = oldLoaded; _suppressSave = oldSuppress; return new LoadResult(LoadStatus.IoError, Loc.T("Store_Err_ReencryptFail")); }
            }
            else
            {
                if (!SaveSync()) { Database = oldDb; _loaded = oldLoaded; _suppressSave = oldSuppress; return new LoadResult(LoadStatus.IoError, Loc.T("Store_Err_WriteFail")); }
            }
            return new LoadResult(LoadStatus.Ok);
        }
        catch (Exception ex)
        {
            Database = oldDb;
            System.Diagnostics.Debug.WriteLine($"NovaraStore 导入备份失败: {ex}");
            // #38: deserialize failure = corrupted backup; other IO/permission -> IoError
            return ex is System.Text.Json.JsonException
                ? new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupDeserialize"))
                : new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    private static LoadResult Corrupted(string detail) => new(LoadStatus.Corrupted, detail);

    public Task SaveAsync()
    {
        if (!_loaded || _suppressSave) return Task.CompletedTask; // N4S-01: suppressed after restore - never write stale memory to disk
        _savePending = true;
        if (_saveRunning) return Task.CompletedTask;
        _saveRunning = true;
        return Task.Run(async () =>
        {
            try
            {
                
                
                await Task.Delay(SaveDebounceMs);
                await _saveGate.WaitAsync();
                try
                {
                    while (_savePending)
                    {
                        _savePending = false;
                        // N5S-01: suppression may flip ON inside the debounce window (Restore runs while a
                        // write is already queued - pending can also be set by non-UI sources). Recheck
                        // under the gate, otherwise stale memory lands on the restored snapshot and - with
                        // the snapshot's paired security.dat - produces ciphertext no password can open.
                        if (_suppressSave) break;
                        WriteSnapshot();
                    }
                }
                finally { _saveGate.Release(); }
            }
            finally
            {
                _saveRunning = false;
                // Race fallback: pending arriving after consume loop is picked up by next round (fix Bug 3)
                if (_savePending) _ = SaveAsync();
            }
        });
    }

    public bool SaveSync()
    {
        if (!_loaded) return false;
        if (_suppressSave) return true; // N4S-01: report success so exit paths proceed - but write nothing, the restored file on disk is authoritative
        _saveGate.Wait();
        try
        {
            
            
            
            var ok = WriteSnapshot();
            if (!ok)
            {
                Thread.Sleep(60);
                ok = WriteSnapshot();
            }
            return ok;
        }
        finally { _saveGate.Release(); }
    }

    public LoadResult ResetDatabase()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.SetAttributes(_filePath, FileAttributes.Normal);
                File.Delete(_filePath);
            }
            Database = new NovaraDatabase();
            _encryptionFlag = FlagPlain;
            _needsFormatMigration = false;   // E4-18: reset stale GCM-migration state (v1-CBC + forgot-password reset)
            _password = null;                // E4-18: no key material after reset
            _fileVersion = FileVersionLegacy; // E4-18: plaintext is always v1 (2.0-compatible)
            _loaded = true;
            _suppressSave = false; // N4S-01: a fresh empty db is authoritative - its write below must land
            if (!SaveSync()) return new LoadResult(LoadStatus.IoError, Loc.T("Store_Err_WriteFail")); // E5-09: a failed empty-DB write must not report EmptyCreated - PerformReset would delete the password files for a reset that never landed on disk
            return new LoadResult(LoadStatus.EmptyCreated);
        }
        catch (Exception ex)
        {
            return new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    /* ========== NovaraStore Encrypted Write ==========
Function: Serialization + header (magic/version/flag/MD5) + AES encrypt + atomic tmp-then-move write + Flush(true); failure raises SaveFailed
Corresponding UI: NovaraStore.cs
Logic Range: Below methods in this region
*/
private bool WriteSnapshot()
    {

        byte[] json;
        try
        {
            json = JsonSerializer.SerializeToUtf8Bytes(Database, JsonOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NovaraStore 序列化失败（UI 并发修改？下次保存自愈）: {ex}");
            return false;
        }
        var md5 = MD5.HashData(json);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Atomic write: tmp file then replace; original intact on failure (fix EnableEncryption half-write)
        var tmpPath = _filePath + ".tmp";
        try
        {
            if (File.Exists(tmpPath)) File.SetAttributes(tmpPath, FileAttributes.Normal);
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var header = new byte[HeaderSize];
                BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
                header[5] = _encryptionFlag;
                if (_encryptionFlag == FlagEncrypted)
                {
                    // v2 GCM: the 16-byte tag authenticates the data, MD5 field stays zero (4.7)
                    header[4] = _fileVersion == FileVersionCurrent ? FileVersionCurrent : FileVersionLegacy;
                    if (header[4] == FileVersionLegacy) md5.CopyTo(header, 6);
                }
                else
                {
                    header[4] = FileVersionLegacy; // plaintext is always v1 (2.0-compatible)
                    _fileVersion = FileVersionLegacy;
                    md5.CopyTo(header, 6);
                }
                fs.Write(header);
                if (_encryptionFlag == FlagEncrypted)
                {

                    var salt = PasswordService.GetDeriveSalt();
                    if (salt == null || string.IsNullOrEmpty(_password)) throw new InvalidOperationException(Loc.T("Store_Err_EncNoKey"));
                    var cipher = _fileVersion == FileVersionCurrent
                        ? CryptoService.EncryptGcm(json, _password, salt)
                        : CryptoService.Encrypt(json, _password, salt);
                    fs.Write(cipher);
                }
                else
                {
                    fs.Write(json);
                }
                fs.Flush(true); // D8-3 (Round 5): flush to disk (incl. cache) against power loss
            }
            if (File.Exists(_filePath)) File.SetAttributes(_filePath, FileAttributes.Normal);
            File.Move(tmpPath, _filePath, true);
            try { File.SetAttributes(_filePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NovaraStore 写盘失败: {ex}");
            try { if (File.Exists(tmpPath)) { File.SetAttributes(tmpPath, FileAttributes.Normal); File.Delete(tmpPath); } } catch { }
            try
            {

                if (ex is InvalidOperationException)
                    SaveFailed?.Invoke(Loc.T("Store_Err_KeyMissing"));
                else
                    SaveFailed?.Invoke(Loc.T("Store_SaveFail_Detail"));
            }
            catch { }
            return false;
        }
    }
}

public enum LoadStatus { Ok, EmptyCreated, Encrypted, WrongPassword, Corrupted, IoError, NeedPassword }

public record LoadResult(LoadStatus Status, string? Detail = null);
