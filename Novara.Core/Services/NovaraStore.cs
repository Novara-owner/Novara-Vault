using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;

public class NovaraStore
{

    internal const uint Magic = 0x41564F4E;
    internal const byte FileVersionLegacy = 1;
    internal const byte FileVersionCurrent = 2;
    internal const byte FlagPlain = 0x00;
    internal const byte FlagEncrypted = 0x01;
    internal const int HeaderSize = 4 + 1 + 1 + 16;



    private const byte FileVersionSha256Backup = 3;
    private const int HeaderSizeSha256 = 4 + 1 + 1 + 32;





    internal const byte FileVersionKdfHardened = 3;








    private const byte FileVersionEncryptedBackup = 4;
    private const int HeaderSizeEncryptedBackup = 4 + 1 + 1 + 1 + 1 + 4 + 32;
    private const int GcmBlockOverhead = 12 + 16;
    private const byte AlgoIdAes256Gcm = 0x00;
    private const byte KdfIdPbkdf2Sha256 = 0x00;
    private const int BackupKdfIterations = 3_000_000;


    private const int BackupKdfIterationsMin = 1000;
    private const int BackupKdfIterationsMax = 5_000_000;


    private const int SaveDebounceMs = 300;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _saveLock = new();
    private volatile bool _savePending;
    private volatile bool _saveRunning;
    private volatile bool _loaded;
    private volatile bool _suppressSave;
    private byte _encryptionFlag = FlagPlain;
    private int _fileVersion = FileVersionLegacy;
    private bool _needsFormatMigration;
    private string? _password;

    public NovaraDatabase Database { get; private set; } = new();

    public byte EncryptionFlag => _encryptionFlag;

    public bool IsLoaded => _loaded;









    public void SetSuppressSave(bool on) => _suppressSave = on;



    public bool IsSaveSuppressed => _suppressSave;





    internal const string RestorePendingMessage = "restore pending - restart first";

    public bool IsEncrypted => _encryptionFlag == FlagEncrypted;



    public void Invalidate()
    {
        _suppressSave = true;
        _loaded = false;
        _password = null;
        CryptoService.ClearKeyCache();


        Database = null!;
    }



    public string? Password => _password;


    public bool NeedsFormatMigration => _needsFormatMigration;



    public bool NeedsKdfMigration
        => _loaded && _fileVersion == FileVersionCurrent && _encryptionFlag == FlagEncrypted;




    public bool MigrateKdf()
    {
        if (_suppressSave) return false;
        if (!NeedsKdfMigration || string.IsNullOrEmpty(_password)) return false;
        var oldVersion = _fileVersion;
        _fileVersion = FileVersionKdfHardened;
        if (!SaveSync())
        {
            _fileVersion = oldVersion;
            return false;
        }
        return true;
    }







    public event Action? Saved;

    public event Action<string>? SaveFailed;

    public NovaraStore(string filePath)
    {
        _filePath = filePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreEnv.DataDirName, "data.novadb");









    public static void DiscardOrphanedPasswordFiles(string dbFilePath)
    {
        try
        {
            if (File.Exists(dbFilePath) || !PasswordService.Exists()) return;
            PasswordService.Delete();
            PasswordService.DeleteLockout();
        }
        catch {  }
    }











