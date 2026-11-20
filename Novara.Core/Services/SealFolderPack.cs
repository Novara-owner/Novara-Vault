using System.IO.Compression;

namespace Novara.Services;


public readonly record struct SealPackResult(int EntryCount, long OriginalTotalBytes);





public sealed class SealPackException(string message) : Exception(message);





public sealed class SealExtractFailedException(string targetPath, string detail)
    : Exception($"folder extraction failed under {targetPath}: {detail}")
{
    public string TargetPath { get; } = targetPath;
}





















public static class SealFolderPack
{




    public static SealPackResult Pack(string folderPath, string zipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderPath);
        ArgumentException.ThrowIfNullOrEmpty(zipPath);

        var root = Path.GetFullPath(folderPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"folder not found: {root}");


        var planned = new List<PlannedEntry>();
        var totalBytes = 0L;

        foreach (var entry in Walk(root, cancellationToken))
        {
            planned.Add(entry);
            if (!entry.IsDirectory) totalBytes = checked(totalBytes + entry.Length);
        }

        try
        {
            using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            foreach (var entry in planned)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {

                    archive.CreateEntry(entry.RelativeName + "/");
                    continue;
                }

                var zipEntry = archive.CreateEntry(entry.RelativeName, CompressionLevel.Optimal);
                using var source = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var target = zipEntry.Open();
                source.CopyTo(target);
            }

            return new SealPackResult(planned.Count, totalBytes);
        }
        catch
        {


            TryDelete(zipPath);
            throw;
        }
    }





    public static int Extract(string zipPath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {



            throw new SealExtractFailedException(targetDirectory, $"the archive cannot be opened: {ex.Message}");
        }

        using (archive)
        {

            var targets = new List<(ZipArchiveEntry Entry, string Target, bool IsDirectory)>(archive.Entries.Count);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = ResolveTarget(targetDirectory, entry.FullName);
                var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                targets.Add((entry, target, isDirectory));
            }



            try
            {
                Directory.CreateDirectory(targetDirectory);

                var written = 0;
                foreach (var (entry, target, isDirectory) in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (isDirectory)
                    {
                        Directory.CreateDirectory(target);
                        written++;
                        continue;
                    }

                    var parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                    entry.ExtractToFile(target, overwrite: false);
                    written++;
                }

                return written;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new SealExtractFailedException(targetDirectory, $"extraction stopped: {ex.Message}");
            }
        }
    }





    internal static string ResolveTarget(string targetDirectory, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            throw new SealExtractFailedException(targetDirectory, "the archive contains an entry with no name");

        var name = entryName.Replace('\\', '/');

        if (name.StartsWith('/') || (name.Length >= 2 && name[1] == ':'))
            throw new SealExtractFailedException(targetDirectory, $"absolute entry name: {entryName}");

        if (name.StartsWith("//", StringComparison.Ordinal))
            throw new SealExtractFailedException(targetDirectory, $"network entry name: {entryName}");

        var root = Path.GetFullPath(targetDirectory);
        var trimRoot = root.TrimEnd(Path.DirectorySeparatorChar);

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SealExtractFailedException(targetDirectory, $"unusable entry name: {entryName}");
        }


        if (!string.Equals(full, trimRoot, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(trimRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new SealExtractFailedException(targetDirectory, $"entry escapes the target: {entryName}");
        }

        return full;
    }



    private readonly record struct PlannedEntry(string FullPath, string RelativeName, bool IsDirectory, long Length);







    internal static bool IsCloudPlaceholder(FileAttributes attributes)
    {
        const FileAttributes CloudRecall = (FileAttributes)0x00040000 | (FileAttributes)0x00400000;
        return (attributes & CloudRecall) != 0;
    }

    private static IEnumerable<PlannedEntry> Walk(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attributes = File.GetAttributes(path);




                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new SealPackException($"a reparse point inside the folder cannot be packed: {path}");





                if (IsCloudPlaceholder(attributes))
                    throw new SealPackException($"a cloud placeholder inside the folder cannot be packed (recall it locally first): {path}");

                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    yield return new PlannedEntry(path, relative, true, 0);
                    pending.Push(path);
                }
                else
                {
                    yield return new PlannedEntry(path, relative, false, new FileInfo(path).Length);
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }
}
