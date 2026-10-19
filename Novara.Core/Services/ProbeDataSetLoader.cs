using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Novara.Services;

public sealed class ProbeDataSet
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("benchmarkQuestions")] public System.Collections.Generic.List<BenchmarkQuestion> BenchmarkQuestions { get; set; } = new();
    [JsonPropertyName("adversarialBenchmarks")] public System.Collections.Generic.List<BenchmarkQuestion>? AdversarialBenchmarks { get; set; } = new();
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




    private const int MaxBenchmarkQuestions = 64;


    private const int MaxAdversarialQuestions = 64;



    public static ProbeDataSet Default() => new()
    {
        Version = 2,
        BenchmarkQuestions = new()
        {
            new BenchmarkQuestion { Key = "strawberry", Prompt = "How many times does the letter 'r' appear in the word 'strawberry'? Answer with just the number.", Expected = "3" },
            new BenchmarkQuestion { Key = "compare", Prompt = "Which number is larger: 9.11 or 9.9? Answer with just the number.", Expected = "9.9" },
            new BenchmarkQuestion { Key = "math", Prompt = "What is 24 * 7 + 5? Answer with just the number.", Expected = "173" },
        },


        AdversarialBenchmarks = new()
        {
            new BenchmarkQuestion { Key = "adv-count", Prompt = "How many times does the letter 'o' appear in the word 'collaboration'? Answer with just the number.", Expected = "3" },
            new BenchmarkQuestion { Key = "adv-words", Prompt = "How many words are in the sentence 'The quick brown fox jumps over the lazy dog'? Answer with just the number.", Expected = "9" },
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





    internal static void Normalize(ProbeDataSet ds)
    {
        ds.BenchmarkQuestions ??= new();
        ds.AdversarialBenchmarks ??= new();
        ds.PoisoningStrongPatterns ??= new();
        ds.PoisoningWeakPatterns ??= new();
        if (ds.BenchmarkQuestions.Count > MaxBenchmarkQuestions)
            ds.BenchmarkQuestions = ds.BenchmarkQuestions.Take(MaxBenchmarkQuestions).ToList();
        if (ds.AdversarialBenchmarks.Count > MaxAdversarialQuestions)
            ds.AdversarialBenchmarks = ds.AdversarialBenchmarks.Take(MaxAdversarialQuestions).ToList();
        if (ds.PoisoningStrongPatterns.Count > MaxPoisoningPatterns)
            ds.PoisoningStrongPatterns = ds.PoisoningStrongPatterns.Take(MaxPoisoningPatterns).ToList();
        if (ds.PoisoningWeakPatterns.Count > MaxPoisoningPatterns)
            ds.PoisoningWeakPatterns = ds.PoisoningWeakPatterns.Take(MaxPoisoningPatterns).ToList();
    }



    public static bool Validate(ProbeDataSet? ds, out string error)
    {
        error = "";
        if (ds == null) { error = "empty"; return false; }
        Normalize(ds);
        if (ds.BenchmarkQuestions.Count == 0) { error = "no-benchmark-questions"; return false; }

        foreach (var q in ds.BenchmarkQuestions)
        {
            if (q == null || string.IsNullOrWhiteSpace(q.Prompt) || string.IsNullOrWhiteSpace(q.Expected))
            { error = "bad-benchmark-question"; return false; }
        }


        foreach (var q in ds.AdversarialBenchmarks!)
        {
            if (q == null || string.IsNullOrWhiteSpace(q.Prompt) || string.IsNullOrWhiteSpace(q.Expected))
            { error = "bad-adversarial-question"; return false; }
        }
        var patterns = ds.PoisoningStrongPatterns.Concat(ds.PoisoningWeakPatterns);
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) { error = "bad-pattern-null"; return false; }
            try { _ = new Regex(p); }
            catch (System.Exception ex) { error = "bad-regex: " + p + " (" + ex.Message + ")"; return false; }
        }
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




    public static string UserDataSetPath =>
        System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), CoreEnv.DataDirName, DefaultFileName);





    public static ProbeDataSet LoadFromFile()
    {
        return LoadFromPaths(new[]
        {
            UserDataSetPath,
            System.IO.Path.Combine(System.AppContext.BaseDirectory, DefaultFileName),
        });
    }




    internal static ProbeDataSet LoadFromPaths(System.Collections.Generic.IReadOnlyList<string> paths)
    {
        ProbeDataSet? best = null;
        foreach (var path in paths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) continue;
                if (!TryParse(System.IO.File.ReadAllText(path), out var ds, out _)) continue;
                if (best == null || ds!.Version > best.Version) best = ds;
            }
            catch { }
        }
        return best ?? Default();
    }
}
