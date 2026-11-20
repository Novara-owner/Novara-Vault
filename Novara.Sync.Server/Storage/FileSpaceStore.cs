using System.Text.Json;

namespace Novara.Sync.Server.Storage;






public sealed class FileSpaceStore : ISpaceStore
{
    private const string SpaceFile = "space.json";
    private const string DevicesFile = "devices.json";
    private const string VersionsFile = "versions.json";
    private const string BlobDir = "blobs";


    private const string SpaceLockFile = "space.lock";
    private const string DevicesLockFile = "devices.lock";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _root;

    public FileSpaceStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("storage root is required", nameof(root));
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;


    public static bool IsSafeId(string? id) => BlobLayout.IsSafeId(id);

    public bool SpaceExists(string spaceId)
        => IsSafeId(spaceId) && File.Exists(Path.Combine(SpaceDir(spaceId), SpaceFile));






    public SpaceRecord? GetSpace(string spaceId)
        => IsSafeId(spaceId) ? Read<SpaceRecord>(Path.Combine(SpaceDir(spaceId), SpaceFile)) : null;

    public void SaveSpace(SpaceRecord space, bool requireExisting = false)
    {
        ArgumentNullException.ThrowIfNull(space);
        RequireSafeId(space.SpaceId);



        if (requireExisting && !SpaceExists(space.SpaceId))
            throw new SpaceGoneException($"space no longer exists: {space.SpaceId}");
        Directory.CreateDirectory(SpaceDir(space.SpaceId));
        WriteAtomic(Path.Combine(SpaceDir(space.SpaceId), SpaceFile), JsonSerializer.Serialize(space, Options));
    }

    public void UpdateSpace(string spaceId, Action<SpaceRecord> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        RequireSafeId(spaceId);
        Directory.CreateDirectory(SpaceDir(spaceId));

        using var lease = AcquireMetadataLock(spaceId, SpaceLockFile);
        var path = Path.Combine(SpaceDir(spaceId), SpaceFile);
        var space = Read<SpaceRecord>(path)
                    ?? throw new SpaceGoneException($"space no longer exists: {spaceId}");
        mutate(space);
        WriteAtomic(path, JsonSerializer.Serialize(space, Options));
    }





    private FileStream AcquireMetadataLock(string spaceId, string fileName)
    {
        var path = Path.Combine(SpaceDir(spaceId), fileName);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }







    public IReadOnlyList<SpaceRecord> ListSpaces()
    {
        var spacesRoot = BlobLayout.SpacesRoot(_root);
        if (!Directory.Exists(spacesRoot)) return Array.Empty<SpaceRecord>();

        var spaces = new List<SpaceRecord>();
        foreach (var dir in EnumerateSpaceDirectories(spacesRoot))
        {
            var id = Path.GetFileName(dir);
            if (!IsSafeId(id)) continue;

            var record = Read<SpaceRecord>(Path.Combine(dir, SpaceFile));
            if (record is not null) spaces.Add(record);
        }

        return spaces.OrderBy(s => s.CreatedAt).ThenBy(s => s.SpaceId, StringComparer.Ordinal).ToList();
    }














