using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Novara.Services;

public enum ProbeVerdict { Pass, Warn, Fail, Skip }

public sealed class ProbeResult
{
    public string Key { get; set; } = "";
    public ProbeVerdict Verdict { get; set; }
    public double Confidence { get; set; }
    public string Summary { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string? SuspectModel { get; set; }
    public int TokensConsumed { get; set; }
    public int CompletionTokens { get; set; }
}

public sealed class RelayProbeReport
{
    public bool Reachable { get; set; }
    public ApiProbeStatus ReachabilityStatus { get; set; } = ApiProbeStatus.Unknown;
    public string Detail { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ModelReturned { get; set; }
    public double Score { get; set; }
    public string Verdict { get; set; } = "";
    public string? SuspectModel { get; set; }
    public int TokensConsumed { get; set; }
    public System.Collections.Generic.List<ProbeResult> Probes { get; set; } = new();
}

public static class RelayProbeService
{









    private const int GateMaxTokens = 1;
    private const int IdentityMaxTokens = 5;
    private const int BenchmarkMaxTokens = 256;
    private const int FormatMaxTokens = 256;
    private const int BillingMaxTokens = 5;
    private const int BillingCalls = 3;
    private const int ToolMaxTokens = 256;
    private const int InjectionMaxTokens = 5;
    private const int PoisoningMaxTokens = 256;
    private const int TruncationMaxTokens = 256;
    private const int BenchmarkQuestionFallback = 3;



    public const int GlobalTimeoutSeconds = 600;
    public const string VerdictTrusted = "trusted";
    public const string VerdictMostlyTrusted = "mostly-trusted";
    public const string VerdictSuspicious = "suspicious";
    public const string VerdictHighRisk = "high-risk";
    public const string VerdictOffline = "offline";
    public const string VerdictUnknown = "unknown";


    private static readonly (string Key, double Weight)[] ProbeWeights =
    {
        ("identity", 0.20),
        ("benchmark", 0.20),
        ("tool", 0.15),
        ("billing", 0.15),
        ("injection", 0.10),
        ("poisoning", 0.10),
        ("format", 0.05),
        ("truncation", 0.05),
    };



    private static ProbeDataSet? _currentDataSet;
    public static ProbeDataSet CurrentDataSet => _currentDataSet ??= ProbeDataSetLoader.LoadFromFile();



    public static string BuildNonce()
    {
        Span<byte> b = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(b);
        return Convert.ToHexString(b).ToLowerInvariant();
    }



    private static readonly System.Text.RegularExpressions.Regex ParamPattern = new(
        @"(\d+(?:\.\d+)?)b\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsSmallModel(string claimedModel)
    {
        if (string.IsNullOrEmpty(claimedModel)) return false;
        foreach (System.Text.RegularExpressions.Match m in ParamPattern.Matches(claimedModel))
            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var param) && param <= 15)
                return true;
        return false;
    }

    public static double ScoreProbe(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Pass => 1.0,
        ProbeVerdict.Warn => 0.5,
        ProbeVerdict.Fail => 0.0,
        _ => double.NaN,
    };



    public static double ComputeScore(System.Collections.Generic.IReadOnlyList<(string Key, ProbeVerdict Verdict)> results)
    {
        double weighted = 0, weightTotal = 0;
        foreach (var (key, verdict) in results)
        {
            if (verdict == ProbeVerdict.Skip) continue;
            var w = GetWeight(key);
            weighted += w * ScoreProbe(verdict);
            weightTotal += w;
        }
        if (weightTotal <= 0) return -1;
        return weighted / weightTotal * 100.0;
    }











    public const double MinCoverageForTrusted = 0.6;




    public static double ComputeCoverage(System.Collections.Generic.IReadOnlyList<(string Key, ProbeVerdict Verdict)> results)
    {
        double covered = 0;
        foreach (var (key, verdict) in results)
            if (verdict != ProbeVerdict.Skip) covered += GetWeight(key);
        return FullWeightTotal <= 0 ? 0 : covered / FullWeightTotal;
    }

    private static readonly double FullWeightTotal = SumWeights();

