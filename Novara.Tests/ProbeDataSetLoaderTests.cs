using Novara.Services;
using Xunit;

namespace Novara.Tests;


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
        ds.PoisoningStrongPatterns.Add("(");
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


        var ds = ProbeDataSetLoader.Default();
        ds.BenchmarkQuestions[0].Expected = "";
        Assert.False(ProbeDataSetLoader.Validate(ds, out var e1));
        Assert.Equal("bad-benchmark-question", e1);

        var ds2 = ProbeDataSetLoader.Default();
        ds2.BenchmarkQuestions[0].Prompt = "";
        Assert.False(ProbeDataSetLoader.Validate(ds2, out var e2));
        Assert.Equal("bad-benchmark-question", e2);
    }

    [Fact]
    public void Validate_CapsPoisoningPatternCount()
    {


        var ds = ProbeDataSetLoader.Default();
        for (int i = 0; i < 200; i++) ds.PoisoningStrongPatterns.Add("curl");
        for (int i = 0; i < 150; i++) ds.PoisoningWeakPatterns.Add("https?://");
        Assert.True(ProbeDataSetLoader.Validate(ds, out var error), error);
        Assert.Equal(64, ds.PoisoningStrongPatterns.Count);
        Assert.Equal(64, ds.PoisoningWeakPatterns.Count);
    }



    [Fact]
    public void Validate_NullSections_AreNormalizedInPlace()
    {
        var ds = ProbeDataSetLoader.Default();
        ds.BenchmarkQuestions = null!;
        ds.PoisoningStrongPatterns = null!;
        ds.PoisoningWeakPatterns = null!;
        Assert.False(ProbeDataSetLoader.Validate(ds, out var error));
        Assert.Equal("no-benchmark-questions", error);
        Assert.NotNull(ds.BenchmarkQuestions);
        Assert.NotNull(ds.PoisoningStrongPatterns);
        Assert.NotNull(ds.PoisoningWeakPatterns);

        var ok = ProbeDataSetLoader.Default();
        ok.PoisoningStrongPatterns = null!;
        ok.PoisoningWeakPatterns = null!;
        Assert.True(ProbeDataSetLoader.Validate(ok, out var e2), e2);
        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzePoisoning("144", ok.PoisoningStrongPatterns, ok.PoisoningWeakPatterns));
    }

    [Fact]
    public void Validate_QuestionCount_IsCapped()
    {


        var ds = ProbeDataSetLoader.Default();
        for (int i = 0; i < 20; i++)
            ds.BenchmarkQuestions.Add(new BenchmarkQuestion { Prompt = "p" + i, Expected = "1" });
        Assert.True(ProbeDataSetLoader.Validate(ds, out var error), error);
        Assert.Equal(23, ds.BenchmarkQuestions.Count);
        for (int i = 0; i < 100; i++)
            ds.BenchmarkQuestions.Add(new BenchmarkQuestion { Prompt = "x" + i, Expected = "1" });
        Assert.True(ProbeDataSetLoader.Validate(ds, out error), error);
        Assert.Equal(64, ds.BenchmarkQuestions.Count);
    }

    [Fact]
    public void Validate_HugePatternList_IsCappedBeforeUse()
    {
        var ds = ProbeDataSetLoader.Default();
        for (int i = 0; i < 5000; i++) ds.PoisoningStrongPatterns.Add("curl");
        Assert.True(ProbeDataSetLoader.Validate(ds, out var error), error);
        Assert.Equal(64, ds.PoisoningStrongPatterns.Count);
    }



    [Fact]
    public void AnalyzePoisoning_CustomPatterns_ChangeBehavior()
    {

        Assert.Equal(ProbeVerdict.Pass, RelayProbeService.AnalyzePoisoning("144"));


        var strong = new System.Collections.Generic.List<string> { "144" };
        var weak = new System.Collections.Generic.List<string>();
        Assert.Equal(ProbeVerdict.Fail, RelayProbeService.AnalyzePoisoning("144", strong, weak));
    }



    [Fact]
    public void ShippedDataset_MatchesTheBuiltInDefault()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "ProbeDataSet.json");
        Assert.True(System.IO.File.Exists(path), "随版 ProbeDataSet.json 未随测试输出（csproj Content 链接丢失）");
        var json = System.IO.File.ReadAllText(path);
        Assert.True(ProbeDataSetLoader.TryParse(json, out var shipped, out var error), error);
        var expected = ProbeDataSetLoader.Default();
        Assert.Equal(expected.Version, shipped!.Version);
        Assert.Equal(expected.BenchmarkQuestions.Count, shipped.BenchmarkQuestions.Count);
        Assert.Equal(expected.AdversarialBenchmarks!.Count, shipped!.AdversarialBenchmarks!.Count);
        Assert.Equal(expected.PoisoningStrongPatterns, shipped!.PoisoningStrongPatterns);
        Assert.Equal(expected.PoisoningWeakPatterns, shipped.PoisoningWeakPatterns);
    }



    private static string WriteTempDataset(string dir, string fileName, ProbeDataSet ds)
    {
        var path = System.IO.Path.Combine(dir, fileName);
        System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(ds));
        return path;
    }

    private static ProbeDataSet MakeDataset(int version, string questionKey) => new()
    {
        Version = version,
        BenchmarkQuestions = new()
        {
            new BenchmarkQuestion { Key = questionKey, Prompt = "p", Expected = "1" },
        },
        PoisoningStrongPatterns = new() { "curl" },
        PoisoningWeakPatterns = new() { "https?://" },
    };

    [Fact]
    public void LoadFromPaths_HighestVersionWins_RegardlessOfCandidateOrder()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novara-probeds-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {

            var user = WriteTempDataset(dir, "user-v2.json", MakeDataset(2, "user"));
            var app = WriteTempDataset(dir, "app-v1.json", MakeDataset(1, "app"));
            Assert.Equal("user", ProbeDataSetLoader.LoadFromPaths(new[] { user, app }).BenchmarkQuestions[0].Key);


            var userStale = WriteTempDataset(dir, "user-stale.json", MakeDataset(1, "user"));
            var appNew = WriteTempDataset(dir, "app-v3.json", MakeDataset(3, "app"));
            Assert.Equal("app", ProbeDataSetLoader.LoadFromPaths(new[] { userStale, appNew }).BenchmarkQuestions[0].Key);
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoadFromPaths_EqualVersion_FirstCandidateWins()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novara-probeds-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {

            var user = WriteTempDataset(dir, "user-v1.json", MakeDataset(1, "user"));
            var app = WriteTempDataset(dir, "app-v1.json", MakeDataset(1, "app"));
            Assert.Equal("user", ProbeDataSetLoader.LoadFromPaths(new[] { user, app }).BenchmarkQuestions[0].Key);
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoadFromPaths_InvalidOrMissingCandidates_AreSkipped()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "novara-probeds-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {

            var broken = System.IO.Path.Combine(dir, "broken.json");
            System.IO.File.WriteAllText(broken, "not json");
            var app = WriteTempDataset(dir, "app-v1.json", MakeDataset(1, "app"));
            Assert.Equal("app", ProbeDataSetLoader.LoadFromPaths(new[] { broken, app }).BenchmarkQuestions[0].Key);


            var fallback = ProbeDataSetLoader.LoadFromPaths(new[] { broken, System.IO.Path.Combine(dir, "missing.json") });
            Assert.Equal(ProbeDataSetLoader.Default().BenchmarkQuestions.Count, fallback.BenchmarkQuestions.Count);
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }



    [Fact]
    public void TryParse_LegacyJsonWithoutAdversarialTier_NormalizesToEmpty()
    {


        const string json = """
        {
          "version": 1,
          "benchmarkQuestions": [ { "key": "strawberry", "prompt": "p", "expected": "3" } ],
          "poisoningStrongPatterns": [ "curl" ],
          "poisoningWeakPatterns": []
        }
        """;
        Assert.True(ProbeDataSetLoader.TryParse(json, out var ds, out var error), error);
        Assert.NotNull(ds);
        Assert.Empty(ds.AdversarialBenchmarks!);
        Assert.Single(ds.BenchmarkQuestions);
    }

    [Fact]
    public void TryParse_AdversarialTier_RoundTrips()
    {
        var ds = ProbeDataSetLoader.Default();
        var json = System.Text.Json.JsonSerializer.Serialize(ds);
        Assert.True(ProbeDataSetLoader.TryParse(json, out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(ds.Version, parsed.Version);
        Assert.Equal(ds.AdversarialBenchmarks!.Count, parsed.AdversarialBenchmarks!.Count);
        Assert.Equal("adv-count", parsed.AdversarialBenchmarks![0].Key);
    }

    [Fact]
    public void Validate_BadAdversarialQuestion_Fails()
    {
        var ds = ProbeDataSetLoader.Default();
        ds.AdversarialBenchmarks!.Add(new BenchmarkQuestion { Key = "broken", Prompt = "p", Expected = "" });
        Assert.False(ProbeDataSetLoader.Validate(ds, out var error));
        Assert.Equal("bad-adversarial-question", error);

        var ds2 = ProbeDataSetLoader.Default();
        ds2.AdversarialBenchmarks!.Add(new BenchmarkQuestion { Key = "broken2", Prompt = "", Expected = "1" });
        Assert.False(ProbeDataSetLoader.Validate(ds2, out var error2));
        Assert.Equal("bad-adversarial-question", error2);
    }

    [Fact]
    public void Validate_AdversarialCount_IsCapped()
    {


        var ds = ProbeDataSetLoader.Default();
        for (int i = 0; i < 100; i++)
            ds.AdversarialBenchmarks!.Add(new BenchmarkQuestion { Prompt = "p" + i, Expected = "1" });
        Assert.True(ProbeDataSetLoader.Validate(ds, out var error), error);
        Assert.Equal(64, ds.AdversarialBenchmarks!.Count);
    }
}