    private static void NormalizeTodoStates(TodoCard td)
    {
        td.CheckedStates ??= new();
        td.SubTexts ??= new();


        for (int i = td.SubTexts.Count - 1; i >= 0; i--)
        {
            if (td.SubTexts[i] != null) continue;
            td.SubTexts.RemoveAt(i);
            if (i + 1 < td.CheckedStates.Count) td.CheckedStates.RemoveAt(i + 1);
        }


        int total = 1 + td.SubTexts.Count;
        while (td.CheckedStates.Count < total) td.CheckedStates.Add(false);
        if (td.CheckedStates.Count > total) td.CheckedStates.RemoveRange(total, td.CheckedStates.Count - total);
    }

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
            if (header[4] != FileVersionLegacy && header[4] != FileVersionCurrent && header[4] != FileVersionKdfHardened) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]));
            _fileVersion = header[4];
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
            if (header[4] != FileVersionLegacy) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]));
            _fileVersion = FileVersionLegacy;

            var md5Actual = MD5.HashData(body);
            if (!md5Stored.AsSpan().SequenceEqual(md5Actual))
                return Corrupted(Loc.T("Store_Err_Md5Fail"));

            var db = JsonSerializer.Deserialize<NovaraDatabase>(body, JsonOptions);
            if (db == null) return Corrupted(Loc.T("Store_Err_Deserialize"));







            db.AppSettings ??= new AppSettings();
            db.AppSettings.McpAllowedProcesses ??= new();
            db.AppSettings.McpAllowedProcesses.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions ??= new();
            db.AppSettings.Workspaces ??= new();
            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();
            db.MemoEntries.RemoveAll(x => x == null);
            db.TodoCards.RemoveAll(x => x == null);
            db.NoteCards.RemoveAll(x => x == null);
            db.PathBackupItems.RemoveAll(x => x == null);
            db.DiaryItems.RemoveAll(x => x == null);
            db.MemoGroups.RemoveAll(x => x == null);
            db.AppSettings.Workspaces.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions.RemoveAll(x => x == null);
            foreach (var en in db.MemoEntries)
            {
                if (en.Fields == null) en.Fields = new();
                else en.Fields.RemoveAll(f => f == null);
                foreach (var f in en.Fields) { f.Label ??= ""; f.Value ??= ""; }
            }
            foreach (var td in db.TodoCards) NormalizeTodoStates(td);

            Database = db;
            _encryptionFlag = FlagPlain;
            _loaded = true;
            return new LoadResult(LoadStatus.Ok);
        }
        catch (Exception ex)
        {

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
                CryptoService.ClearKeyCache();
                _loaded = true;
                SaveSync();
                return new LoadResult(LoadStatus.EmptyCreated);
            }
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < HeaderSize) return Corrupted(Loc.T("Store_Err_HeaderIncomplete"));
            var header = new byte[HeaderSize];
            fs.ReadExactly(header);
            if (BitConverter.ToUInt32(header, 0) != Magic) return Corrupted(Loc.T("Store_Err_BadMagic"));
            if (header[4] != FileVersionLegacy && header[4] != FileVersionCurrent && header[4] != FileVersionKdfHardened) return Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]));
            _fileVersion = header[4];
            if (header[5] == FlagPlain)
                return header[4] != FileVersionLegacy
                    ? Corrupted(string.Format(Loc.T("Store_Err_Version"), header[4]))
                    : new LoadResult(LoadStatus.WrongPassword, Loc.T("Store_Err_NotEncrypted"));
            if (header[5] != FlagEncrypted) return Corrupted(string.Format(Loc.T("Store_Err_UnknownEnc"), header[5]));
            var md5Stored = header.AsSpan(6, 16).ToArray();
            var body = new byte[fs.Length - HeaderSize];
            fs.ReadExactly(body);










            var verified = PasswordService.Verify(password);
            var stagedAccepted = false;
            if (!verified && PasswordService.HasStagedChange() && PasswordService.VerifyStaged(password))
            {
                stagedAccepted = true;
            }
            if (!verified && !stagedAccepted)
            {


                return PasswordService.CheckSecurityHealth() switch
                {
                    PasswordService.SecurityHealth.Ok => new LoadResult(LoadStatus.WrongPassword, Loc.T("Store_Err_WrongPassword")),
                    PasswordService.SecurityHealth.TransientError => new LoadResult(LoadStatus.IoError, "security.dat read failed"),
                    _ => Corrupted(Loc.T("Store_Err_SecurityMissing")),
                };
            }
            var salt = PasswordService.GetDeriveSalt();
            if (salt == null) return Corrupted(Loc.T("Store_Err_SecurityMissing"));

            byte[] plain;
            if (_fileVersion == FileVersionCurrent || _fileVersion == FileVersionKdfHardened)
            {





                try
                {
                    plain = _fileVersion == FileVersionKdfHardened
                        ? CryptoService.DecryptGcm(body, password, salt, CryptoService.CurrentIterations)
                        : CryptoService.DecryptGcm(body, password, salt);
                }






                catch { return DecryptFailedResult(); }
                _needsFormatMigration = false;
            }
            else
            {
                try { plain = CryptoService.Decrypt(body, password, salt); }
                catch { return DecryptFailedResult(); }
                var md5Actual = MD5.HashData(plain);
                if (!md5Stored.AsSpan().SequenceEqual(md5Actual)) return Corrupted(Loc.T("Store_Err_Md5Fail"));
                _needsFormatMigration = true;
            }






            if (!verified)
                PasswordService.PromoteStaged();
            else
                PasswordService.DiscardStaged();

            var db = JsonSerializer.Deserialize<NovaraDatabase>(plain, JsonOptions);
            if (db == null) return Corrupted(Loc.T("Store_Err_Deserialize"));


            db.AppSettings ??= new AppSettings();
            db.AppSettings.McpAllowedProcesses ??= new();
            db.AppSettings.McpAllowedProcesses.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions ??= new();
            db.AppSettings.Workspaces ??= new();
            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();
            db.MemoEntries.RemoveAll(x => x == null);
            db.TodoCards.RemoveAll(x => x == null);
            db.NoteCards.RemoveAll(x => x == null);
            db.PathBackupItems.RemoveAll(x => x == null);
            db.DiaryItems.RemoveAll(x => x == null);
            db.MemoGroups.RemoveAll(x => x == null);
            db.AppSettings.Workspaces.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions.RemoveAll(x => x == null);
            foreach (var en in db.MemoEntries)
            {
                if (en.Fields == null) en.Fields = new();
                else en.Fields.RemoveAll(f => f == null);
                foreach (var f in en.Fields) { f.Label ??= ""; f.Value ??= ""; }
            }
            foreach (var td in db.TodoCards) NormalizeTodoStates(td);

            Database = db;
            _encryptionFlag = FlagEncrypted;
            _password = password;
            _loaded = true;
            return new LoadResult(LoadStatus.Ok);
        }
        catch (Exception ex)
        {

            return ex is System.Text.Json.JsonException
                ? Corrupted(Loc.T("Store_Err_Deserialize"))
                : new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    public bool EnableEncryption(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        if (_suppressSave) return false;
        if (!_loaded) return false;

        _saveGate.Wait();
        try
        {
            _password = password;
            _encryptionFlag = FlagEncrypted;
            _fileVersion = FileVersionKdfHardened;
            if (!SaveSyncCore())
            {

                _encryptionFlag = FlagPlain;
                _fileVersion = FileVersionLegacy;
                _password = null;
                CryptoService.ClearKeyCache();
                return false;
            }
            return true;
        }
        finally { _saveGate.Release(); }
    }






    public bool MigrateFormat()
    {
        if (_suppressSave) return false;
        if (!_needsFormatMigration || _fileVersion != FileVersionLegacy || string.IsNullOrEmpty(_password))
            return false;

        _saveGate.Wait();
        try
        {
            var oldVersion = _fileVersion;
            _fileVersion = FileVersionCurrent;
            if (!SaveSyncCore())
            {
                _fileVersion = oldVersion;
                return false;
            }
            _needsFormatMigration = false;
            return true;
        }
        finally { _saveGate.Release(); }
    }

    public bool DisableEncryption()
    {
        if (_suppressSave) return false;

        _saveGate.Wait();
        try
        {
            var oldVersion = _fileVersion;
            _encryptionFlag = FlagPlain;
            if (!SaveSyncCore())
            {

                _encryptionFlag = FlagEncrypted;
                _fileVersion = oldVersion;
                return false;
            }
            _password = null;
            CryptoService.ClearKeyCache();
            _needsFormatMigration = false;
            return true;
        }
        finally { _saveGate.Release(); }
    }

    public bool Reencrypt(string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword)) return false;
        if (_suppressSave) return false;

        _saveGate.Wait();
        try
        {
            var oldPassword = _password;
            _password = newPassword;
            _encryptionFlag = FlagEncrypted;
            if (!SaveSyncCore()) { _password = oldPassword; return false; }
            return true;
        }
        finally { _saveGate.Release(); }
    }

    public bool ExportBackup(string backupPath, bool includeFilePathEntries)
    {
        var tmpPath = backupPath + ".tmp";
        try
        {
            if (_suppressSave) return false;
            if (!_loaded) return false;
            var export = CloneForExport(includeFilePathEntries);
            if (export == null) return false;
            var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);



            var sha = SHA256.HashData(json);
            var header = new byte[HeaderSizeSha256];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
            header[4] = FileVersionSha256Backup;
            header[5] = FlagPlain;
            sha.CopyTo(header, 6);
            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(header);
                fs.Write(json);
                fs.Flush(true);
            }
            File.Move(tmpPath, backupPath, true);
            return true;
        }
        catch (Exception ex)
        {

            try { if (File.Exists(tmpPath)) { File.SetAttributes(tmpPath, FileAttributes.Normal); File.Delete(tmpPath); } } catch { }
            System.Diagnostics.Debug.WriteLine($"NovaraStore 导出备份失败: {ex}");
            return false;
        }
    }







    private NovaraDatabase? CloneForExport(bool includeFilePathEntries)
    {
        try
        {



            var copy = JsonSerializer.Deserialize<NovaraDatabase>(JsonSerializer.SerializeToUtf8Bytes(Database, JsonOptions), JsonOptions);



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


            return null;
        }
    }








    public bool ExportBackupEncrypted(string backupPath, string password, bool includeFilePathEntries)
    {
        var tmpPath = backupPath + ".tmp";
        try
        {
            if (_suppressSave) return false;
            if (!_loaded) return false;
            if (string.IsNullOrEmpty(password)) return false;
            var export = CloneForExport(includeFilePathEntries);
            if (export == null) return false;
            var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);



            var salt = RandomNumberGenerator.GetBytes(32);
            var header = new byte[HeaderSizeEncryptedBackup];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
            header[4] = FileVersionEncryptedBackup;
            header[5] = FlagEncrypted;
            header[6] = AlgoIdAes256Gcm;
            header[7] = KdfIdPbkdf2Sha256;
            BitConverter.TryWriteBytes(header.AsSpan(8, 4), BackupKdfIterations);
            salt.CopyTo(header, 12);


            var cipher = CryptoService.EncryptGcm(json, password, salt, BackupKdfIterations, header);

            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(header);
                fs.Write(cipher);
                fs.Flush(true);
            }
            File.Move(tmpPath, backupPath, true);
            return true;
        }
        catch (Exception ex)
        {

            try { if (File.Exists(tmpPath)) { File.SetAttributes(tmpPath, FileAttributes.Normal); File.Delete(tmpPath); } } catch { }
            System.Diagnostics.Debug.WriteLine($"NovaraStore 加密导出失败: {ex}");
            return false;
        }
    }







    public string? ExportSnapshotCipher(string password, bool includeMemo, bool includePaths, bool includePlan, bool includeDiary)
    {
        try
        {
            if (_suppressSave) return null;
            if (!_loaded) return null;
            if (string.IsNullOrEmpty(password)) return null;
            var export = CloneForSnapshot(includeMemo, includePaths, includePlan, includeDiary);
            if (export == null) return null;
            var json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);


            var salt = RandomNumberGenerator.GetBytes(32);
            var header = new byte[HeaderSizeEncryptedBackup];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), Magic);
            header[4] = FileVersionEncryptedBackup;
            header[5] = FlagEncrypted;
            header[6] = AlgoIdAes256Gcm;
            header[7] = KdfIdPbkdf2Sha256;
            BitConverter.TryWriteBytes(header.AsSpan(8, 4), BackupKdfIterations);
            salt.CopyTo(header, 12);
            var cipher = CryptoService.EncryptGcm(json, password, salt, BackupKdfIterations, header);

            var result = new byte[HeaderSizeEncryptedBackup + cipher.Length];
            header.CopyTo(result, 0);
            cipher.CopyTo(result, HeaderSizeEncryptedBackup);
            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NovaraStore 生成快照密文失败: {ex}");
            return null;
        }
    }


    private NovaraDatabase? CloneForSnapshot(bool includeMemo, bool includePaths, bool includePlan, bool includeDiary)
    {
        try
        {
            var copy = JsonSerializer.Deserialize<NovaraDatabase>(JsonSerializer.SerializeToUtf8Bytes(Database, JsonOptions), JsonOptions);
            if (copy == null) return null;
            copy.MemoEntries?.RemoveAll(x => x.IsDeleted);
            copy.PathBackupItems?.RemoveAll(x => x.IsDeleted);
            copy.TodoCards?.RemoveAll(x => x.IsDeleted);
            copy.NoteCards?.RemoveAll(x => x.IsDeleted);
            copy.DiaryItems?.RemoveAll(x => x.IsDeleted);
            if (!includeMemo) { copy.MemoGroups?.Clear(); copy.MemoEntries?.Clear(); }
            if (!includePaths) copy.PathBackupItems?.Clear();
            if (!includePlan) { copy.TodoCards?.Clear(); copy.NoteCards?.Clear(); }
            if (!includeDiary) copy.DiaryItems?.Clear();





            if (copy.AppSettings != null)
            {
                copy.AppSettings.McpToken = "";
                copy.AppSettings.McpTokenGeneratedAt = null;
                copy.AppSettings.McpAllowedProcesses?.Clear();
                copy.AppSettings.McpClientPermissions?.Clear();
                copy.AppSettings.McpEnabled = false;
                copy.AppSettings.McpDeleteEnabled = false;
                copy.AppSettings.McpPermMigrated = false;
                copy.AppSettings.McpDetailExpanded = true;
            }
            return copy;
        }
        catch { return null; }
    }






    public LoadResult ImportBackup(string backupPath, string? backupPassword = null)
    {



        if (_suppressSave) return new LoadResult(LoadStatus.IoError, RestorePendingMessage);
        var oldDb = Database;
        try
        {
            if (!File.Exists(backupPath)) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupMissing"));
            byte[] body;
            using (var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {





                if (fs.Length < HeaderSize) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupHeader"));
                var prefix = new byte[6];
                fs.ReadExactly(prefix);
                if (BitConverter.ToUInt32(prefix, 0) != Magic) return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupMagic"));
                var ver = prefix[4];
                if (ver != FileVersionLegacy && ver != FileVersionCurrent && ver != FileVersionSha256Backup && ver != FileVersionEncryptedBackup)
                    return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));

                if (ver == FileVersionEncryptedBackup)
                {




                    if (prefix[5] != FlagEncrypted) return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));
                    if (fs.Length < HeaderSizeEncryptedBackup + GcmBlockOverhead + 1)
                        return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupHeader"));
                    if (string.IsNullOrEmpty(backupPassword))
                        return new LoadResult(LoadStatus.NeedPassword);
                    var header44 = new byte[HeaderSizeEncryptedBackup];
                    fs.Position = 0;
                    fs.ReadExactly(header44);
                    var algoId = header44[6];
                    var kdfId = header44[7];
                    var iter32 = BitConverter.ToUInt32(header44, 8);
                    if (algoId != AlgoIdAes256Gcm || kdfId != KdfIdPbkdf2Sha256 || iter32 < BackupKdfIterationsMin || iter32 > BackupKdfIterationsMax)
                        return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));
                    var salt = header44[12..44];
                    var encBody = new byte[fs.Length - HeaderSizeEncryptedBackup];
                    fs.ReadExactly(encBody);
                    try
                    {


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


                    if (ver == FileVersionCurrent) return new LoadResult(LoadStatus.Corrupted, string.Format(Loc.T("Store_Err_BackupVersion"), ver));

                    var headerSize = ver == FileVersionSha256Backup ? HeaderSizeSha256 : HeaderSize;
                    var digestLen = headerSize - 6;



                    if (fs.Length < headerSize + 1)
                        return new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupHeader"));
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
            db.AppSettings.McpAllowedProcesses.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions ??= new();
            db.AppSettings.Workspaces ??= new();
            db.AppSettings.PrivacyLockEnabled = _encryptionFlag == FlagEncrypted;


            db.MemoGroups ??= new();
            db.MemoEntries ??= new();
            db.TodoCards ??= new();
            db.NoteCards ??= new();
            db.PathBackupItems ??= new();
            db.DiaryItems ??= new();


            db.MemoGroups.RemoveAll(x => x == null);
            db.MemoEntries.RemoveAll(x => x == null);
            db.TodoCards.RemoveAll(x => x == null);
            db.NoteCards.RemoveAll(x => x == null);
            db.PathBackupItems.RemoveAll(x => x == null);
            db.DiaryItems.RemoveAll(x => x == null);
            db.AppSettings.Workspaces.RemoveAll(x => x == null);
            db.AppSettings.McpClientPermissions.RemoveAll(x => x == null);
            foreach (var en in db.MemoEntries)
            {
                if (en.Fields == null) en.Fields = new();
                else en.Fields.RemoveAll(f => f == null);
                foreach (var f in en.Fields) { f.Label ??= ""; f.Value ??= ""; }
            }
            foreach (var td in db.TodoCards) NormalizeTodoStates(td);
            var gseen = new HashSet<Guid>();
            foreach (var g in db.MemoGroups)
                if (!gseen.Add(g.Id)) { g.Id = Guid.NewGuid(); gseen.Add(g.Id); }
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


            var wseen = new HashSet<string>();
            foreach (var w in db.AppSettings.Workspaces ?? new())
                if (!wseen.Add(w.Id)) { w.Id = Guid.NewGuid().ToString(); wseen.Add(w.Id); }
            var permSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


            db.AppSettings.McpClientPermissions?.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Path) || !permSeen.Add(r.Path));



            foreach (var e in db.MemoEntries) if (!string.IsNullOrEmpty(e.WorkspaceId) && !wseen.Contains(e.WorkspaceId)) e.WorkspaceId = "";
            foreach (var t in db.TodoCards) if (!string.IsNullOrEmpty(t.WorkspaceId) && !wseen.Contains(t.WorkspaceId)) t.WorkspaceId = "";
            foreach (var n in db.NoteCards) if (!string.IsNullOrEmpty(n.WorkspaceId) && !wseen.Contains(n.WorkspaceId)) n.WorkspaceId = "";
            foreach (var d in db.DiaryItems) if (!string.IsNullOrEmpty(d.WorkspaceId) && !wseen.Contains(d.WorkspaceId)) d.WorkspaceId = "";
            foreach (var p in db.PathBackupItems) if (!string.IsNullOrEmpty(p.WorkspaceId) && !wseen.Contains(p.WorkspaceId)) p.WorkspaceId = "";



            Database = db;


            var oldLoaded = _loaded;
            var oldSuppress = _suppressSave;
            _loaded = true;






            if (!oldSuppress) _suppressSave = false;












            if (_suppressSave)
            {
                Database = oldDb;
                _loaded = oldLoaded;
                _suppressSave = oldSuppress;
                return new LoadResult(LoadStatus.IoError, RestorePendingMessage);
            }
            if (_encryptionFlag == FlagEncrypted)
            {

                if (string.IsNullOrEmpty(_password) || PasswordService.GetDeriveSalt() == null)
                {
                    Database = oldDb;
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

            return ex is System.Text.Json.JsonException
                ? new LoadResult(LoadStatus.Corrupted, Loc.T("Store_Err_BackupDeserialize"))
                : new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }

    private static LoadResult Corrupted(string detail) => new(LoadStatus.Corrupted, detail);










    private static LoadResult DecryptFailedResult()



        => PasswordService.HasStagedChange()
            ? new(LoadStatus.WrongPassword, Loc.T("Store_Err_DecryptFail"))
            : Corrupted(Loc.T("Store_Err_DecryptFail"));

    public Task SaveAsync()
    {
        if (!_loaded || _suppressSave) return Task.CompletedTask;

        lock (_saveLock)
        {
            _savePending = true;
            if (_saveRunning) return Task.CompletedTask;
            _saveRunning = true;
        }
        return Task.Run(async () =>
        {


            var landed = false;
            try
            {


                await Task.Delay(SaveDebounceMs);
                await _saveGate.WaitAsync();
                try
                {
                    while (_savePending)
                    {
                        _savePending = false;




                        if (_suppressSave) break;
                        if (!WriteSnapshot())
                        {

                            Thread.Sleep(60);
                            if (!WriteSnapshot())
                            {






                                _savePending = false;
                                break;
                            }
                        }
                        landed = true;
                    }
                }
                finally { _saveGate.Release(); }
            }
            finally
            {
                _saveRunning = false;

                if (_savePending) _ = SaveAsync();



                if (landed)
                {
                    try { Saved?.Invoke(); } catch { }
                }
            }
        });
    }

    public bool SaveSync()
    {
        if (!_loaded) return false;
        if (_suppressSave) return true;
        _saveGate.Wait();
        try { return SaveSyncCore(); }
        finally { _saveGate.Release(); }
    }





    private bool SaveSyncCore()
    {
        if (!_loaded) return false;
        if (_suppressSave) return true;



        var ok = WriteSnapshot();
        if (!ok)
        {
            Thread.Sleep(60);
            ok = WriteSnapshot();
        }
        return ok;
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
            _needsFormatMigration = false;
            _password = null;
            CryptoService.ClearKeyCache();
            _fileVersion = FileVersionLegacy;
            _loaded = true;
            _suppressSave = false;
            if (!SaveSync()) return new LoadResult(LoadStatus.IoError, Loc.T("Store_Err_WriteFail"));
            return new LoadResult(LoadStatus.EmptyCreated);
        }
        catch (Exception ex)
        {
            return new LoadResult(LoadStatus.IoError, ex.Message);
        }
    }






