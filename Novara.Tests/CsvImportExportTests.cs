using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;

/// <summary>
/// N4A-07: CSV import parse-chain regression coverage (previously zero tests).
/// Covers: Novara dialect roundtrip, KeePass 1.x mapping, quoted-field space preservation (NC1),
/// formula-prefix strip on import (N2C-2), multi-line note continuation (NC4) and the
/// notes-fill-empty-only rule (N4A-03).
/// </summary>
public class CsvImportExportTests
{
    private static MemoEntry MakeEntry(string name, string type, string keyInfo, List<EntryField> fields)
        => new() { Name = name, Type = type, KeyInfo = keyInfo, Fields = fields };

    [Fact]
    public void RoundTrip_NovaraDialect_PreservesCoreColumns()
    {
        var entry = MakeEntry("站点A", "网站", "https://a.example.com", new List<EntryField>
        {
            new() { Label = "网址", Value = "https://a.example.com", CanCopy = true },
            new() { Label = "账号", Value = "user", CanCopy = true },
            new() { Label = "密码", Value = "p@ss word", CanCopy = true }, 
            new() { Label = "备注", Value = "line1\nline2", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());
        Assert.False(string.IsNullOrEmpty(csv));

        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.True(parsed.IsNovaraFormat);
        Assert.Single(parsed.Entries);
        var back = parsed.Entries[0];
        Assert.Equal("站点A", back.Name);
        Assert.Equal("p@ss word", FirstValue(back, "密码")); // NC1
        Assert.Equal("user", FirstValue(back, "账号"));
        
        Assert.Equal("line1\nline2", parsed.Notes0());
    }

    [Fact]
    public void ParseCsv_KeePassDialect_MapsLoginNameAndPassword()
    {
        var csv = "\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\n" +
                  "\"工作\",\"alice\",\"s3cret\",\"https://x.example.com\",\"note here\"";
        var parsed = CsvImportExportService.ParseCsv(csv, "账户");
        Assert.False(parsed.IsNovaraFormat);
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        
        Assert.Equal("工作", e.Name);
        Assert.Contains("alice", AllText(e));
        Assert.Contains("s3cret", AllText(e));
        Assert.Null(parsed.GroupNames[0]);
    }

    [Fact]
    public void ParseCsv_RealKeePassExport_IsRecognized()
    {
        
        var csv = "\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\n" +
                  "\"Sample Entry\",\"User Name\",\"Password\",\"https://keepass.info/\",\"Notes\"\n" +
                  "\"Sample Entry #2\",\"Michael321\",\"12345\",\"https://keepass.info/help/kb/testform.html\",\"\n";
        var parsed = CsvImportExportService.ParseCsv(csv, "账户");
        Assert.Equal(2, parsed.Entries.Count); 
        Assert.Equal(0, parsed.SkippedCount);
        Assert.Equal("Sample Entry", parsed.Entries[0].Name);
        Assert.Contains("User Name", AllText(parsed.Entries[0]));
        Assert.Contains("12345", AllText(parsed.Entries[1]));
    }

    [Fact]
    public void ParseCsv_FormulaPrefixIsStrippedOnImport()
    {
        
        var entry = MakeEntry("calc", "网站", "https://c.example.com", new List<EntryField>
        {
            new() { Label = "网址", Value = "https://c.example.com", CanCopy = true },
            new() { Label = "账号", Value = "u", CanCopy = true },
            new() { Label = "密码", Value = "=1+1", CanCopy = true },
            new() { Label = "备注", Value = "", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());
        Assert.Contains("'=1+1", csv);

        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.Single(parsed.Entries);
        Assert.Equal("=1+1", FirstValue(parsed.Entries[0], "密码"));
    }

    [Fact]
    public void ParseCsv_NoteLineFillsEmptyButNeverOverwritesColumnValue()
    {
        
        var csv = "Group,Type,Name,URL,Username,Password,Notes\n" +
                  ",网站,n1,https://n.example.com,u1,real-secret,\"密码: fake-from-note\"";
        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.Single(parsed.Entries);
        Assert.Equal("real-secret", FirstValue(parsed.Entries[0], "密码"));
        Assert.DoesNotContain("fake-from-note", FirstValue(parsed.Entries[0], "密码"));
    }

    private static string? FirstValue(MemoEntry e, string label)
        => e.Fields.FirstOrDefault(f => f.Label == label)?.Value;

    private static string AllText(MemoEntry e)
        => string.Join("\n", new[] { e.Name, e.KeyInfo }.Concat(e.Fields.Select(f => f.Label + ":" + f.Value)));
}

// N4A-07: tiny accessor so tests can read the raw Notes column round-tripped through fields.
file static class CsvTestExtensions
{
    public static string Notes0(this CsvImportResult r)
        => r.Entries.Count > 0 ? (r.Entries[0].Fields.FirstOrDefault(f => f.Label == "备注")?.Value ?? "") : "";
}
