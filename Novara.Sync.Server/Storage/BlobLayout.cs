namespace Novara.Sync.Server.Storage;










internal static class BlobLayout
{
    public const string ConflictSuffix = ".conflict";





    public static bool IsSafeId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length <= 128 &&
           id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

    public static string SpacesRoot(string root) => Path.Combine(root, "spaces");

    public static string SpaceDirectory(string root, string spaceId) => Path.Combine(SpacesRoot(root), spaceId);







    public static bool RemoveSpaceDirectory(string root, string spaceId)
    {
        var dir = SpaceDirectory(root, spaceId);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    public static string BlobDirectory(string root, string spaceId) => Path.Combine(SpaceDirectory(root, spaceId), "blobs");



    public static string ChunkDirectory(string root, string spaceId) => Path.Combine(SpaceDirectory(root, spaceId), "chunks");


    public static bool IsSafeChunkId(string? chunkId)
        => !string.IsNullOrEmpty(chunkId) && chunkId.Length == 64 &&
           chunkId.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));


    public static bool ChunkExists(string root, string spaceId, string chunkId)
        => File.Exists(ChunkPath(root, spaceId, chunkId));

    public static string ChunkPath(string root, string spaceId, string chunkId)
        => Path.Combine(ChunkDirectory(root, spaceId), chunkId + ".blob");

    public static void WriteChunk(string root, string spaceId, string chunkId, byte[] data)
    {
        var dir = ChunkDirectory(root, spaceId);
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, chunkId + ".tmp");
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, Path.Combine(dir, chunkId + ".blob"), overwrite: true);
    }

    public static byte[]? ReadChunk(string root, string spaceId, string chunkId)
    {
        var path = ChunkPath(root, spaceId, chunkId);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }


    public static Dictionary<string, long> ChunkSizes(string root, string spaceId)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var dir = ChunkDirectory(root, spaceId);
        if (!Directory.Exists(dir)) return result;
        foreach (var f in Directory.GetFiles(dir, "*.blob"))
        {
            var id = Path.GetFileNameWithoutExtension(f);
            if (IsSafeChunkId(id)) result[id] = new FileInfo(f).Length;
        }
        return result;
    }


    public static List<string> PruneChunks(string root, string spaceId, ISet<string> keepChunkIds)
    {
        var removed = new List<string>();
        var dir = ChunkDirectory(root, spaceId);
        if (!Directory.Exists(dir)) return removed;
        foreach (var f in Directory.GetFiles(dir, "*.blob"))
        {
            var id = Path.GetFileNameWithoutExtension(f);
            if (keepChunkIds.Contains(id)) continue;
            try { File.Delete(f); removed.Add(id); } catch { }
        }
        return removed;
    }

    public static string BlobPath(string root, string spaceId, VersionRecord version)
        => Path.Combine(
            BlobDirectory(root, spaceId),
            version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + (version.IsConflict ? ConflictSuffix : ""));
}
