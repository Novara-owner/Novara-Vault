using System.Net;
using System.Text;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server.Security;
using Xunit;

namespace Novara.Tests;




public class SyncReadTokenTests
{
    private const string ReadScheme = "Novara-Read";
    private const string SpaceHeader = "X-Novara-Space";
    private const string DeviceHeader = "X-Novara-Device";


    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";



    [Fact]
    public void ReadToken_Is24CrockfordChars_AndNeverRepeats()
    {
        var a = TokenAuth.NewReadToken();
        var b = TokenAuth.NewReadToken();

        Assert.Equal(24, a.Length);
        Assert.All(a, c => Assert.Contains(c, Crockford));
        Assert.NotEqual(a, b);

        Assert.DoesNotContain(a, c => c is 'I' or 'L' or 'O' or 'U');
    }

    [Fact]
    public void NormalizeReadToken_FoldsSeparatorsCaseAndLookAlikes()
    {
        const string canonical = "ABCDEFGHJKMNPQRSTVWYZ012";

        Assert.Equal(canonical, TokenAuth.NormalizeReadToken("abcdefghjkmnpqrstvwyz012"));
        Assert.Equal(canonical, TokenAuth.NormalizeReadToken("abcd-efgh-jkmn-pqrs-tvwy-zO12"));
        Assert.Equal(canonical, TokenAuth.NormalizeReadToken("ABCD EFGH JKMN PQRS TVWY Z012"));
        Assert.Equal(canonical, TokenAuth.NormalizeReadToken("ABCDEFGHJKMNPQRSTVWYZO12"));
        Assert.Equal("", TokenAuth.NormalizeReadToken(null));
        Assert.Equal("", TokenAuth.NormalizeReadToken(""));
    }

    [Fact]
    public void NormalizeReadToken_MapsBothAmbiguousLettersToTheSameCanonicalToken()
    {

        Assert.Equal(TokenAuth.NormalizeReadToken("ABC1"), TokenAuth.NormalizeReadToken("ABCI"));
        Assert.Equal(TokenAuth.NormalizeReadToken("ABC1"), TokenAuth.NormalizeReadToken("ABCL"));
    }

    [Fact]
    public void DisplayReadToken_GroupsInSixBlocks_AndRoundTrips()
    {
        const string token = "0123456789ABCDEFGHJKMNPQ";
        var shown = TokenAuth.DisplayReadToken(token);

        Assert.Equal("0123-4567-89AB-CDEF-GHJK-MNPQ", shown);

        Assert.Equal(token, TokenAuth.NormalizeReadToken(shown));

        Assert.Equal("ABCD", TokenAuth.DisplayReadToken("ABCD"));
    }



    [Fact]
    public async Task ReadToken_ReadsTheKeyWrapAndThePayload_OverRealHttp()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();

