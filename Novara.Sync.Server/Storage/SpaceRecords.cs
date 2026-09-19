namespace Novara.Sync.Server.Storage;





public sealed class SpaceRecord
{
    public string SpaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }


    public long CurrentVersion { get; set; }

    public long KeyWrapVersion { get; set; }
    public string KeyWrapJson { get; set; } = "";


    public string EnrollmentHash { get; set; } = "";










    public string ReadTokenHash { get; set; } = "";

    public long QuotaBytes { get; set; }
    public int MaxVersions { get; set; }
}







public enum DeviceKind
{
    Standard = 0,
    Editor = 1,
}


public sealed class DeviceRecord
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool Revoked { get; set; }


    public DeviceKind Kind { get; set; } = DeviceKind.Standard;
}





public sealed class VersionRecord
{
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DeviceId { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public bool IsConflict { get; set; }
    public long ConflictOf { get; set; }


    public bool Retained { get; set; } = true;
}
