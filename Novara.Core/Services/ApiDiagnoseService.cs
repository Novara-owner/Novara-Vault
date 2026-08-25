

using System.Diagnostics;
using System.Net.Http;

namespace Novara.Services;

public enum ApiDiagItemStatus { Pass, Warn, Fail, Skip }

public sealed class ApiDiagItem
{
    public string Key { get; set; } = "";       // reachability / balance / metadata / latency
    public ApiDiagItemStatus Status { get; set; }
    public string Summary { get; set; } = "";   // short semantic value, UI maps to i18n
    public string Evidence { get; set; } = "";  // masked raw data for the card
}

public sealed class ApiDiagnoseReport
{
    public bool Reachable { get; set; }
    public ApiProbeStatus ReachabilityStatus { get; set; } = ApiProbeStatus.Unknown;
    public string Vendor { get; set; } = "generic";
    public string Protocol { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string ReachabilityDetail { get; set; } = "";
    public string Model { get; set; } = "";              // model actually used (claimed or first fallback)
    public string? ModelReturned { get; set; }
    public System.Collections.Generic.Dictionary<string, string> MetadataHeaders { get; set; } = new();
    public string? BalanceInfer { get; set; }            // null / "ok" / "quota" / "unknown"
    public string? BalanceError { get; set; }            // masked
    public string? RawErrorBody { get; set; }            // masked original response for the evidence area
    public long LatencyMs { get; set; }                  // single-request end-to-end latency (streamed preferred)
    public long? TtftMs { get; set; }
    public double? TokensPerSecond { get; set; }
    public int TokensConsumed { get; set; }              // real total_tokens summed across chat requests (0 when unknown)
    public System.Collections.Generic.List<ApiDiagItem> Items { get; set; } = new();
}

public static class ApiDiagnoseService
{
    public const string ItemReachability = "reachability";
    public const string ItemBalance = "balance";
    public const string ItemMetadata = "metadata";
    public const string ItemLatency = "latency";

    // ---- Pure helpers ----

