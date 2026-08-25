using Novara.Services;
using Xunit;

namespace Novara.Tests;

// NA7 (R1): coverage for the post-refactor probe behaviors - URL tail normalization, bare-array
// success acceptance, expanded vendor matrix, key-prefix hints, and CSV-injection escaping.
public class ApiProbeRefactorTests
{
    // ---- NormalizeBaseUrl ----

    [Theory]
    [InlineData("https://api.x.com/v1/chat/completions", "https://api.x.com/v1")]
    [InlineData("https://api.x.com/v1/completions", "https://api.x.com/v1")]
    [InlineData("https://api.x.com/v1/Chat/Completions", "https://api.x.com/v1")] // case-insensitive
    [InlineData("https://api.x.com/v1", "https://api.x.com/v1")]                 // no suffix -> untouched
    [InlineData("https://api.x.com/", "https://api.x.com/")]                     // plain trailing slash untouched here
    public void NormalizeBaseUrl_StripsKnownCompletionTails(string input, string expected)
    {
        Assert.Equal(expected, ApiProbeService.NormalizeBaseUrl(input));
    }

    // ---- SuggestVendorForKey ----

    [Theory]
    [InlineData("sk-ant-api03-xxx", "Anthropic")]
    [InlineData("AIzaSyXXX", "Google Gemini")]
    [InlineData("gsk_XXX", "Groq")]
    [InlineData("xai-XXX", "xAI Grok")]
    [InlineData("sk-or-v1-XXX", "OpenRouter")]
    public void SuggestVendorForKey_KnownPrefixes_MapToVendor(string key, string vendor)
    {
        Assert.Equal(vendor, ApiProbeService.SuggestVendorForKey(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sk-proj-generic-openai-style")] // generic sk- deliberately unmapped
    [InlineData("sk-antx-not-quite")]            // prefix must match exactly at start
    public void SuggestVendorForKey_UnknownOrMissing_ReturnsNull(string? key)
    {
        Assert.Null(ApiProbeService.SuggestVendorForKey(key));
    }

    // ---- Bare-root-array success acceptance ----

    [Fact]
    public void IsValidModelsBody_RootArray_IsAccepted()
    {
        Assert.True(ApiProbeService.IsValidModelsBody("""[{"id":"m1"},{"id":"m2"}]""", "data"));
        Assert.True(ApiProbeService.IsValidModelsBody("[]", "data")); // empty still a valid endpoint
    }

    [Fact]
    public void IsValidModelsBody_NamedFieldArray_IsAccepted_AndNonArrayRejected()
    {
        Assert.True(ApiProbeService.IsValidModelsBody("{\"data\":[{\"id\":\"m\"}]}", "data"));
        Assert.False(ApiProbeService.IsValidModelsBody("{\"error\":{\"message\":\"nope\"}}", "data"));
        Assert.False(ApiProbeService.IsValidModelsBody("not json", "data"));
    }

    [Fact]
    public void ExtractModels_RootArrayObjectsAndStrings_BothExtracted()
    {
        var models = ApiProbeService.ExtractModels("""["a-model",{"id":"b-model"}]""", "data"); // N5V-02: direct internal call (was reflection)
        Assert.Equal(new[] { "a-model", "b-model" }, models);
    }

    // ---- Expanded vendor matrix recognition ----

    [Theory]
    [InlineData("https://api.mistral.ai/v1", "Mistral")]
    [InlineData("https://api.x.ai/v1", "xAI Grok")]
    [InlineData("https://api.siliconflow.cn", "SiliconFlow")]
    [InlineData("https://api.stepfun.com/v1", "StepFun")]
    [InlineData("https://api.lingyiwanwu.com/v1", "Yi")]
    [InlineData("https://api.deepinfra.com/v1/openai", "DeepInfra")]
    [InlineData("https://api.fireworks.ai/inference/v1", "Fireworks")]
    [InlineData("https://integrate.api.nvidia.com/v1", "NVIDIA NIM")]
    [InlineData("https://api.cohere.com", "Cohere")]
    [InlineData("https://qianfan.baidubce.com/v2", "Qianfan")]
    public void RecognizeVendor_NewMatrixEntries_Recognized(string url, string vendor)
    {
        var (v, _, endpoint, _) = ApiProbeService.RecognizeVendor(url);
        Assert.Equal(vendor, v);
        Assert.False(string.IsNullOrEmpty(endpoint));
    }

    [Fact]
    public void RecognizeVendor_QianfanBaseWithV2Suffix_DoesNotDuplicatePath()
    {
        var (_, _, endpoint, _) = ApiProbeService.RecognizeVendor("https://qianfan.baidubce.com/v2");
        Assert.Equal("https://qianfan.baidubce.com/v2/models", endpoint);
    }

    [Fact]
    public void RecognizeVendor_ZhipuBaseWithPaasSuffix_DoesNotDuplicatePath()
    {
        var (_, _, endpoint, _) = ApiProbeService.RecognizeVendor("https://open.bigmodel.cn/api/paas/v4");
        Assert.Equal("https://open.bigmodel.cn/api/paas/v4/models", endpoint);
    }
}

// NC7: OWASP CSV injection guard on the export path.
public class CsvExportEscapeTests
{
    private static string ExportOne(Novara.Models.MemoEntry entry)
        => CsvImportExportService.ExportMemoEntriesToCsv(
            new List<Novara.Models.MemoEntry> { entry },
            new List<Novara.Models.MemoGroup>());

    [Fact]
    public void LeadingEquals_GetsQuotePrefix()
    {
        var e = new Novara.Models.MemoEntry { Type = "自定义", Name = "=HYPERLINK(\"http://evil\")" };
        var csv = ExportOne(e);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=HYPERLINK", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void LeadingAtPlusMinus_AlsoPrefixed()
    {
        foreach (var prefix in new[] { "@cmd", "+1", "-2" })
        {
            var e = new Novara.Models.MemoEntry { Type = "自定义", Name = prefix };
            var csv = ExportOne(e);
            Assert.Contains("'" + prefix, csv, StringComparison.Ordinal);
        }
    }
}
