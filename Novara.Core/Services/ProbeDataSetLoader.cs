/* ========== ProbeDataSet + Loader (API upgrade Step 4) ==========
Function: externalize the relay-probe dataset (benchmark questions / poisoning regex) into a local
JSON file so fingerprints can be updated WITHOUT code changes.
Freeze constraint: no runtime hot-reload - replace the file then restart the app. Loader validates the
file and falls back to the built-in default on any parse/validation error (never crash on a bad file).
*/
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

    /// <summary>Built-in default dataset - the fallback used when the JSON file is missing or invalid.
    /// Kept in sync with the shipped ProbeDataSet.json.</summary>
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

    /// <summary>Validate a dataset: required sections present + every regex compiles (a bad external
    /// regex must not crash the app at probe time).</summary>
    public static bool Validate(ProbeDataSet? ds, out string error)
    {
        error = "";
        if (ds == null) { error = "empty"; return false; }
        if (ds.BenchmarkQuestions == null || ds.BenchmarkQuestions.Count == 0) { error = "no-benchmark-questions"; return false; }
        
        foreach (var q in ds.BenchmarkQuestions)
        {
            if (string.IsNullOrWhiteSpace(q.Prompt) || string.IsNullOrWhiteSpace(q.Expected))
            { error = "bad-benchmark-question"; return false; }
        }
        var patterns = (ds.PoisoningStrongPatterns ?? new()) .Concat(ds.PoisoningWeakPatterns ?? new());
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { _ = new Regex(p); }
            catch (System.Exception ex) { error = "bad-regex: " + p + " (" + ex.Message + ")"; return false; }
        }
        return true;
    }

    /// <summary>Parse + validate JSON. Returns null + an error string on any failure (never throws).</summary>
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

    /// <summary>Load the shipped / externally-replaced ProbeDataSet.json from the app directory
    /// (restart-to-apply, no runtime hot-reload). Falls back to Default() when the file is missing or
    /// invalid - never throws, never crashes the probe suite.</summary>
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
