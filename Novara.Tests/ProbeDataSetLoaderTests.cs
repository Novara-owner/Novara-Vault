using Novara.Services;
using Xunit;

namespace Novara.Tests;

// API upgrade Step 4: dataset loader validation + proving the data-driven refactor works.
public class ProbeDataSetLoaderTests
{
    [Fact]
    public void Default_IsValid()
    {
        Assert.True(ProbeDataSetLoader.Validate(ProbeDataSetLoader.Default(), out var error), error);
        Assert.Equal("", error);
    }

    [Fact]
    public void Validate_MissingSections_Fails()
    {
        Assert.False(ProbeDataSetLoader.Validate(null, out var e1));
        Assert.Equal("empty", e1);

        var noQuestions = ProbeDataSetLoader.Default();
        noQuestions.BenchmarkQuestions.Clear();
        Assert.False(ProbeDataSetLoader.Validate(noQuestions, out var e3));
        Assert.Equal("no-benchmark-questions", e3);
    }

    [Fact]
    public void Validate_BadRegex_Fails()
    {
        var ds = ProbeDataSetLoader.Default();
        ds.PoisoningStrongPatterns.Add("("); // unbalanced group
        Assert.False(ProbeDataSetLoader.Validate(ds, out var error));
        Assert.StartsWith("bad-regex:", error);
    }

    [Fact]
    public void TryParse_ValidJson_Succeeds()
    {
        const string json = """
        {
          "version": 1,
          "benchmarkQuestions": [ { "key": "strawberry", "prompt": "p", "expected": "3" } ],
          "poisoningStrongPatterns": [ "curl" ],
          "poisoningWeakPatterns": [ "https?://" ]
        }
        """;
        Assert.True(ProbeDataSetLoader.TryParse(json, out var ds, out var error), error);
        Assert.NotNull(ds);
        Assert.Equal("strawberry", ds.BenchmarkQuestions[0].Key);
    }

    [Fact]
    public void TryParse_InvalidJson_Fails()
    {
        Assert.False(ProbeDataSetLoader.TryParse("not json", out var ds, out _));
        Assert.Null(ds);
    }

    [Fact]
    public void TryParse_ValidJsonButMissingSection_Fails()
    {
        const string json = """{ "version": 1, "benchmarkQuestions": [] }""";
        Assert.False(ProbeDataSetLoader.TryParse(json, out _, out var error));
        Assert.Equal("no-benchmark-questions", error);
    }

    [Fact]
    public void Validate_EmptyPromptOrExpected_Fails()
    {
        // N5V-03: regression for N3D-2 - a question with an empty Expected used to slip through and
        // made CheckBenchmarkAnswer("", reply) trivially true (every answer "correct").
        var ds = ProbeDataSetLoader.Default();
        ds.BenchmarkQuestions[0].Expected = "";
        Assert.False(ProbeDataSetLoader.Validate(ds, out var e1));
        Assert.Equal("bad-benchmark-question", e1);

        var ds2 = ProbeDataSetLoader.Default();
        ds2.BenchmarkQuestions[0].Prompt = "";
        Assert.False(ProbeDataSetLoader.Validate(ds2, out var e2));
        Assert.Equal("bad-benchmark-question", e2);
    }

    // ---- Prove the refactor is data-driven (not hardcoded) ----

    [Fact]
    public void AnalyzePoisoning_CustomPatterns_ChangeBehavior()
    {
        // A harmless reply is PASS under the default rules.
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzePoisoning("144"));

        // A custom strong pattern makes that same reply FAIL.
        var strong = new System.Collections.Generic.List<string> { "144" };
        var weak = new System.Collections.Generic.List<string>();
        Assert.Equal(ProbeVerdict.Fail, RelayProbeService.AnalyzePoisoning("144", strong, weak));
    }
}
