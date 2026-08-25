using Novara.Services;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace Novara.Tests;


public class CryptoServiceTests
{
    private static readonly byte[] Salt = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

    [Fact]
    public void Cbc_EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var plain = System.Text.Encoding.UTF8.GetBytes("Novara 加密往返测试 hello 123");
        var cipher = CryptoService.Encrypt(plain, "password", Salt);
        Assert.NotEqual(plain, cipher);
        var decrypted = CryptoService.Decrypt(cipher, "password", Salt);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Gcm_EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        var plain = System.Text.Encoding.UTF8.GetBytes("Novara GCM 加密往返测试");
        var cipher = CryptoService.EncryptGcm(plain, "password", Salt);
        var decrypted = CryptoService.DecryptGcm(cipher, "password", Salt);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Gcm_WrongPassword_Throws()
    {
        var cipher = CryptoService.EncryptGcm(new byte[] { 9, 9, 9 }, "right-password", Salt);
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            CryptoService.DecryptGcm(cipher, "wrong-password", Salt));
    }

    [Fact]
    public void Cbc_TooShortCipher_ThrowsInvalidData()
    {
        Assert.Throws<System.IO.InvalidDataException>(() =>
            CryptoService.Decrypt(new byte[] { 1, 2, 3 }, "password", Salt));
    }
}

public class ApiProbeServiceTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "OpenAI", "bearer")]
    [InlineData("https://api.anthropic.com", "Anthropic", "x-api-key")]
    [InlineData("https://generativelanguage.googleapis.com", "Google Gemini", "query")]
    [InlineData("https://openai.azure.com", "Azure OpenAI", "api-key")]
    [InlineData("http://localhost:11434", "Ollama", "none")]
    public void RecognizeVendor_ReturnsExpected(string url, string vendor, string auth)
    {
        var (v, a, _, _) = ApiProbeService.RecognizeVendor(url);
        Assert.Equal(vendor, v);
        Assert.Equal(auth, a);
    }

    [Fact]
    public void RecognizeVendor_Unknown_ReturnsGeneric()
    {
        var (v, a, ep, _) = ApiProbeService.RecognizeVendor("https://example.com/custom");
        Assert.Equal("generic", v);
        Assert.Equal("bearer", a);
        Assert.Equal("https://example.com/custom/v1/models", ep);
    }

    // Zhipu: the official base URL already carries /api/paas/v4, so the template endpoint must not
    // duplicate that prefix (regression for the .../api/paas/v4/api/paas/v4/models 404).
    [Theory]
    [InlineData("https://open.bigmodel.cn/api/paas/v4", "https://open.bigmodel.cn/api/paas/v4/models")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/", "https://open.bigmodel.cn/api/paas/v4/models")]
    [InlineData("https://open.bigmodel.cn", "https://open.bigmodel.cn/api/paas/v4/models")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/models", "https://open.bigmodel.cn/api/paas/v4/models")]
    public void RecognizeVendor_Zhipu_EndpointNotDuplicated(string url, string expected)
    {
        var (v, a, ep, _) = ApiProbeService.RecognizeVendor(url);
        Assert.Equal("Zhipu", v);
        Assert.Equal(expected, ep);
    }

    // Qwen: base may include /compatible-mode/v1 or stay bare - both must yield a valid endpoint.
    [Theory]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    [InlineData("https://dashscope.aliyuncs.com", "https://dashscope.aliyuncs.com/compatible-mode/v1/models")]
    public void RecognizeVendor_Qwen_Endpoint(string url, string expected)
    {
        var (v, a, ep, _) = ApiProbeService.RecognizeVendor(url);
        Assert.Equal("Qwen", v);
        Assert.Equal(expected, ep);
    }

    [Fact]
    public void RecognizeVendor_OpenAI_BareAndV1()
    {
        var (_, _, ep1, _) = ApiProbeService.RecognizeVendor("https://api.openai.com");
        Assert.Equal("https://api.openai.com/v1/models", ep1);
        var (_, _, ep2, _) = ApiProbeService.RecognizeVendor("https://api.openai.com/v1");
        Assert.Equal("https://api.openai.com/v1/models", ep2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiProbeStatus.InvalidKey)]
    [InlineData(HttpStatusCode.Forbidden, ApiProbeStatus.Permission)]
    [InlineData(HttpStatusCode.NotFound, ApiProbeStatus.NoModels)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiProbeStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiProbeStatus.ServerError)]
    public void MapStatus_ReturnsExpected(HttpStatusCode code, ApiProbeStatus status)
    {
        Assert.Equal(status, ApiProbeService.MapStatus(code));
    }

    [Theory]
    [InlineData("{\"error\":{\"code\":\"invalid_api_key\",\"message\":\"bad key\"}}", ApiProbeStatus.InvalidKey)]
    [InlineData("{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"no money\"}}", ApiProbeStatus.InsufficientQuota)]
    [InlineData("{\"error\":{\"code\":\"rate_limit_exceeded\",\"message\":\"slow down\"}}", ApiProbeStatus.RateLimited)]
    public void ParseErrorBody_ReturnsExpected(string body, ApiProbeStatus status)
    {
        var (s, detail) = ApiProbeService.ParseErrorBody(body);
        Assert.Equal(status, s);
        Assert.NotEmpty(detail);
    }

    
    [Theory]
    [InlineData("{\"error\":{\"code\":\"10004\",\"message\":\"账号余额不足\"}}", ApiProbeStatus.InsufficientQuota)]
    [InlineData("{\"error\":{\"message\":\"Your credit balance is too low to access the Anthropic API\"}}", ApiProbeStatus.InsufficientQuota)]
    [InlineData("{\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", ApiProbeStatus.InvalidKey)]
    [InlineData("{\"error\":{\"type\":\"not_found_error\",\"message\":\"model not found\"}}", ApiProbeStatus.NoModels)]
    public void ParseErrorBody_HeuristicAndAnthropicType(string body, ApiProbeStatus status)
    {
        var (s, _) = ApiProbeService.ParseErrorBody(body);
        Assert.Equal(status, s);
    }

    [Theory]
    [InlineData(null, "(empty)")]
    [InlineData("short", "****")]
    [InlineData("sk-abcdefghijklmnop", "sk-a…mnop")]
    public void MaskKey_ReturnsExpected(string? key, string expected)
    {
        Assert.Equal(expected, ApiProbeService.MaskKey(key));
    }

    [Theory]
    [InlineData("{\"data\":[]}", "data", true)]   
    [InlineData("{\"data\":[{\"id\":\"gpt-4\"}]}", "data", true)]
    [InlineData("{\"models\":[]}", "models", true)]
    [InlineData("{\"data\":{}}", "data", false)]  
    [InlineData("{\"data\":null}", "data", false)]
    [InlineData("not json", "data", false)]
    public void IsValidModelsBody_ReturnsExpected(string body, string field, bool expected)
    {
        Assert.Equal(expected, ApiProbeService.IsValidModelsBody(body, field));
    }
}