    public void DeleteSpace(string spaceId)
    {
        RequireSafeId(spaceId);
        var dir = BlobLayout.SpaceDirectory(_root, spaceId);
        var parked = dir + ".deleting";



        if (!Directory.Exists(dir))
        {
            if (Directory.Exists(parked))
            {
                try { Directory.Delete(parked, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new SpaceStoreException(
                        $"the space was deleted but its parked tree could not be removed: {parked}", e);
                }
            }
            return;
        }

        try
        {
            if (Directory.Exists(parked)) Directory.Delete(parked, recursive: true);
            Directory.Move(dir, parked);
            Directory.Delete(parked, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {


            if (!Directory.Exists(dir))
                throw new SpaceStoreException(
                    $"the space was deleted but its parked tree could not be removed: {parked}", e);
            throw new SpaceStoreException(
                $"failed to remove the space directory: {dir}", e);
        }
    }

    public IReadOnlyList<DeviceRecord> GetDevices(string spaceId)
        => IsSafeId(spaceId)
            ? Read<List<DeviceRecord>>(Path.Combine(SpaceDir(spaceId), DevicesFile)) ?? new List<DeviceRecord>()
            : new List<DeviceRecord>();

    public DeviceRecord? GetDevice(string spaceId, string deviceId)
        => GetDevices(spaceId).FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

    public void SaveDevice(string spaceId, DeviceRecord device, bool requireExisting = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        RequireSafeId(spaceId);
        if (requireExisting && !SpaceExists(spaceId))
            throw new SpaceGoneException($"space no longer exists: {spaceId}");

        Directory.CreateDirectory(SpaceDir(spaceId));
        using var lease = AcquireMetadataLock(spaceId, DevicesLockFile);
        var devices = GetDevices(spaceId).ToList();
        var index = devices.FindIndex(d => string.Equals(d.DeviceId, device.DeviceId, StringComparison.Ordinal));
        if (index >= 0) devices[index] = device; else devices.Add(device);

        WriteAtomic(Path.Combine(SpaceDir(spaceId), DevicesFile), JsonSerializer.Serialize(devices, Options));
    }






    public void TouchDevice(string spaceId, string deviceId, DateTime lastSeenAt)
    {
        RequireSafeId(spaceId);

        Directory.CreateDirectory(SpaceDir(spaceId));
        using var lease = AcquireMetadataLock(spaceId, DevicesLockFile);
        var devices = GetDevices(spaceId).ToList();
        var index = devices.FindIndex(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
        if (index < 0) return;

        devices[index].LastSeenAt = lastSeenAt;
        WriteAtomic(Path.Combine(SpaceDir(spaceId), DevicesFile), JsonSerializer.Serialize(devices, Options));
    }

    public IReadOnlyList<VersionRecord> GetVersions(string spaceId)
        => IsSafeId(spaceId)
            ? Read<List<VersionRecord>>(Path.Combine(SpaceDir(spaceId), VersionsFile)) ?? new List<VersionRecord>()
            : new List<VersionRecord>();

    public void SaveVersions(string spaceId, IReadOnlyList<VersionRecord> versions, bool requireExisting = true)
    {
        ArgumentNullException.ThrowIfNull(versions);
        RequireSafeId(spaceId);
        if (requireExisting && !SpaceExists(spaceId))
            throw new SpaceGoneException($"space no longer exists: {spaceId}");

        var ordered = versions.OrderBy(v => v.Version).ToList();
        Directory.CreateDirectory(SpaceDir(spaceId));
        WriteAtomic(Path.Combine(SpaceDir(spaceId), VersionsFile), JsonSerializer.Serialize(ordered, Options));
    }

    public void WriteBlob(string spaceId, VersionRecord version, ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(version);
        RequireSafeId(spaceId);

        var dir = Path.Combine(SpaceDir(spaceId), BlobDir);
        Directory.CreateDirectory(dir);
        WriteAtomicBytes(BlobPath(spaceId, version), payload);
    }

    public byte[]? ReadBlob(string spaceId, VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!IsSafeId(spaceId)) return null;

        var path = BlobPath(spaceId, version);
        if (!File.Exists(path)) return null;
        try
        {


            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {

            return null;
        }
    }

    public void DeleteBlob(string spaceId, VersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!IsSafeId(spaceId)) return;

        var path = BlobPath(spaceId, version);
        if (File.Exists(path)) File.Delete(path);
    }

    private string BlobPath(string spaceId, VersionRecord version) => BlobLayout.BlobPath(_root, spaceId, version);

    private string SpaceDir(string spaceId) => BlobLayout.SpaceDirectory(_root, spaceId);

    private static void RequireSafeId(string spaceId)
    {
        if (!IsSafeId(spaceId)) throw new SpaceStoreException("space id contains unsupported characters");
    }





    private static IReadOnlyList<string> EnumerateSpaceDirectories(string spacesRoot)
    {
        try { return Directory.EnumerateDirectories(spacesRoot).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new SpaceStoreException("failed to enumerate the spaces directory", e);
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {




            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Options);
        }
        catch (JsonException e) { throw new SpaceStoreException($"corrupt metadata: {Path.GetFileName(path)}", e); }
    }

    private static void WriteAtomic(string path, string content)
        => WriteAtomicBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static void WriteAtomicBytes(string path, ReadOnlyMemory<byte> content)
    {


        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {

            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                stream.Write(content.Span);

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }



    public void WriteChunk(string spaceId, string chunkId, byte[] data)
        => BlobLayout.WriteChunk(_root, BlobLayout.IsSafeId(spaceId) ? spaceId : throw new ArgumentException("space id is not valid", nameof(spaceId)), chunkId.ToLowerInvariant(), data);

    public byte[]? ReadChunk(string spaceId, string chunkId)
        => BlobLayout.IsSafeId(spaceId) && BlobLayout.IsSafeChunkId(chunkId)
            ? BlobLayout.ReadChunk(_root, spaceId, chunkId.ToLowerInvariant())
            : null;

    public bool ChunkExists(string spaceId, string chunkId)
        => BlobLayout.IsSafeId(spaceId) && BlobLayout.IsSafeChunkId(chunkId)
            && BlobLayout.ChunkExists(_root, spaceId, chunkId.ToLowerInvariant());

    public string? ChunkPath(string spaceId, string chunkId)
    {
        if (!BlobLayout.IsSafeId(spaceId) || !BlobLayout.IsSafeChunkId(chunkId)) return null;
        var id = chunkId.ToLowerInvariant();
        return BlobLayout.ChunkExists(_root, spaceId, id) ? BlobLayout.ChunkPath(_root, spaceId, id) : null;
    }

    public Dictionary<string, long> ChunkSizes(string spaceId)
        => BlobLayout.IsSafeId(spaceId) ? BlobLayout.ChunkSizes(_root, spaceId) : new Dictionary<string, long>();

    public List<string> PruneChunks(string spaceId, ISet<string> keepChunkIds)
        => BlobLayout.IsSafeId(spaceId) ? BlobLayout.PruneChunks(_root, spaceId, keepChunkIds) : new List<string>();
}
