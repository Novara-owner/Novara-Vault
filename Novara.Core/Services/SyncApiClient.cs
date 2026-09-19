using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Novara.Models;

namespace Novara.Services;


public sealed class SyncApiResult<T>
{
    private SyncApiResult(bool success, T? value, SyncErrorCode? error, string message, long? currentVersion, int status,
        long? limitBytes = null, long? usedBytes = null)
    {
        Success = success;
        Value = value;
        Error = error;
        Message = message;
        CurrentVersion = currentVersion;
        HttpStatus = status;
        LimitBytes = limitBytes;
        UsedBytes = usedBytes;
    }

    public bool Success { get; }
    public T? Value { get; }
    public SyncErrorCode? Error { get; }
    public string Message { get; }
    public long? CurrentVersion { get; }
    public int HttpStatus { get; }






    public long? LimitBytes { get; }


    public long? UsedBytes { get; }


    public bool TransportFailure => !Success && Error is null;

    public static SyncApiResult<T> Ok(T value, int status = 200)
        => new(true, value, null, "", null, status);

    public static SyncApiResult<T> Fail(SyncErrorCode error, string message, long? currentVersion, int status,
        long? limitBytes = null, long? usedBytes = null)
        => new(false, default, error, message, currentVersion, status, limitBytes, usedBytes);

    public static SyncApiResult<T> Unreachable(string message)
        => new(false, default, null, message, null, 0);
}


public sealed record RemoteEnvelope(string Json, long Version);


public sealed record RemoteKeyWrap(string Json, long Version);







public sealed class SyncApiClient : IDisposable
{


    private const string SpaceHeader = SyncApi.SpaceHeader;
    private const string DeviceHeader = SyncApi.DeviceHeader;
    private const string VersionHeader = SyncApi.VersionHeader;
    private const string KeyWrapVersionHeader = SyncApi.KeyWrapVersionHeader;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _baseUrl;
    private readonly string _spaceId;
    private readonly string? _deviceId;
    private readonly string? _deviceToken;
    private readonly TimeSpan _timeout;





