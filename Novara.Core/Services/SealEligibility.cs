namespace Novara.Services;


public enum SealRejectReason
{
    None = 0,


    ReparsePoint,


    CloudPlaceholder,


    PipeOrDevice,


    ReservedName,


    PathTooLong,


    InsideSealRoot,


    ContainsSealRoot,


    NotFound,


    InUse,


    Denied,


    ReadOnly,


    Unreachable,


    Fat32TooLarge,
}


















public static class SealEligibility
{

    public const int MaxPathLength = 32767;


    public const long Fat32MaxFileSize = 4L * 1024 * 1024 * 1024 - 1;


    private const int RecallOnDataAccess = 0x400000;
    private const int RecallOnOpen = 0x40000;

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];




    public static (bool Ok, SealRejectReason Reason) Evaluate(string path, SealSpacePaths space)
        => EvaluateCore(path, space, ReadFileSize, ReadVolumeFormat);






    internal static (bool Ok, SealRejectReason Reason) EvaluateCore(
        string path, SealSpacePaths space, Func<string, long?> sizeProbe, Func<string, string?> formatProbe)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(sizeProbe);
        ArgumentNullException.ThrowIfNull(formatProbe);

        if (string.IsNullOrWhiteSpace(path)) return (false, SealRejectReason.NotFound);
        if (path.Length > MaxPathLength) return (false, SealRejectReason.PathTooLong);
        if (IsDevicePath(path)) return (false, SealRejectReason.PipeOrDevice);
        if (IsReservedName(path)) return (false, SealRejectReason.ReservedName);

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (PathTooLongException)
        {
            return (false, SealRejectReason.PathTooLong);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return (false, SealRejectReason.NotFound);
        }

        if (space.Contains(full)) return (false, SealRejectReason.InsideSealRoot);



        if (IsSameOrAncestor(full, space.Root)) return (false, SealRejectReason.ContainsSealRoot);

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(full);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (false, IsRootMissing(full) ? SealRejectReason.Unreachable : SealRejectReason.NotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, SealRejectReason.Denied);
        }
        catch (IOException)
        {
            return (false, SealRejectReason.Unreachable);
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return (false, SealRejectReason.ReparsePoint);

        if (HasCloudPlaceholder(attributes))
            return (false, SealRejectReason.CloudPlaceholder);

        var isDirectory = (attributes & FileAttributes.Directory) != 0;





        if (!isDirectory && (attributes & FileAttributes.ReadOnly) != 0)
            return (false, SealRejectReason.ReadOnly);

        if (isDirectory) return (true, SealRejectReason.None);



        try
        {
            using var probe = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, SealRejectReason.Denied);
        }
        catch (IOException)
        {
            return (false, SealRejectReason.InUse);
        }
        catch (Exception ex) when (ex is NotSupportedException)
        {
            return (false, SealRejectReason.PipeOrDevice);
        }

        var size = sizeProbe(full);
        if (size is null) return (false, SealRejectReason.Unreachable);







        var containerBytes = SealPreflight.Estimate(size.Value, Directory.Exists(full));
        if (containerBytes > Fat32MaxFileSize
            && string.Equals(formatProbe(space.Root), "FAT32", StringComparison.OrdinalIgnoreCase))
            return (false, SealRejectReason.Fat32TooLarge);

        return (true, SealRejectReason.None);
    }


    internal static bool HasCloudPlaceholder(FileAttributes attributes)
    {
        var value = (int)attributes;
        return (value & (RecallOnDataAccess | (int)FileAttributes.Offline | RecallOnOpen)) != 0;
    }


    private static bool IsSameOrAncestor(string candidate, string root)
    {



        var a = SealSpacePaths.Canonical(candidate).TrimEnd(Path.DirectorySeparatorChar);
        var b = SealSpacePaths.Canonical(root).TrimEnd(Path.DirectorySeparatorChar);
        if (a.Length == 0 || b.Length == 0) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }


    internal static bool IsDevicePath(string path)
        => path.StartsWith(@"\\.\", StringComparison.Ordinal)
           || path.StartsWith(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase);





    internal static bool IsReservedName(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return false;

        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(stem)) return false;

        return Array.Exists(ReservedNames, reserved => string.Equals(reserved, stem, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRootMissing(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root) && !Directory.Exists(root);
    }

    private static long? ReadFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadVolumeFormat(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).DriveFormat;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {

            return null;
        }
    }
}
