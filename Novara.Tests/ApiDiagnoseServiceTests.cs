using Novara.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using Xunit;

namespace Novara.Tests;

// API upgrade Step 2: interface status diagnosis pure helpers + short-circuit / balance-infer pipeline.
public class ApiDiagnoseServiceTests
{
    // ---- Pure helpers ----

    [Theory]
    [InlineData(ApiProbeStatus.InvalidKey, null, true)]
    [InlineData(ApiProbeStatus.InsufficientQuota, null, false)] 
    [InlineData(ApiProbeStatus.RateLimited, null, true)]
    [InlineData(ApiProbeStatus.Permission, null, true)]
    [InlineData(ApiProbeStatus.NetworkError, "dns", true)]
    [InlineData(ApiProbeStatus.NetworkError, "connect", true)]
    [InlineData(ApiProbeStatus.NetworkError, "timeout", false)] // timeout may be a single slow path - don't stop
    [InlineData(ApiProbeStatus.NoModels, null, false)]
    [InlineData(ApiProbeStatus.ServerError, null, false)]
    [InlineData(ApiProbeStatus.Success, null, false)]
    public void ShouldShortCircuit_HardFailuresOnly(ApiProbeStatus status, string? detail, bool expected)
    {
        Assert.Equal(expected, ApiDiagnoseService.ShouldShortCircuit(status, detail));
    }

    [Fact]
    public void InferBalanceFromChat_Ok_Quota_Unknown()
    {
        Assert.Equal("ok", ApiDiagnoseService.InferBalanceFromChat(new ApiChatResult { Ok = true, Status = ApiProbeStatus.Success }));
        Assert.Equal("quota", ApiDiagnoseService.InferBalanceFromChat(new ApiChatResult { Status = ApiProbeStatus.InsufficientQuota }));
        Assert.Equal("unknown", ApiDiagnoseService.InferBalanceFromChat(new ApiChatResult { Status = ApiProbeStatus.ServerError }));
    }

    [Fact]
    public void FirstModelOrNull_SkipsBlankPicksFirst()
    {
        Assert.Equal("gpt-4o", ApiDiagnoseService.FirstModelOrNull(new[] { "", "gpt-4o", "claude" }));
        Assert.Null(ApiDiagnoseService.FirstModelOrNull(new[] { "", " " }));
        Assert.Null(ApiDiagnoseService.FirstModelOrNull(null));
    }

    // ---- Stub transport ----

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly string ModelsOk = """{"data":[{"id":"gpt-4o"}]}""";
    private static readonly string ChatOk = """{"model":"gpt-4o","choices":[{"message":{"content":"hi"}}],"usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}""";
    private static readonly string StreamOk =
        "data: {\"model\":\"gpt-4o\",\"choices\":[{\"delta\":{\"content\":\"H\"}}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{\"content\":\"i\"}}]}\n\n" +
        "data: {\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":1,\"total_tokens\":6}}\n\n" +
        "data: [DONE]\n\n";

    private static HttpResponseMessage ChatOkWithHeader(HttpRequestMessage req)
    {
        var resp = Json(HttpStatusCode.OK, ChatOk);
        resp.Headers.TryAddWithoutValidation("x-request-id", "req-123");
        return resp;
    }

    // ---- Pipeline scenarios ----

    [Fact]
    public async Task Diagnose_ModelsOkAndChatOk_AllReachable()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, ModelsOk);
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"stream\":true")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StreamOk) };
            return ChatOkWithHeader(req);
        });

        var report = await ApiDiagnoseService.DiagnoseAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.True(report.Reachable);
        Assert.Equal("ok", report.BalanceInfer);
        Assert.Equal("gpt-4o", report.ModelReturned);
        Assert.Equal("req-123", report.MetadataHeaders["x-request-id"]);
        Assert.Equal(12, report.TokensConsumed); // 6 (non-stream) + 6 (stream)
        Assert.Equal(4, report.Items.Count);
        Assert.All(report.Items, i => Assert.NotEqual(ApiDiagItemStatus.Fail, i.Status));
    }

    [Fact]
    public async Task Diagnose_ChatInsufficientQuota_BalanceWarnConservative()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, ModelsOk);
            return Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"insufficient_quota","message":"You exceeded your current quota"}}""");
        });

        var report = await ApiDiagnoseService.DiagnoseAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.True(report.Reachable); // endpoint answered - just out of money
        Assert.Equal("quota", report.BalanceInfer);
        var balance = report.Items.Single(i => i.Key == ApiDiagnoseService.ItemBalance);
        Assert.Equal(ApiDiagItemStatus.Warn, balance.Status);
    }

    [Fact]
    public async Task Diagnose_Models404ButChatOk_ReachableViaChat()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Get) return Json(HttpStatusCode.NotFound, """{"error":{"message":"not found"}}""");
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("\"stream\":true")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StreamOk) };
            return Json(HttpStatusCode.OK, ChatOk);
        });

        var report = await ApiDiagnoseService.DiagnoseAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.True(report.Reachable);
        Assert.Equal("ok", report.BalanceInfer);
        var reach = report.Items.Single(i => i.Key == ApiDiagnoseService.ItemReachability);
        Assert.Equal(ApiDiagItemStatus.Pass, reach.Status);
        Assert.Equal("chat", reach.Evidence); // reachability satisfied via chat, not the model list
    }

    [Fact]
    public async Task Diagnose_InvalidKey_ShortCircuits()
    {
        var handler = new StubHandler(req => Json(HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_api_key","message":"bad key"}}"""));

        var report = await ApiDiagnoseService.DiagnoseAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.False(report.Reachable);
        Assert.Equal(ApiProbeStatus.InvalidKey, report.ReachabilityStatus);
        Assert.Equal(4, report.Items.Count);
        Assert.Equal(ApiDiagItemStatus.Fail, report.Items.Single(i => i.Key == ApiDiagnoseService.ItemReachability).Status);
        Assert.All(report.Items.Where(i => i.Key != ApiDiagnoseService.ItemReachability), i => Assert.Equal(ApiDiagItemStatus.Skip, i.Status));
    }

    [Fact]
    public async Task Diagnose_MissingUrl_ShortCircuitsWithoutNetwork()
    {
        var report = await ApiDiagnoseService.DiagnoseAsync(null, "sk-test", "gpt-4o");
        Assert.False(report.Reachable);
        Assert.Equal("invalid-url", report.ReachabilityDetail);
        Assert.Equal(ApiDiagItemStatus.Fail, report.Items.Single(i => i.Key == ApiDiagnoseService.ItemReachability).Status);
    }
}
