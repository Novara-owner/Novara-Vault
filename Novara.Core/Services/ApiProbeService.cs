
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Novara.Services;

public enum ApiProbeStatus
{
    Success,        // endpoint reached + body shape OK (models may be empty)
    InvalidKey,     // 401 / invalid_api_key / authentication_error
    InsufficientQuota, // insufficient_quota / billing_hard_limit_reached
    RateLimited,    // 429 / rate_limit_exceeded
    NoModels,       // 404 / model_not_found / endpoint not present
    Permission,     // 403 / permission_error
    ServerError,    // 5xx
    NetworkError,   // DNS / TLS / connect / timeout
    Unknown,        // everything else (incl. non-2xx with unparseable body)
}

public sealed class ApiProbeReport
{
    public ApiProbeStatus Status { get; set; }
    public string Vendor { get; set; } = "generic";
    public string Protocol { get; set; } = "none";   // bearer / x-api-key / api-key / query / none
    public string Endpoint { get; set; } = "";
    public string Detail { get; set; } = "";          // server-returned reason (masked)
    public string[] Models { get; set; } = System.Array.Empty<string>();
    public long LatencyMs { get; set; }
    /// <summary>Non-empty when the key's prefix clearly belongs to a known vendor (UI shows a
    /// mismatch hint only on failure and only when it differs from the recognized vendor).</summary>
    public string KeyHintVendor { get; set; } = "";
}

public static class ApiProbeService
{
    // ---- Vendor matrix: host keyword -> template (display name / auth kind / endpoint / success field) ----
    private sealed record VendorTemplate(string Name, string AuthKind, string Endpoint, string SuccessField);

    private static readonly (string[] HostKeys, VendorTemplate Template)[] VendorMatrix =
    {
        (new[] { "api.openai.com" },                          new("OpenAI",        "bearer",    "/v1/models", "data")),
        (new[] { "openrouter.ai" },                           new("OpenRouter",    "bearer",    "/api/v1/models", "data")),
        (new[] { "api.anthropic.com" },                       new("Anthropic",     "x-api-key", "/v1/models", "data")),
        (new[] { "generativelanguage.googleapis.com" },       new("Google Gemini", "query",     "/v1beta/models", "models")),
        (new[] { "openai.azure.com" },                        new("Azure OpenAI",  "api-key",   "/openai/deployments?api-version=2024-02-01", "data")),
        (new[] { "api.groq.com" },                            new("Groq",          "bearer",    "/openai/v1/models", "data")),
        (new[] { "api.together.xyz" },                        new("Together",      "bearer",    "/v1/models", "data")),
        (new[] { "api.perplexity.ai" },                       new("Perplexity",    "bearer",    "/v1/models", "data")),
        (new[] { "bigmodel.cn" },                             new("Zhipu",         "bearer",    "/api/paas/v4/models", "data")),
        (new[] { "dashscope.aliyuncs.com" },                  new("Qwen",          "bearer",    "/compatible-mode/v1/models", "data")),
        (new[] { "api.deepseek.com" },                        new("DeepSeek",      "bearer",    "/v1/models", "data")),
        (new[] { "api.moonshot.cn" },                         new("Moonshot",      "bearer",    "/v1/models", "data")),
        (new[] { "localhost", "127.0.0.1" },                  new("Ollama",        "none",      "/api/tags", "models")),
        
        (new[] { "api.mistral.ai" },                          new("Mistral",       "bearer",    "/v1/models", "data")),
        (new[] { "api.x.ai" },                                new("xAI Grok",      "bearer",    "/v1/models", "data")),
        (new[] { "api.siliconflow.cn" },                      new("SiliconFlow",   "bearer",    "/v1/models", "data")),
        (new[] { "api.stepfun.com" },                         new("StepFun",       "bearer",    "/v1/models", "data")),
        (new[] { "api.lingyiwanwu.com" },                     new("Yi",            "bearer",    "/v1/models", "data")),
        (new[] { "api.deepinfra.com" },                       new("DeepInfra",     "bearer",    "/v1/models", "data")),
        (new[] { "api.fireworks.ai" },                        new("Fireworks",     "bearer",    "/v1/models", "data")),
        (new[] { "integrate.api.nvidia.com" },                new("NVIDIA NIM",    "bearer",    "/v1/models", "data")),
        (new[] { "api.cohere.com" },                          new("Cohere",        "bearer",    "/v1/models", "models")),
        (new[] { "qianfan.baidubce.com" },                    new("Qianfan",       "bearer",    "/v2/models", "data")),
        
        (new[] { "api.xiaomimimo.com" },                      new("Xiaomi MiMo",   "api-key",   "/v1/models", "data")),
        (new[] { "token-plan-cn.xiaomimimo.com" },            new("Xiaomi MiMo",   "api-key",   "/v1/models", "data")),
    };

