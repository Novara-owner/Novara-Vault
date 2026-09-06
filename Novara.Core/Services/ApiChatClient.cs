/* ========== ApiChatClient - Shared Chat Request Client (API upgrade Step 1) ==========
Function: Send a minimal chat request across three vendor-native protocols (OpenAI-compatible /
Anthropic Messages / Google Gemini generateContent), collecting content + usage + latency/TTFT.
Used by entry 2 (interface status diagnosis) and entry 3 (relay-station probe).
Design: pure helpers (protocol mapping / endpoint / request body / usage / stream parsing) are
public & unit-testable; the network path accepts an injectable HttpMessageHandler like ApiProbeService.
Security: URL http/https whitelist only; key never leaves the request it authorizes; errors are masked.
*/
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Novara.Services;

public enum ApiChatProtocol { OpenAI, Anthropic, Gemini }

public sealed class ApiChatRequest
{
    public string Model { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string? SystemPrompt { get; set; }
    public int MaxTokens { get; set; } = 1;
    public bool Stream { get; set; }
    /// <summary>OpenAI function-calling "tools" array (passed through verbatim; only serialized for OpenAI).</summary>
    public object? Tools { get; set; }
    public double? Temperature { get; set; }
}

public sealed class ApiChatUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    /// <summary>Present on some providers; large values hint at a cached hidden system prompt (entry 3).</summary>
    public int? CacheReadInputTokens { get; set; }
    public bool HasUsage { get; set; }
}

public sealed class ApiChatResult
{
    public bool Ok { get; set; }
    public ApiProbeStatus Status { get; set; } = ApiProbeStatus.Unknown;
    public string Detail { get; set; } = "";         // masked server reason or transport category
    public string Content { get; set; } = "";
    public string ModelReturned { get; set; } = "";
    public long LatencyMs { get; set; }
    public long? TtftMs { get; set; }                // streamed first-token latency (OpenAI only)
    public double? TokensPerSecond { get; set; }
    public ApiChatUsage Usage { get; set; } = new();
    /// <summary>Metadata response headers (x-request-id / x-ratelimit-* / server), lower-cased key.</summary>
    public System.Collections.Generic.Dictionary<string, string> Headers { get; set; } = new();
    public string RawBody { get; set; } = "";        // evidence area (key already masked)
}

public static class ApiChatClient
{
    // ---- Pure helpers (unit-testable, no network) ----

