using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Novara.Services;

public sealed class ProbeDataSet
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("benchmarkQuestions")] public System.Collections.Generic.List<BenchmarkQuestion> BenchmarkQuestions { get; set; } = new();
    [JsonPropertyName("poisoningStrongPatterns")] public System.Collections.Generic.List<string> PoisoningStrongPatterns { get; set; } = new();
    [JsonPropertyName("poisoningWeakPatterns")] public System.Collections.Generic.List<string> PoisoningWeakPatterns { get; set; } = new();
}

public sealed class BenchmarkQuestion
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("expected")] public string Expected { get; set; } = "";
}

public static class ProbeDataSetLoader
{
    public const string DefaultFileName = "ProbeDataSet.json";

    private const int MaxPoisoningPatterns = 64;



    public static ProbeDataSet Default() => new()
    {
        Version = 1,
        BenchmarkQuestions = new()
        {
            new BenchmarkQuestion { Key = "strawberry", Prompt = "How many times does the letter 'r' appear in the word 'strawberry'? Answer with just the number.", Expected = "3" },
            new BenchmarkQuestion { Key = "compare", Prompt = "Which number is larger: 9.11 or 9.9? Answer with just the number.", Expected = "9.9" },
            new BenchmarkQuestion { Key = "math", Prompt = "What is 24 * 7 + 5? Answer with just the number.", Expected = "173" },
        },
        PoisoningStrongPatterns = new()
        {
            @"\b(curl|wget)\s", @"\b(sh|bash)\s+-c", @"powershell", @"cmd\.exe",
            @"[\u200B-\u200D\uFEFF]",
            @"data:image/[^;]+;base64,[A-Za-z0-9+/=]{100,}",
        },
        PoisoningWeakPatterns = new()
        {
            @"!\[[^\]]*\]\(https?://",
            @"[A-Za-z0-9+/=]{200,}",
        },
    };



    public static bool Validate(ProbeDataSet? ds, out string error)
    {
        error = "";
        if (ds == null) { error = "empty"; return false; }
        if (ds.BenchmarkQuestions == null || ds.BenchmarkQuestions.Count == 0) { error = "no-benchmark-questions"; return false; }

        foreach (var q in ds.BenchmarkQuestions)
        {
            if (q == null || string.IsNullOrWhiteSpace(q.Prompt) || string.IsNullOrWhiteSpace(q.Expected))
            { error = "bad-benchmark-question"; return false; }
        }
        var patterns = (ds.PoisoningStrongPatterns ?? new()) .Concat(ds.PoisoningWeakPatterns ?? new());
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) { error = "bad-pattern-null"; return false; }
            try { _ = new Regex(p); }
            catch (System.Exception ex) { error = "bad-regex: " + p + " (" + ex.Message + ")"; return false; }
        }



        if (ds.BenchmarkQuestions.Count > 7)
            ds.BenchmarkQuestions = ds.BenchmarkQuestions.Take(7).ToList();



        if ((ds.PoisoningStrongPatterns?.Count ?? 0) > MaxPoisoningPatterns)
            ds.PoisoningStrongPatterns = ds.PoisoningStrongPatterns!.Take(MaxPoisoningPatterns).ToList();
        if ((ds.PoisoningWeakPatterns?.Count ?? 0) > MaxPoisoningPatterns)
            ds.PoisoningWeakPatterns = ds.PoisoningWeakPatterns!.Take(MaxPoisoningPatterns).ToList();
        return true;
    }


    public static bool TryParse(string json, out ProbeDataSet? dataSet, out string error)
    {
        dataSet = null;
        error = "";
        try
        {
            var parsed = JsonSerializer.Deserialize<ProbeDataSet>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (!Validate(parsed, out error)) return false;
            dataSet = parsed;
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }




    public static ProbeDataSet LoadFromFile()
    {
        try
        {
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, DefaultFileName);
            if (System.IO.File.Exists(path) && TryParse(System.IO.File.ReadAllText(path), out var ds, out _))
                return ds!;
        }
        catch { }
        return Default();
    }
}
