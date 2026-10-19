using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Novara.Services;






public static class AutoBackupService
{
    public const int MaxSnapshots = 10;
    private const string AutoPrefix = "data-auto-";
    private const string ManualPrefix = "data-manual-";
    private const byte FlagEncrypted = 0x01;
    private const string SecuritySuffix = ".security";
    private const string RestoreJournalSuffix = ".restore-journal";

    private static DispatcherTimer? _timer;


    private static string? _lastPreRestoreSnapshot;



    private static string? _activeRestoreTarget;

    public static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.DataDirName, "backups");

    public static string DataFilePath => NovaraStore.DefaultFilePath;







    private static string ReminderResyncMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.DataDirName, "reminder-resync.pending");

    public static void MarkReminderResyncPending()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReminderResyncMarkerPath)!);
            File.WriteAllText(ReminderResyncMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch { }
    }




    public static bool ConsumeReminderResync()
    {
        try
        {
            if (!File.Exists(ReminderResyncMarkerPath)) return false;
            File.Delete(ReminderResyncMarkerPath);
            return true;
        }
        catch { return false; }
    }


    public sealed record SnapshotInfo(string FileName, DateTime Timestamp, long SizeBytes, bool IsManual);




    private const string ManifestFileName = "manifest.jsonl";
    private static string ManifestPath => Path.Combine(BackupDir, ManifestFileName);

    private sealed record SnapshotManifestEntry(
        string File, string Sha256, long SizeBytes, string? SecuritySha256, long? SecuritySizeBytes, bool IsManual, string CreatedUtc);


    private static string? Sha256OfFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { return null; }
    }


    private static void AppendManifestEntry(string snapshotPath)
    {
        try
        {
            var sha = Sha256OfFile(snapshotPath);
            if (sha == null) return;
            string? secSha = null;
            long? secSize = null;
            var sec = SecurityPairPath(snapshotPath);
            if (File.Exists(sec))
            {
                secSha = Sha256OfFile(sec);
                secSize = new FileInfo(sec).Length;
            }
            var name = Path.GetFileName(snapshotPath);
            var entry = new SnapshotManifestEntry(name, sha, new FileInfo(snapshotPath).Length, secSha, secSize,
                name.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase), DateTime.UtcNow.ToString("O"));
            Directory.CreateDirectory(BackupDir);
            File.AppendAllText(ManifestPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { }
    }


    private static (string? Sha256, string? SecuritySha256) ReadManifestFor(string fileName)
    {
        try
        {
            if (!File.Exists(ManifestPath)) return (null, null);
            (string?, string?) found = (null, null);
            foreach (var line in File.ReadLines(ManifestPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<SnapshotManifestEntry>(line);
                    if (e != null && string.Equals(e.File, fileName, StringComparison.OrdinalIgnoreCase))
                        found = (e.Sha256, e.SecuritySha256);
                }
                catch { }
            }
            return found;
        }
        catch { return (null, null); }
    }


    private static void RemoveManifestEntries(IEnumerable<string> fileNames)
    {
        try
        {
            if (!File.Exists(ManifestPath)) return;
            var remove = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
            var kept = new List<string>();
            foreach (var line in File.ReadLines(ManifestPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? file = null;
                try { file = JsonSerializer.Deserialize<SnapshotManifestEntry>(line)?.File; }
                catch { }
                if (file == null || !remove.Contains(file)) kept.Add(line);
            }
            File.WriteAllLines(ManifestPath, kept);
        }
        catch { }
    }


    public static void SyncAutoBackupTimer(DispatcherQueue queue)
    {






        RecoverInterruptedRestore();
        var settings = App.Store?.Database?.AppSettings;
        bool shouldRun = settings is { BackupEnabled: true, AutoBackupEnabled: true };
        if (!shouldRun)
        {
            _timer?.Stop();
            _timer = null;
            return;
        }

        var interval = settings!.BackupFreq switch
        {
            1 => TimeSpan.FromHours(1),
            2 => TimeSpan.FromHours(6),
            3 => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(30),
        };

        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => System.Threading.Tasks.Task.Run(() =>
            {

                var path = CreateSnapshot(isManual: false);
                if (path == null) System.Diagnostics.Debug.WriteLine("AutoBackup: periodic snapshot failed (disk full / file locked / no data file)");
            });
            _timer.Start();


            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var newestAuto = ListSnapshots().FirstOrDefault(s => !s.IsManual);
                    if (newestAuto == null || DateTime.Now - newestAuto.Timestamp > interval)
                    {
                        if (CreateSnapshot(isManual: false) == null)
                            System.Diagnostics.Debug.WriteLine("AutoBackup: startup compensation snapshot failed");
                    }
                }
                catch { }
            });
        }
        else
        {
            _timer.Interval = interval;
        }
    }




    public static string? CreateSnapshot(bool isManual, string? keepAliveName = null)
    {
        string? path = null;
        try
        {
            if (!File.Exists(DataFilePath)) return null;


            try { App.Store?.SaveSync(); } catch { }
            Directory.CreateDirectory(BackupDir);
            var prefix = isManual ? ManualPrefix : AutoPrefix;
            path = Path.Combine(BackupDir, $"{prefix}{DateTime.Now:yyyyMMddHHmmss}.novadb");

            int n = 1;
            while (File.Exists(path))
                path = Path.Combine(BackupDir, $"{prefix}{DateTime.Now:yyyyMMddHHmmss}-{n++}.novadb");




            var encryptionFlag = ReadEncryptionFlag(DataFilePath);
            if (encryptionFlag < 0)
            {
                CrashLogger.LogNote("AutoBackupService.CreateSnapshot",
                    "snapshot aborted: the encryption flag could not be read (" + Path.GetFileName(DataFilePath) + ")");
                return null;
            }



            if (encryptionFlag == FlagEncrypted && !File.Exists(PasswordService.SecurityFilePath))
            {
                CrashLogger.LogNote("AutoBackupService.CreateSnapshot",
                    "snapshot aborted: the database is encrypted but the paired security.dat is missing");
                return null;
            }



            var dataTmp = path + ".tmp";
            File.Copy(DataFilePath, dataTmp, overwrite: false);





            if (encryptionFlag == FlagEncrypted && File.Exists(PasswordService.SecurityFilePath))
            {
                var secPath = SecurityPairPath(path);
                var secTmp = secPath + ".tmp";
                File.Copy(PasswordService.SecurityFilePath, secTmp, overwrite: false);
                File.Move(secTmp, secPath);
                try { File.SetAttributes(secPath, FileAttributes.Hidden); } catch { }
            }
            File.Move(dataTmp, path);
            try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }

            AppendManifestEntry(path);
            PruneSnapshots(keepAliveName);
            return path;
        }
        catch (Exception ex)
        {



            CrashLogger.LogNote("AutoBackupService.CreateSnapshot", ex.Message);
            DeleteQuietly(path);
            DeleteQuietly(path + ".tmp");


            if (path != null)
            {
                DeleteQuietly(SecurityPairPath(path));
                DeleteQuietly(SecurityPairPath(path) + ".tmp");
            }
            return null;
        }
    }


    private static void DeleteQuietly(string? p)
    {
        if (string.IsNullOrEmpty(p)) return;
        try
        {
            if (!File.Exists(p)) return;




            File.SetAttributes(p, FileAttributes.Normal);
            File.Delete(p);
        }
        catch { }
    }


    public static List<SnapshotInfo> ListSnapshots()
    {
        var result = new List<SnapshotInfo>();
        try
        {
            if (!Directory.Exists(BackupDir)) return result;
            foreach (var f in Directory.GetFiles(BackupDir, "*.novadb"))
            {
                var name = Path.GetFileName(f);
                bool isManual = name.StartsWith(ManualPrefix, StringComparison.OrdinalIgnoreCase);
                var fi = new FileInfo(f);
                result.Add(new SnapshotInfo(name, fi.LastWriteTime, fi.Length, isManual));
            }
        }
        catch { }
        return result.OrderByDescending(s => s.Timestamp).ToList();
    }


    public static bool HasSnapshots() => ListSnapshots().Count > 0;




    public static (int Deleted, int Failed) DeleteSnapshots(IEnumerable<string> fileNames)
    {
        int deleted = 0, failed = 0;
        try
        {
            if (!Directory.Exists(BackupDir)) return (0, 0);
            var names = fileNames.ToList();
            var gone = new List<string>();
            foreach (var name in names)
            {


                if (!IsSafeSnapshotName(name)) { failed++; continue; }
                try
                {
                    var p = Path.Combine(BackupDir, name);
                    if (File.Exists(p))
                    {
                        File.SetAttributes(p, FileAttributes.Normal);
                        File.Delete(p);
                    }
                    var sec = SecurityPairPath(p);
                    if (File.Exists(sec)) { File.SetAttributes(sec, FileAttributes.Normal); File.Delete(sec); }


                    if (File.Exists(p)) { failed++; continue; }
                    gone.Add(name);
                    deleted++;
                }
                catch { failed++; }
            }
            RemoveManifestEntries(gone);
        }
        catch { }
        return (deleted, failed);
    }



    public static (int Deleted, int Failed) ClearAll()
    {
        int deleted = 0, failed = 0;
        try
        {
            if (!Directory.Exists(BackupDir)) return (0, 0);
            foreach (var f in Directory.GetFiles(BackupDir, "*.novadb"))
            {
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    if (File.Exists(f)) failed++; else deleted++;
                }
                catch { failed++; }
            }

            foreach (var f in Directory.GetFiles(BackupDir, "*.novadb" + SecuritySuffix))
            {
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    if (File.Exists(f)) failed++;
                }
                catch { failed++; }
            }
            try { File.Delete(ManifestPath); } catch { }
        }
        catch { }
        return (deleted, failed);
    }







    public static (bool Ok, string? ErrorKey) Restore(string snapshotFileName)
    {
        try
        {


            if (!IsSafeSnapshotName(snapshotFileName)) return (false, "Setting_Backup_Restore_Missing");
            var snapshotPath = Path.Combine(BackupDir, snapshotFileName);
            if (!File.Exists(snapshotPath)) return (false, "Setting_Backup_Restore_Missing");


            _activeRestoreTarget = snapshotFileName;



            var (expectSha, expectSecSha) = ReadManifestFor(snapshotFileName);
            if (expectSha != null)
            {
                var actual = Sha256OfFile(snapshotPath);
                if (actual is null || !string.Equals(actual, expectSha, StringComparison.OrdinalIgnoreCase))
                {
                    CrashLogger.LogNote("AutoBackupService.Restore", "snapshot hash mismatch vs manifest - restore aborted: " + snapshotFileName);
                    return (false, "Setting_Backup_Restore_BadSnapshot");
                }
                if (expectSecSha != null && File.Exists(SecurityPairPath(snapshotPath)))
                {
                    var actualSec = Sha256OfFile(SecurityPairPath(snapshotPath));
                    if (actualSec is null || !string.Equals(actualSec, expectSecSha, StringComparison.OrdinalIgnoreCase))
                    {
                        CrashLogger.LogNote("AutoBackupService.Restore", "security pair hash mismatch vs manifest - restore aborted: " + snapshotFileName);
                        return (false, "Setting_Backup_Restore_BadSnapshot");
                    }
                }
            }


            _lastPreRestoreSnapshot = null;
            if (File.Exists(DataFilePath))
            {




                var backup = CreateSnapshot(isManual: true, keepAliveName: snapshotFileName);
                if (backup == null) return (false, "Store_Err_IoFail");
                _lastPreRestoreSnapshot = Path.GetFileName(backup);
            }







            if (App.Store is { } restoreStore)
            {
                if (!restoreStore.SaveSync())
                {
                    CrashLogger.LogNote("AutoBackupService.Restore", "pre-restore flush failed - restore aborted before touching the disk");
                    return (false, "Store_Err_IoFail");
                }
                restoreStore.SetSuppressSave(true);
            }
            var ok = OverwriteWithSnapshot(snapshotPath);
            if (ok)
            {





                if (!SyncService.MarkRestorePendingConfirm())
                    CrashLogger.LogNote("AutoBackupService.Restore",
                        "sync-state.json write failed - the restore-pending gate will NOT survive the restart; do not let sync run until the direction is confirmed");



                MarkReminderResyncPending();
            }
            else
            {


                App.Store?.SetSuppressSave(false);
            }
            return ok ? (true, null) : (false, "Store_Err_IoFail");
        }
        catch { return (false, "Store_Err_IoFail"); }
        finally { _activeRestoreTarget = null; }
    }





    public static bool RollbackLastRestore()
    {
        try
        {
            if (string.IsNullOrEmpty(_lastPreRestoreSnapshot)) return false;
            var backupPath = Path.Combine(BackupDir, _lastPreRestoreSnapshot);
            _lastPreRestoreSnapshot = null;
            if (!File.Exists(backupPath)) return false;
            var ok = OverwriteWithSnapshot(backupPath);
            if (ok)
            {
                App.Store?.SetSuppressSave(false);
                try { File.Delete(ReminderResyncMarkerPath); } catch { }
            }
            return ok;
        }
        catch { return false; }
    }





    public static void RecoverInterruptedRestore()
    {
        try
        {
            var journal = DataFilePath + RestoreJournalSuffix;
            if (!File.Exists(journal)) return;
            var parts = File.ReadAllText(journal).Trim().Split('|');
            var stage = parts.Length > 0 ? parts[0] : "";
            bool hadData = parts.Length > 1 && parts[1] == "1";
            bool hadSec = parts.Length > 2 && parts[2] == "1";
            if (stage != "committed")
            {
                var rollbackData = DataFilePath + ".restore-rollback";
                var rollbackSec = PasswordService.SecurityFilePath + ".restore-rollback";                if (hadData && File.Exists(rollbackData))
                {
                    try { File.SetAttributes(DataFilePath, FileAttributes.Normal); } catch { }
                    File.Copy(rollbackData, DataFilePath, overwrite: true);
                    try { File.SetAttributes(DataFilePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
                }
                else if (!hadData) DeleteQuietly(DataFilePath);
                if (hadSec && File.Exists(rollbackSec))
                {
                    File.Copy(rollbackSec, PasswordService.SecurityFilePath, overwrite: true);
                    try { File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Hidden); } catch { }
                }
                else if (!hadSec) DeleteQuietly(PasswordService.SecurityFilePath);
                CrashLogger.LogNote("AutoBackupService.RecoverInterruptedRestore",
                    "rolled back an interrupted restore at stage '" + stage + "' (data/security pair was mismatched)");
            }
            else
            {

                DeleteQuietly(DataFilePath + ".restore-rollback");
                DeleteQuietly(PasswordService.SecurityFilePath + ".restore-rollback");
                DeleteQuietly(DataFilePath + ".restoretmp");
                DeleteQuietly(PasswordService.SecurityFilePath + ".restoretmp");
            }
            DeleteQuietly(journal);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("AutoBackupService.RecoverInterruptedRestore", ex.Message);
        }
    }

    private static void WriteRestoreJournal(string stage, bool hadData, bool hadSec)
    {
        try
        {
            File.WriteAllText(DataFilePath + RestoreJournalSuffix,
                stage + "|" + (hadData ? "1" : "0") + "|" + (hadSec ? "1" : "0"));
        }
        catch (Exception ex)
        {


            CrashLogger.LogNote("AutoBackupService.WriteRestoreJournal", ex.Message);
        }
    }





    private static bool OverwriteWithSnapshot(string snapshotPath)
    {



        int snapshotFlag = ReadEncryptionFlag(snapshotPath);
        if (snapshotFlag < 0) return false;
        bool snapshotEncrypted = snapshotFlag == FlagEncrypted;
        var secPair = SecurityPairPath(snapshotPath);
        if (snapshotEncrypted && !File.Exists(secPair)) return false;



        var tmpPath = DataFilePath + ".restoretmp";
        var tmpSec = PasswordService.SecurityFilePath + ".restoretmp";
        var rollbackData = DataFilePath + ".restore-rollback";
        var rollbackSec = PasswordService.SecurityFilePath + ".restore-rollback";



        foreach (var stale in new[] { tmpPath, tmpSec, rollbackData, rollbackSec })
        {
            if (File.Exists(stale))
            {
                try { File.SetAttributes(stale, FileAttributes.Normal); File.Delete(stale); } catch { }
            }
        }

        var hadData = File.Exists(DataFilePath);
        var hadSec = File.Exists(PasswordService.SecurityFilePath);
        try
        {

            if (hadData) File.Copy(DataFilePath, rollbackData, overwrite: true);
            if (hadSec) File.Copy(PasswordService.SecurityFilePath, rollbackSec, overwrite: true);
            WriteRestoreJournal("prepared", hadData, hadSec);


            File.Copy(snapshotPath, tmpPath, overwrite: true);
            try { File.SetAttributes(tmpPath, FileAttributes.Normal); } catch { }
            if (snapshotEncrypted)
            {
                File.Copy(secPair, tmpSec, overwrite: true);
                try { File.SetAttributes(tmpSec, FileAttributes.Normal); } catch { }
            }






            if (File.Exists(DataFilePath))
            {
                try { File.SetAttributes(DataFilePath, FileAttributes.Normal); } catch { }
            }
            File.Move(tmpPath, DataFilePath, true);
            try { File.SetAttributes(DataFilePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
            WriteRestoreJournal("data-moved", hadData, hadSec);
            if (snapshotEncrypted)
            {
                if (hadSec) File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Normal);
                File.Move(tmpSec, PasswordService.SecurityFilePath, true);
                try { File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Hidden); } catch { }
            }
            else if (hadSec)
            {
                File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Normal);
                File.Delete(PasswordService.SecurityFilePath);
            }
            WriteRestoreJournal("committed", hadData, hadSec);
            return true;
        }
        catch
        {

            try
            {
                if (hadData && File.Exists(rollbackData))
                {


                    try { File.SetAttributes(DataFilePath, FileAttributes.Normal); } catch { }
                    File.Copy(rollbackData, DataFilePath, overwrite: true);
                    try { File.SetAttributes(DataFilePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
                }
                if (hadSec && File.Exists(rollbackSec))
                {
                    File.Copy(rollbackSec, PasswordService.SecurityFilePath, overwrite: true);
                    try { File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Hidden); } catch { }
                }
            }
            catch { }
            return false;
        }
        finally
        {
            foreach (var leftover in new[] { tmpPath, tmpSec, rollbackData, rollbackSec })
            {
                try { if (File.Exists(leftover)) { File.SetAttributes(leftover, FileAttributes.Normal); File.Delete(leftover); } } catch { }
            }


            DeleteQuietly(DataFilePath + RestoreJournalSuffix);
        }
    }







    private static int ReadEncryptionFlag(string path) => NovaraStore.ReadContainerFlag(path);


    private static string SecurityPairPath(string snapshotPath) => snapshotPath + SecuritySuffix;






    private static bool IsSafeSnapshotName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        if (name == "." || name == "..") return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return Path.GetFileName(name) == name;
    }







    private static void PruneSnapshots(string? keepAliveName = null)
    {
        try
        {

            var files = Directory.GetFiles(BackupDir, "*.novadb")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fi in files.Take(MaxSnapshots)) keep.Add(fi.Name);

            void KeepNewestIn(DateTime startInclusive, DateTime endExclusive)
            {
                var hit = files.FirstOrDefault(f => f.LastWriteTime >= startInclusive && f.LastWriteTime < endExclusive);
                if (hit != null) keep.Add(hit.Name);
            }
            var now = DateTime.Now;
            for (var d = 0; d < 7; d++) { var day = now.Date.AddDays(-d); KeepNewestIn(day, day.AddDays(1)); }
            var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
            for (var w = 0; w < 4; w++) { var ws = weekStart.AddDays(-7 * w); KeepNewestIn(ws, ws.AddDays(7)); }
            for (var m = 0; m < 3; m++) { var ms = new DateTime(now.Year, now.Month, 1).AddMonths(-m); KeepNewestIn(ms, ms.AddMonths(1)); }
            if (!string.IsNullOrEmpty(_lastPreRestoreSnapshot)) keep.Add(_lastPreRestoreSnapshot);
            if (!string.IsNullOrEmpty(keepAliveName)) keep.Add(keepAliveName);


            if (!string.IsNullOrEmpty(_activeRestoreTarget)) keep.Add(_activeRestoreTarget);

            var removed = new List<string>();
            foreach (var fi in files)
            {
                if (keep.Contains(fi.Name)) continue;
                try
                {
                    var f = fi.FullName;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    removed.Add(fi.Name);
                    var sec = SecurityPairPath(f);
                    if (File.Exists(sec)) { File.SetAttributes(sec, FileAttributes.Normal); File.Delete(sec); }
                }
                catch { }
            }
            if (removed.Count > 0) RemoveManifestEntries(removed);
        }
        catch { }
    }





    public static void TakeExitSnapshotIfDue()
    {
        try
        {
            var settings = App.Store?.Database?.AppSettings;
            if (settings is not { BackupEnabled: true, AutoBackupEnabled: true }) return;
            var newestAuto = ListSnapshots().FirstOrDefault(s => !s.IsManual);
            if (newestAuto != null && DateTime.Now - newestAuto.Timestamp < TimeSpan.FromSeconds(60)) return;
            CreateSnapshot(isManual: false);
        }
        catch { }
    }
}