    private static double SumWeights()
    {
        double t = 0;
        foreach (var (_, w) in ProbeWeights) t += w;
        return t;
    }

    private static int VerdictRank(string verdict) => verdict switch
    {
        VerdictTrusted => 3,
        VerdictMostlyTrusted => 2,
        VerdictSuspicious => 1,
        VerdictHighRisk => 0,
        _ => -1,
    };














    public static string ApplyVerdictCaps(string verdict, System.Collections.Generic.IReadOnlyList<(string Key, ProbeVerdict Verdict)> results)
    {
        if (VerdictRank(verdict) < 0) return verdict;

        bool identityNotPass = false;
        foreach (var (key, v) in results)
            if (key == "identity" && v != ProbeVerdict.Pass) { identityNotPass = true; break; }

        var capped = verdict;
        if (identityNotPass || ComputeCoverage(results) < MinCoverageForTrusted)
            if (VerdictRank(capped) > VerdictRank(VerdictMostlyTrusted)) capped = VerdictMostlyTrusted;
        return capped;
    }























    public static bool LooksLikeEcho(string? reply, string? prompt, System.Collections.Generic.IReadOnlyList<string> promptOnlySignatures)
    {
        if (string.IsNullOrEmpty(reply)) return false;
        foreach (var f in promptOnlySignatures)
            if (!string.IsNullOrWhiteSpace(f) && reply.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(prompt)) return false;

        var r = CollapseWhitespace(reply);
        var p = CollapseWhitespace(prompt);
        for (int i = 0; i + EchoRunLength <= r.Length; i++)
            if (p.Contains(r.Substring(i, EchoRunLength), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }










    public const int EchoRunLength = 40;

    private static string CollapseWhitespace(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool pending = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { pending = sb.Length > 0; continue; }
            if (pending) { sb.Append(' '); pending = false; }
            sb.Append(ch);
        }
        return sb.ToString();
    }





    private static readonly string[] FormatEchoSignatures = { "只回复【", "不要输出任何其他文字" };
    private static readonly string[] TruncationEchoSignatures = { "请复述我最后给出的特殊标记" };
    private static readonly string[] PoisoningEchoSignatures = { "只回答数字" };


    private const string PoisoningPrompt = "12×7 等于几？只回答数字。";
    private static string FormatPrompt(string token) => "只回复【" + token + "】，不要输出任何其他文字。";
    private static string TruncationPrompt(string anchor)
        => string.Join(" ", System.Linq.Enumerable.Repeat("filler", 200)) + " 请复述我最后给出的特殊标记：" + anchor;






    public static int ComputeOutputBudget(int benchmarkQuestionCount)
    {
        var questions = benchmarkQuestionCount > 0 ? benchmarkQuestionCount : BenchmarkQuestionFallback;
        var ceilings = GateMaxTokens + IdentityMaxTokens + BenchmarkMaxTokens * questions
                     + FormatMaxTokens + BillingMaxTokens * BillingCalls + ToolMaxTokens
                     + InjectionMaxTokens + PoisoningMaxTokens + TruncationMaxTokens;
        return ceilings * 2;
    }

    public static string VerdictFromScore(double score) => score switch
    {
        < 0 => VerdictUnknown,
        >= 90 => VerdictTrusted,
        >= 75 => VerdictMostlyTrusted,
        >= 50 => VerdictSuspicious,
        _ => VerdictHighRisk,
    };







    public static (ProbeVerdict Verdict, string? Suspect, string Summary) AnalyzeIdentity(string claimedModel, string? modelReturned)
    {
        if (string.IsNullOrWhiteSpace(modelReturned))
            return (ProbeVerdict.Warn, null, "no-metadata");

        if (ModelsMatch(claimedModel, modelReturned))
            return (ProbeVerdict.Pass, null, "ok");

        return (ProbeVerdict.Warn, modelReturned, "model-mismatch");
    }




    public static bool ModelsMatch(string claimed, string returned)
    {




        var core = returned.Split('/').Last();
        var c = NormalizeModelName(claimed.Split('/').Last());
        var r = NormalizeModelName(core);
        if (c.Length == 0 || r.Length == 0) return false;
        if (c == r) return true;
        if (r.StartsWith(c, StringComparison.Ordinal) && r.Length > c.Length && r.Substring(c.Length).All(char.IsDigit)) return true;
        if (c.StartsWith(r, StringComparison.Ordinal) && c.Length > r.Length && c.Substring(r.Length).All(char.IsDigit)) return true;
        return false;
    }

    private static string NormalizeModelName(string s)
        => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static readonly System.Text.RegularExpressions.Regex NumberRegex = new(@"\d+(?:\.\d+)?");





    private static readonly System.Text.RegularExpressions.Regex NonceMarkerRegex = new(@"REQ-[0-9a-f]{8}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool CheckBenchmarkAnswer(string expected, string reply)
    {

        if (string.IsNullOrEmpty(reply)) return false;
        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var exp)) return false;
        foreach (System.Text.RegularExpressions.Match m in NumberRegex.Matches(NonceMarkerRegex.Replace(reply, " ")))
        {
            if (double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val)
                && Math.Abs(val - exp) < 1e-9)
                return true;
        }
        return false;
    }




