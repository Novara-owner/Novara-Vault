using System.Diagnostics;
using System.IO;

namespace Novara.Services;

public enum PathPropertyState
{

    Invalid,

    Ready,

    Unavailable,

    Error,
}



public sealed record PathPropertyResult(
    PathPropertyState State,
    long? Bytes,
    long? Files,
    long? Folders,
    DateTime? CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    bool IsReadOnly,
    bool IsHidden,
    string? Detail = null);


public sealed record PathPropertyProgress(long Bytes, long Files, long Folders);

public static class PathPropertyReader
{


    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);



    public static string? NormalizeKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var driveRoot = DriveRootPath(path);
        if (driveRoot != null) return driveRoot;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (root != null && full.Length <= root.Length) return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch { return null; }
    }


    private static string? DriveRootPath(string path)
    {
        var t = path.Trim();
        if (t.Length is < 2 or > 3 || t[1] != ':' || !char.IsAsciiLetter(t[0])) return null;
        if (t.Length == 3 && t[2] is not ('\\' or '/')) return null;
        return char.ToUpperInvariant(t[0]) + ":\\";
    }





    public static PathPropertyResult Read(
        string path, TimeSpan stallTimeout, Action<PathPropertyProgress>? progress, CancellationToken external)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(external);



        using var watchdog = new Timer(_ =>
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }, null, stallTimeout, Timeout.InfiniteTimeSpan);
        var ct = cts.Token;

        Action<PathPropertyProgress>? feeding = progress == null ? null : p =>
        {
            watchdog.Change(stallTimeout, Timeout.InfiniteTimeSpan);
            progress(p);
        };
        try
        {
            return ReadCore(path, feeding, ct);
        }
        catch (OperationCanceledException)
        {

            if (external.IsCancellationRequested) throw;
            return new PathPropertyResult(PathPropertyState.Unavailable, null, null, null, null, null, false, false, "stall");
        }
        catch (PathTooLongException ex)
        {

            return new PathPropertyResult(PathPropertyState.Error, null, null, null, null, null, false, false, ex.GetType().Name);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            return new PathPropertyResult(PathPropertyState.Invalid, null, null, null, null, null, false, false, ex.GetType().Name);
        }
        catch (Exception ex)
        {
            return new PathPropertyResult(PathPropertyState.Error, null, null, null, null, null, false, false, ex.GetType().Name);
        }
    }

    private static PathPropertyResult ReadCore(string path, Action<PathPropertyProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();


        var driveRoot = DriveRootPath(path);
        if (driveRoot != null)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(driveRoot))
                return new PathPropertyResult(PathPropertyState.Invalid, null, null, null, null, null, false, false);
            var rootInfo = new DirectoryInfo(driveRoot);
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady)
                return new PathPropertyResult(PathPropertyState.Unavailable, null, null, null, null, null, false, false, "drive-not-ready");
            return new PathPropertyResult(PathPropertyState.Ready, drive.TotalSize, null, null,
                ToLocal(rootInfo.CreationTimeUtc), ToLocal(rootInfo.LastWriteTimeUtc),
                rootInfo.Attributes.HasFlag(FileAttributes.ReadOnly), rootInfo.Attributes.HasFlag(FileAttributes.Hidden));
        }

        bool isFile = File.Exists(path);
        bool isDir = !isFile && Directory.Exists(path);
        if (!isFile && !isDir)
        {
            return new PathPropertyResult(PathPropertyState.Invalid, null, null, null, null, null, false, false);
        }

        if (isFile)
        {
            ct.ThrowIfCancellationRequested();
            var fi = new FileInfo(path);

            return new PathPropertyResult(PathPropertyState.Ready, fi.Length, 1, null,
                ToLocal(fi.CreationTimeUtc), ToLocal(fi.LastWriteTimeUtc),
                fi.IsReadOnly, fi.Attributes.HasFlag(FileAttributes.Hidden));
        }

        var di = new DirectoryInfo(path);


        var root = Path.GetPathRoot(di.FullName);
        bool isDriveRoot = root != null && di.FullName.Length <= root.Length && root.Length == 3 && root[1] == ':';
        if (isDriveRoot && root != null)
        {
            ct.ThrowIfCancellationRequested();
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return new PathPropertyResult(PathPropertyState.Unavailable, null, null, null, null, null, false, false, "drive-not-ready");
            return new PathPropertyResult(PathPropertyState.Ready, drive.TotalSize, null, null,
                ToLocal(di.CreationTimeUtc), ToLocal(di.LastWriteTimeUtc),
                di.Attributes.HasFlag(FileAttributes.ReadOnly), di.Attributes.HasFlag(FileAttributes.Hidden));
        }



        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        long bytes = 0, files = 0, folders = 0;
        long lastPushEntries = 0;
        var sw = Stopwatch.StartNew();
        foreach (var entry in di.EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (entry is FileInfo f) { bytes += f.Length; files++; }
            else if (entry is DirectoryInfo) folders++;
            if (entry is FileInfo or DirectoryInfo && (files + folders - lastPushEntries >= 512 || sw.ElapsedMilliseconds >= 150))
            {
                lastPushEntries = files + folders;
                sw.Restart();
                progress?.Invoke(new PathPropertyProgress(bytes, files, folders));
            }
        }
        return new PathPropertyResult(PathPropertyState.Ready, bytes, files, folders,
            ToLocal(di.CreationTimeUtc), ToLocal(di.LastWriteTimeUtc),
            di.Attributes.HasFlag(FileAttributes.ReadOnly), di.Attributes.HasFlag(FileAttributes.Hidden));
    }

    private static DateTime? ToLocal(DateTime utc) => utc == default ? null : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
