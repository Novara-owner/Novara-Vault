using System.Net;
using System.Net.Http;
using Novara.Server;
using Xunit;

namespace Novara.Tests;

public class SyncHostPipelineTests
{
    [Fact]
    public async Task ForwardedHeaders_AreProcessed_SoARemoteClaimedPeerCannotSkipTheTransportGuard()
    {



        await using var server = await HostedSyncServer.StartAsync();
        using var http = new HttpClient();


        var plain = await http.GetAsync(server.BaseUrl + "/api/v1/__host-probe");
        Assert.NotEqual(HttpStatusCode.Forbidden, plain.StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
        req.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");
        var forged = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
    }

    [Fact]
    public async Task AllowedHosts_RejectsAForgedHostHeader_AndAcceptsTheConfiguredOne()
    {



        await using var server = await HostedSyncServer.StartAsync(allowedHosts: "novara.example.com");
        using var http = new HttpClient();

        var forged = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
        forged.Headers.Host = "evil.example.com";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(forged)).StatusCode);

        var allowed = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
        allowed.Headers.Host = "novara.example.com";
        Assert.NotEqual(HttpStatusCode.BadRequest, (await http.SendAsync(allowed)).StatusCode);
    }

    [Fact]
    public async Task AllowedHosts_MalformedHostPort_IsTheDocumentedBadRequest_NotA500()
    {




        await using var server = await HostedSyncServer.StartAsync(allowedHosts: "novara.example.com");
        using var http = new HttpClient();

        string[] malformed =
        [
            "novara.example.com:abc",
            "novara.example.com:99999999999999999999",
            "novara.example.com:",
            "[::1]:abc",
            "evil.example.com:abc",
        ];

        foreach (var host in malformed)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
            request.Headers.TryAddWithoutValidation("Host", host);
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }




        using var ported = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
        ported.Headers.TryAddWithoutValidation("Host", "novara.example.com:8080");
        using var portedResponse = await http.SendAsync(ported);
        Assert.Equal(HttpStatusCode.NotFound, portedResponse.StatusCode);
    }

    [Fact]
    public void AllowedHosts_EntriesThatCanNeverMatch_AreReportedAtStartup()
    {





        var warnings = new List<string>();
        var hosts = SyncHostSetup.NormalizeAllowedHosts(
            "sync.example.com:443, bad.example.com., ok.example.com", warnings.Add);

        Assert.Equal(new[] { "sync.example.com:443", "bad.example.com.", "ok.example.com" }, hosts);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("port", warnings[0]);
        Assert.Contains("dot", warnings[1]);


        var quiet = new List<string>();
        Assert.Equal(new[] { "[::1]", "*.example.com" },
            SyncHostSetup.NormalizeAllowedHosts("[::1], *.example.com", quiet.Add));
        Assert.Empty(quiet);
    }

    [Fact]
    public async Task WithoutAllowedHosts_AnyHostIsAccepted()
    {


        await using var server = await HostedSyncServer.StartAsync();
        using var http = new HttpClient();

        var req = new HttpRequestMessage(HttpMethod.Get, server.BaseUrl + "/api/v1/__host-probe");
        req.Headers.Host = "whatever.example.com";
        Assert.NotEqual(HttpStatusCode.BadRequest, (await http.SendAsync(req)).StatusCode);
    }
}
