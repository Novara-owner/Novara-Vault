using System.Net;
using System.Text;
using Novara.Models;
using Novara.Services;
using Novara.Sync.Server.Storage;
using Xunit;

namespace Novara.Tests;




public class SyncEditorDeviceTests
{
    private const string SpaceHeader = "X-Novara-Space";
    private const string DeviceHeader = "X-Novara-Device";



    [Fact]
    public async Task Issue_CreatesExactlyOneFixedRow_AndRotationInvalidatesTheOldToken()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId, baseVersion: 0);

        var first = await client.IssueEditorDeviceAsync();
        Assert.True(first.Success, first.Message);
        Assert.Equal(SyncApi.EditorDeviceId, first.Value!.DeviceId);
        Assert.False(string.IsNullOrEmpty(first.Value!.DeviceToken));


        var devices = server.Service.ListDevices(spaceId).Value!;
        var row = devices.Single(d => d.DeviceId == SyncApi.EditorDeviceId);
        Assert.Equal("editor", row.Kind);
        Assert.False(row.Revoked);

        var second = await client.IssueEditorDeviceAsync();
        Assert.True(second.Success);
        Assert.NotEqual(first.Value.DeviceToken, second.Value!.DeviceToken);
        Assert.Equal(1, server.Service.ListDevices(spaceId).Value!.Count(d => d.DeviceId == SyncApi.EditorDeviceId));


        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await EditorGetAsync(http, server.BaseUrl, "/api/v1/space/data", first.Value.DeviceToken, spaceId)).Status);
        Assert.Equal(HttpStatusCode.OK,
            (await EditorGetAsync(http, server.BaseUrl, "/api/v1/space/data", second.Value.DeviceToken, spaceId)).Status);
    }

    [Fact]
    public async Task InfoReportsWhetherTheEditorDeviceIsConfigured()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, _, _, _, _) = await server.CreateSpaceAsync();

        Assert.False((await client.GetInfoAsync()).Value!.HasEditorDevice);
        Assert.True((await client.IssueEditorDeviceAsync()).Success);
        Assert.True((await client.GetInfoAsync()).Value!.HasEditorDevice);


        Assert.True((await client.RevokeEditorDeviceAsync()).Success);
        var after = await client.GetInfoAsync();
        Assert.True(after.Success, after.Message);
        Assert.False(after.Value!.HasEditorDevice);
    }



    [Fact]
    public async Task Editor_ReadsKeywrapAndData_WithDeviceHeaders()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await UploadKeyWrapAsync(client, spaceKey);
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId, baseVersion: 0);
        var editorToken = (await client.IssueEditorDeviceAsync()).Value!.DeviceToken;

        using var http = new HttpClient();
        var keywrap = await EditorGetAsync(http, server.BaseUrl, "/api/v1/space/keywrap", editorToken, spaceId);
        var data = await EditorGetAsync(http, server.BaseUrl, "/api/v1/space/data", editorToken, spaceId);

        Assert.Equal(HttpStatusCode.OK, keywrap.Status);
        Assert.Contains("pbkdf2-sha256", keywrap.Body);
        Assert.Equal(HttpStatusCode.OK, data.Status);
        Assert.Equal("1", data.Version);
    }

    [Fact]
    public async Task Editor_PushesANewVersion_AndItIsAttributable()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await UploadKeyWrapAsync(client, spaceKey);
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId, baseVersion: 0);
        var editorToken = (await client.IssueEditorDeviceAsync()).Value!.DeviceToken;

        using var http = new HttpClient();
        var pushed = await EditorPutAsync(http, server.BaseUrl, editorToken, spaceId,
            spaceKey, baseVersion: 1);
        Assert.Equal(HttpStatusCode.OK, pushed.Status);
        Assert.Contains("\"version\":2", pushed.Body);


        var versions = await client.ListVersionsAsync();
        Assert.Equal(SyncApi.EditorDeviceId, versions.Value!.First(v => v.Version == 2).DeviceId);
    }

    [Fact]
    public async Task Editor_StaleBase_Is409WithCurrentVersion()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await UploadKeyWrapAsync(client, spaceKey);
        await PushOneVersionAsync(client, spaceId, spaceKey, deviceId, baseVersion: 0);
        var editorToken = (await client.IssueEditorDeviceAsync()).Value!.DeviceToken;

        using var http = new HttpClient();
        var stale = await EditorPutAsync(http, server.BaseUrl, editorToken, spaceId, spaceKey, baseVersion: 0);

        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Contains("version_conflict", stale.Body);
        Assert.Contains("\"currentVersion\":1", stale.Body);
    }



    public static TheoryData<string, string> ManagementEndpoints() => new()
    {
        { "GET", "/api/v1/space/versions" },
        { "PUT", "/api/v1/space/keywrap" },
        { "GET", "/api/v1/devices" },
        { "DELETE", "/api/v1/devices/some-device" },
        { "POST", "/api/v1/devices/some-device/reset-token" },
        { "POST", "/api/v1/space/read-token" },
        { "DELETE", "/api/v1/space/read-token" },
        { "POST", "/api/v1/space/editor-device" },
    };

    [Theory]
    [MemberData(nameof(ManagementEndpoints))]
    public async Task Editor_IsForbidden_OnEveryManagementEndpoint(string method, string path)
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var editorToken = (await client.IssueEditorDeviceAsync()).Value!.DeviceToken;

        using var http = new HttpClient();
        var result = await EditorSendAsync(http, new HttpMethod(method), server.BaseUrl + path,
            editorToken, spaceId, body: "{}", ifMatch: "0");


        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
        Assert.Contains("data-only", result.Body);
    }

    [Fact]
    public async Task Editor_CanReadInfo_ButReadOnlyTokenCannot()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var editorToken = (await client.IssueEditorDeviceAsync()).Value!.DeviceToken;
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.OK,
            (await EditorGetAsync(http, server.BaseUrl, "/api/v1/space/info", editorToken, spaceId)).Status);

        using var read = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl}/api/v1/space/info");
        read.Headers.TryAddWithoutValidation("Authorization", $"Novara-Read {readToken}");
        read.Headers.TryAddWithoutValidation(SpaceHeader, spaceId);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(read)).StatusCode);
    }

    [Fact]
    public async Task ReadOnlyToken_CannotIssueAnEditorDevice()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, _, _, _) = await server.CreateSpaceAsync();
        var readToken = (await client.IssueReadTokenAsync()).Value!;

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server.BaseUrl}/api/v1/space/editor-device");
        request.Headers.TryAddWithoutValidation("Authorization", $"Novara-Read {readToken}");
        request.Headers.TryAddWithoutValidation(SpaceHeader, spaceId);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task StandardDevice_KeepsFullAccess_AfterEditorIssued()
    {
        await using var server = await HostedSyncServer.StartAsync();
        var (client, spaceId, spaceKey, deviceId, _) = await server.CreateSpaceAsync();
        await UploadKeyWrapAsync(client, spaceKey);
        Assert.True((await client.IssueEditorDeviceAsync()).Success);


        Assert.True((await client.GetInfoAsync()).Success);
        Assert.True((await client.ListVersionsAsync()).Success);
        Assert.True((await client.GetKeyWrapAsync()).Success);
        Assert.True((await client.ListDevicesAsync()).Success);
    }



    [Fact]
    public void OlderDatabaseWithoutKindColumn_MigratesIdempotently()
    {
        var root = Directory.CreateTempSubdirectory("novara-editor-mig").FullName;
        try
        {
            var spaceId = "MIGRATIONTESTSPACE01";
            var first = new SqliteSpaceStore(root);


            first.SaveSpace(new SpaceRecord
            { SpaceId = spaceId, Name = "mig", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            first.SaveDevice(spaceId, new DeviceRecord
            { DeviceId = "dev-old", Name = "old", TokenHash = "hash", CreatedAt = DateTime.UtcNow });



            var dbPath = Path.Combine(root, "novara-sync.db");
            using (var cn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
            using (var cmd = cn.CreateCommand())
            {
                cn.Open();
                cmd.CommandText = "ALTER TABLE devices DROP COLUMN kind;";
                cmd.ExecuteNonQuery();
            }

            var reopened = new SqliteSpaceStore(root);
            var device = reopened.GetDevice(spaceId, "dev-old");
            Assert.NotNull(device);
            Assert.Equal(DeviceKind.Standard, device!.Kind);
            device.Kind = DeviceKind.Editor;
            reopened.SaveDevice(spaceId, device);
            Assert.Equal(DeviceKind.Editor, reopened.GetDevice(spaceId, "dev-old")!.Kind);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }



    private sealed record HttpOutcome(HttpStatusCode Status, string Body, string? Version);

    private static async Task<HttpOutcome> EditorSendAsync(
        HttpClient http, HttpMethod method, string url, string editorToken, string spaceId,
        string? body = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {editorToken}");
        request.Headers.TryAddWithoutValidation(SpaceHeader, spaceId);
        request.Headers.TryAddWithoutValidation(DeviceHeader, SyncApi.EditorDeviceId);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var version = response.Headers.TryGetValues("X-Novara-Version", out var values) ? values.FirstOrDefault() : null;
        return new HttpOutcome(response.StatusCode, text, version);
    }

    private static Task<HttpOutcome> EditorGetAsync(HttpClient http, string baseUrl, string path, string editorToken, string spaceId)
        => EditorSendAsync(http, HttpMethod.Get, baseUrl + path, editorToken, spaceId);


    private static async Task<HttpOutcome> EditorPutAsync(
        HttpClient http, string baseUrl, string editorToken, string spaceId, string spaceKey, long baseVersion)
    {
        var container = SyncContainer.Seal(
            Encoding.UTF8.GetBytes($"{{\"k\":\"v{baseVersion + 1}\"}}"), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Sync = SyncEnvelope.CurrentSyncVersion,
            Crypto = 4,
            Space = spaceId,
            Version = baseVersion + 1,
            Base = baseVersion,
            Device = SyncApi.EditorDeviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTime.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        return await EditorSendAsync(http, HttpMethod.Put, $"{baseUrl}/api/v1/space/data",
            editorToken, spaceId, body: SyncEnvelopeCodec.Serialize(envelope), ifMatch: baseVersion.ToString());
    }

    private static async Task UploadKeyWrapAsync(SyncApiClient client, string spaceKey)
    {
        var record = SyncKeyWrap.Wrap(spaceKey, "lock-password");
        var uploaded = await client.PutKeyWrapAsync(SyncJson.SerializeKeyWrap(record), ifMatchVersion: 0);
        Assert.True(uploaded.Success, uploaded.Message);
    }

    private static async Task PushOneVersionAsync(SyncApiClient client, string spaceId, string spaceKey, string deviceId, long baseVersion)
    {
        var container = SyncContainer.Seal(
            Encoding.UTF8.GetBytes("{\"k\":\"v\"}"), spaceKey, SyncContainer.NewVersionSalt());
        var envelope = new SyncEnvelope
        {
            Sync = SyncEnvelope.CurrentSyncVersion,
            Crypto = 4,
            Space = spaceId,
            Version = baseVersion + 1,
            Base = baseVersion,
            Device = deviceId,
            Ts = SyncEnvelopeCodec.UtcStamp(DateTime.UtcNow),
            Payload = Convert.ToBase64String(container),
        };
        SyncEnvelopeCodec.Seal(envelope, spaceKey);
        var pushed = await client.PutDataAsync(SyncEnvelopeCodec.Serialize(envelope), baseVersion);
        Assert.True(pushed.Success, pushed.Message);
    }
}