    public SyncApiClient(
        string baseUrl,
        string spaceId,
        string? deviceId = null,
        string? deviceToken = null,
        HttpClient? httpClient = null,
        TimeSpan? timeout = null)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _spaceId = string.IsNullOrWhiteSpace(spaceId) ? throw new ArgumentException("space id is required", nameof(spaceId)) : spaceId;
        _deviceId = deviceId;
        _deviceToken = deviceToken;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient();
    }

    public string BaseUrl => _baseUrl;
    public string SpaceId => _spaceId;














    public static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("server url is required", nameof(baseUrl));
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("server url must be absolute, e.g. https://novara.example.com", nameof(baseUrl));

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("server url must use https (or http on loopback)", nameof(baseUrl));
        if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri))
            throw new ArgumentException("plain http is only allowed on loopback; use https for remote servers", nameof(baseUrl));

        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).TrimEnd('/');
    }

    private static bool IsLoopback(Uri uri)
        => uri.IsLoopback ||
           uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);



    public Task<SyncApiResult<DeviceRegistrationResponse>> RegisterDeviceAsync(
        string enrollmentSecret, string deviceName, CancellationToken ct = default)
        => SendAsync<DeviceRegistrationResponse>(
            HttpMethod.Post,
            $"{SyncApi.ApiPrefix}/devices/register",
            new DeviceRegistrationRequest { SpaceId = _spaceId, EnrollmentSecret = enrollmentSecret, DeviceName = deviceName },
            authenticated: false,
            ct);



    public Task<SyncApiResult<SpaceInfo>> GetInfoAsync(CancellationToken ct = default)
        => SendAsync<SpaceInfo>(HttpMethod.Get, $"{SyncApi.ApiPrefix}/space/info", null, authenticated: true, ct);


    public async Task<SyncApiResult<RemoteEnvelope>> GetDataAsync(long version = 0, CancellationToken ct = default)
    {
        var path = $"{SyncApi.ApiPrefix}/space/data" + (version > 0 ? $"?version={version.ToString(CultureInfo.InvariantCulture)}" : "");
        var (reply, failure) = await ExecuteAsync(HttpMethod.Get, path, null, authenticated: true, ct).ConfigureAwait(false);
        if (failure is not null) return failure.To<RemoteEnvelope>();




        var reported = TryParseLong(reply!.Header(VersionHeader));
        if (reported is null)
            return SyncApiResult<RemoteEnvelope>.Fail(SyncErrorCode.BadRequest,
                "the sync server did not report its version", null, reply.Status);
        return SyncApiResult<RemoteEnvelope>.Ok(new RemoteEnvelope(reply.Body, reported.Value), reply.Status);
    }


    public async Task<SyncApiResult<long>> PutDataAsync(string envelopeJson, long baseVersion, bool force = false, CancellationToken ct = default)
    {
        var path = $"{SyncApi.ApiPrefix}/space/data" + (force ? "?force=1" : "");
        var (reply, failure) = await ExecuteAsync(HttpMethod.Put, path, envelopeJson, authenticated: true, ct, baseVersion).ConfigureAwait(false);
        if (failure is not null) return failure.To<long>();




        var version = ReadLongProperty(reply!.Body, "version");
        if (version is null)
            return SyncApiResult<long>.Fail(SyncErrorCode.BadRequest,
                "the sync server accepted the push but did not report the new version", null, reply.Status);
        return SyncApiResult<long>.Ok(version.Value, reply.Status);
    }

    public Task<SyncApiResult<List<VersionInfo>>> ListVersionsAsync(CancellationToken ct = default)
        => SendAsync<List<VersionInfo>>(HttpMethod.Get, $"{SyncApi.ApiPrefix}/space/versions", null, authenticated: true, ct);



    public async Task<SyncApiResult<RemoteKeyWrap>> GetKeyWrapAsync(CancellationToken ct = default)
    {
        var (reply, failure) = await ExecuteAsync(HttpMethod.Get, $"{SyncApi.ApiPrefix}/space/keywrap", null, authenticated: true, ct).ConfigureAwait(false);
        if (failure is not null) return failure.To<RemoteKeyWrap>();



        var version = TryParseLong(reply!.Header(KeyWrapVersionHeader));
        if (version is null)
            return SyncApiResult<RemoteKeyWrap>.Fail(SyncErrorCode.BadRequest,
                "the sync server did not report the keywrap version", null, reply.Status);
        return SyncApiResult<RemoteKeyWrap>.Ok(new RemoteKeyWrap(reply.Body, version.Value), reply.Status);
    }


    public async Task<SyncApiResult<long>> PutKeyWrapAsync(string keyWrapJson, long ifMatchVersion, CancellationToken ct = default)
    {
        var (reply, failure) = await ExecuteAsync(HttpMethod.Put, $"{SyncApi.ApiPrefix}/space/keywrap", keyWrapJson, authenticated: true, ct, ifMatchVersion).ConfigureAwait(false);
        if (failure is not null) return failure.To<long>();

        var version = ReadLongProperty(reply!.Body, "keyWrapVersion");
        if (version is null)
            return SyncApiResult<long>.Fail(SyncErrorCode.BadRequest,
                "the sync server accepted the keywrap but did not report its version", null, reply.Status);
        return SyncApiResult<long>.Ok(version.Value, reply.Status);
    }



    public Task<SyncApiResult<List<DeviceInfo>>> ListDevicesAsync(CancellationToken ct = default)
        => SendAsync<List<DeviceInfo>>(HttpMethod.Get, $"{SyncApi.ApiPrefix}/devices", null, authenticated: true, ct);

    public async Task<SyncApiResult<bool>> RevokeDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var path = $"{SyncApi.ApiPrefix}/devices/{Uri.EscapeDataString(deviceId)}";
        var (reply, failure) = await ExecuteAsync(HttpMethod.Delete, path, null, authenticated: true, ct).ConfigureAwait(false);
        return failure is not null ? failure.To<bool>() : SyncApiResult<bool>.Ok(true, reply!.Status);
    }

    public Task<SyncApiResult<DeviceRegistrationResponse>> ResetTokenAsync(string deviceId, CancellationToken ct = default)
        => SendAsync<DeviceRegistrationResponse>(
            HttpMethod.Post,
            $"{SyncApi.ApiPrefix}/devices/{Uri.EscapeDataString(deviceId)}/reset-token",
            null,
            authenticated: true,
            ct);







    public async Task<SyncApiResult<string>> IssueReadTokenAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<ReadTokenResponse>(
            HttpMethod.Post, $"{SyncApi.ApiPrefix}/space/read-token", null, authenticated: true, ct).ConfigureAwait(false);

        if (!result.Success)
            return result.TransportFailure
                ? SyncApiResult<string>.Unreachable(result.Message)
                : SyncApiResult<string>.Fail(result.Error!.Value, result.Message, result.CurrentVersion, result.HttpStatus);

        return SyncApiResult<string>.Ok(result.Value!.ReadToken, result.HttpStatus);
    }


    public async Task<SyncApiResult<bool>> RevokeReadTokenAsync(CancellationToken ct = default)
    {
        var (reply, failure) = await ExecuteAsync(HttpMethod.Delete, $"{SyncApi.ApiPrefix}/space/read-token", null, authenticated: true, ct).ConfigureAwait(false);
        return failure is not null ? failure.To<bool>() : SyncApiResult<bool>.Ok(true, reply!.Status);
    }






    public async Task<SyncApiResult<DeviceRegistrationResponse>> IssueEditorDeviceAsync(CancellationToken ct = default)
    {
        var result = await SendAsync<DeviceRegistrationResponse>(
            HttpMethod.Post, $"{SyncApi.ApiPrefix}/space/editor-device", null, authenticated: true, ct).ConfigureAwait(false);

        if (!result.Success)
            return result.TransportFailure
                ? SyncApiResult<DeviceRegistrationResponse>.Unreachable(result.Message)
                : SyncApiResult<DeviceRegistrationResponse>.Fail(result.Error!.Value, result.Message, result.CurrentVersion, result.HttpStatus);

        return SyncApiResult<DeviceRegistrationResponse>.Ok(result.Value!, result.HttpStatus);
    }


    public async Task<SyncApiResult<bool>> RevokeEditorDeviceAsync(CancellationToken ct = default)
    {
        var (reply, failure) = await ExecuteAsync(HttpMethod.Delete, $"{SyncApi.ApiPrefix}/devices/{SyncApi.EditorDeviceId}", null, authenticated: true, ct).ConfigureAwait(false);
        return failure is not null ? failure.To<bool>() : SyncApiResult<bool>.Ok(true, reply!.Status);
    }




    private sealed record Reply(int Status, string Body, HttpResponseHeaders Headers)
    {
        public string? Header(string name) => Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    private sealed record Failure(SyncErrorCode? Error, string Message, long? CurrentVersion, int Status,
        long? LimitBytes = null, long? UsedBytes = null)
    {
        public SyncApiResult<T> To<T>() => Error is null
            ? SyncApiResult<T>.Unreachable(Message)
            : SyncApiResult<T>.Fail(Error.Value, Message, CurrentVersion, Status, LimitBytes, UsedBytes);
    }

    private async Task<SyncApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? payload, bool authenticated, CancellationToken ct)
    {
        var body = payload is null ? null : JsonSerializer.Serialize(payload, JsonOptions);
        var (reply, failure) = await ExecuteAsync(method, path, body, authenticated, ct).ConfigureAwait(false);
        if (failure is not null) return failure.To<T>();
        if (string.IsNullOrWhiteSpace(reply!.Body)) return SyncApiResult<T>.Ok(default!, reply.Status);

        try
        {
            var value = JsonSerializer.Deserialize<T>(reply.Body, JsonOptions);
            return value is null
                ? SyncApiResult<T>.Fail(SyncErrorCode.BadRequest, "the sync server returned an empty body", null, reply.Status)
                : SyncApiResult<T>.Ok(value, reply.Status);
        }
        catch (JsonException)
        {
            return SyncApiResult<T>.Fail(SyncErrorCode.BadRequest, "the sync server returned an unreadable body", null, reply.Status);
        }
    }

    private async Task<(Reply? Reply, Failure? Failure)> ExecuteAsync(
        HttpMethod method, string path, string? jsonBody, bool authenticated, CancellationToken ct, long? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, _baseUrl + path);

        if (authenticated)
        {
            if (string.IsNullOrEmpty(_deviceId) || string.IsNullOrEmpty(_deviceToken))
                return (null, new Failure(SyncErrorCode.Unauthenticated, "device credentials are missing", null, 0));

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);
            request.Headers.TryAddWithoutValidation(SpaceHeader, _spaceId);
            request.Headers.TryAddWithoutValidation(DeviceHeader, _deviceId);
        }
        else
        {

            request.Headers.TryAddWithoutValidation(SpaceHeader, _spaceId);
        }

        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch.Value.ToString(CultureInfo.InvariantCulture));
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, new Failure(null, "the sync server did not respond in time", null, 0));
        }
        catch (HttpRequestException e)
        {
            return (null, new Failure(null, $"the sync server is unreachable: {e.Message}", null, 0));
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return (new Reply(status, body, response.Headers), null);
            return (null, ParseError(body, status));
        }
    }





    private static Failure ParseError(string body, int status)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<SyncErrorResponse>(body, JsonOptions);
                if (parsed is not null)
                {
                    var code = SyncErrorCodes.FromWire(parsed.Error) ?? CodeFromStatus(status);
                    return new Failure(code, parsed.Message, parsed.CurrentVersion, status,
                        parsed.LimitBytes, parsed.UsedBytes);
                }
            }
            catch (JsonException) {  }
        }

        return new Failure(CodeFromStatus(status), $"the sync server returned HTTP {status}", null, status);
    }

    private static SyncErrorCode? CodeFromStatus(int status) => status switch
    {
        400 => SyncErrorCode.BadRequest,
        401 => SyncErrorCode.Unauthenticated,
        403 => SyncErrorCode.Forbidden,
        404 => SyncErrorCode.NotFound,
        409 => SyncErrorCode.VersionConflict,
        413 => SyncErrorCode.PayloadTooLarge,
        429 => SyncErrorCode.RateLimited,
        507 => SyncErrorCode.QuotaExceeded,



        _ when status >= 500 => SyncErrorCode.ServerError,





        _ when status >= 400 => SyncErrorCode.BadRequest,
        _ => null,
    };

    private static long? TryParseLong(string? text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;






    private static long? ReadLongProperty(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetInt64(out var parsed)
                ? parsed
                : null;
        }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