private bool WriteSnapshot()
    {






        var json = StableJson.SerializeToUtf8BytesStable(Database, JsonOptions);
        if (json == null)
        {
            System.Diagnostics.Debug.WriteLine("NovaraStore 序列化失败（快照不稳定/并发修改，下次保存自愈）");
            return false;
        }





        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);


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


                    header[4] = _fileVersion == FileVersionKdfHardened ? FileVersionKdfHardened
                              : _fileVersion == FileVersionCurrent ? FileVersionCurrent
                              : FileVersionLegacy;
                    if (header[4] == FileVersionLegacy) MD5.HashData(json).CopyTo(header, 6);
                }
                else
                {
                    header[4] = FileVersionLegacy;
                    _fileVersion = FileVersionLegacy;
                    MD5.HashData(json).CopyTo(header, 6);
                }
                fs.Write(header);
                if (_encryptionFlag == FlagEncrypted)
                {

                    var salt = PasswordService.GetDeriveSalt();
                    if (salt == null || string.IsNullOrEmpty(_password)) throw new InvalidOperationException(Loc.T("Store_Err_EncNoKey"));

                    var cipher = _fileVersion == FileVersionLegacy
                        ? CryptoService.Encrypt(json, _password, salt)
                        : CryptoService.EncryptGcm(json, _password, salt, _fileVersion == FileVersionKdfHardened ? CryptoService.CurrentIterations : CryptoService.LegacyIterations);
                    fs.Write(cipher);
                }
                else
                {
                    fs.Write(json);
                }
                fs.Flush(true);
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

            try { if (File.Exists(_filePath)) File.SetAttributes(_filePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
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
