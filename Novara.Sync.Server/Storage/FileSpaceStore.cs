using System.Text.Json;

namespace Novara.Sync.Server.Storage;






public sealed class FileSpaceStore : ISpaceStore
{
    private const string SpaceFile = "space.json";
    private const string DevicesFile = "devices.json";
    private const string VersionsFile = "versions.json";
    private const string BlobDir = "blobs";

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

    public void SaveSpace(SpaceRecord space)
    {
        ArgumentNullException.ThrowIfNull(space);
        RequireSafeId(space.SpaceId);
        Directory.CreateDirectory(SpaceDir(space.SpaceId));
        WriteAtomic(Path.Combine(SpaceDir(space.SpaceId), SpaceFile), JsonSerializer.Serialize(space, Options));
    }

    public IReadOnlyList<DeviceRecord> GetDevices(string spaceId)
        => IsSafeId(spaceId)
            ? Read<List<DeviceRecord>>(Path.Combine(SpaceDir(spaceId), DevicesFile)) ?? new List<DeviceRecord>()
            : new List<DeviceRecord>();

    public DeviceRecord? GetDevice(string spaceId, string deviceId)
        => GetDevices(spaceId).FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));

    public void SaveDevice(string spaceId, DeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);
        RequireSafeId(spaceId);

        var devices = GetDevices(spaceId).ToList();
        var index = devices.FindIndex(d => string.Equals(d.DeviceId, device.DeviceId, StringComparison.Ordinal));
        if (index >= 0) devices[index] = device; else devices.Add(device);

        Directory.CreateDirectory(SpaceDir(spaceId));
        WriteAtomic(Path.Combine(SpaceDir(spaceId), DevicesFile), JsonSerializer.Serialize(devices, Options));
    }






    public void TouchDevice(string spaceId, string deviceId, DateTime lastSeenAt)
    {
        RequireSafeId(spaceId);

        var devices = GetDevices(spaceId).ToList();
        var index = devices.FindIndex(d => string.Equals(d.DeviceId, deviceId, StringComparison.Ordinal));
        if (index < 0) return;

        devices[index].LastSeenAt = lastSeenAt;
        Directory.CreateDirectory(SpaceDir(spaceId));
        WriteAtomic(Path.Combine(SpaceDir(spaceId), DevicesFile), JsonSerializer.Serialize(devices, Options));
    }

    public IReadOnlyList<VersionRecord> GetVersions(string spaceId)
        => IsSafeId(spaceId)
            ? Read<List<VersionRecord>>(Path.Combine(SpaceDir(spaceId), VersionsFile)) ?? new List<VersionRecord>()
            : new List<VersionRecord>();

    public void SaveVersions(string spaceId, IReadOnlyList<VersionRecord> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        RequireSafeId(spaceId);

        var ordered = versions.OrderBy(v => v.Version).ToList();
        Directory.CreateDirectory(SpaceDir(spaceId));
        WriteAtomic(Path.Combine(SpaceDir(spaceId), VersionsFile), JsonSerializer.Serialize(ordered, Options));
    }

    public void WriteBlob(string spaceId, VersionRecord version, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(payload);
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
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
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

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options); }
        catch (JsonException e) { throw new SpaceStoreException($"corrupt metadata: {Path.GetFileName(path)}", e); }
    }

    private static void WriteAtomic(string path, string content)
        => WriteAtomicBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    private static void WriteAtomicBytes(string path, byte[] content)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, content);

        File.Move(temp, path, overwrite: true);
    }
}