    public static bool CheckFormatCompliance(string reply, string nonceToken)
        => !string.IsNullOrWhiteSpace(reply) && reply.Contains(nonceToken, StringComparison.OrdinalIgnoreCase);


    public static ProbeVerdict AnalyzeBilling(int[] promptTokens)
    {
        if (promptTokens == null || promptTokens.Length < 3) return ProbeVerdict.Skip;
        int min = promptTokens.Min(), max = promptTokens.Max();
        return max - min > 2 ? ProbeVerdict.Warn : ProbeVerdict.Pass;
    }



    public static ProbeVerdict AnalyzeInjection(int? cacheReadInputTokens, int? promptTokens = null)
    {

        if (cacheReadInputTokens != null && cacheReadInputTokens.Value >= 50) return ProbeVerdict.Warn;


        if (promptTokens != null && promptTokens.Value > 200) return ProbeVerdict.Warn;
        return ProbeVerdict.Pass;
    }

    public static ProbeVerdict AnalyzePoisoning(string reply, System.Collections.Generic.IReadOnlyList<string>? strongPatterns = null, System.Collections.Generic.IReadOnlyList<string>? weakPatterns = null)
    {
        if (string.IsNullOrEmpty(reply)) return ProbeVerdict.Pass;
        strongPatterns ??= CurrentDataSet.PoisoningStrongPatterns;
        weakPatterns ??= CurrentDataSet.PoisoningWeakPatterns;

        var timeout = TimeSpan.FromSeconds(1);
        try
        {
            foreach (var p in strongPatterns)
                if (Regex.IsMatch(reply, p, RegexOptions.IgnoreCase, timeout)) return ProbeVerdict.Fail;
            foreach (var p in weakPatterns)
                if (Regex.IsMatch(reply, p, RegexOptions.IgnoreCase, timeout)) return ProbeVerdict.Warn;
            return ProbeVerdict.Pass;
        }
        catch (RegexMatchTimeoutException)
        {

            return ProbeVerdict.Pass;
        }
    }

    public static bool CheckAnchor(string reply, string anchor)
        => !string.IsNullOrEmpty(reply) && reply.Contains(anchor, StringComparison.OrdinalIgnoreCase);


