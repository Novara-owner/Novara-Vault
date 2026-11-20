using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealFaultInjectionTests
{
    private const string ProbeRootVariable = "NOVARA_SEAL_FAULT_PROBE";
    private const string ProbeOutVariable = "NOVARA_SEAL_FAULT_OUT";
    private const string OnlyVariable = "NOVARA_SEAL_FAULT_ONLY";

    [Fact]
    public void EmitFixtures()
    {
        var root = SealFaultFixtures.DefaultRoot();
        var names = SealFaultFixtures.Emit(root);

        Assert.Equal(9, names.Count);
        Assert.Equal(9, Directory.EnumerateDirectories(root).Count());

        foreach (var name in names)
        {
            Assert.True(File.Exists(Path.Combine(root, name, "expected.json")));
            Assert.True(File.Exists(Path.Combine(root, name, "seal", "index.nseal")));
            Assert.True(File.Exists(Path.Combine(root, name, "seal", "journal.nsj")));
        }
    }

    [Fact]
    public void TheFixtureMatrixPlansAsDocumented()
    {
        var root = SealFaultFixtures.DefaultRoot();



        if (!Directory.Exists(root)) SealFaultFixtures.Emit(root);

        var cases = ReadCases(root);
        Assert.Equal(9, cases.Count);

        var failures = new List<string>();

        foreach (var item in cases)
        {
            var (status, actions) = SealFaultProbe.Plan(Path.Combine(root, item.Case), SealFaultFixtures.TestVmk);

            if (!actions.SequenceEqual(item.Actions))
            {
                failures.Add(
                    $"{item.Case}: journal={status} expected [{string.Join(", ", item.Actions)}] " +
                    $"actual [{string.Join(", ", actions)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }





    [Fact]
    public void ProbeModeWritesTheMatrixAsJson()
    {
        var probeRoot = Environment.GetEnvironmentVariable(ProbeRootVariable);
        var outFile = Environment.GetEnvironmentVariable(ProbeOutVariable);
        if (string.IsNullOrEmpty(probeRoot) || string.IsNullOrEmpty(outFile)) return;

        var only = Environment.GetEnvironmentVariable(OnlyVariable);
        var results = new List<ProbeResult>();

        foreach (var item in ReadCases(probeRoot))
        {
            if (!string.IsNullOrEmpty(only) && !string.Equals(item.Case, only, StringComparison.Ordinal)) continue;

            var (status, actions) = SealFaultProbe.Plan(Path.Combine(probeRoot, item.Case), SealFaultFixtures.TestVmk);

            results.Add(new ProbeResult(
                item.Case,
                item.Setup,
                item.Actions,
                actions,
                status,
                actions.SequenceEqual(item.Actions)));
        }

        File.WriteAllText(
            outFile,
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IReadOnlyList<FixtureCase> ReadCases(string root)
    {
        var cases = new List<FixtureCase>();

        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var expectedPath = Path.Combine(directory, "expected.json");
            if (!File.Exists(expectedPath)) continue;

            var expected = JsonSerializer.Deserialize<ExpectedJson>(File.ReadAllText(expectedPath))
                           ?? throw new InvalidOperationException($"unreadable fixture description: {expectedPath}");

            cases.Add(new FixtureCase(
                expected.Case ?? Path.GetFileName(directory),
                expected.Setup ?? "",
                expected.Actions ?? []));
        }

        return cases;
    }

    private sealed record FixtureCase(string Case, string Setup, string[] Actions);

    private sealed class ExpectedJson
    {
        [JsonPropertyName("case")]
        public string? Case { get; set; }

        [JsonPropertyName("setup")]
        public string? Setup { get; set; }

        [JsonPropertyName("actions")]
        public string[]? Actions { get; set; }
    }

    private sealed record ProbeResult(
        [property: JsonPropertyName("case")] string Case,
        [property: JsonPropertyName("setup")] string Setup,
        [property: JsonPropertyName("expected")] string[] Expected,
        [property: JsonPropertyName("actual")] string[] Actual,
        [property: JsonPropertyName("journalStatus")] string JournalStatus,
        [property: JsonPropertyName("pass")] bool Pass);
}






internal static class SealFaultProbe
{
    internal static (string Status, string[] Actions) Plan(string caseRoot, byte[] vmk)
    {
        var paths = new SealSpacePaths(Path.Combine(caseRoot, "seal"));

        var index = SealIndex.Load(paths.Index, vmk);
        var journal = SealJournal.Load(paths.Journal, vmk);
        var root = Path.GetFullPath(caseRoot);

        foreach (var record in journal.Records)
        {
            record.SourcePath = Expand(record.SourcePath, root);
            if (record.TargetPath is not null) record.TargetPath = Expand(record.TargetPath, root);
        }

        var plan = SealRecovery.Plan(journal, index, new SealFileProbe(paths, index));

        return (plan.JournalStatus.ToString(),
            plan.Actions.Select(a => a.Kind.ToString()).ToArray());
    }

    private static string Expand(string value, string root)
        => value.Replace("%ROOT%", root, StringComparison.Ordinal);
}
