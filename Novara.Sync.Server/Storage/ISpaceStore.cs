namespace Novara.Sync.Server.Storage;







public interface ISpaceStore
{
    bool SpaceExists(string spaceId);
    SpaceRecord? GetSpace(string spaceId);
    void SaveSpace(SpaceRecord space);

    IReadOnlyList<DeviceRecord> GetDevices(string spaceId);
    DeviceRecord? GetDevice(string spaceId, string deviceId);
    void SaveDevice(string spaceId, DeviceRecord device);







    void TouchDevice(string spaceId, string deviceId, DateTime lastSeenAt);

    IReadOnlyList<VersionRecord> GetVersions(string spaceId);
    void SaveVersions(string spaceId, IReadOnlyList<VersionRecord> versions);

    void WriteBlob(string spaceId, VersionRecord version, byte[] payload);
    byte[]? ReadBlob(string spaceId, VersionRecord version);


    void DeleteBlob(string spaceId, VersionRecord version);
}


public sealed class SpaceStoreException : Exception
{
    public SpaceStoreException(string message) : base(message) { }
    public SpaceStoreException(string message, Exception inner) : base(message, inner) { }
}