    /// <summary>Hard transport/auth failures that make retrying meaningless: stop immediately.
    /// InsufficientQuota is deliberately NOT short-circuited - it means "reachable but out of money",
    /// which must fall through to the chat branch to report balance=quota rather than "unreachable" (N3A-6).</summary>
    public static bool ShouldShortCircuit(ApiProbeStatus status, string? detail)
    {
        if (status is ApiProbeStatus.InvalidKey or ApiProbeStatus.RateLimited or ApiProbeStatus.Permission) return true;
        // A network failure that isn't a timeout means the host is unreachable (DNS/TLS/connect) - stop.
        if (status == ApiProbeStatus.NetworkError && !string.Equals(detail, "timeout", System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Map a minimal-chat result to a conservative balance inference. Never a definitive verdict.</summary>
    public static string? InferBalanceFromChat(ApiChatResult chat)
    {
        if (chat.Ok) return "ok";
        if (chat.Status == ApiProbeStatus.InsufficientQuota) return "quota";
        // A failed chat for any other reason says nothing reliable about balance.
        return "unknown";
    }

    
    public static string? FirstModelOrNull(string[]? models)
    {
        if (models == null) return null;
        foreach (var m in models)
            if (!string.IsNullOrWhiteSpace(m)) return m.Trim();
        return null;
    }

    // ---- Pipeline ----

    /// <summary>Run the four-item diagnosis. Pure of UI; inject a handler for offline testing.</summary>
    public static async System.Threading.Tasks.Task<ApiDiagnoseReport> DiagnoseAsync(
        string? url, string? key, string? model,
        System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new ApiDiagnoseReport();

        // Pre-flight validation (short-circuit with a single reachability FAIL + rest SKIP).
        if (!ApiChatClient.ValidateUrl(url))
        {
            report.ReachabilityDetail = "invalid-url";
            FillShortCircuit(report, ApiProbeStatus.Unknown, "invalid-url", sw);
            return report;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            report.ReachabilityDetail = "missing-key";
            FillShortCircuit(report, ApiProbeStatus.Unknown, "missing-key", sw);
            return report;
        }

        // Step A: reachability gate via the model-list probe (0 token).
        var probe = await ApiProbeService.ProbeAsync(url, key, ct, handler);
        report.Vendor = probe.Vendor;
        report.Protocol = probe.Protocol;
        report.Endpoint = probe.Endpoint;

        if (ShouldShortCircuit(probe.Status, probe.Detail))
        {
            report.ReachabilityDetail = probe.Detail;
            FillShortCircuit(report, probe.Status, probe.Detail, sw);
            return report;
        }

        
        report.Model = !string.IsNullOrWhiteSpace(model) ? model.Trim() : FirstModelOrNull(probe.Models) ?? "";
        var modelsOk = probe.Status == ApiProbeStatus.Success;

        if (report.Model.Length == 0)
        {
            // No model to chat with: reachability is the model-list result, the rest cannot run.
            report.Reachable = modelsOk;
            report.ReachabilityStatus = modelsOk ? ApiProbeStatus.Success : probe.Status;
            report.ReachabilityDetail = modelsOk ? "" : probe.Detail;
            report.Items.Add(new ApiDiagItem { Key = ItemReachability, Status = modelsOk ? ApiDiagItemStatus.Pass : ApiDiagItemStatus.Fail, Summary = modelsOk ? "ok" : "unreachable", Evidence = probe.Detail });
            report.Items.Add(new ApiDiagItem { Key = ItemBalance, Status = ApiDiagItemStatus.Skip, Summary = "no-model" });
            report.Items.Add(new ApiDiagItem { Key = ItemMetadata, Status = ApiDiagItemStatus.Skip, Summary = "no-model" });
            report.Items.Add(new ApiDiagItem { Key = ItemLatency, Status = ApiDiagItemStatus.Skip, Summary = "no-model" });
            report.LatencyMs = probe.LatencyMs;
            return report;
        }

        // Step B: one minimal non-streamed chat - reachability fallback + balance infer + metadata, all in one.
        var chatReq = new ApiChatRequest { Model = report.Model, Prompt = "hi", MaxTokens = 1, Stream = false };
        var chat = await ApiChatClient.ChatAsync(url, key, chatReq, ct, handler);

        if (chat.Ok)
        {
            report.Reachable = true;
            report.ReachabilityStatus = ApiProbeStatus.Success;
            report.BalanceInfer = "ok";
            report.ModelReturned = chat.ModelReturned;
            report.MetadataHeaders = chat.Headers;
            report.TokensConsumed += chat.Usage.HasUsage ? chat.Usage.TotalTokens : 0;
            report.Items.Add(new ApiDiagItem { Key = ItemReachability, Status = ApiDiagItemStatus.Pass, Summary = "ok", Evidence = modelsOk ? "models+chat" : "chat" });
            report.Items.Add(new ApiDiagItem { Key = ItemBalance, Status = ApiDiagItemStatus.Pass, Summary = "ok" });
        }
        else if (chat.Status == ApiProbeStatus.InsufficientQuota)
        {
            report.Reachable = true; // it answered - the endpoint is reachable, just out of money
            report.ReachabilityStatus = ApiProbeStatus.Success;
            report.BalanceInfer = "quota";
            report.BalanceError = chat.Detail;
            report.RawErrorBody = chat.RawBody;
            report.Items.Add(new ApiDiagItem { Key = ItemReachability, Status = ApiDiagItemStatus.Pass, Summary = "ok" });
            report.Items.Add(new ApiDiagItem { Key = ItemBalance, Status = ApiDiagItemStatus.Warn, Summary = "quota", Evidence = chat.Detail });
        }
        else if (modelsOk)
        {
            // Model-list OK but the chat failed for an ambiguous reason: reachable, balance unknown (conservative).
            report.Reachable = true;
            report.ReachabilityStatus = ApiProbeStatus.Success;
            report.BalanceInfer = "unknown";
            report.BalanceError = chat.Detail;
            report.RawErrorBody = chat.RawBody;
            report.Items.Add(new ApiDiagItem { Key = ItemReachability, Status = ApiDiagItemStatus.Pass, Summary = "ok" });
            report.Items.Add(new ApiDiagItem { Key = ItemBalance, Status = ApiDiagItemStatus.Warn, Summary = "unknown", Evidence = chat.Detail });
        }
        else
        {
            // Neither model-list nor chat worked: unreachable.
            report.ReachabilityDetail = chat.Detail;
            FillShortCircuit(report, chat.Status, chat.Detail, sw);
            return report;
        }

        // Step B continued: metadata item (model id + response headers).
        bool hasMeta = !string.IsNullOrEmpty(report.ModelReturned) || report.MetadataHeaders.Count > 0;
        report.Items.Add(new ApiDiagItem
        {
            Key = ItemMetadata,
            Status = hasMeta ? ApiDiagItemStatus.Pass : ApiDiagItemStatus.Warn,
            Summary = hasMeta ? "ok" : "missing",
            Evidence = report.ModelReturned ?? ""
        });

        // Step C: latency/TTFT - streamed for OpenAI-compatible; non-streamed latency only for the others.
        var protocol = ApiChatClient.DetectProtocol(report.Vendor, url);
        report.LatencyMs = chat.LatencyMs;
        if (protocol == ApiChatProtocol.OpenAI)
        {
            var streamReq = new ApiChatRequest { Model = report.Model, Prompt = "hi", MaxTokens = 1, Stream = true };
            var s = await ApiChatClient.ChatAsync(url, key, streamReq, ct, handler);
            if (s.Ok)
            {
                report.TtftMs = s.TtftMs;
                report.TokensPerSecond = s.TokensPerSecond;
                report.LatencyMs = s.LatencyMs;
                report.TokensConsumed += s.Usage.HasUsage ? s.Usage.TotalTokens : 0;
                report.Items.Add(new ApiDiagItem { Key = ItemLatency, Status = ApiDiagItemStatus.Pass, Summary = "ok" });
            }
            else
            {
                report.Items.Add(new ApiDiagItem { Key = ItemLatency, Status = ApiDiagItemStatus.Skip, Summary = "stream-failed", Evidence = s.Detail });
            }
        }
        else
        {
            // Anthropic / Gemini: streamed TTFT not implemented in v1; report total latency only.
            report.Items.Add(new ApiDiagItem { Key = ItemLatency, Status = ApiDiagItemStatus.Warn, Summary = "no-ttft" });
        }

        return report;
    }

    private static void FillShortCircuit(ApiDiagnoseReport report, ApiProbeStatus status, string detail, Stopwatch sw)
    {
        report.Reachable = false;
        report.ReachabilityStatus = status;
        report.Items.Add(new ApiDiagItem { Key = ItemReachability, Status = ApiDiagItemStatus.Fail, Summary = "unreachable", Evidence = detail });
        report.Items.Add(new ApiDiagItem { Key = ItemBalance, Status = ApiDiagItemStatus.Skip, Summary = "unreachable" });
        report.Items.Add(new ApiDiagItem { Key = ItemMetadata, Status = ApiDiagItemStatus.Skip, Summary = "unreachable" });
        report.Items.Add(new ApiDiagItem { Key = ItemLatency, Status = ApiDiagItemStatus.Skip, Summary = "unreachable" });
        report.LatencyMs = sw.ElapsedMilliseconds;
    }
}
