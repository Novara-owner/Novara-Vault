using System.Collections.Concurrent;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server.Policy;
using Novara.Sync.Server.Security;
using Novara.Sync.Server.Storage;

namespace Novara.Sync.Server;


public sealed class SyncResult<T>
{
    public bool Success { get; private init; }
    public SyncErrorCode Error { get; private init; }
    public string Message { get; private init; } = "";
    public long? CurrentVersion { get; private init; }
    public T? Value { get; private init; }






    public long? LimitBytes { get; private init; }


    public long? UsedBytes { get; private init; }

    public static SyncResult<T> Ok(T value) => new() { Success = true, Value = value };

    public static SyncResult<T> Fail(SyncErrorCode error, string message, long? currentVersion = null)
        => new() { Success = false, Error = error, Message = message, CurrentVersion = currentVersion };


    public static SyncResult<T> FailSized(SyncErrorCode error, string message, long limitBytes, long? usedBytes = null,
        long? currentVersion = null)
        => new()
        {
            Success = false,
            Error = error,
            Message = message,
            CurrentVersion = currentVersion,
            LimitBytes = limitBytes,
            UsedBytes = usedBytes,
        };
}







public sealed record NewSpaceCredentials(string SpaceId, string EnrollmentSecret);


public sealed record NewDeviceCredentials(string DeviceId, string DeviceToken);


public sealed record StoredEnvelope(string Json, VersionRecord Version);






public sealed record SpaceSummary(
    string SpaceId,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long CurrentVersion,
    long KeyWrapVersion,
    int DeviceCount,
    int RevokedDeviceCount,
    bool HasReadToken,
    bool HasEditorDevice,
    long UsedBytes,
    long QuotaBytes,
    int MaxVersions);






public sealed class SpaceService
{
    private readonly ISpaceStore _store;
    private readonly SyncServerOptions _options;
    private readonly FailureRateLimiter _limiter;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SpaceService(ISpaceStore store, SyncServerOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new SyncServerOptions();
        _limiter = new FailureRateLimiter(_options.MaxAuthFailures, _options.AuthFailureWindow);
    }

    public SyncServerOptions Options => _options;


    public DeviceRecord? DeviceOf(string spaceId, string deviceId) => _store.GetDevice(spaceId, deviceId);





    public SyncResult<NewSpaceCredentials> CreateSpace(string name)
    {
        var spaceId = TokenAuth.NewSpaceId();
        var enrollmentSecret = TokenAuth.NewEnrollmentSecret();

        var record = new SpaceRecord
        {
            SpaceId = spaceId,
            Name = string.IsNullOrWhiteSpace(name) ? "novara" : name.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CurrentVersion = 0,
            KeyWrapVersion = 0,
            KeyWrapJson = "",
            EnrollmentHash = TokenAuth.Hash(enrollmentSecret),
            QuotaBytes = _options.DefaultQuotaBytes,
            MaxVersions = _options.DefaultMaxVersions,
        };
        _store.SaveSpace(record);
        _store.SaveVersions(spaceId, Array.Empty<VersionRecord>());

        return SyncResult<NewSpaceCredentials>.Ok(
            new NewSpaceCredentials(spaceId, enrollmentSecret));
    }





    public SyncResult<IReadOnlyList<SpaceSummary>> ListSpaces()
        => SyncResult<IReadOnlyList<SpaceSummary>>.Ok(_store.ListSpaces().Select(Summarise).ToList());


    public SyncResult<SpaceSummary> DescribeSpace(string spaceId)
    {
        var space = _store.GetSpace(spaceId);
        return space is null
            ? SyncResult<SpaceSummary>.Fail(SyncErrorCode.SpaceNotFound, "space not found")
            : SyncResult<SpaceSummary>.Ok(Summarise(space));
    }









