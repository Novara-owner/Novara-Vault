using Novara.Services;
using System.Text.Json;
using Xunit;

namespace Novara.Tests;

// API upgrade Step 1: shared chat client pure helpers + Xiaomi MiMo vendor-matrix recognition.
public class ApiChatClientTests
{
    // ---- ValidateUrl (SSRF whitelist) ----

    [Theory]
    [InlineData("https://api.x.com/v1", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://api.xiaomimimo.com/anthropic", true)]
    [InlineData("ftp://api.x.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateUrl_OnlyHttpHttpsAllowed(string? url, bool expected)
    {
        Assert.Equal(expected, ApiChatClient.ValidateUrl(url));
    }

    // ---- DetectProtocol ----

    [Theory]
    [InlineData("Anthropic", null, ApiChatProtocol.Anthropic)]
    [InlineData("Google Gemini", null, ApiChatProtocol.Gemini)]
    [InlineData("OpenAI", null, ApiChatProtocol.OpenAI)]
    [InlineData("DeepSeek", null, ApiChatProtocol.OpenAI)]
    [InlineData(null, "https://api.x.com/v1", ApiChatProtocol.OpenAI)]
    public void DetectProtocol_VendorMapsToProtocol(string? vendor, string? baseUrl, ApiChatProtocol expected)
    {
        Assert.Equal(expected, ApiChatClient.DetectProtocol(vendor, baseUrl));
    }

    [Fact]
    public void DetectProtocol_XiaomiAnthropicBase_UsesAnthropic()
    {
        Assert.Equal(ApiChatProtocol.Anthropic, ApiChatClient.DetectProtocol("Xiaomi MiMo", "https://api.xiaomimimo.com/anthropic"));
    }

    [Fact]
    public void DetectProtocol_XiaomiOpenAiBase_UsesOpenAi()
    {
        Assert.Equal(ApiChatProtocol.OpenAI, ApiChatClient.DetectProtocol("Xiaomi MiMo", "https://api.xiaomimimo.com/v1"));
    }

    [Fact]
    public void DetectProtocol_ReverseProxyContainingAnthropic_NotMisclassified()
    {
        // N3-31: a user reverse proxy whose URL merely CONTAINS "/anthropic" (relay name / path
        // segment) must not flip a matrix-recognized OpenAI-compatible vendor to the Anthropic
        // protocol - only Xiaomi MiMo / unknown / generic rely on the path signal.
        Assert.Equal(ApiChatProtocol.OpenAI, ApiChatClient.DetectProtocol("OpenAI", "https://my-relay.com/anthropic-compat/v1"));
        Assert.Equal(ApiChatProtocol.OpenAI, ApiChatClient.DetectProtocol("DeepSeek", "https://relay.example/anthropic/v1"));
        // Xiaomi MiMo / unknown vendors keep the path heuristic.
        Assert.Equal(ApiChatProtocol.Anthropic, ApiChatClient.DetectProtocol("Xiaomi MiMo", "https://api.xiaomimimo.com/anthropic"));
        Assert.Equal(ApiChatProtocol.Anthropic, ApiChatClient.DetectProtocol(null, "https://relay.example/anthropic"));
        Assert.Equal(ApiChatProtocol.Anthropic, ApiChatClient.DetectProtocol("generic", "https://relay.example/anthropic"));
    }

    // ---- DeriveChatFromModels / ResolveChatEndpoint ----

    [Theory]
    [InlineData("https://api.x.com/v1/models", "https://api.x.com/v1/chat/completions")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/models", "https://open.bigmodel.cn/api/paas/v4/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1/models", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://api.groq.com/openai/v1/models", "https://api.groq.com/openai/v1/chat/completions")]
    [InlineData("https://qianfan.baidubce.com/v2/models", "https://qianfan.baidubce.com/v2/chat/completions")]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1/models", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")]
    public void DeriveChatFromModels_ParentDirPlusChatCompletions(string modelsEndpoint, string expected)
    {
        Assert.Equal(expected, ApiChatClient.DeriveChatFromModels(modelsEndpoint));
    }

    [Fact]
    public void ResolveChatEndpoint_Anthropic_AppendsMessages()
    {
        Assert.Equal("https://api.anthropic.com/v1/messages",
            ApiChatClient.ResolveChatEndpoint("https://api.anthropic.com", "Anthropic", "https://api.anthropic.com/v1/models", ApiChatProtocol.Anthropic, "claude-x"));
    }

    [Fact]
    public void ResolveChatEndpoint_Gemini_EmbedsModel()
    {
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent",
            ApiChatClient.ResolveChatEndpoint("https://generativelanguage.googleapis.com", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/models", ApiChatProtocol.Gemini, "gemini-2.0-flash"));
    }

    [Fact]
    public void ResolveChatEndpoint_Ollama_UsesV1Chat()
    {
        // Ollama's model list is /api/tags, but its OpenAI-compatible chat is /v1/chat/completions.
        Assert.Equal("http://localhost:11434/v1/chat/completions",
            ApiChatClient.ResolveChatEndpoint("http://localhost:11434", "Ollama", "http://localhost:11434/api/tags", ApiChatProtocol.OpenAI, "llama3"));
    }

    [Fact]
    public void ResolveChatEndpoint_AzureOpenAI_DeploymentUrl()
    {
        Assert.Equal("https://x.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-01",
            ApiChatClient.ResolveChatEndpoint("https://x.openai.azure.com", "Azure OpenAI", "https://x.openai.azure.com/openai/deployments?api-version=2024-02-01", ApiChatProtocol.OpenAI, "gpt-4o"));
    }

    [Fact]
    public void ResolveChatEndpoint_Zhipu_DerivesCorrectPath()
    {
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4/chat/completions",
            ApiChatClient.ResolveChatEndpoint("https://open.bigmodel.cn/api/paas/v4", "Zhipu", "https://open.bigmodel.cn/api/paas/v4/models", ApiChatProtocol.OpenAI, "glm-4"));
    }

    // ---- BuildChatRequestBody ----

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public void BuildChatRequestBody_OpenAi_HasModelMessagesMaxTokens()
    {
        var body = Root(ApiChatClient.BuildChatRequestBody(new ApiChatRequest { Model = "gpt-4o", Prompt = "hi", MaxTokens = 1 }, ApiChatProtocol.OpenAI));
        Assert.Equal("gpt-4o", body.GetProperty("model").GetString());
        Assert.Equal(1, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.GetProperty("stream").GetBoolean());
        var messages = body.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("hi", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public void BuildChatRequestBody_OpenAi_StreamIncludesUsageOption_AndSystemFirst()
    {
        var body = Root(ApiChatClient.BuildChatRequestBody(
            new ApiChatRequest { Model = "m", Prompt = "hi", SystemPrompt = "sys", Stream = true }, ApiChatProtocol.OpenAI));
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.True(body.TryGetProperty("stream_options", out _));
        var messages = body.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public void BuildChatRequestBody_OpenAi_ToolsPassedThrough()
    {
        var tools = new[] { new { type = "function", function = new { name = "get_weather" } } };
        var body = Root(ApiChatClient.BuildChatRequestBody(
            new ApiChatRequest { Model = "m", Prompt = "hi", Tools = tools }, ApiChatProtocol.OpenAI));
        Assert.True(body.TryGetProperty("tools", out _));
    }

    [Fact]
    public void BuildChatRequestBody_Anthropic_SystemTopLevel()
    {
        var body = Root(ApiChatClient.BuildChatRequestBody(
            new ApiChatRequest { Model = "claude", Prompt = "hi", MaxTokens = 2, SystemPrompt = "sys" }, ApiChatProtocol.Anthropic));
        Assert.Equal("claude", body.GetProperty("model").GetString());
        Assert.Equal(2, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal("sys", body.GetProperty("system").GetString());
        var messages = body.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public void BuildChatRequestBody_Gemini_ContentsAndGenerationConfig()
    {
        var body = Root(ApiChatClient.BuildChatRequestBody(
            new ApiChatRequest { Model = "gemini-2.0-flash", Prompt = "hi", MaxTokens = 3, SystemPrompt = "sys" }, ApiChatProtocol.Gemini));
        var contents = body.GetProperty("contents");
        Assert.Equal(1, contents.GetArrayLength());
        Assert.Equal("hi", contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(3, body.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        Assert.True(body.TryGetProperty("systemInstruction", out _));
    }

    // ---- ParseUsage ----

    [Fact]
    public void ParseUsage_OpenAi_MapsPromptCompletionTotal()
    {
        var u = ApiChatClient.ParseUsage("""{"usage":{"prompt_tokens":10,"completion_tokens":3,"total_tokens":13}}""", ApiChatProtocol.OpenAI);
        Assert.True(u.HasUsage);
        Assert.Equal(10, u.PromptTokens);
        Assert.Equal(3, u.CompletionTokens);
        Assert.Equal(13, u.TotalTokens);
    }

    [Fact]
    public void ParseUsage_OpenAi_CacheReadInputTokens()
    {
        var u = ApiChatClient.ParseUsage("""{"usage":{"cache_read_input_tokens":500}}""", ApiChatProtocol.OpenAI);
        Assert.Equal(500, u.CacheReadInputTokens);
    }

    [Fact]
    public void ParseUsage_Anthropic_SumInputOutput()
    {
        var u = ApiChatClient.ParseUsage("""{"usage":{"input_tokens":10,"output_tokens":2}}""", ApiChatProtocol.Anthropic);
        Assert.True(u.HasUsage);
        Assert.Equal(10, u.PromptTokens);
        Assert.Equal(2, u.CompletionTokens);
        Assert.Equal(12, u.TotalTokens);
    }

    [Fact]
    public void ParseUsage_Gemini_CamelCaseFields()
    {
        var u = ApiChatClient.ParseUsage("""{"usageMetadata":{"promptTokenCount":3,"candidatesTokenCount":1,"totalTokenCount":4}}""", ApiChatProtocol.Gemini);
        Assert.True(u.HasUsage);
        Assert.Equal(3, u.PromptTokens);
        Assert.Equal(1, u.CompletionTokens);
        Assert.Equal(4, u.TotalTokens);
    }

    [Fact]
    public void ParseUsage_NonJson_NoCrash()
    {
        var u = ApiChatClient.ParseUsage("not json", ApiChatProtocol.OpenAI);
        Assert.False(u.HasUsage);
    }

    // ---- ExtractContent / ExtractModel ----

    [Theory]
    [InlineData("""{"choices":[{"message":{"content":"hi"}}]}""", ApiChatProtocol.OpenAI, "hi")]
    [InlineData("""{"content":[{"type":"text","text":"hello"}]}""", ApiChatProtocol.Anthropic, "hello")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"yo"}]}}]}""", ApiChatProtocol.Gemini, "yo")]
    public void ExtractContent_AllProtocols(string body, ApiChatProtocol protocol, string expected)
    {
        Assert.Equal(expected, ApiChatClient.ExtractContent(body, protocol));
    }

    [Fact]
    public void ExtractModel_ReturnsModelField()
    {
        Assert.Equal("gpt-4o-2024-08-06", ApiChatClient.ExtractModel("""{"model":"gpt-4o-2024-08-06"}""", ApiChatProtocol.OpenAI));
    }

    // ---- ParseOpenAiStreamChunk ----

    [Fact]
    public void ParseOpenAiStreamChunk_ExtractsDeltaModelUsage()
    {
        var (delta, model, usage) = ApiChatClient.ParseOpenAiStreamChunk(
            """{"model":"gpt-4o","choices":[{"delta":{"content":"He"}}],"usage":{"completion_tokens":5,"prompt_tokens":9,"total_tokens":14}}""");
        Assert.Equal("He", delta);
        Assert.Equal("gpt-4o", model);
        Assert.True(usage.HasUsage);
        Assert.Equal(5, usage.CompletionTokens);
    }

    [Fact]
    public void ParseOpenAiStreamChunk_NonJson_Safe()
    {
        var (delta, model, usage) = ApiChatClient.ParseOpenAiStreamChunk("garbage");
        Assert.Equal("", delta);
        Assert.Equal("", model);
        Assert.False(usage.HasUsage);
    }

    // ---- Xiaomi MiMo vendor recognition ----

    [Theory]
    [InlineData("https://api.xiaomimimo.com/v1")]
    [InlineData("https://token-plan-cn.xiaomimimo.com/v1")]
    public void RecognizeVendor_XiaomiMiMo_ApiKeyAuth(string url)
    {
        var (vendor, authKind, endpoint, _) = ApiProbeService.RecognizeVendor(url);
        Assert.Equal("Xiaomi MiMo", vendor);
        Assert.Equal("api-key", authKind);
        Assert.False(string.IsNullOrEmpty(endpoint));
    }

    

    [Theory]
    [InlineData("https://api.example.com/v1")]
    [InlineData("https://api.example.com/v1/")]
    [InlineData("https://api.example.com")]
    public void GenericRelay_BaseWithV1_NoDuplicateV1(string baseUrl)
    {
        var (vendor, _, modelsEndpoint, _) = ApiProbeService.RecognizeVendor(baseUrl);
        Assert.Equal("generic", vendor);
        
        Assert.Equal("https://api.example.com/v1/models", modelsEndpoint);
        Assert.Equal("https://api.example.com/v1/chat/completions",
            ApiChatClient.ResolveChatEndpoint(baseUrl.TrimEnd('/'), vendor, modelsEndpoint, ApiChatProtocol.OpenAI, "gpt-4o"));
    }
}