    public static (bool HasToolCall, string? Name, bool HasRequiredParams) CheckToolCall(string rawBody, string toolName, string[] requiredParams)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return (false, null, false);
            var msg = choices[0].GetProperty("message");
            if (!msg.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0) return (false, null, false);
            var fn = calls[0].GetProperty("function");
            var name = fn.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
            bool hasParams = false;
            if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                using var argsDoc = System.Text.Json.JsonDocument.Parse(args.GetString() ?? "{}");
                hasParams = true;
                foreach (var p in requiredParams)
                    if (!argsDoc.RootElement.TryGetProperty(p, out _)) { hasParams = false; break; }
            }
            return (true, name, hasParams);
        }
        catch { return (false, null, false); }
    }

    private static double GetWeight(string key)
    {
        foreach (var (k, w) in ProbeWeights)
            if (k == key) return w;
        return 0.05;
    }

    private static double ConfidenceFor(ProbeVerdict v) => v switch
    {
        ProbeVerdict.Pass => 0.9,
        ProbeVerdict.Fail => 0.85,
        ProbeVerdict.Warn => 0.6,
        _ => 0.0,
    };

    private static ProbeResult Result(string key, ProbeVerdict v, string summary, string evidence = "", string? suspect = null, int tokens = 0, int completionTokens = 0)
        => new() { Key = key, Verdict = v, Summary = summary, Evidence = evidence, SuspectModel = suspect, Confidence = ConfidenceFor(v), TokensConsumed = tokens, CompletionTokens = completionTokens };





    public static async System.Threading.Tasks.Task<RelayProbeReport> ProbeAsync(
        string? url, string? key, string? model,
        System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null, ProbeDataSet? dataSet = null)
    {
        NetworkActivityService.Begin("NetActivity_Kind_Relay", url ?? "");
        bool activityOk = false;
        try { var r = await ProbeAsyncCore(url, key, model, ct, handler, dataSet); activityOk = r.Reachable; return r; }
        finally { NetworkActivityService.End(activityOk); }
    }

    private static async System.Threading.Tasks.Task<RelayProbeReport> ProbeAsyncCore(
        string? url, string? key, string? model,
        System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null, ProbeDataSet? dataSet = null)
    {
        var report = new RelayProbeReport();

        if (!ApiChatClient.ValidateUrl(url)) { report.Detail = "invalid-url"; return Offline(report); }
        if (string.IsNullOrWhiteSpace(key)) { report.Detail = "missing-key"; return Offline(report); }
        report.Model = string.IsNullOrWhiteSpace(model) ? "" : model.Trim();
        if (report.Model.Length == 0) { report.Detail = "missing-model"; return Offline(report); }

        dataSet ??= CurrentDataSet;

        using var globalCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(TimeSpan.FromSeconds(GlobalTimeoutSeconds));
        var token = globalCts.Token;


        var gate = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = report.Model, Prompt = "hi", MaxTokens = GateMaxTokens }, token, handler);
        if (!gate.Ok)
        {
            report.ReachabilityStatus = gate.Status;
            report.Detail = gate.Detail;
            return Offline(report);
        }
        report.Reachable = true;
        report.ReachabilityStatus = ApiProbeStatus.Success;
        report.ModelReturned = gate.ModelReturned;
        report.TokensConsumed += gate.Usage.HasUsage ? gate.Usage.TotalTokens : 0;


        int outputBudget = gate.Usage.HasUsage ? gate.Usage.CompletionTokens : 0;

        var outputBudgetLimit = ComputeOutputBudget(dataSet.BenchmarkQuestions.Count);

        var nonce = BuildNonce();
        string baseUrl = url!;
        var probes = new (string Key, System.Func<System.Threading.Tasks.Task<ProbeResult>> Fn)[]
        {
            ("identity",   () => ProbeIdentityAsync(baseUrl, key, report.Model, nonce, token, handler)),
            ("benchmark",  () => ProbeBenchmarkAsync(baseUrl, key, report.Model, nonce, dataSet, token, handler)),
            ("format",     () => ProbeFormatAsync(baseUrl, key, report.Model, nonce, token, handler)),
            ("billing",    () => ProbeBillingAsync(baseUrl, key, report.Model, token, handler)),
            ("tool",       () => ProbeToolAsync(baseUrl, key, report.Model, token, handler)),
            ("injection",  () => ProbeInjectionAsync(baseUrl, key, report.Model, token, handler)),
            ("poisoning",  () => ProbePoisoningAsync(baseUrl, key, report.Model, dataSet, token, handler)),
            ("truncation", () => ProbeTruncationAsync(baseUrl, key, report.Model, nonce, token, handler)),
        };

        foreach (var (probeKey, fn) in probes)
        {
            if (token.IsCancellationRequested || outputBudget >= outputBudgetLimit)
            {
                report.Probes.Add(Result(probeKey, ProbeVerdict.Skip, token.IsCancellationRequested ? "timeout" : "token-budget"));
                continue;
            }
            try
            {
                var r = await fn();
                report.TokensConsumed += r.TokensConsumed;
                outputBudget += r.CompletionTokens;
                report.Probes.Add(r);
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                report.Probes.Add(Result(probeKey, ProbeVerdict.Skip, "timeout"));
            }
            catch
            {
                report.Probes.Add(Result(probeKey, ProbeVerdict.Skip, "error"));
            }
        }

        var probeResults = report.Probes.Select(p => (p.Key, p.Verdict)).ToList();
        var score = ComputeScore(probeResults);
        report.Score = score < 0 ? 0 : score;



        report.Verdict = ApplyVerdictCaps(VerdictFromScore(score), probeResults);

        var identity = report.Probes.FirstOrDefault(p => p.Key == "identity");
        if (!string.IsNullOrEmpty(identity?.SuspectModel)) report.SuspectModel = identity.SuspectModel;

        return report;
    }

    private static RelayProbeReport Offline(RelayProbeReport report)
    {
        report.Reachable = false;
        report.Verdict = VerdictOffline;
        foreach (var (key, _) in ProbeWeights)
            report.Probes.Add(Result(key, ProbeVerdict.Skip, "offline"));
        return report;
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeIdentityAsync(string url, string key, string model, string nonce, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {




        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "hi REQ-" + nonce, MaxTokens = IdentityMaxTokens }, ct, handler);
        if (!chat.Ok) return Result("identity", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var (v, suspect, summary) = AnalyzeIdentity(model, chat.ModelReturned);
        return Result("identity", v, summary, chat.ModelReturned ?? "", suspect, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeBenchmarkAsync(string url, string key, string model, string nonce, ProbeDataSet dataSet, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        int correct = 0, total = 0, tokens = 0, completion = 0;
        foreach (var q in dataSet.BenchmarkQuestions)
        {


            var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = q.Prompt + " REQ-" + nonce, MaxTokens = BenchmarkMaxTokens }, ct, handler);


            total++;
            tokens += chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0;
            completion += chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0;


            if (chat.Ok && !string.IsNullOrEmpty(chat.Content)
                && !chat.Content.Contains(q.Prompt, StringComparison.OrdinalIgnoreCase)
                && CheckBenchmarkAnswer(q.Expected, chat.Content)) correct++;
        }
        if (total == 0) return Result("benchmark", ProbeVerdict.Skip, "unreachable", "", null, tokens, completion);
        var verdict = correct == total ? ProbeVerdict.Pass : correct * 2 >= total ? ProbeVerdict.Warn : ProbeVerdict.Fail;
        if (verdict == ProbeVerdict.Fail && IsSmallModel(model)) verdict = ProbeVerdict.Warn;
        return Result("benchmark", verdict, $"{correct}/{total}", $"{correct}/{total} correct", null, tokens, completion);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeFormatAsync(string url, string key, string model, string nonce, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var token = "NOVARA-VERIFY-" + nonce;
        var prompt = FormatPrompt(token);
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = prompt, MaxTokens = FormatMaxTokens }, ct, handler);
        if (!chat.Ok) return Result("format", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);


        if (LooksLikeEcho(chat.Content, prompt, FormatEchoSignatures))
            return Result("format", ProbeVerdict.Fail, "echoed", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
        var ok = CheckFormatCompliance(chat.Content, token);
        return Result("format", ok ? ProbeVerdict.Pass : ProbeVerdict.Fail, ok ? "ok" : "non-compliant", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeBillingAsync(string url, string key, string model, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var promptTokens = new System.Collections.Generic.List<int>();
        int tokens = 0, completion = 0;
        for (int i = 0; i < BillingCalls; i++)
        {
            var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "Reply with exactly: OK", MaxTokens = BillingMaxTokens }, ct, handler);
            if (!chat.Ok || !chat.Usage.HasUsage) continue;
            promptTokens.Add(chat.Usage.PromptTokens);
            tokens += chat.Usage.TotalTokens;
            completion += chat.Usage.CompletionTokens;
        }
        if (promptTokens.Count < BillingCalls) return Result("billing", ProbeVerdict.Skip, "no-usage", "", null, tokens, completion);
        var v = AnalyzeBilling(promptTokens.ToArray());
        return Result("billing", v, v == ProbeVerdict.Pass ? "ok" : "padding", string.Join(",", promptTokens), null, tokens, completion);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeToolAsync(string url, string key, string model, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {


        var (vendor, _, _, _) = ApiProbeService.RecognizeVendor(ApiProbeService.NormalizeBaseUrl(url));
        if (ApiChatClient.DetectProtocol(vendor, url) != ApiChatProtocol.OpenAI)
            return Result("tool", ProbeVerdict.Skip, "unsupported-protocol");

        var tools = new[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "get_weather",
                    description = "Get current weather for a location",
                    parameters = new { type = "object", properties = new { location = new { type = "string" } }, required = new[] { "location" } },
                },
            },
        };
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, SystemPrompt = "You MUST call the get_weather tool to answer the weather question. Never answer weather questions directly without calling the tool.", Prompt = "What is the weather in Beijing?", MaxTokens = ToolMaxTokens, Tools = tools }, ct, handler);
        if (!chat.Ok) return Result("tool", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var (has, name, hasParams) = CheckToolCall(chat.RawBody, "get_weather", new[] { "location" });
        ProbeVerdict v = has && name == "get_weather" && hasParams ? ProbeVerdict.Pass : has ? ProbeVerdict.Warn : ProbeVerdict.Fail;
        if (v == ProbeVerdict.Fail && IsSmallModel(model)) v = ProbeVerdict.Warn;
        return Result("tool", v, v == ProbeVerdict.Pass ? "ok" : "bad-tool-call", chat.RawBody, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeInjectionAsync(string url, string key, string model, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "Hi", MaxTokens = InjectionMaxTokens }, ct, handler);
        if (!chat.Ok) return Result("injection", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var v = AnalyzeInjection(chat.Usage.CacheReadInputTokens, chat.Usage.HasUsage ? chat.Usage.PromptTokens : (int?)null);
        return Result("injection", v, v == ProbeVerdict.Pass ? "ok" : "cache-hint", $"input={chat.Usage.PromptTokens}, cache={chat.Usage.CacheReadInputTokens?.ToString() ?? "-"}", null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbePoisoningAsync(string url, string key, string model, ProbeDataSet dataSet, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = PoisoningPrompt, MaxTokens = PoisoningMaxTokens }, ct, handler);
        if (!chat.Ok) return Result("poisoning", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);


        if (LooksLikeEcho(chat.Content, PoisoningPrompt, PoisoningEchoSignatures))
            return Result("poisoning", ProbeVerdict.Fail, "echoed", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
        var v = AnalyzePoisoning(chat.Content, dataSet.PoisoningStrongPatterns, dataSet.PoisoningWeakPatterns);
        return Result("poisoning", v, v == ProbeVerdict.Pass ? "ok" : v == ProbeVerdict.Warn ? "weak-signature" : "strong-signature", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeTruncationAsync(string url, string key, string model, string nonce, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var anchor = "NOVARA-ANCHOR-" + nonce;


        var prompt = TruncationPrompt(anchor);
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = prompt, MaxTokens = TruncationMaxTokens }, ct, handler);
        if (!chat.Ok) return Result("truncation", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);




        if (LooksLikeEcho(chat.Content, prompt, TruncationEchoSignatures))
            return Result("truncation", ProbeVerdict.Warn, "echoed", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);

        var ok = CheckAnchor(chat.Content, anchor);
        return Result("truncation", ok ? ProbeVerdict.Pass : ProbeVerdict.Warn, ok ? "ok" : "suspected-truncation", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static string ChatFailSummary(ApiChatResult chat) => chat.Status switch
    {
        ApiProbeStatus.InsufficientQuota => "quota",
        ApiProbeStatus.RateLimited => "rate-limited",
        ApiProbeStatus.NetworkError => "network",
        _ => "failed",
    };
}