    private static readonly string[] GenericEndpoints = { "/v1/models", "/models", "/api/v1/models" };

    // ---- Key masking (never leak the key into logs or the report detail) ----
    public static string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(empty)";
        if (key.Length <= 8) return "****";
        return key.Substring(0, 4) + "…" + key.Substring(key.Length - 4);
    }

    
    /// generic "sk-" deliberately excluded (half the industry uses it).</summary>
    public static string? SuggestVendorForKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.StartsWith("sk-ant-", StringComparison.Ordinal)) return "Anthropic";
        if (key.StartsWith("AIza", StringComparison.Ordinal)) return "Google Gemini";
        if (key.StartsWith("gsk_", StringComparison.Ordinal)) return "Groq";
        if (key.StartsWith("xai-", StringComparison.Ordinal)) return "xAI Grok";
        if (key.StartsWith("sk-or-", StringComparison.Ordinal)) return "OpenRouter";
        return null;
    }

    /// <summary>Redact the key wherever it appears inside server-returned text (paranoia: some
    /// misconfigured endpoints echo the request back in their error body).</summary>
    private static string MaskKeyInText(string? text, string? key)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (string.IsNullOrEmpty(key)) return text;
        return text.Replace(key, MaskKey(key), StringComparison.Ordinal);
    }

    // ---- Pure helpers (vendor recognition / endpoint / success-body check / error mapping) ----

    public static (string Vendor, string AuthKind, string Endpoint, string SuccessField) RecognizeVendor(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return ("generic", "bearer", "/v1/models", "data");
        var u = baseUrl.Trim().TrimEnd('/');
        foreach (var (hostKeys, tpl) in VendorMatrix)
            foreach (var hk in hostKeys)
                if (u.Contains(hk, StringComparison.OrdinalIgnoreCase))
                    return (tpl.Name, tpl.AuthKind, NormalizeEndpoint(u, tpl.Endpoint), tpl.SuccessField);
        
        
        return ("generic", "bearer", NormalizeEndpoint(u, "/v1/models"), "data");
    }

    private static string NormalizeEndpoint(string baseUrl, string path)
    {
        var raw = baseUrl.Trim().TrimEnd('/');
        
        var query = "";
        var q = raw.IndexOf('?');
        if (q >= 0) { query = raw.Substring(q); raw = raw.Substring(0, q).TrimEnd('/'); }
        var u = raw;
        var p = path.StartsWith('/') ? path : "/" + path;
        // Avoid duplicating a path prefix the caller already included (e.g. Zhipu's base often ends
        // with /api/paas/v4 while the template endpoint is /api/paas/v4/models).
        string result;
        if (u.EndsWith(p, StringComparison.OrdinalIgnoreCase)) result = u;
        else
        {
            // NA5: base already ends with the path's own FIRST segment (e.g. "/openai" vs
            // "/openai/v1/models") - strip it from the path instead of stacking the segment twice.
            var firstSeg = p.Trim('/').Split('/')[0];
            if (firstSeg.Length > 0 && u.EndsWith("/" + firstSeg, StringComparison.OrdinalIgnoreCase))
                result = u + p.Substring(1 + firstSeg.Length);
            else
            {
                var parent = p.Substring(0, p.LastIndexOf('/')); // e.g. "/api/paas/v4" for "/api/paas/v4/models"
                if (parent.Length > 0 && u.EndsWith(parent, StringComparison.OrdinalIgnoreCase))
                    result = u + p.Substring(parent.Length);
                else
                    result = u + p;
            }
        }
        return result + query;
    }

    /// <summary>Success check relaxed: the field merely has to be a JSON array (empty array = still a valid
    /// endpoint). Some gateways also return a bare root-level array - accepted as well.</summary>
    public static bool IsValidModelsBody(string body, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array) return true; 
            if (!doc.RootElement.TryGetProperty(field, out var arr)) return false;
            if (arr.ValueKind != JsonValueKind.Array) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Map an HTTP status code to a report status (before consulting the error body).</summary>
    public static ApiProbeStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => ApiProbeStatus.InvalidKey,
        HttpStatusCode.Forbidden => ApiProbeStatus.Permission,
        HttpStatusCode.NotFound => ApiProbeStatus.NoModels,
        HttpStatusCode.MethodNotAllowed => ApiProbeStatus.NoModels,
        HttpStatusCode.TooManyRequests => ApiProbeStatus.RateLimited,
        _ when (int)code >= 500 => ApiProbeStatus.ServerError,
        _ => ApiProbeStatus.Unknown,
    };

    /// <summary>Parse the OpenAI-style error body {"error":{"code","message"}} into a precise status.
    /// Also tolerates Anthropic's {"error":{"type","message"}} and Gemini's numeric-code + prose message.</summary>
    public static (ApiProbeStatus Status, string Detail) ParseErrorBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Object) return (ApiProbeStatus.Unknown, "");
            // OpenAI uses error.code; Anthropic uses error.type. Fall back to whichever is present.
            var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? ""
                     : err.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? ""
                     : "";
            var msg = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var status = code switch
            {
                "invalid_api_key" or "authentication_error" => ApiProbeStatus.InvalidKey,
                "insufficient_quota" or "billing_hard_limit_reached" => ApiProbeStatus.InsufficientQuota,
                "rate_limit_exceeded" or "rate_limit" or "rate_limit_error" => ApiProbeStatus.RateLimited,
                "permission_error" or "permission_denied" => ApiProbeStatus.Permission,
                "model_not_found" or "not_found_error" => ApiProbeStatus.NoModels,
                _ => ApiProbeStatus.Unknown,
            };
            // NA6: Google/Gemini bodies carry a numeric code + prose message ("API key not valid")
            // - map the obvious key failure so the fallback chain reports something actionable.
            if (status == ApiProbeStatus.Unknown && !string.IsNullOrEmpty(msg) &&
                msg.Contains("api key", StringComparison.OrdinalIgnoreCase))
                status = ApiProbeStatus.InvalidKey;
            
            
            if (status == ApiProbeStatus.Unknown && !string.IsNullOrEmpty(msg) && LooksLikeQuota(msg))
                status = ApiProbeStatus.InsufficientQuota;
            return (status, msg);
        }
        catch { return (ApiProbeStatus.Unknown, ""); }
    }

    /// <summary>Heuristic: does an error message describe an exhausted quota / balance (not a hard vendor-specific code)?</summary>
    private static bool LooksLikeQuota(string msg)
    {
        
        
        if (msg.Contains("rate", StringComparison.OrdinalIgnoreCase) || msg.Contains("too many", StringComparison.OrdinalIgnoreCase)) return false;
        return msg.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("insufficient quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("insufficient credit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("balance", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("credit", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("arrear", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("欠费") || msg.Contains("余额") || msg.Contains("额度");
    }

    /// <summary>Classify a transport exception into a human-readable network failure category.</summary>
    public static string ClassifyNetworkError(System.Exception ex) => ex switch
    {
        HttpRequestException hre when hre.InnerException is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound => "dns",
        System.Security.Authentication.AuthenticationException => "tls",
        System.Threading.Tasks.TaskCanceledException => "timeout",
        HttpRequestException => "connect",
        _ => "network",
    };

    // ---- Probe pipeline ----

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // SocketsHttpHandler lets us bound the connect phase (5s) separately from the overall
        // timeout (15s) - a blackholed host fails fast instead of hanging the whole probe.
        var h = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // security: never forward the key across hosts
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Probe a base URL + key, returning a structured report. Pure of UI; no key leakage.
    /// An optional handler injects a mock transport for offline testing.</summary>
    public static async System.Threading.Tasks.Task<ApiProbeReport> ProbeAsync(string? url, string? key, System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var sw = Stopwatch.StartNew();
        var report = new ApiProbeReport();

        if (string.IsNullOrWhiteSpace(url))
        {
            report.Status = ApiProbeStatus.Unknown;
            report.Detail = "missing-url";
            report.LatencyMs = sw.ElapsedMilliseconds;
            return report;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            report.Status = ApiProbeStatus.Unknown;
            report.Detail = "missing-key";
            report.LatencyMs = sw.ElapsedMilliseconds;
            return report;
        }

        using var ownedClient = handler != null ? new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) } : null;
        var client = ownedClient ?? Http;
        url = NormalizeBaseUrl(url);
        var (vendor, authKind, endpoint, field) = RecognizeVendor(url);
        report.Vendor = vendor;

        
        
        
        
        
        var baseUrl = url.Trim().TrimEnd('/');
        var attempts = new List<(string Ep, string Mode)>();
        if (vendor != "generic") attempts.Add((endpoint, authKind));
        foreach (var ep in GenericEndpoints)
        {
            
            var full = NormalizeEndpoint(baseUrl, ep);
            foreach (var mode in new[] { "bearer", "raw" })
                if (!attempts.Any(a => a.Ep.Equals(full, StringComparison.OrdinalIgnoreCase) && a.Mode == mode))
                    attempts.Add((full, mode));
        }

        const int TotalBudgetMs = 16000; 
        const int PerAttemptMs = 15000;  

        ApiProbeReport? last = null;
        foreach (var (epUrl, mode) in attempts)
        {
            var remaining = TotalBudgetMs - sw.ElapsedMilliseconds;
            
            if (last != null && remaining <= 0)
                return Finalize(last, vendor, last.Protocol, last.Endpoint, sw, key);

            
            
            
            
            using var attemptCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            int attemptBudgetMs = (int)Math.Min(remaining, PerAttemptMs);
            attemptCts.CancelAfter(TimeSpan.FromMilliseconds(attemptBudgetMs));

            var r = await ProbeEndpointAsync(client, epUrl, key, mode, field, attemptCts.Token);
            if (r.Status == ApiProbeStatus.Success) return Finalize(r, vendor, mode, epUrl, sw, key);
            if (ct.IsCancellationRequested) return Cancelled(sw);

            
            bool budgetCut = !ct.IsCancellationRequested && attemptCts.IsCancellationRequested;
            if (budgetCut)
            {
                if (last != null) return Finalize(last, vendor, last.Protocol, last.Endpoint, sw, key);
                r.Detail = "budget"; 
                return Finalize(r, vendor, mode, epUrl, sw, key);
            }

            
            
            bool hardStop = r.Status is ApiProbeStatus.InvalidKey or ApiProbeStatus.InsufficientQuota
                         or ApiProbeStatus.RateLimited or ApiProbeStatus.Permission
                         || (r.Status == ApiProbeStatus.NetworkError && !string.Equals(r.Detail, "timeout", StringComparison.Ordinal));
            if (hardStop) return Finalize(r, vendor, mode, epUrl, sw, key);
            last = r;
        }
        
        if (last != null) return Finalize(last, vendor, last.Protocol, last.Endpoint, sw, key);
        return Finalize(new ApiProbeReport { Status = ApiProbeStatus.Unknown, Detail = "endpoint" }, vendor, "bearer", baseUrl + GenericEndpoints[0], sw, key);
    }

    
    
    public static string NormalizeBaseUrl(string url)
    {
        var u = url.Trim();
        foreach (var suffix in new[] { "/chat/completions", "/completions" })
        {
            if (u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                u = u.Substring(0, u.Length - suffix.Length).TrimEnd('/');
                break;
            }
        }
        return u;
    }

    /// <summary>Manual retry: probe with the user-chosen protocol against /v1/models (no vendor auto-detection
    /// or fallback chain - the user explicitly picked the scheme).</summary>
    public static async System.Threading.Tasks.Task<ApiProbeReport> ProbeWithProtocolAsync(
        string? url, string? key, string protocol, System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            return new ApiProbeReport { Status = ApiProbeStatus.Unknown, Detail = string.IsNullOrWhiteSpace(url) ? "missing-url" : "missing-key", LatencyMs = sw.ElapsedMilliseconds };
        }
        var endpoint = NormalizeEndpoint(NormalizeBaseUrl(url), "/v1/models");
        var authKind = protocol switch
        {
            "NoBearer" => "raw",
            "Raw" => "none",   // Raw = disabled detection; not reached (button disabled), guard anyway
            _ => "bearer",
        };
        using var ownedClient = handler != null ? new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) } : null;
        var client = ownedClient ?? Http;
        // N4A-04: this manual-retry path bypassed ProbeAsync's per-attempt budget - with
        // ResponseHeadersRead the body read ignores HttpClient.Timeout, so a server that stalls after
        // the response header hung the dialog forever. Give it the same single-attempt cap.
        const int PerAttemptMs = 15000; // mirrors ProbeAsync's PerAttemptMs
        using var attemptCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
        attemptCts.CancelAfter(TimeSpan.FromMilliseconds(PerAttemptMs));
        var r = await ProbeEndpointAsync(client, endpoint, key, authKind, "data", attemptCts.Token);
        if (ct.IsCancellationRequested) return Cancelled(sw);
        if (!ct.IsCancellationRequested && attemptCts.IsCancellationRequested)
            return Finalize(new ApiProbeReport { Status = ApiProbeStatus.NetworkError, Detail = "timeout" }, "generic", protocol, endpoint, sw, key); // budget cut -> honest timeout signal
        return Finalize(r, "generic", protocol, endpoint, sw, key);
    }

    private static ApiProbeReport Finalize(ApiProbeReport r, string vendor, string protocol, string endpoint, Stopwatch sw, string? key = null)
    {
        r.Vendor = vendor;
        r.Protocol = protocol;
        // N5A-04: base URLs with ?key= carry the raw key in the endpoint - mask it before the value
        // reaches the report card / log surfaces.
        if (!string.IsNullOrEmpty(key) && endpoint.Contains(key, StringComparison.Ordinal))
            endpoint = endpoint.Replace(key, "***");
        r.Endpoint = endpoint;
        r.LatencyMs = sw.ElapsedMilliseconds;
        if (!string.IsNullOrEmpty(key)) r.KeyHintVendor = SuggestVendorForKey(key) ?? "";
        return r;
    }

    private static ApiProbeReport Cancelled(Stopwatch sw)
    {
        return new ApiProbeReport { Status = ApiProbeStatus.Unknown, Detail = "cancelled", LatencyMs = sw.ElapsedMilliseconds };
    }

    private static async System.Threading.Tasks.Task<ApiProbeReport> ProbeEndpointAsync(
        HttpClient client, string endpoint, string key, string authKind, string successField, System.Threading.CancellationToken ct)
    {
        var report = new ApiProbeReport { Status = ApiProbeStatus.Unknown, Protocol = authKind, Endpoint = endpoint };
        try
        {
            // One 429 retry with Retry-After backoff (bounded).
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var req = BuildRequest(endpoint, key, authKind);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (IsValidModelsBody(body, successField))
                    {
                        report.Status = ApiProbeStatus.Success;
                        report.Models = ExtractModels(body, successField);
                        report.Detail = "";
                    }
                    else
                    {
                        report.Status = ApiProbeStatus.Unknown;
                        report.Detail = "bad-body";
                    }
                    return report;
                }

                if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    var delay = ParseRetryAfter(resp.Headers.RetryAfter);
                    // NA2: cap the wait - a huge Retry-After (hours) must not hang the probe past
                    // the 16s total budget; if the provider really needs that long, the retry will
                    // simply fail again and surface RateLimited.
                    const int MaxRetryWaitMs = 2000;
                    if (delay.TotalMilliseconds > MaxRetryWaitMs) delay = TimeSpan.FromMilliseconds(MaxRetryWaitMs);
                    if (delay.TotalMilliseconds > 0)
                    {
                        try { await System.Threading.Tasks.Task.Delay(delay, ct); } catch { }
                        continue;
                    }
                }

                report.Status = MapStatus(resp.StatusCode);
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                var (parsedStatus, parsedDetail) = ParseErrorBody(errBody);
                if (parsedStatus != ApiProbeStatus.Unknown) { report.Status = parsedStatus; report.Detail = MaskKeyInText(parsedDetail, key); }
                else if (!string.IsNullOrEmpty(parsedDetail)) report.Detail = MaskKeyInText(parsedDetail, key);
                else report.Detail = $"{(int)resp.StatusCode}";
                return report;
            }
        }
        catch (System.Threading.Tasks.TaskCanceledException) when (ct.IsCancellationRequested)
        {
            // propagate cancellation; caller reports cancelled
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (System.Exception ex)
        {
            report.Status = ApiProbeStatus.NetworkError;
            report.Detail = ClassifyNetworkError(ex);
        }
        return report;
    }

    private static HttpRequestMessage BuildRequest(string endpoint, string key, string authKind)
    {
        var url = endpoint;
        if (authKind == "query")
            url = endpoint + (endpoint.Contains('?') ? '&' : '?') + "key=" + Uri.EscapeDataString(key);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        switch (authKind)
        {
            case "x-api-key":
                req.Headers.TryAddWithoutValidation("x-api-key", key);
                break;
            case "api-key":
                req.Headers.TryAddWithoutValidation("api-key", key);
                break;
            case "none":
                break;
            case "raw":
                req.Headers.TryAddWithoutValidation("Authorization", key);
                break;
            default: // bearer
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                break;
        }
        return req;
    }

    private static TimeSpan ParseRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? r)
    {
        if (r == null) return TimeSpan.Zero;
        if (r.Delta.HasValue) return r.Delta.Value;
        if (r.Date.HasValue) { var d = r.Date.Value - DateTimeOffset.UtcNow; return d > TimeSpan.Zero ? d : TimeSpan.Zero; }
        return TimeSpan.Zero;
    }

    internal static string[] ExtractModels(string body, string field) // N5V-02: internal + IVT so tests call the contract directly (was private via reflection)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            JsonElement arr;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                arr = doc.RootElement; 
            else if (!doc.RootElement.TryGetProperty(field, out arr) || arr.ValueKind != JsonValueKind.Array)
                return System.Array.Empty<string>();
            var list = new System.Collections.Generic.List<string>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String) list.Add(el.GetString() ?? "");
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    
                    if (el.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) list.Add(id.GetString() ?? "");
                    else if (el.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) list.Add(name.GetString() ?? "");
                }
            }
            return list.ToArray();
        }
        catch { return System.Array.Empty<string>(); }
    }
}
