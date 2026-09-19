namespace Novara.Models;






public static class SyncApi
{
    public const string ApiPrefix = "/api/v1";








    public const string SpaceHeader = "X-Novara-Space";
    public const string DeviceHeader = "X-Novara-Device";
    public const string VersionHeader = "X-Novara-Version";
    public const string KeyWrapVersionHeader = "X-Novara-Keywrap-Version";







    public const string ReadScheme = "Novara-Read";






    public const string EditorDeviceId = "web-editor";
}


public sealed class DeviceRegistrationRequest
{
    public string SpaceId { get; set; } = "";
    public string EnrollmentSecret { get; set; } = "";
    public string DeviceName { get; set; } = "";
}


public sealed class DeviceRegistrationResponse
{
    public string DeviceId { get; set; } = "";
    public string DeviceToken { get; set; } = "";
}





public sealed class ReadTokenResponse
{
    public string ReadToken { get; set; } = "";
}


public sealed class SpaceInfo
{
    public string SpaceId { get; set; } = "";
    public long Version { get; set; }
    public long KeyWrapVersion { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long SizeBytes { get; set; }
    public long QuotaUsedBytes { get; set; }
    public long QuotaMaxBytes { get; set; }
    public int MaxVersions { get; set; }





    public bool HasReadToken { get; set; }





    public bool HasEditorDevice { get; set; }
}


public sealed class VersionInfo
{
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DeviceId { get; set; } = "";
    public long Size { get; set; }
    public bool IsConflict { get; set; }
    public long ConflictOf { get; set; }

    public bool Retained { get; set; } = true;
}


public sealed class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool Revoked { get; set; }


    public string Kind { get; set; } = "standard";


    public bool Current { get; set; }
}











public enum SyncErrorCode
{
    BadRequest,
    Unauthenticated,
    Forbidden,
    SpaceNotFound,
    NotFound,
    VersionConflict,
    PayloadTooLarge,
    RateLimited,
    QuotaExceeded,
    ServerError,
}







public sealed class SyncErrorResponse
{
    public string Error { get; set; } = "";
    public string Message { get; set; } = "";
    public long? CurrentVersion { get; set; }


    public long? LimitBytes { get; set; }


    public long? UsedBytes { get; set; }
}





public static class SyncErrorCodes
{
    public static string ToWire(SyncErrorCode code) => code switch
    {
        SyncErrorCode.BadRequest => "bad_request",
        SyncErrorCode.Unauthenticated => "unauthenticated",
        SyncErrorCode.Forbidden => "forbidden",
        SyncErrorCode.SpaceNotFound => "space_not_found",
        SyncErrorCode.NotFound => "not_found",
        SyncErrorCode.VersionConflict => "version_conflict",
        SyncErrorCode.PayloadTooLarge => "payload_too_large",
        SyncErrorCode.RateLimited => "rate_limited",
        SyncErrorCode.QuotaExceeded => "quota_exceeded",
        SyncErrorCode.ServerError => "server_error",
        _ => "error",
    };


    public static SyncErrorCode? FromWire(string? value) => value switch
    {
        "bad_request" => SyncErrorCode.BadRequest,
        "unauthenticated" => SyncErrorCode.Unauthenticated,
        "forbidden" => SyncErrorCode.Forbidden,
        "space_not_found" => SyncErrorCode.SpaceNotFound,
        "not_found" => SyncErrorCode.NotFound,
        "version_conflict" => SyncErrorCode.VersionConflict,
        "payload_too_large" => SyncErrorCode.PayloadTooLarge,
        "rate_limited" => SyncErrorCode.RateLimited,
        "quota_exceeded" => SyncErrorCode.QuotaExceeded,
        "server_error" => SyncErrorCode.ServerError,
        _ => null,
    };


    public static int HttpStatus(SyncErrorCode code) => code switch
    {
        SyncErrorCode.BadRequest => 400,
        SyncErrorCode.Unauthenticated => 401,
        SyncErrorCode.Forbidden => 403,
        SyncErrorCode.SpaceNotFound => 404,
        SyncErrorCode.NotFound => 404,
        SyncErrorCode.VersionConflict => 409,
        SyncErrorCode.PayloadTooLarge => 413,
        SyncErrorCode.RateLimited => 429,
        SyncErrorCode.QuotaExceeded => 507,
        _ => 500,
    };
}
