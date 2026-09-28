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

    public static string BlobPath(string root, string spaceId, VersionRecord version)
        => Path.Combine(
            BlobDirectory(root, spaceId),
            version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + (version.IsConflict ? ConflictSuffix : ""));
}
