





using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Novara.Services;

public enum ProbeVerdict { Pass, Warn, Fail, Skip }

public sealed class ProbeResult
{
    public string Key { get; set; } = "";       // identity / benchmark / format / billing / tool / injection / poisoning / truncation
    public ProbeVerdict Verdict { get; set; }
    public double Confidence { get; set; }      // 0~1
    public string Summary { get; set; } = "";   // short semantic value, UI maps to i18n
    public string Evidence { get; set; } = "";  // masked raw data for the card
    public string? SuspectModel { get; set; }   // identity probe: self-reported model / vendor
    public int TokensConsumed { get; set; }     // total_tokens (input+output) for the report
    public int CompletionTokens { get; set; }   // output tokens only - used for the hard budget (immune to hidden-injection padding)
}

public sealed class RelayProbeReport
{
    public bool Reachable { get; set; }
    public ApiProbeStatus ReachabilityStatus { get; set; } = ApiProbeStatus.Unknown;
    public string Detail { get; set; } = "";
    public string Model { get; set; } = "";             // claimed model
    public string? ModelReturned { get; set; }
    public double Score { get; set; }                   // 0~100 (-1/unknown -> reported as 0 + verdict "unknown")
    public string Verdict { get; set; } = "";           // trusted / mostly-trusted / suspicious / high-risk / offline / unknown
    public string? SuspectModel { get; set; }
    public int TokensConsumed { get; set; }
    public System.Collections.Generic.List<ProbeResult> Probes { get; set; } = new();
}

public static class RelayProbeService
{
    
    
    public const int MaxTokenBudget = 2000;               // hard cap: stop further probes beyond this
    
    
    public const int GlobalTimeoutSeconds = 600;
    public const string VerdictTrusted = "trusted";
    public const string VerdictMostlyTrusted = "mostly-trusted";
    public const string VerdictSuspicious = "suspicious";
    public const string VerdictHighRisk = "high-risk";
    public const string VerdictOffline = "offline";
    public const string VerdictUnknown = "unknown";

    // ---- Probe weights (skipped probes are excluded and the rest re-normalized) ----
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

    /// <summary>Probe dataset used at runtime: loaded once from ProbeDataSet.json in the app directory
    /// (restart-to-apply), falling back to the built-in Default() when missing/invalid.</summary>
    private static ProbeDataSet? _currentDataSet;
    public static ProbeDataSet CurrentDataSet => _currentDataSet ??= ProbeDataSetLoader.LoadFromFile();

