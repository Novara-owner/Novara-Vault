using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Novara.Models;
using Novara.Server;
using Novara.Services;
using Novara.Sync.Server;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;

public class SyncApiClientTests
{
    private static string SealedEnvelope(string spaceKey, string spaceId, string deviceId, long baseVersion, string payload = "{\"k\":\"v\"}")
    {
        var container = SyncContainer.Seal(Encoding.UTF8.GetBytes(payload), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Space = spaceId,
            Device = deviceId,
            Base = baseVersion,
            Version = baseVersion + 1,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTimeOffset.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return SyncEnvelopeCodec.Serialize(envelope);
    }


    private static async Task<(SyncApiClient Client, string DeviceId, string Token)> PairDeviceAsync(
        HostedSyncServer server, string spaceId, string enrollmentSecret, string deviceName)
    {
        using var pairing = new SyncApiClient(server.BaseUrl, spaceId);
        var registered = await pairing.RegisterDeviceAsync(enrollmentSecret, deviceName);
        Assert.True(registered.Success, registered.Message);

        return (new SyncApiClient(server.BaseUrl, spaceId, registered.Value!.DeviceId, registered.Value.DeviceToken),
            registered.Value.DeviceId, registered.Value.DeviceToken);
    }


    private static async Task<(SyncApiClient Client, string SpaceKey, string SpaceId, string DeviceId, string Token)>
        PairAsync(HostedSyncServer server, string deviceName = "pc")
    {
        var created = server.Service.CreateSpace("client-test");
        var spaceId = created.Value!.SpaceId;
        var spaceKey = SyncKeyWrap.CreateSpaceKey();

        var (client, deviceId, token) = await PairDeviceAsync(server, spaceId, created.Value.EnrollmentSecret, deviceName);
        return (client, spaceKey, spaceId, deviceId, token);
    }



    [Fact]
    public void ServerUrl_RequiresHttpsOffLoopback_AndAcceptsLoopbackHttp()
    {
        Assert.Equal("https://novara.example.com", SyncApiClient.NormalizeBaseUrl("https://novara.example.com/"));
        Assert.Equal("https://novara.example.com:8443", SyncApiClient.NormalizeBaseUrl("https://novara.example.com:8443/some/path"));
        Assert.Equal("http://127.0.0.1:5000", SyncApiClient.NormalizeBaseUrl("http://127.0.0.1:5000"));
        Assert.Equal("http://localhost:5000", SyncApiClient.NormalizeBaseUrl("http://localhost:5000"));

        Assert.Throws<ArgumentException>(() => SyncApiClient.NormalizeBaseUrl("http://novara.example.com"));
        Assert.Throws<ArgumentException>(() => SyncApiClient.NormalizeBaseUrl("ftp://novara.example.com"));
        Assert.Throws<ArgumentException>(() => SyncApiClient.NormalizeBaseUrl("not-a-url"));
        Assert.Throws<ArgumentException>(() => SyncApiClient.NormalizeBaseUrl(""));
        Assert.Throws<ArgumentException>(() => new SyncApiClient("https://novara.example.com", ""));
    }

    [Fact]
    public void ServerUrl_DropsUserInfo_SoAPastedCredentialNeverReachesTheStateFile()
    {





        Assert.Equal("https://novara.example.com:8443",
            SyncApiClient.NormalizeBaseUrl("https://alice:s3cr3t@novara.example.com:8443/some/path"));
        Assert.Equal("http://127.0.0.1:5000",
            SyncApiClient.NormalizeBaseUrl("http://alice:s3cr3t@127.0.0.1:5000"));
        Assert.Equal("https://novara.example.com",
            SyncApiClient.NormalizeBaseUrl("https://token-only@novara.example.com/"));

        Assert.Equal("https://novara.example.com:8443", SyncApiClient.NormalizeBaseUrl("https://novara.example.com:8443/"));
        Assert.Equal("http://[::1]:5000", SyncApiClient.NormalizeBaseUrl("http://[::1]:5000"));
        Assert.Equal("https://[2001:db8::1]:8443", SyncApiClient.NormalizeBaseUrl("https://[2001:db8::1]:8443/x"));
    }



    [Fact]
    public async Task RegisterDevice_SucceedsWithTheEnrollmentSecret_AndFailsWithout()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var created = server.Service.CreateSpace("client-test");

        using var client = new SyncApiClient(server.BaseUrl, created.Value!.SpaceId);
        var bad = await client.RegisterDeviceAsync("wrong-secret", "pc");
        Assert.False(bad.Success);
        Assert.Equal(SyncErrorCode.Forbidden, bad.Error);

        var good = await client.RegisterDeviceAsync(created.Value.EnrollmentSecret, "pc");
        Assert.True(good.Success, good.Message);
        Assert.False(string.IsNullOrEmpty(good.Value!.DeviceId));
        Assert.False(string.IsNullOrEmpty(good.Value.DeviceToken));
    }