    /// <summary>SSRF guard: only plain http/https are ever dialed.</summary>
    public static bool ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Map a recognized vendor to the chat protocol used for minimal-chat requests.</summary>
    public static ApiChatProtocol DetectProtocol(string? vendor, string? baseUrl)
    {
        if (string.Equals(vendor, "Anthropic", StringComparison.OrdinalIgnoreCase)) return ApiChatProtocol.Anthropic;
        if (string.Equals(vendor, "Google Gemini", StringComparison.OrdinalIgnoreCase)) return ApiChatProtocol.Gemini;
        // Xiaomi MiMo exposes an Anthropic-compatible base (https://api.xiaomimimo.com/anthropic);
        // detect it by path so the vendor-matrix host match doesn't force OpenAI onto it.
        // N3-31: the path heuristic must NOT override a vendor already recognized by the matrix -
        // a user reverse-proxy URL that merely CONTAINS "/anthropic" (e.g. /anthropic-relay) would
        // otherwise misclassify an OpenAI-compatible endpoint as Anthropic. Gate it to vendors that
        // legitimately rely on the path signal: Xiaomi MiMo, unknown/generic relays and null vendor.
        if (string.Equals(vendor, "Xiaomi MiMo", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(vendor) || string.Equals(vendor, "generic", StringComparison.OrdinalIgnoreCase))
        {
            if (baseUrl?.Contains("/anthropic", StringComparison.OrdinalIgnoreCase) == true) return ApiChatProtocol.Anthropic;
        }
        return ApiChatProtocol.OpenAI;
    }

    /// <summary>N4A-01: drop any "?query" from a URL used as a PATH base. NormalizeEndpoint re-attaches
    /// user queries (?key=...) to the endpoints it returns (N3A-3); appending /chat/completions after a
    /// query corrupts the request target, and Anthropic/Gemini bases carrying a query embed it mid-path.</summary>
    public static string StripQuery(string url)
    {
        int i = url.IndexOf('?');
        return i >= 0 ? url.Substring(0, i) : url;
    }

    /// <summary>Resolve the correct chat endpoint for a vendor. OpenAI-compatible vendors derive the
    /// chat path from their model-list endpoint (parent dir + /chat/completions) - NOT a hardcoded
    /// /v1 - because many providers use a different prefix (Zhipu /api/paas/v4, Qwen /compatible-mode/v1,
    /// OpenRouter /api/v1, Groq /openai/v1, Qianfan /v2, etc). Ollama and Azure OpenAI are special-cased.</summary>
    public static string ResolveChatEndpoint(string baseUrl, string vendor, string modelsEndpoint, ApiChatProtocol protocol, string model)
    {
        var u = StripQuery(baseUrl.Trim()).TrimEnd('/'); // N5A-01: strip the query FIRST, then trim trailing slash - the reverse order left ".../v1/" from ".../v1/?key=x" and broke JoinEndpoint's prefix match
        switch (protocol)
        {
            case ApiChatProtocol.Anthropic:
                return JoinEndpoint(u, "/v1/messages", "/v1");
            case ApiChatProtocol.Gemini:
            {
                // N4A-06: model ids copied from a /models listing may arrive as "models/gemini-1.5-flash" -
                // EscapeDataString turned the slash into %2F and produced an invalid endpoint. Use the bare id.
                var bareModel = model.Contains('/') ? model.Substring(model.LastIndexOf('/') + 1) : model;
                return JoinEndpoint(u, "/v1beta/models/" + Uri.EscapeDataString(bareModel) + ":generateContent", "/v1beta");
            }
            default:
                if (vendor == "Ollama") return u + "/v1/chat/completions"; // Ollama's model list is /api/tags, chat is OpenAI-compatible /v1
                if (vendor == "Azure OpenAI") return AzureChatEndpoint(u, model, modelsEndpoint); // Azure reads api-version FROM the query - pass modelsEndpoint unstripped
                return DeriveChatFromModels(StripQuery(modelsEndpoint)); // N4A-01
        }
    }

    /// <summary>OpenAI-compatible chat endpoint = the model-list endpoint's parent directory + /chat/completions.
    /// e.g. "/api/paas/v4/models" -> "/api/paas/v4/chat/completions".</summary>
    public static string DeriveChatFromModels(string modelsEndpoint)
    {
        var m = modelsEndpoint.Trim();
        const string suffix = "/models";
        if (m.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return m.Substring(0, m.Length - suffix.Length) + "/chat/completions";
        return m + "/chat/completions";
    }

    private static string AzureChatEndpoint(string baseUrl, string model, string modelsEndpoint)
    {
        var apiVersion = "2024-02-01";
        var idx = modelsEndpoint.IndexOf("api-version=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = modelsEndpoint.Substring(idx + "api-version=".Length);
            var amp = rest.IndexOf('&');
            apiVersion = amp >= 0 ? rest.Substring(0, amp) : rest;
        }
        // N2-36: a base already ending in /openai (e.g. https://xx.openai.azure.com/openai) would
        // double up to /openai/openai/deployments/... and 404 - strip it before re-appending.
        var root = baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)
            ? baseUrl.Substring(0, baseUrl.Length - "/openai".Length)
            : baseUrl;
        return root + "/openai/deployments/" + Uri.EscapeDataString(model) + "/chat/completions?api-version=" + apiVersion;
    }

    /// <summary>Append a path, avoiding a duplicated prefix when the base URL already ends with it
    
    private static string JoinEndpoint(string baseUrl, string path, string prefix)
    {
        if (baseUrl.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return baseUrl + path.Substring(prefix.Length);
        return baseUrl + path;
    }

    /// <summary>Serialize the minimal-chat body for the given protocol.</summary>
    public static string BuildChatRequestBody(ApiChatRequest r, ApiChatProtocol protocol)
    {
        var opts = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        switch (protocol)
        {
            case ApiChatProtocol.Anthropic:
            {
                var body = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["model"] = r.Model,
                    ["max_tokens"] = r.MaxTokens,
                    ["messages"] = new[] { new { role = "user", content = r.Prompt } },
                };
                if (!string.IsNullOrEmpty(r.SystemPrompt)) body["system"] = r.SystemPrompt;
                return JsonSerializer.Serialize(body, opts);
            }
            case ApiChatProtocol.Gemini:
            {
                var body = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["contents"] = new[] { new { role = "user", parts = new[] { new { text = r.Prompt } } } },
                    ["generationConfig"] = new System.Collections.Generic.Dictionary<string, object?> { ["maxOutputTokens"] = r.MaxTokens },
                };
                if (!string.IsNullOrEmpty(r.SystemPrompt))
                    body["systemInstruction"] = new { parts = new[] { new { text = r.SystemPrompt } } };
                return JsonSerializer.Serialize(body, opts);
            }
            default: // OpenAI-compatible
            {
                var messages = new System.Collections.Generic.List<object>();
                if (!string.IsNullOrEmpty(r.SystemPrompt)) messages.Add(new { role = "system", content = r.SystemPrompt });
                messages.Add(new { role = "user", content = r.Prompt });
                var body = new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["model"] = r.Model,
                    ["messages"] = messages,
                    ["max_tokens"] = r.MaxTokens,
                    ["stream"] = r.Stream,
                };
                if (r.Stream) body["stream_options"] = new { include_usage = true };
                if (r.Tools != null) body["tools"] = r.Tools;
                if (r.Temperature.HasValue) body["temperature"] = r.Temperature.Value;
                return JsonSerializer.Serialize(body, opts);
            }
        }
    }

    /// <summary>Parse the usage block of a non-streaming chat response across the three protocols.</summary>
    public static ApiChatUsage ParseUsage(string jsonBody, ApiChatProtocol protocol)
    {
        var usage = new ApiChatUsage();
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;
            if (protocol == ApiChatProtocol.OpenAI && root.TryGetProperty("usage", out var ou) && ou.ValueKind == JsonValueKind.Object)
            {
                usage.PromptTokens = GetInt(ou, "prompt_tokens");
                usage.CompletionTokens = GetInt(ou, "completion_tokens");
                usage.TotalTokens = GetInt(ou, "total_tokens");
                usage.CacheReadInputTokens = GetNullableInt(ou, "cache_read_input_tokens")
                    ?? GetNullableInt(ou, "prompt_cache_hit_tokens");
                usage.HasUsage = true;
            }
            else if (protocol == ApiChatProtocol.Anthropic && root.TryGetProperty("usage", out var au) && au.ValueKind == JsonValueKind.Object)
            {
                usage.PromptTokens = GetInt(au, "input_tokens");
                usage.CompletionTokens = GetInt(au, "output_tokens");
                usage.TotalTokens = usage.PromptTokens + usage.CompletionTokens;
                usage.CacheReadInputTokens = GetNullableInt(au, "cache_read_input_tokens");
                usage.HasUsage = true;
            }
            else if (protocol == ApiChatProtocol.Gemini && root.TryGetProperty("usageMetadata", out var gu) && gu.ValueKind == JsonValueKind.Object)
            {
                usage.PromptTokens = GetInt(gu, "promptTokenCount");
                usage.CompletionTokens = GetInt(gu, "candidatesTokenCount");
                // N3-29: Gemini sometimes omits totalTokenCount - fall back to prompt+completion instead
                // of reporting a misleading 0 (display only; the completion-token budget is unaffected).
                usage.TotalTokens = GetInt(gu, "totalTokenCount");
                if (usage.TotalTokens == 0) usage.TotalTokens = usage.PromptTokens + usage.CompletionTokens;
                usage.HasUsage = true;
            }
        }
        catch { }
        return usage;
    }

    /// <summary>Extract the assistant text from a non-streaming chat response.</summary>
    public static string ExtractContent(string jsonBody, ApiChatProtocol protocol)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;
            if (protocol == ApiChatProtocol.OpenAI)
            {
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var msg = choices[0].GetProperty("message");
                    return msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                }
            }
            else if (protocol == ApiChatProtocol.Anthropic)
            {
                if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var block in content.EnumerateArray())
                        if (block.ValueKind == JsonValueKind.Object && block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                            sb.Append(t.GetString());
                    return sb.ToString();
                }
            }
            else if (protocol == ApiChatProtocol.Gemini)
            {
                if (root.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array && cands.GetArrayLength() > 0
                    && cands[0].TryGetProperty("content", out var c0) && c0.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in parts.EnumerateArray())
                        if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                            sb.Append(t.GetString());
                    return sb.ToString();
                }
            }
        }
        catch { }
        return "";
    }

    /// <summary>Extract the concrete model id the server reported back (entry 2 metadata / entry 3 identity).</summary>
    public static string ExtractModel(string jsonBody, ApiChatProtocol protocol)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) return m.GetString() ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>Parse one OpenAI SSE "data:" payload into (delta text, model, usage). Pure.</summary>
    public static (string Delta, string Model, ApiChatUsage Usage) ParseOpenAiStreamChunk(string payload)
    {
        var delta = "";
        var model = "";
        var usage = new ApiChatUsage();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString() ?? "";
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                usage.PromptTokens = GetInt(u, "prompt_tokens");
                usage.CompletionTokens = GetInt(u, "completion_tokens");
                usage.TotalTokens = GetInt(u, "total_tokens");
                usage.CacheReadInputTokens = GetNullableInt(u, "cache_read_input_tokens") ?? GetNullableInt(u, "prompt_cache_hit_tokens");
                usage.HasUsage = true;
            }
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                delta = c.GetString() ?? "";
            }
        }
        catch { }
        return (delta, model, usage);
    }

    private static int GetInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static int? GetNullableInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    // ---- Network pipeline ----

    // ---- Diagnostic logging: every chat request is appended to %LOCALAPPDATA%\{DataDirName}\relay-probe.log ----
    private static readonly object LogLock = new();
    // N3-34: honor CoreEnv.DataDirName (Novara vs Novara-Dev) - the hardcoded "Novara" path made
    // Debug builds write into the Release data directory, polluting it with probe logs.
    private static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CoreEnv.DataDirName, "relay-probe.log");

    private static void DebugLog(string line)
    {
        try
        {
            lock (LogLock)
            {
                
                try
                {
                    var fi = new System.IO.FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 1_000_000) System.IO.File.Delete(LogPath);
                }
                catch { }
                System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
        }
        catch { }
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // Connect phase bounded separately (5s) from the overall chat timeout (30s): chat can be slower
        // than a model-list probe, but a blackholed host must still fail fast.
        var h = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // security: never forward the key across hosts
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(120) }; // N2-33: 30s capped the WHOLE chat (think chains alone exceed it) - 120s covers reasoning models + slow relays; the probe's own 600s budget still governs totals
    }

    /// <summary>Send one minimal chat request and collect content + usage + latency (optionally streamed for OpenAI).</summary>
    public static async System.Threading.Tasks.Task<ApiChatResult> ChatAsync(
        string? baseUrl, string? key, ApiChatRequest request,
        System.Threading.CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var sw = Stopwatch.StartNew();
        var result = new ApiChatResult();

        if (!ValidateUrl(baseUrl))
        {
            result.Status = ApiProbeStatus.Unknown; result.Detail = "invalid-url"; result.LatencyMs = sw.ElapsedMilliseconds; return result;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            result.Status = ApiProbeStatus.Unknown; result.Detail = "missing-key"; result.LatencyMs = sw.ElapsedMilliseconds; return result;
        }

        
        if (handler is SocketsHttpHandler ssh) ssh.AllowAutoRedirect = false;
        else if (handler is HttpClientHandler hch) hch.AllowAutoRedirect = false;
        using var ownedClient = handler != null ? new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(120) } : null; // N2-33: 120s (see CreateHttpClient)
        var client = ownedClient ?? Http;
        baseUrl = ApiProbeService.NormalizeBaseUrl(baseUrl!);
        var (vendor, authKind, modelsEndpoint, _) = ApiProbeService.RecognizeVendor(baseUrl);
        var protocol = DetectProtocol(vendor, baseUrl);
        var endpoint = ResolveChatEndpoint(baseUrl, vendor, modelsEndpoint, protocol, request.Model);

        
        
        
        System.Threading.CancellationTokenSource? chatCts = null;
        try
        {
            chatCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct);
            chatCts.CancelAfter(TimeSpan.FromSeconds(120)); // N2-33: 30s killed legit think-heavy chats mid-stream; 120s covers reasoning models, the probe's 600s budget still bounds totals
            using var req = BuildChatHttpRequest(endpoint, key, authKind, protocol, request);
            if (request.Stream && protocol == ApiChatProtocol.OpenAI)
                await RunOpenAiStreamAsync(client, req, chatCts.Token, sw, result, key);
            else
                await RunNonStreamAsync(client, req, protocol, chatCts.Token, sw, result, key);
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result.Status = ApiProbeStatus.Unknown; result.Detail = "cancelled";
        }
        catch (System.OperationCanceledException) when (chatCts != null && chatCts.IsCancellationRequested)
        {
            result.Status = ApiProbeStatus.Unknown; result.Detail = "timeout";
        }
        catch (System.Exception ex)
        {
            result.Status = ApiProbeStatus.NetworkError; result.Detail = ApiProbeService.ClassifyNetworkError(ex);
        }
        finally
        {
            chatCts?.Dispose();
        }
        result.LatencyMs = sw.ElapsedMilliseconds;
        DebugLog($"CHAT {endpoint} | model={request.Model} | prompt={Truncate(request.Prompt, 60)} | ok={result.Ok} | status={result.Status} | detail={result.Detail} | usage(p={result.Usage.PromptTokens},c={result.Usage.CompletionTokens},t={result.Usage.TotalTokens},cache={result.Usage.CacheReadInputTokens?.ToString() ?? "-"}) | raw={Truncate(result.RawBody, 300)}");
        return result;
    }

    private static HttpRequestMessage BuildChatHttpRequest(string endpoint, string key, string authKind, ApiChatProtocol protocol, ApiChatRequest request)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildChatRequestBody(request, protocol), Encoding.UTF8, "application/json"),
        };
        ApplyChatAuth(req, key, authKind, protocol);
        return req;
    }

    private static void ApplyChatAuth(HttpRequestMessage req, string key, string authKind, ApiChatProtocol protocol)
    {
        switch (protocol)
        {
            case ApiChatProtocol.Anthropic:
                req.Headers.TryAddWithoutValidation("x-api-key", key);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                return;
            case ApiChatProtocol.Gemini:
                req.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                return;
            default:
                switch (authKind)
                {
                    case "x-api-key": req.Headers.TryAddWithoutValidation("x-api-key", key); break;
                    case "api-key": req.Headers.TryAddWithoutValidation("api-key", key); break; 
                    case "none": break;
                    case "raw": req.Headers.TryAddWithoutValidation("Authorization", key); break;
                    default: req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key); break;
                }
                return;
        }
    }

    private static async System.Threading.Tasks.Task RunNonStreamAsync(
        HttpClient client, HttpRequestMessage req, ApiChatProtocol protocol,
        System.Threading.CancellationToken ct, Stopwatch sw, ApiChatResult result, string key)
    {
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        CollectHeaders(resp, result);
        var body = await ReadBodyCapped(resp, ct);
        result.RawBody = MaskKeyInText(body, key); // N5A-03: the raw channel must honor the same masking as Detail

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            result.Content = ExtractContent(body, protocol);
            result.ModelReturned = ExtractModel(body, protocol);
            result.Usage = ParseUsage(body, protocol);
            result.Ok = true; result.Status = ApiProbeStatus.Success;
            return;
        }
        result.Status = ApiProbeService.MapStatus(resp.StatusCode);
        var (parsedStatus, parsedDetail) = ApiProbeService.ParseErrorBody(body);
        if (parsedStatus != ApiProbeStatus.Unknown) { result.Status = parsedStatus; result.Detail = MaskKeyInText(parsedDetail, key); }
        else if (!string.IsNullOrEmpty(parsedDetail)) result.Detail = MaskKeyInText(parsedDetail, key);
        else result.Detail = $"{(int)resp.StatusCode}";
    }

    private static async System.Threading.Tasks.Task RunOpenAiStreamAsync(
        HttpClient client, HttpRequestMessage req,
        System.Threading.CancellationToken ct, Stopwatch sw, ApiChatResult result, string key)
    {
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        CollectHeaders(resp, result);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await ReadBodyCapped(resp, ct);
            result.RawBody = MaskKeyInText(body, key); // N5A-03: the raw channel must honor the same masking as Detail
            result.Status = ApiProbeService.MapStatus(resp.StatusCode);
            var (parsedStatus, parsedDetail) = ApiProbeService.ParseErrorBody(body);
            if (parsedStatus != ApiProbeStatus.Unknown) { result.Status = parsedStatus; result.Detail = MaskKeyInText(parsedDetail, key); }
            else if (!string.IsNullOrEmpty(parsedDetail)) result.Detail = MaskKeyInText(parsedDetail, key);
            else result.Detail = $"{(int)resp.StatusCode}";
            return;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sb = new StringBuilder();
        var raw = new StringBuilder();
        long ttftAt = 0;
        ApiChatUsage usage = new();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (raw.Length > MaxResponseBodyBytes) throw new InvalidOperationException("streamed response exceeds the 32MB safety limit"); // N5-S14-04
            raw.AppendLine(line);
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line.Substring(5).Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            var (delta, model, chunkUsage) = ParseOpenAiStreamChunk(payload);
            if (chunkUsage.HasUsage) usage = chunkUsage;
            if (!string.IsNullOrEmpty(model)) result.ModelReturned = model;
            if (delta.Length > 0)
            {
                if (ttftAt == 0) { ttftAt = sw.ElapsedMilliseconds; result.TtftMs = ttftAt; }
                sb.Append(delta);
            }
        }
        result.RawBody = MaskKeyInText(raw.ToString(), key); // N5A-03
        result.Content = sb.ToString();
        result.Usage = usage;
        if (result.TtftMs.HasValue)
        {
            var total = sw.ElapsedMilliseconds;
            if (usage.HasUsage && usage.CompletionTokens > 0 && total > result.TtftMs.Value)
                result.TokensPerSecond = usage.CompletionTokens * 1000.0 / (total - result.TtftMs.Value);
        }
        result.Ok = true; result.Status = ApiProbeStatus.Success;
    }

    
    
    private const int MaxResponseBodyBytes = 32 * 1024 * 1024;

    private static async System.Threading.Tasks.Task<string> ReadBodyCapped(System.Net.Http.HttpResponseMessage resp, System.Threading.CancellationToken ct)
    {
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        long total = 0;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxResponseBodyBytes) throw new InvalidOperationException("response body exceeds the 32MB safety limit");
            ms.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void CollectHeaders(HttpResponseMessage resp, ApiChatResult result)
    {
        foreach (var h in new[] { "x-request-id", "x-ratelimit-limit-requests", "x-ratelimit-remaining-requests", "x-ratelimit-reset-requests", "server" })
        {
            if (resp.Headers.TryGetValues(h, out var vals))
                result.Headers[h] = string.Join(",", vals);
        }
    }

    private static string MaskKeyInText(string? text, string? key)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return text ?? "";
        return text.Replace(key, ApiProbeService.MaskKey(key), StringComparison.Ordinal);
    }
}
