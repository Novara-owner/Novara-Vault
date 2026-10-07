namespace Novara.Sync.Server.Storage;







public interface ISpaceStore
{
    bool SpaceExists(string spaceId);
    SpaceRecord? GetSpace(string spaceId);








    void SaveSpace(SpaceRecord space, bool requireExisting = false);












    IReadOnlyList<SpaceRecord> ListSpaces();




















    void DeleteSpace(string spaceId);

    IReadOnlyList<DeviceRecord> GetDevices(string spaceId);
    DeviceRecord? GetDevice(string spaceId, string deviceId);



    void SaveDevice(string spaceId, DeviceRecord device, bool requireExisting = true);







    void TouchDevice(string spaceId, string deviceId, DateTime lastSeenAt);

    IReadOnlyList<VersionRecord> GetVersions(string spaceId);




    void SaveVersions(string spaceId, IReadOnlyList<VersionRecord> versions, bool requireExisting = true);

    void WriteBlob(string spaceId, VersionRecord version, byte[] payload);
    byte[]? ReadBlob(string spaceId, VersionRecord version);


    void DeleteBlob(string spaceId, VersionRecord version);
}


public sealed class SpaceStoreException : Exception
{
    public SpaceStoreException(string message) : base(message) { }
    public SpaceStoreException(string message, Exception inner) : base(message, inner) { }
}