    // ---- Pure helpers (unit-testable) ----

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
        _ => double.NaN, // Skip: excluded by the caller
    };

    /// <summary>Weighted score 0~100. Skipped probes are excluded and the remaining weights re-normalized.
    /// Returns -1 when every probe was skipped (cannot score).</summary>
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

    public static string VerdictFromScore(double score) => score switch
    {
        < 0 => VerdictUnknown,
        >= 90 => VerdictTrusted,
        >= 75 => VerdictMostlyTrusted,
        >= 50 => VerdictSuspicious,
        _ => VerdictHighRisk,
    };

    /// <summary>Identity: response-metadata "model" is the ONLY source of truth. Asking the model
    /// "who are you" is pointless - models routinely echo a random model name from their training data.
    /// We compare the claimed id against the reported id by normalizing case / separators / org-path
    /// prefixes. No vendor whitelist: there are thousands of models and more every day, so a list can
    /// never be complete. A mismatch is WARN (not FAIL) - a different id may be a naming variant or a
    /// tier change, not necessarily a swap; the user sees the actual id and judges for themselves.</summary>
    public static (ProbeVerdict Verdict, string? Suspect, string Summary) AnalyzeIdentity(string claimedModel, string? modelReturned)
    {
        if (string.IsNullOrWhiteSpace(modelReturned))
            return (ProbeVerdict.Warn, null, "no-metadata"); 

        if (ModelsMatch(claimedModel, modelReturned))
            return (ProbeVerdict.Pass, null, "ok");

        return (ProbeVerdict.Warn, modelReturned, "model-mismatch"); 
    }

    /// <summary>Compare a claimed model id against the id the server reported back, after normalizing
    
    /// Also accepts a pure-numeric version/date suffix (gpt-4o vs gpt-4o-2024-08-06).</summary>
    public static bool ModelsMatch(string claimed, string returned)
    {
        // N4A-02: strip the org-path prefix on BOTH sides - servers frequently echo the exact claimed id
        // including its slash ("deepseek/deepseek-chat"), and a claimed id may carry a prefix the server
        // drops ("deepseek-ai/DeepSeek-V4-Pro" vs "DeepSeek-V4-Pro"). Only stripping `returned` made both
        // common shapes report a false model-mismatch WARN.
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

    public static bool CheckBenchmarkAnswer(string expected, string reply)
    {
        
        if (string.IsNullOrEmpty(reply)) return false;
        if (!double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var exp)) return false;
        foreach (System.Text.RegularExpressions.Match m in NumberRegex.Matches(reply))
        {
            if (double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val)
                && Math.Abs(val - exp) < 1e-9)
                return true;
        }
        return false;
    }

    /// <summary>Format compliance: the meaningful signal is whether the model reproduced the random nonce
    /// token (proving it actually read the instruction). We do NOT require an exact match - models often
    
    public static bool CheckFormatCompliance(string reply, string nonceToken)
        => !string.IsNullOrWhiteSpace(reply) && reply.Contains(nonceToken, StringComparison.OrdinalIgnoreCase);

    /// <summary>Three identical short prompts must bill a consistent input_tokens; a big spread hints padding.</summary>
    public static ProbeVerdict AnalyzeBilling(int[] promptTokens)
    {
        if (promptTokens == null || promptTokens.Length < 3) return ProbeVerdict.Skip;
        int min = promptTokens.Min(), max = promptTokens.Max();
        return max - min > 2 ? ProbeVerdict.Warn : ProbeVerdict.Pass;
    }

    /// <summary>Large cache_read_input_tokens OR an abnormally large input for a one-word "Hi" prompt both
    /// hint a hidden cached system prompt. WARN at most, never FAIL (a missing field means nothing either way).</summary>
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
                if (Regex.IsMatch(reply, p, RegexOptions.IgnoreCase, timeout)) return ProbeVerdict.Warn; // N5V-05: match the strong-pattern case posture - casing must not gate a WARN
            return ProbeVerdict.Pass;
        }
        catch (RegexMatchTimeoutException)
        {
            
            return ProbeVerdict.Pass;
        }
    }

    public static bool CheckAnchor(string reply, string anchor)
        => !string.IsNullOrEmpty(reply) && reply.Contains(anchor, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parse an OpenAI tool-call response: presence of tool_calls + name + required argument keys.</summary>
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

    // ---- Pipeline ----

    /// <summary>Run the full 8-probe suite. Pure of UI; inject a handler for offline testing.</summary>
    // 9.3: wrapper reports the activity; core body untouched.
    public static async System.Threading.Tasks.Task<RelayProbeReport> ProbeAsync(
        string? url, string? key, string? model,
        System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null, ProbeDataSet? dataSet = null)
    {
        NetworkActivityService.Begin("NetActivity_Kind_Relay", url ?? "");
        bool activityOk = false; // N3-06: report the real outcome (Reachable), not a hardcoded true
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

        // Reachability gate: one minimal chat. Any failure short-circuits the whole suite.
        var gate = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = report.Model, Prompt = "hi", MaxTokens = 1 }, token, handler);
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

        var nonce = BuildNonce();
        string baseUrl = url!; // ValidateUrl passed above - non-null alias so the probe table below stays free of CS8604s
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
            if (token.IsCancellationRequested || outputBudget >= MaxTokenBudget)
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

        var score = ComputeScore(report.Probes.Select(p => (p.Key, p.Verdict)).ToList());
        report.Score = score < 0 ? 0 : score;
        report.Verdict = VerdictFromScore(score);

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
        
        
        // N3-28: carry the per-probe REQ-nonce (auditability + defeats transparent caching that
        // could serve a canned "hi" reply) - the nonce parameter was dead before.
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "hi REQ-" + nonce, MaxTokens = 5 }, ct, handler);
        if (!chat.Ok) return Result("identity", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var (v, suspect, summary) = AnalyzeIdentity(model, chat.ModelReturned);
        return Result("identity", v, summary, chat.ModelReturned ?? "", suspect, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeBenchmarkAsync(string url, string key, string model, string nonce, ProbeDataSet dataSet, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        int correct = 0, total = 0, tokens = 0, completion = 0;
        foreach (var q in dataSet.BenchmarkQuestions)
        {
            
            
            var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = q.Prompt + " REQ-" + nonce, MaxTokens = 256 }, ct, handler);
            // N2-35: count EVERY question - the old `if (!chat.Ok) continue` let a relay answer 1/3
            // and score a Pass (failures never dented the 0.20 weight).
            total++;
            tokens += chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0;
            completion += chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0;
            if (chat.Ok && CheckBenchmarkAnswer(q.Expected, chat.Content)) correct++;
        }
        if (total == 0) return Result("benchmark", ProbeVerdict.Skip, "unreachable", "", null, tokens, completion);
        var verdict = correct == total ? ProbeVerdict.Pass : correct * 2 >= total ? ProbeVerdict.Warn : ProbeVerdict.Fail;
        if (verdict == ProbeVerdict.Fail && IsSmallModel(model)) verdict = ProbeVerdict.Warn; 
        return Result("benchmark", verdict, $"{correct}/{total}", $"{correct}/{total} correct", null, tokens, completion);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeFormatAsync(string url, string key, string model, string nonce, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var token = "NOVARA-VERIFY-" + nonce;
        var prompt = "只回复【" + token + "】，不要输出任何其他文字。";
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = prompt, MaxTokens = 2000 }, ct, handler);
        if (!chat.Ok) return Result("format", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var ok = CheckFormatCompliance(chat.Content, token);
        return Result("format", ok ? ProbeVerdict.Pass : ProbeVerdict.Fail, ok ? "ok" : "non-compliant", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeBillingAsync(string url, string key, string model, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var promptTokens = new System.Collections.Generic.List<int>();
        int tokens = 0, completion = 0;
        for (int i = 0; i < 3; i++)
        {
            var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "Reply with exactly: OK", MaxTokens = 5 }, ct, handler);
            if (!chat.Ok || !chat.Usage.HasUsage) continue;
            promptTokens.Add(chat.Usage.PromptTokens);
            tokens += chat.Usage.TotalTokens;
            completion += chat.Usage.CompletionTokens;
        }
        if (promptTokens.Count < 3) return Result("billing", ProbeVerdict.Skip, "no-usage", "", null, tokens, completion);
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
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, SystemPrompt = "You MUST call the get_weather tool to answer the weather question. Never answer weather questions directly without calling the tool.", Prompt = "What is the weather in Beijing?", MaxTokens = 2000, Tools = tools }, ct, handler);
        if (!chat.Ok) return Result("tool", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var (has, name, hasParams) = CheckToolCall(chat.RawBody, "get_weather", new[] { "location" });
        ProbeVerdict v = has && name == "get_weather" && hasParams ? ProbeVerdict.Pass : has ? ProbeVerdict.Warn : ProbeVerdict.Fail;
        if (v == ProbeVerdict.Fail && IsSmallModel(model)) v = ProbeVerdict.Warn; 
        return Result("tool", v, v == ProbeVerdict.Pass ? "ok" : "bad-tool-call", chat.RawBody, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeInjectionAsync(string url, string key, string model, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "Hi", MaxTokens = 5 }, ct, handler);
        if (!chat.Ok) return Result("injection", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var v = AnalyzeInjection(chat.Usage.CacheReadInputTokens, chat.Usage.HasUsage ? chat.Usage.PromptTokens : (int?)null);
        return Result("injection", v, v == ProbeVerdict.Pass ? "ok" : "cache-hint", $"input={chat.Usage.PromptTokens}, cache={chat.Usage.CacheReadInputTokens?.ToString() ?? "-"}", null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbePoisoningAsync(string url, string key, string model, ProbeDataSet dataSet, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = "12×7 等于几？只回答数字。", MaxTokens = 2000 }, ct, handler);
        if (!chat.Ok) return Result("poisoning", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        var v = AnalyzePoisoning(chat.Content, dataSet.PoisoningStrongPatterns, dataSet.PoisoningWeakPatterns);
        return Result("poisoning", v, v == ProbeVerdict.Pass ? "ok" : v == ProbeVerdict.Warn ? "weak-signature" : "strong-signature", chat.Content, null, chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0, chat.Usage.HasUsage ? chat.Usage.CompletionTokens : 0);
    }

    private static async System.Threading.Tasks.Task<ProbeResult> ProbeTruncationAsync(string url, string key, string model, string nonce, System.Threading.CancellationToken ct, HttpMessageHandler? handler)
    {
        var anchor = "NOVARA-ANCHOR-" + nonce;
        
        
        var prefix = string.Join(" ", System.Linq.Enumerable.Repeat("filler", 200));
        var prompt = prefix + " 请复述我最后给出的特殊标记：" + anchor;
        var chat = await ApiChatClient.ChatAsync(url, key, new ApiChatRequest { Model = model, Prompt = prompt, MaxTokens = 2000 }, ct, handler);
        if (!chat.Ok) return Result("truncation", ProbeVerdict.Skip, ChatFailSummary(chat), chat.Detail);
        // Threshold is deliberately relaxed: failure is reported as "suspected truncation" (WARN), never FAIL.
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
