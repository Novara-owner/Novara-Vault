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
            _timer.Tick += (_, _) =>
            {
                var path = CreateSnapshot(isManual: false);
                if (path == null) System.Diagnostics.Debug.WriteLine("AutoBackup: periodic snapshot failed (disk full / file locked / no data file)"); // N4S-05: at least log it - silent null hid failures completely
            };
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

            File.Copy(DataFilePath, path, overwrite: false);
            try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }

            
            if (ReadEncryptionFlag(DataFilePath) == FlagEncrypted && File.Exists(PasswordService.SecurityFilePath))
            {
                var secPath = SecurityPairPath(path);
                File.Copy(PasswordService.SecurityFilePath, secPath, overwrite: false);
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
        bool snapshotEncrypted = ReadEncryptionFlag(snapshotPath) == FlagEncrypted;
        var secPair = SecurityPairPath(snapshotPath);
        if (snapshotEncrypted && !File.Exists(secPair)) return false; 

        
        var tmpPath = DataFilePath + ".restoretmp";
        File.Copy(snapshotPath, tmpPath, overwrite: true);
        if (File.Exists(DataFilePath)) File.SetAttributes(DataFilePath, FileAttributes.Normal);
        File.Move(tmpPath, DataFilePath, true);
        try { File.SetAttributes(DataFilePath, FileAttributes.Hidden | FileAttributes.ReadOnly); } catch { }

        
        
        if (snapshotEncrypted)
        {
            if (File.Exists(PasswordService.SecurityFilePath)) File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Normal);
            File.Copy(secPair, PasswordService.SecurityFilePath, overwrite: true);
            try { File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Hidden); } catch { }
        }
        else
        {
            if (File.Exists(PasswordService.SecurityFilePath))
            {
                File.SetAttributes(PasswordService.SecurityFilePath, FileAttributes.Normal);
                File.Delete(PasswordService.SecurityFilePath);
            }
        }
        return true;
    }

    
    private static byte ReadEncryptionFlag(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 6) return 0;
            fs.Seek(5, SeekOrigin.Begin);
            return (byte)fs.ReadByte();
        }
        catch { return 0; }
    }

    
    private static string SecurityPairPath(string snapshotPath) => snapshotPath + SecuritySuffix;

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