    [Fact]
    public async Task Info_ReportsAnEmptySpace_AndRejectsABadToken()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, _, spaceId, _, token) = await PairAsync(server);

        var info = await client.GetInfoAsync();
        Assert.True(info.Success, info.Message);
        Assert.Equal(spaceId, info.Value!.SpaceId);
        Assert.Equal(0, info.Value.Version);
        Assert.Equal(500L * 1024 * 1024, info.Value.QuotaMaxBytes);
        Assert.Equal(10, info.Value.MaxVersions);

        using var impostor = new SyncApiClient(server.BaseUrl, spaceId, "device-unknown", token);
        var rejected = await impostor.GetInfoAsync();
        Assert.False(rejected.Success);
        Assert.Equal(SyncErrorCode.Unauthenticated, rejected.Error);
    }



    [Fact]
    public async Task PushAndPull_RoundTripsTheExactEnvelope_AndReportsConflicts()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceKey, spaceId, deviceId, _) = await PairAsync(server);

        var first = SealedEnvelope(spaceKey, spaceId, deviceId, 0);
        var pushed = await client.PutDataAsync(first, baseVersion: 0);
        Assert.True(pushed.Success, pushed.Message);
        Assert.Equal(1, pushed.Value);

        var pulled = await client.GetDataAsync();
        Assert.True(pulled.Success, pulled.Message);
        Assert.Equal(first, pulled.Value!.Json);
        Assert.Equal(1, pulled.Value.Version);

        var stale = await client.PutDataAsync(first, baseVersion: 0);
        Assert.False(stale.Success);
        Assert.Equal(SyncErrorCode.VersionConflict, stale.Error);
        Assert.Equal(1, stale.CurrentVersion);

        var second = SealedEnvelope(spaceKey, spaceId, deviceId, 1, "{\"round\":2}");
        Assert.Equal(2, (await client.PutDataAsync(second, baseVersion: 1)).Value);

        var versions = await client.ListVersionsAsync();
        Assert.True(versions.Success);
        Assert.Equal(new long[] { 2, 1 }, versions.Value!.Select(v => v.Version).ToArray());

        var historic = await client.GetDataAsync(version: 1);
        Assert.Equal(first, historic.Value!.Json);

        var unknown = await client.GetDataAsync(version: 99);
        Assert.Equal(SyncErrorCode.NotFound, unknown.Error);
    }

    [Fact]
    public async Task ForcePush_IsAccepted_AndMarksTheReplacedVersion()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceKey, spaceId, deviceId, _) = await PairAsync(server);

        await client.PutDataAsync(SealedEnvelope(spaceKey, spaceId, deviceId, 0), baseVersion: 0);
        var forced = await client.PutDataAsync(SealedEnvelope(spaceKey, spaceId, deviceId, 1, "{\"mine\":1}"), baseVersion: 1, force: true);

        Assert.True(forced.Success, forced.Message);
        var versions = await client.ListVersionsAsync();
        Assert.True(versions.Value!.Single(v => v.Version == 1).IsConflict);
        Assert.Equal(2, versions.Value!.Single(v => v.Version == 1).ConflictOf);
    }

    [Fact]
    public async Task Push_RejectsAMalformedEnvelope_WithAProtocolError()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, _, _, _, _) = await PairAsync(server);

        var result = await client.PutDataAsync("not json", baseVersion: 0);
        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.BadRequest, result.Error);
        Assert.False(result.TransportFailure);
        Assert.Contains("JSON", result.Message);
    }



    [Fact]
    public async Task KeyWrap_CanBeUploadedFetched_AndStaysIfMatchGuarded()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceKey, _, _, _) = await PairAsync(server);

        var missing = await client.GetKeyWrapAsync();
        Assert.Equal(SyncErrorCode.NotFound, missing.Error);

        var json = SyncJson.SerializeKeyWrap(SyncKeyWrap.Wrap(spaceKey, "lock-pw"));
        var stored = await client.PutKeyWrapAsync(json, ifMatchVersion: 0);
        Assert.True(stored.Success, stored.Message);
        Assert.Equal(1, stored.Value);

        var fetched = await client.GetKeyWrapAsync();
        Assert.True(fetched.Success);
        Assert.Equal(json, fetched.Value!.Json);
        Assert.Equal(1, fetched.Value.Version);
        Assert.Equal(spaceKey, SyncKeyWrap.Unwrap(SyncJson.DeserializeKeyWrap(fetched.Value.Json), "lock-pw"));

        var stale = await client.PutKeyWrapAsync(json, ifMatchVersion: 0);
        Assert.Equal(SyncErrorCode.VersionConflict, stale.Error);
    }



    [Fact]
    public async Task DeviceAdministration_ListsRevokesAndResets()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var created = server.Service.CreateSpace("client-test");
        var spaceId = created.Value!.SpaceId;
        var enrollment = created.Value.EnrollmentSecret;



        var pc = await PairDeviceAsync(server, spaceId, enrollment, "pc");
        var phone = await PairDeviceAsync(server, spaceId, enrollment, "phone");

        var devices = await pc.Client.ListDevicesAsync();
        Assert.True(devices.Success);
        Assert.Equal(2, devices.Value!.Count);
        var listed = Assert.Single(devices.Value!, d => d.DeviceId == phone.DeviceId);
        Assert.False(listed.Revoked);
        Assert.Null(listed.LastSeenAt);
        Assert.NotNull(Assert.Single(devices.Value!, d => d.DeviceId == pc.DeviceId).LastSeenAt);


        var reset = await pc.Client.ResetTokenAsync(phone.DeviceId);
        Assert.True(reset.Success, reset.Message);
        Assert.NotEqual(phone.Token, reset.Value!.DeviceToken);
        using var rotated = new SyncApiClient(server.BaseUrl, spaceId, phone.DeviceId, reset.Value.DeviceToken);
        Assert.True((await rotated.GetInfoAsync()).Success);
        Assert.Equal(SyncErrorCode.Unauthenticated, (await phone.Client.GetInfoAsync()).Error);



        Assert.True((await pc.Client.RevokeDeviceAsync(phone.DeviceId)).Success);
        Assert.Equal(SyncErrorCode.Forbidden, (await rotated.GetInfoAsync()).Error);
        Assert.True(Assert.Single((await pc.Client.ListDevicesAsync()).Value!, d => d.DeviceId == phone.DeviceId).Revoked);

        Assert.False((await pc.Client.ResetTokenAsync(phone.DeviceId)).Success);
        Assert.True(Assert.Single((await pc.Client.ListDevicesAsync()).Value!, d => d.DeviceId == phone.DeviceId).Revoked);
        Assert.Equal(SyncErrorCode.Forbidden, (await rotated.GetInfoAsync()).Error);

        Assert.Equal(SyncErrorCode.NotFound, (await pc.Client.RevokeDeviceAsync("no-such-device")).Error);
    }



    [Fact]
    public async Task UnreachableServer_IsReportedAsATransportFailure_NotAsAProtocolError()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var deadPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var client = new SyncApiClient($"http://127.0.0.1:{deadPort}", "space", "device", "token", timeout: TimeSpan.FromSeconds(3));
        var result = await client.GetInfoAsync();

        Assert.False(result.Success);
        Assert.True(result.TransportFailure);
        Assert.Null(result.Error);
        Assert.False(string.IsNullOrEmpty(result.Message));
    }

    [Fact]
    public async Task MissingDeviceCredentials_FailWithoutTouchingTheNetwork()
    {
        using var client = new SyncApiClient("https://novara.invalid", "space");
        var result = await client.GetInfoAsync();

        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.Unauthenticated, result.Error);
        Assert.False(result.TransportFailure);
    }







    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly (string Name, string Value)? _header;

        public StubHandler(HttpStatusCode status, string body, (string Name, string Value)? header = null)
        {
            _status = status;
            _body = body;
            _header = header;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
            if (_header is { } header) response.Headers.Add(header.Name, header.Value);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ServerError5xx_IsAProtocolFailure_NotATransportFailure()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "<html>boom</html>"));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.GetInfoAsync();



        Assert.False(result.Success);
        Assert.False(result.TransportFailure);
        Assert.Equal(SyncErrorCode.ServerError, result.Error);
        Assert.Equal(500, result.HttpStatus);
        Assert.Contains("500", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    [InlineData(HttpStatusCode.UnavailableForLegalReasons)]
    public async Task Unmapped4xx_IsAProtocolFailure_NotATransportFailure(HttpStatusCode status)
    {



        using var http = new HttpClient(new StubHandler(status, "<html>nope</html>"));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.GetInfoAsync();

        Assert.False(result.Success);
        Assert.False(result.TransportFailure);
        Assert.Equal(SyncErrorCode.BadRequest, result.Error);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Contains(((int)status).ToString(), result.Message);
    }

    [Fact]
    public async Task GetData_WithoutVersionHeader_FailsInsteadOfDegradingToZero()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"v\":1}"));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.GetDataAsync();



        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.BadRequest, result.Error);
        Assert.Contains("version", result.Message);
    }

    [Fact]
    public async Task PutData_WithoutVersionInBody_FailsInsteadOfDegradingToZero()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.PutDataAsync("{}", baseVersion: 1);

        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.BadRequest, result.Error);
        Assert.Contains("version", result.Message);
    }

    [Fact]
    public async Task GetKeyWrap_WithoutVersionHeader_FailsInsteadOfDegradingToZero()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"wrap\":1}"));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.GetKeyWrapAsync();



        Assert.False(result.Success);
        Assert.Equal(SyncErrorCode.BadRequest, result.Error);
        Assert.Contains("version", result.Message);
    }

    [Fact]
    public async Task GetKeyWrap_WithVersionHeader_StillParses()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"wrap\":1}", ("X-Novara-Keywrap-Version", "3")));
        using var client = new SyncApiClient("https://novara.example.com", "space", "device", "token", httpClient: http);

        var result = await client.GetKeyWrapAsync();

        Assert.True(result.Success);
        Assert.Equal(3, result.Value!.Version);
    }
}