// Global-search fuzzy matching (regression: searching "novara" must NOT hit an unrelated entry
// whose API key merely contains the letters n,o,v,a,r,a scattered far apart).
public class SearchFuzzyTests
{
    [Theory]
    [InlineData("novara", "novara")]
    [InlineData("memo", "memorandum")]
    [InlineData("novara", "Novara is a local app")]
    [InlineData("cmput", "computer")]
    public void ContainsFuzzy_ShouldMatch(string word, string text) =>
        Assert.True(SearchFuzzy.ContainsFuzzy(text.ToLowerInvariant(), word.ToLowerInvariant()));

    [Theory]
    [InlineData("novara", "sk-9n7z2o88x1v4a9q3r6a0k2m1j")]
    [InlineData("novara", "glm53: apikey qn2ov...xa9r3al")]
    public void ContainsFuzzy_ShouldNotMatchScattered(string word, string text) =>
        Assert.False(SearchFuzzy.ContainsFuzzy(text.ToLowerInvariant(), word.ToLowerInvariant()));

    [Fact]
    public void ShortWord_OnlyExactMatch()
    {
        Assert.True(SearchFuzzy.ContainsFuzzy("a", "a"));
        Assert.False(SearchFuzzy.ContainsFuzzy("ab", "a x b"));
    }

    [Fact]
    public void MatchesQuery_AllWordsMustHit()
    {
        Assert.True(SearchFuzzy.MatchesQuery("api glm models", "glm api"));
        Assert.False(SearchFuzzy.MatchesQuery("api glm models", "glm novara"));
    }
}