        await UploadKeyWrapAsync(client, spaceKey);
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId);

        var issued = await client.IssueReadTokenAsync();
        Assert.True(issued.Success, issued.Message);
        var readToken = issued.Value!;

        using var http = new HttpClient();
        var keywrap = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap", readAuth: readToken, spaceId);
        var data = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/data", readAuth: readToken, spaceId);

        Assert.Equal(HttpStatusCode.OK, keywrap.Status);
        Assert.Contains("pbkdf2-sha256", keywrap.Body);
        Assert.Equal(HttpStatusCode.OK, data.Status);

        var back = SyncEnvelopeCodec.Deserialize(data.Body);
        Assert.Equal(SyncEnvelope.CurrentSyncVersion, back.Sync);
        Assert.Equal(spaceId, back.Space);

        Assert.Equal("1", data.Version);
    }

    [Fact]
    public async Task ReadToken_NeedsNoDeviceHeader()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId);
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();

        var data = await GetDataAsync(http, server.BaseUrl, readToken, spaceId);

        Assert.Equal(HttpStatusCode.OK, data.Status);
    }



    public static TheoryData<string, string> WriteEndpoints() => new()
    {
        { "PUT", "/api/v1/space/data" },
        { "PUT", "/api/v1/space/keywrap" },
        { "POST", "/api/v1/space/read-token" },
        { "DELETE", "/api/v1/space/read-token" },
        { "DELETE", "/api/v1/devices/some-device" },
        { "POST", "/api/v1/devices/some-device/reset-token" },
    };

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task ReadToken_IsForbidden_OnEveryWriteEndpoint(string method, string path)
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        var result = await SendAsync(http, new HttpMethod(method), server.BaseUrl + path,
            readAuth: readToken, spaceId, body: "{}", ifMatch: "0");


        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
        Assert.Contains("forbidden", result.Body);
    }

    [Theory]
    [InlineData("/api/v1/space/info")]
    [InlineData("/api/v1/space/versions")]
    [InlineData("/api/v1/devices")]
    public async Task ReadToken_IsForbidden_OnManagementReadEndpoints(string path)
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        var result = await SendAsync(http, HttpMethod.Get, server.BaseUrl + path, readAuth: readToken, spaceId);


        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
    }

    [Fact]
    public async Task ReadToken_CannotBeIssuedWithAnotherReadToken()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();

        var again = await SendAsync(http, HttpMethod.Post, $"{server.BaseUrl}/api/v1/space/read-token", readAuth: readToken, spaceId);
        Assert.Equal(HttpStatusCode.Forbidden, again.Status);
    }

    [Fact]
    public async Task DeviceToken_StillWorksOnEveryEndpoint_AfterReadTokenIssued()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, _, _) = await server.CreateSpaceAsync();
        await UploadKeyWrapAsync(client, spaceKey);
        Assert.True((await client.IssueReadTokenAsync()).Success);


        Assert.True((await client.GetInfoAsync()).Success);
        Assert.True((await client.ListVersionsAsync()).Success);
        Assert.True((await client.GetKeyWrapAsync()).Success);
        Assert.True((await client.ListDevicesAsync()).Success);
    }



    [Fact]
    public async Task MissingSpace_UnconfiguredTokenAndWrongToken_AnswerIdentically()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var configured = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        var missingSpace = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap",
            readAuth: configured, spaceId: "AAAAAAAAAAAAAAAAAAAAAA");
        var noReadToken = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap",
            readAuth: configured, spaceId: await SpaceWithoutReadTokenAsync(server));
        var wrongToken = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap",
            readAuth: "ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ", spaceId);

        Assert.Equal(HttpStatusCode.Unauthorized, missingSpace.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, noReadToken.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.Status);

        Assert.Equal(missingSpace.Body, noReadToken.Body);
        Assert.Equal(missingSpace.Body, wrongToken.Body);
    }



    [Fact]
    public async Task RotatingTheToken_InvalidatesThePreviousOne()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId);
        var first = (await client.IssueReadTokenAsync()).Value!;
        var second = (await client.IssueReadTokenAsync()).Value!;

        Assert.NotEqual(first, second);
        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetDataAsync(http, server.BaseUrl, first, spaceId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetDataAsync(http, server.BaseUrl, second, spaceId)).Status);
    }

    [Fact]
    public async Task RevokingTheToken_InvalidatesItImmediately()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId);
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.OK, (await GetDataAsync(http, server.BaseUrl, readToken, spaceId)).Status);

        Assert.True((await client.RevokeReadTokenAsync()).Success);


        Assert.Equal(HttpStatusCode.Unauthorized, (await GetDataAsync(http, server.BaseUrl, readToken, spaceId)).Status);
    }

    [Fact]
    public async Task IssuingAReadToken_CreatesNoDeviceRow()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var before = server.Service.ListDevices(spaceId).Value!.Count;

        var readToken = (await client.IssueReadTokenAsync()).Value!;
        using var http = new HttpClient();
        await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap", readAuth: readToken, spaceId);


        Assert.Equal(before, server.Service.ListDevices(spaceId).Value!.Count);
    }



    [Fact]
    public async Task ReadTokenFailures_UseTheirOwnCounter_AndDoNotLockDevicesOut()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var readToken = (await client.IssueReadTokenAsync()).Value!;
        var failures = server.Service.Options.MaxAuthFailures;

        using var http = new HttpClient();
        for (var i = 0; i < failures; i++)
            await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap", readAuth: "ZZZZZZZZZZZZZZZZZZZZZZZZ", spaceId);


        var blocked = await SendAsync(http, HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/keywrap", readAuth: readToken, spaceId);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.Status);


        Assert.True((await client.GetInfoAsync()).Success);
    }

    [Fact]
    public async Task ReadTokenFailures_FromOneAddress_DoNotLockOtherReadersOut()
    {




        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId);
        var readToken = (await client.IssueReadTokenAsync()).Value!;
        var failures = server.Service.Options.MaxAuthFailures;
        var data = $"{server.BaseUrl}/api/v1/space/data";

        using var http = new HttpClient();
        for (var i = 0; i < failures; i++)
            await SendAsync(http, HttpMethod.Get, data, readAuth: "ZZZZZZZZZZZZZZZZZZZZZZZZ", spaceId,
                callerAddress: "203.0.113.9");


        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await SendAsync(http, HttpMethod.Get, data, readAuth: readToken, spaceId, callerAddress: "203.0.113.9")).Status);


        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(http, HttpMethod.Get, data, readAuth: readToken, spaceId, callerAddress: "198.51.100.7")).Status);
    }



    private sealed record HttpOutcome(HttpStatusCode Status, string Body, string? Version);

    private static async Task<HttpOutcome> SendAsync(
        HttpClient http, HttpMethod method, string url,
        string? readAuth = null, string? spaceId = null,
        string? body = null, string? ifMatch = null, string? callerAddress = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (readAuth is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"{ReadScheme} {readAuth}");
        if (spaceId is not null)
            request.Headers.TryAddWithoutValidation(SpaceHeader, spaceId);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (callerAddress is not null)
        {



            request.Headers.TryAddWithoutValidation("X-Forwarded-For", callerAddress);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        }
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var version = response.Headers.TryGetValues("X-Novara-Version", out var values) ? values.FirstOrDefault() : null;
        return new HttpOutcome(response.StatusCode, text, version);
    }


    private static Task<HttpOutcome> GetDataAsync(HttpClient http, string baseUrl, string readToken, string spaceId)
        => SendAsync(http, HttpMethod.Get, $"{baseUrl}/api/v1/space/data", readAuth: readToken, spaceId);





    private static async Task UploadKeyWrapAsync(SyncApiClient client, string spaceKey)
    {
        var record = SyncKeyWrap.Wrap(spaceKey, "lock-password");
        var uploaded = await client.PutKeyWrapAsync(SyncJson.SerializeKeyWrap(record), ifMatchVersion: 0);
        Assert.True(uploaded.Success, uploaded.Message);
    }


    private static async Task PushOneVersionAsync(SyncApiClient client, string spaceId, string spaceKey, string deviceId)
    {
        var envelope = new SyncEnvelope
        {
            Sync = SyncEnvelope.CurrentSyncVersion,
            Crypto = 4,
            Space = spaceId,
            Version = 1,
            Base = 0,
            Device = deviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTime.UtcNow),
            Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("opaque")),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);

        var pushed = await client.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), baseVersion: 0);
        Assert.True(pushed.Success, pushed.Message);
    }


    private static Task<string> SpaceWithoutReadTokenAsync(HostedSyncServer server)
        => Task.FromResult(server.Service.CreateSpace("no-read-token").Value!.SpaceId);





    [Fact]
    public async Task InfoReportsWhetherAReadTokenIsConfigured()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, _, _, _, _) = await server.CreateSpaceAsync();

        var before = await client.GetInfoAsync();
        Assert.True(before.Success, before.Message);
        Assert.False(before.Value!.HasReadToken);

        Assert.True((await client.IssueReadTokenAsync()).Success);
        var issued = await client.GetInfoAsync();
        Assert.True(issued.Success, issued.Message);
        Assert.True(issued.Value!.HasReadToken);

        Assert.True((await client.RevokeReadTokenAsync()).Success);
        var revoked = await client.GetInfoAsync();
        Assert.True(revoked.Success, revoked.Message);
        Assert.False(revoked.Value!.HasReadToken);
    }

}
