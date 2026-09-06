using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Novara.Services;

/// <summary>



/// </summary>
public static class AutoBackupService
{
    public const int MaxSnapshots = 10;
    private const string AutoPrefix = "data-auto-";
    private const string ManualPrefix = "data-manual-";
    private const byte FlagEncrypted = 0x01; 
    private const string SecuritySuffix = ".security";

    private static DispatcherTimer? _timer;

    
    private static string? _lastPreRestoreSnapshot;

    public static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), App.DataDirName, "backups");

    public static string DataFilePath => NovaraStore.DefaultFilePath;

    
    public sealed record SnapshotInfo(string FileName, DateTime Timestamp, long SizeBytes, bool IsManual);

    
    public static void SyncAutoBackupTimer(DispatcherQueue queue)
    {
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
                if (path == null) System.Diagnostics.Debug.WriteLine("AutoBackup: periodic snapshot failed (disk full / file locked / no data file)"); // N4S-05: at least log it - silent null hid failures completely
            });
            _timer.Start();
        }
        else
        {
            _timer.Interval = interval;
        }
    }

    
    public static string? CreateSnapshot(bool isManual)
    {
        try
        {
            if (!File.Exists(DataFilePath)) return null;
            
            
            try { App.Store?.SaveSync(); } catch { }
            Directory.CreateDirectory(BackupDir);
            var prefix = isManual ? ManualPrefix : AutoPrefix;
            var path = Path.Combine(BackupDir, $"{prefix}{DateTime.Now:yyyyMMddHHmmss}.novadb");
            
            int n = 1;
            while (File.Exists(path))
                path = Path.Combine(BackupDir, $"{prefix}{DateTime.Now:yyyyMMddHHmmss}-{n++}.novadb");

            
            
            var dataTmp = path + ".tmp";
            File.Copy(DataFilePath, dataTmp, overwrite: false);
            File.Move(dataTmp, path);
            try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }

            
            if (ReadEncryptionFlag(DataFilePath) == FlagEncrypted && File.Exists(PasswordService.SecurityFilePath))
            {
                var secPath = SecurityPairPath(path);
                var secTmp = secPath + ".tmp";
                File.Copy(PasswordService.SecurityFilePath, secTmp, overwrite: false);
                File.Move(secTmp, secPath);
                try { File.SetAttributes(secPath, FileAttributes.Hidden); } catch { }
            }

            TrimToMax();
            return path;
        }
        catch { return null; }
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

    
    public static void DeleteSnapshots(IEnumerable<string> fileNames)
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return;
            foreach (var name in fileNames)
            {
                // N5-S2-03: names are composed into delete paths - reject anything that could
                // escape BackupDir (traversal, separators, invalid name chars) before touching disk.
                if (!IsSafeSnapshotName(name)) continue;
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
                }
                catch { }
            }
        }
        catch { }
    }

    
    public static void ClearAll()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return;
            foreach (var f in Directory.GetFiles(BackupDir, "*.novadb"))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
            }
            
            foreach (var f in Directory.GetFiles(BackupDir, "*.novadb" + SecuritySuffix))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    
    
    
    /// </summary>
    public static bool Restore(string snapshotFileName)
    {
        try
        {
            // N5-S2-03: same containment guard as DeleteSnapshots - the name composes into an
            // overwrite path (data.novadb), so traversal must never reach Path.Combine.
            if (!IsSafeSnapshotName(snapshotFileName)) return false;
            var snapshotPath = Path.Combine(BackupDir, snapshotFileName);
            if (!File.Exists(snapshotPath)) return false;

            
            _lastPreRestoreSnapshot = null;
            if (File.Exists(DataFilePath))
            {
                
                
                var backup = CreateSnapshot(isManual: true);
                if (backup == null) return false;
                _lastPreRestoreSnapshot = Path.GetFileName(backup);
            }

            // N4S-01: the disk now holds the restored snapshot while memory still holds the old DB -
            // suppress every save path until restart (or rollback / import / reset re-establishes consistency).
            var ok = OverwriteWithSnapshot(snapshotPath);
            if (ok) App.Store?.SetSuppressSave(true);
            return ok;
        }
        catch { return false; }
    }

    /// <summary>
    
    
    /// </summary>
    public static bool RollbackLastRestore()
    {
        try
        {
            if (string.IsNullOrEmpty(_lastPreRestoreSnapshot)) return false;
            var backupPath = Path.Combine(BackupDir, _lastPreRestoreSnapshot);
            _lastPreRestoreSnapshot = null;
            if (!File.Exists(backupPath)) return false;
            var ok = OverwriteWithSnapshot(backupPath);
            if (ok) App.Store?.SetSuppressSave(false); // N4S-01: disk is back in sync with memory - normal saving resumes
            return ok;
        }
        catch { return false; }
    }

    /// <summary>
    
    
    /// </summary>
    private static bool OverwriteWithSnapshot(string snapshotPath)
    {
        // N2-15: a transient read failure must ABORT the restore, not treat the snapshot as
        // plaintext - falling through would File.Delete security.dat of an encrypted db and
        
        int snapshotFlag = ReadEncryptionFlag(snapshotPath);
        if (snapshotFlag < 0) return false;
        bool snapshotEncrypted = snapshotFlag == FlagEncrypted;
        var secPair = SecurityPairPath(snapshotPath);
        if (snapshotEncrypted && !File.Exists(secPair)) return false; 

        
        
        var tmpPath = DataFilePath + ".restoretmp";
        var tmpSec = PasswordService.SecurityFilePath + ".restoretmp";
        var rollbackData = DataFilePath + ".restore-rollback";
        var rollbackSec = PasswordService.SecurityFilePath + ".restore-rollback";
        // N2-66: a previous interrupted restore leaves tmpPath carrying the snapshot's Hidden
        // attribute - File.Copy(overwrite:true) then fails with ACCESS_DENIED forever (every
        // retry, until the file is deleted by hand). Clear the leftover before copying.
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

            
            File.Copy(snapshotPath, tmpPath, overwrite: true);
            try { File.SetAttributes(tmpPath, FileAttributes.Normal); } catch { } // copy carries the snapshot's Hidden attribute - clear it so Move never trips
            if (snapshotEncrypted)
            {
                File.Copy(secPair, tmpSec, overwrite: true);
                try { File.SetAttributes(tmpSec, FileAttributes.Normal); } catch { }
            }

            
            File.Move(tmpPath, DataFilePath, true);
            try { File.SetAttributes(DataFilePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }
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
            return true;
        }
        catch
        {
            
            try
            {
                if (hadData && File.Exists(rollbackData))
                {
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
        }
    }

    
    
    private static int ReadEncryptionFlag(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 6) return -1; // truncated snapshot - unknown state, callers must not guess
            fs.Seek(5, SeekOrigin.Begin);
            return (byte)fs.ReadByte();
        }
        catch { return -1; }
    }

    
    private static string SecurityPairPath(string snapshotPath) => snapshotPath + SecuritySuffix;

    /// <summary>
    /// N5-S2-03: snapshot file names compose into delete/overwrite paths - accept only bare file
    /// names that cannot escape BackupDir. GetInvalidFileNameChars already rejects separators,
    /// the GetFileName round-trip additionally catches ".." segments and rooted paths.
    /// </summary>
    private static bool IsSafeSnapshotName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        // "." and ".." carry no separator so the round-trip below cannot catch them - reject directly.
        if (name == "." || name == "..") return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return Path.GetFileName(name) == name;
    }

    private static void TrimToMax()
    {
        try
        {
            
            
            var files = Directory.GetFiles(BackupDir, "*.novadb")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(MaxSnapshots);
            foreach (var fi in files)
            {
                try
                {
                    var f = fi.FullName;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    var sec = SecurityPairPath(f);
                    if (File.Exists(sec)) { File.SetAttributes(sec, FileAttributes.Normal); File.Delete(sec); }
                }
                catch { }
            }
        }
        catch { }
    }
}