    public SyncResult<SpaceSummary> DeleteSpace(string spaceId)
    {
        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var space = _store.GetSpace(spaceId);
            if (space is null) return SyncResult<SpaceSummary>.Fail(SyncErrorCode.SpaceNotFound, "space not found");



            var summary = Summarise(space);
            _store.DeleteSpace(spaceId);
            return SyncResult<SpaceSummary>.Ok(summary);
        }
        finally { gate.Release(); }
    }










    public SyncResult<NewSpaceCredentials> RotateEnrollmentSecret(string spaceId)
    {
        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {



            var fresh = _store.GetSpace(spaceId);
            if (fresh is null) return SyncResult<NewSpaceCredentials>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

            var enrollmentSecret = TokenAuth.NewEnrollmentSecret();
            fresh.EnrollmentHash = TokenAuth.Hash(enrollmentSecret);
            fresh.UpdatedAt = DateTime.UtcNow;
            _store.SaveSpace(fresh);

            return SyncResult<NewSpaceCredentials>.Ok(new NewSpaceCredentials(fresh.SpaceId, enrollmentSecret));
        }
        finally { gate.Release(); }
    }






    private SpaceSummary Summarise(SpaceRecord space)
    {
        var versions = _store.GetVersions(space.SpaceId);
        var devices = _store.GetDevices(space.SpaceId);
        return new SpaceSummary(
            space.SpaceId,
            space.Name,
            space.CreatedAt,
            space.UpdatedAt,
            space.CurrentVersion,
            space.KeyWrapVersion,
            devices.Count,
            devices.Count(d => d.Revoked),
            !string.IsNullOrEmpty(space.ReadTokenHash),
            devices.Any(d => string.Equals(d.DeviceId, EditorDeviceId, StringComparison.Ordinal) && !d.Revoked),
            RetentionPolicy.UsedBytes(versions),
            space.QuotaBytes,
            space.MaxVersions);
    }









    public SyncResult<NewDeviceCredentials> RegisterDevice(string spaceId, string enrollmentSecret, string deviceName, string? callerAddress = null)
    {




        var enrollKey = "enroll:" + spaceId + ":" + (callerAddress ?? "unknown");
        if (_limiter.IsBlocked(enrollKey))
            return SyncResult<NewDeviceCredentials>.Fail(SyncErrorCode.RateLimited, "too many failed attempts");

        var space = _store.GetSpace(spaceId);
        if (space is null) return SyncResult<NewDeviceCredentials>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

        if (!TokenAuth.Verify(enrollmentSecret, space.EnrollmentHash))
        {
            _limiter.RecordFailure(enrollKey);
            return SyncResult<NewDeviceCredentials>.Fail(SyncErrorCode.Forbidden, "enrollment secret is not valid");
        }

        _limiter.Reset(enrollKey);

        var device = new DeviceRecord
        {
            DeviceId = TokenAuth.NewDeviceId(),
            Name = string.IsNullOrWhiteSpace(deviceName) ? "device" : deviceName.Trim(),
            TokenHash = "",
            CreatedAt = DateTime.UtcNow,
        };
        var token = TokenAuth.NewToken();
        device.TokenHash = TokenAuth.Hash(token);




        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try { _store.SaveDevice(spaceId, device); }
        finally { gate.Release(); }

        return SyncResult<NewDeviceCredentials>.Ok(new NewDeviceCredentials(device.DeviceId, token));
    }






    public const string EditorDeviceId = SyncApi.EditorDeviceId;





    public SyncResult<NewDeviceCredentials> IssueEditorDevice(SpaceRecord space)
    {
        ArgumentNullException.ThrowIfNull(space);

        var gate = _locks.GetOrAdd(space.SpaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {



            var device = _store.GetDevice(space.SpaceId, EditorDeviceId) ?? new DeviceRecord
            {
                DeviceId = EditorDeviceId,
                Name = "Web Editor",
                CreatedAt = DateTime.UtcNow,
            };

            var token = TokenAuth.NewToken();
            device.TokenHash = TokenAuth.Hash(token);
            device.Kind = DeviceKind.Editor;
            device.Revoked = false;
            _store.SaveDevice(space.SpaceId, device);

            return SyncResult<NewDeviceCredentials>.Ok(new NewDeviceCredentials(EditorDeviceId, token));
        }
        finally { gate.Release(); }
    }


    public SyncResult<DeviceRecord> Authenticate(string spaceId, string? deviceId, string? token)
    {
        if (_limiter.IsBlocked("auth:" + spaceId))
            return SyncResult<DeviceRecord>.Fail(SyncErrorCode.RateLimited, "too many failed attempts");

        var space = _store.GetSpace(spaceId);
        if (space is null) return SyncResult<DeviceRecord>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

        var device = string.IsNullOrEmpty(deviceId) ? null : _store.GetDevice(spaceId, deviceId);
        if (device is null || !TokenAuth.Verify(token, device.TokenHash))
        {
            _limiter.RecordFailure("auth:" + spaceId);
            return SyncResult<DeviceRecord>.Fail(SyncErrorCode.Unauthenticated, "device token is not valid");
        }

        if (device.Revoked)
        {
            _limiter.RecordFailure("auth:" + spaceId);
            return SyncResult<DeviceRecord>.Fail(SyncErrorCode.Forbidden, "device has been revoked");
        }

        _limiter.Reset("auth:" + spaceId);









        var seenAt = DateTime.UtcNow;
        device.LastSeenAt = seenAt;
        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try { _store.TouchDevice(spaceId, device.DeviceId, seenAt); }
        finally { gate.Release(); }
        return SyncResult<DeviceRecord>.Ok(device);
    }




    public SyncResult<SpaceRecord> Authorize(string spaceId, string? deviceId, string? token)
    {
        var auth = Authenticate(spaceId, deviceId, token);
        if (!auth.Success) return SyncResult<SpaceRecord>.Fail(auth.Error, auth.Message, auth.CurrentVersion);

        var space = _store.GetSpace(spaceId);
        return space is null
            ? SyncResult<SpaceRecord>.Fail(SyncErrorCode.SpaceNotFound, "space not found")
            : SyncResult<SpaceRecord>.Ok(space);
    }





    public SyncResult<SpaceRecord> AuthorizeRead(string spaceId, string? token, string? callerAddress = null)
    {








        var key = "read:" + spaceId + ":" + (callerAddress ?? "unknown");
        if (_limiter.IsBlocked(key))
            return SyncResult<SpaceRecord>.Fail(SyncErrorCode.RateLimited, "too many failed attempts");

        var space = _store.GetSpace(spaceId);



        if (space is null || !TokenAuth.Verify(TokenAuth.NormalizeReadToken(token), space.ReadTokenHash))
        {
            _limiter.RecordFailure(key);
            return SyncResult<SpaceRecord>.Fail(SyncErrorCode.Unauthenticated, "the read token is not valid");
        }

        _limiter.Reset(key);
        return SyncResult<SpaceRecord>.Ok(space);
    }





    public SyncResult<string> IssueReadToken(SpaceRecord space)
    {
        ArgumentNullException.ThrowIfNull(space);

        var token = TokenAuth.NewReadToken();
        var gate = _locks.GetOrAdd(space.SpaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {




            var fresh = _store.GetSpace(space.SpaceId);
            if (fresh is null) return SyncResult<string>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

            fresh.ReadTokenHash = TokenAuth.Hash(token);
            fresh.UpdatedAt = DateTime.UtcNow;
            _store.SaveSpace(fresh);
            return SyncResult<string>.Ok(token);
        }
        finally { gate.Release(); }
    }


    public SyncResult<bool> RevokeReadToken(SpaceRecord space)
    {
        ArgumentNullException.ThrowIfNull(space);

        var gate = _locks.GetOrAdd(space.SpaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var fresh = _store.GetSpace(space.SpaceId);
            if (fresh is null) return SyncResult<bool>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

            fresh.ReadTokenHash = "";
            fresh.UpdatedAt = DateTime.UtcNow;
            _store.SaveSpace(fresh);
            return SyncResult<bool>.Ok(true);
        }
        finally { gate.Release(); }
    }

    public SyncResult<SpaceInfo> GetInfo(SpaceRecord space)
        => SyncResult<SpaceInfo>.Ok(new SpaceInfo
        {
            SpaceId = space.SpaceId,
            Version = space.CurrentVersion,
            KeyWrapVersion = space.KeyWrapVersion,
            UpdatedAt = space.UpdatedAt,
            SizeBytes = _store.GetVersions(space.SpaceId).Where(v => v.Version == space.CurrentVersion).Sum(v => v.Size),
            QuotaUsedBytes = RetentionPolicy.UsedBytes(_store.GetVersions(space.SpaceId)),
            QuotaMaxBytes = space.QuotaBytes,
            MaxVersions = space.MaxVersions,
            HasReadToken = !string.IsNullOrEmpty(space.ReadTokenHash),
            HasEditorDevice = _store.GetDevice(space.SpaceId, EditorDeviceId) is { Revoked: false },
        });


    public SyncResult<StoredEnvelope> GetData(SpaceRecord space, long version = 0)
    {
        var versions = _store.GetVersions(space.SpaceId);
        var target = version <= 0 ? space.CurrentVersion : version;
        if (target <= 0) return SyncResult<StoredEnvelope>.Fail(SyncErrorCode.NotFound, "the space has no data yet");

        var row = versions.FirstOrDefault(v => v.Version == target);
        if (row is null || !row.Retained) return SyncResult<StoredEnvelope>.Fail(SyncErrorCode.NotFound, "version not available");

        var blob = _store.ReadBlob(space.SpaceId, row);
        if (blob is null) return SyncResult<StoredEnvelope>.Fail(SyncErrorCode.NotFound, "version blob is missing");

        return SyncResult<StoredEnvelope>.Ok(new StoredEnvelope(System.Text.Encoding.UTF8.GetString(blob), row));
    }

    public SyncResult<IReadOnlyList<VersionInfo>> ListVersions(SpaceRecord space)
        => SyncResult<IReadOnlyList<VersionInfo>>.Ok(_store.GetVersions(space.SpaceId)
            .OrderByDescending(v => v.Version)
            .Select(v => new VersionInfo
            {
                Version = v.Version,
                CreatedAt = v.CreatedAt,
                DeviceId = v.DeviceId,
                Size = v.Size,
                IsConflict = v.IsConflict,
                ConflictOf = v.ConflictOf,
                Retained = v.Retained,
            })
            .ToList());

    public SyncResult<IReadOnlyList<DeviceInfo>> ListDevices(string spaceId)
        => SyncResult<IReadOnlyList<DeviceInfo>>.Ok(_store.GetDevices(spaceId)
            .Select(d => new DeviceInfo
            {
                DeviceId = d.DeviceId,
                Name = d.Name,
                CreatedAt = d.CreatedAt,
                LastSeenAt = d.LastSeenAt,
                Revoked = d.Revoked,
                Kind = d.Kind == DeviceKind.Editor ? "editor" : "standard",
            })
            .ToList());










    public SyncResult<long> PutData(SpaceRecord space, DeviceRecord device, string envelopeJson, long baseVersion, bool force)
    {
        if (envelopeJson.Length > _options.MaxPayloadBytes)
            return SyncResult<long>.FailSized(SyncErrorCode.PayloadTooLarge,
                "payload exceeds the server limit", _options.MaxPayloadBytes);

        SyncEnvelope envelope;
        try { envelope = SyncEnvelopeCodec.Deserialize(envelopeJson); }
        catch (InvalidDataException e)
        {
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, e.Message);
        }

        if (!string.Equals(envelope.Space, space.SpaceId, StringComparison.Ordinal))
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, "envelope space does not match the request path");
        if (!string.Equals(envelope.Device, device.DeviceId, StringComparison.Ordinal))
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, "envelope device does not match the authenticated device");

        var gate = _locks.GetOrAdd(space.SpaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var fresh = _store.GetSpace(space.SpaceId);
            if (fresh is null) return SyncResult<long>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

            var versions = _store.GetVersions(space.SpaceId).ToList();






            var highestStored = versions.Count == 0 ? 0 : versions.Max(v => v.Version);
            if (fresh.CurrentVersion < highestStored)
            {
                fresh.CurrentVersion = highestStored;
                fresh.UpdatedAt = DateTime.UtcNow;
                _store.SaveSpace(fresh);
            }

            if (baseVersion != fresh.CurrentVersion)
                return SyncResult<long>.Fail(SyncErrorCode.VersionConflict,
                    "base version is stale; re-base and retry", fresh.CurrentVersion);

            if (envelope.Base != baseVersion || envelope.Version != baseVersion + 1)
                return SyncResult<long>.Fail(SyncErrorCode.BadRequest,
                    "envelope version must be base + 1", fresh.CurrentVersion);

            var payload = System.Text.Encoding.UTF8.GetBytes(envelopeJson);
            var newVersion = envelope.Version;

            var row = new VersionRecord
            {
                Version = newVersion,
                CreatedAt = DateTime.UtcNow,
                DeviceId = device.DeviceId,
                Sha256 = envelope.PayloadSha256,
                Size = payload.Length,
                Retained = true,
            };






            var projected = versions.Select(v => new VersionRecord
            {
                Version = v.Version,
                Size = v.Size,
                Retained = v.Retained,


                IsConflict = force && v.Version == fresh.CurrentVersion ? true : v.IsConflict,
                ConflictOf = force && v.Version == fresh.CurrentVersion ? newVersion : v.ConflictOf,
            }).ToList();
            projected.Add(row);
            var planned = RetentionPolicy.PlanEviction(projected, newVersion, fresh.MaxVersions, fresh.QuotaBytes);
            var bytesAfterRotation = projected.Where(v => v.Retained && !planned.Contains(v)).Sum(v => v.Size);
            if (fresh.QuotaBytes > 0 && bytesAfterRotation > fresh.QuotaBytes)
                return SyncResult<long>.FailSized(SyncErrorCode.QuotaExceeded,
                    "space quota exceeded", fresh.QuotaBytes, bytesAfterRotation, fresh.CurrentVersion);

            if (force)
            {
                foreach (var previous in versions.Where(v => v.Version == fresh.CurrentVersion))
                {





                    var preservedPayload = previous.IsConflict ? null : _store.ReadBlob(space.SpaceId, previous);
                    previous.IsConflict = true;
                    previous.ConflictOf = newVersion;

                    if (preservedPayload is not null)
                    {
                        _store.WriteBlob(space.SpaceId, previous, preservedPayload);
                        _store.DeleteBlob(space.SpaceId, new VersionRecord { Version = previous.Version });
                    }
                }
            }

            versions.Add(row);
            _store.WriteBlob(space.SpaceId, row, payload);

            foreach (var victim in RetentionPolicy.PlanEviction(versions, newVersion, fresh.MaxVersions, fresh.QuotaBytes))
            {
                _store.DeleteBlob(space.SpaceId, victim);
                victim.Retained = false;
            }

            _store.SaveVersions(space.SpaceId, versions);
            fresh.CurrentVersion = newVersion;
            fresh.UpdatedAt = DateTime.UtcNow;
            _store.SaveSpace(fresh);

            return SyncResult<long>.Ok(newVersion);
        }
        finally { gate.Release(); }
    }

    public SyncResult<bool> RevokeDevice(string spaceId, string deviceId)
    {
        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {


            var device = _store.GetDevice(spaceId, deviceId);
            if (device is null) return SyncResult<bool>.Fail(SyncErrorCode.NotFound, "device not found");

            device.Revoked = true;
            _store.SaveDevice(spaceId, device);
            return SyncResult<bool>.Ok(true);
        }
        finally { gate.Release(); }
    }


    public SyncResult<NewDeviceCredentials> ResetToken(string spaceId, string deviceId)
    {
        var gate = _locks.GetOrAdd(spaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var device = _store.GetDevice(spaceId, deviceId);
            if (device is null) return SyncResult<NewDeviceCredentials>.Fail(SyncErrorCode.NotFound, "device not found");




            if (device.Revoked)
                return SyncResult<NewDeviceCredentials>.Fail(SyncErrorCode.Forbidden,
                    "device has been revoked; pair it again instead");

            var token = TokenAuth.NewToken();
            device.TokenHash = TokenAuth.Hash(token);
            _store.SaveDevice(spaceId, device);
            return SyncResult<NewDeviceCredentials>.Ok(new NewDeviceCredentials(device.DeviceId, token));
        }
        finally { gate.Release(); }
    }




    public SyncResult<string> GetKeyWrap(SpaceRecord space)
        => string.IsNullOrEmpty(space.KeyWrapJson)
            ? SyncResult<string>.Fail(SyncErrorCode.NotFound, "the space has no keywrap record yet")
            : SyncResult<string>.Ok(space.KeyWrapJson);





    public SyncResult<long> PutKeyWrap(SpaceRecord space, string keyWrapJson, long ifMatchVersion)
    {


        if (keyWrapJson.Length > _options.MaxPayloadBytes)
            return SyncResult<long>.Fail(SyncErrorCode.PayloadTooLarge, "keywrap record exceeds the server limit");

        SyncKeyWrapRecord record;
        try { record = SyncJson.DeserializeKeyWrap(keyWrapJson); }
        catch (InvalidDataException e) { return SyncResult<long>.Fail(SyncErrorCode.BadRequest, e.Message); }

        if (record.Wrap != SyncKeyWrap.WrapVersion)
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, $"unsupported keywrap version: {record.Wrap}");
        if (!string.Equals(record.Kdf, SyncKeyWrap.KdfName, StringComparison.Ordinal))
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, $"unsupported keywrap kdf: {record.Kdf}");
        if (record.Iter < 1000 || record.Iter > 5_000_000)
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, "keywrap iteration count out of range");
        if (string.IsNullOrEmpty(record.Salt) || string.IsNullOrEmpty(record.Nonce) || string.IsNullOrEmpty(record.Ct))
            return SyncResult<long>.Fail(SyncErrorCode.BadRequest, "keywrap record is incomplete");

        var gate = _locks.GetOrAdd(space.SpaceId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            var fresh = _store.GetSpace(space.SpaceId);
            if (fresh is null) return SyncResult<long>.Fail(SyncErrorCode.SpaceNotFound, "space not found");

            if (ifMatchVersion != fresh.KeyWrapVersion)
                return SyncResult<long>.Fail(SyncErrorCode.VersionConflict, "keywrap version is stale", fresh.KeyWrapVersion);

            fresh.KeyWrapJson = keyWrapJson;
            fresh.KeyWrapVersion = fresh.KeyWrapVersion + 1;
            fresh.UpdatedAt = DateTime.UtcNow;
            _store.SaveSpace(fresh);
            return SyncResult<long>.Ok(fresh.KeyWrapVersion);
        }
        finally { gate.Release(); }
    }
}
