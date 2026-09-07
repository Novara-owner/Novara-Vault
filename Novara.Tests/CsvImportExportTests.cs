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
    public void RoundTrip_BankCardAndIdCard_SecondaryFieldsNotPolluted()
    {
        
        
        var bank = MakeEntry("我的银行卡", "银行卡", "622200001", new List<EntryField>
        {
            new() { Label = "卡号", Value = "622200001", CanCopy = true },
            new() { Label = "持卡人", Value = "张三", CanCopy = false },
            new() { Label = "有效期", Value = "2030/12", CanCopy = false },
            new() { Label = "CVV", Value = "999", CanCopy = true },
            new() { Label = "密码", Value = "bankpw", CanCopy = true },
            new() { Label = "备注", Value = "工资卡", CanCopy = true },
        });
        var idcard = MakeEntry("我的证件", "证件", "110101", new List<EntryField>
        {
            new() { Label = "证件号", Value = "110101", CanCopy = true },
            new() { Label = "姓名", Value = "李四", CanCopy = false },
            new() { Label = "签发机构", Value = "某分局", CanCopy = false },
            new() { Label = "有效期", Value = "2035/01", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { bank, idcard }, new List<MemoGroup>());

        var result = CsvImportExportService.ParseCsv(csv, null!);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(2, result.Entries.Count);

        var bankBack = result.Entries.Single(e => e.Type == "银行卡");
        Assert.Equal("622200001", GetFieldBy(bankBack, "卡号"));
        Assert.Equal("张三", GetFieldBy(bankBack, "持卡人"));
        Assert.Equal("2030/12", GetFieldBy(bankBack, "有效期"));
        Assert.Equal("999", GetFieldBy(bankBack, "CVV"));
        Assert.Equal("bankpw", GetFieldBy(bankBack, "密码"));
        Assert.Equal("工资卡", GetFieldBy(bankBack, "备注"));

        var idBack = result.Entries.Single(e => e.Type == "证件");
        Assert.Equal("110101", GetFieldBy(idBack, "证件号"));
        Assert.Equal("李四", GetFieldBy(idBack, "姓名"));
        Assert.Equal("某分局", GetFieldBy(idBack, "签发机构"));
        Assert.Equal("2035/01", GetFieldBy(idBack, "有效期"));
    }

    private static string GetFieldBy(MemoEntry entry, string label)
        => entry.Fields.FirstOrDefault(f => f.Label == label)?.Value ?? "";

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
    public void RoundTrip_QuoteLeadingValues_NotCorrupted()
    {
        // N5-S14-01/N5-RC-04: '-leading values must escape on export (CsvEscape adds one quote) and
        // strip exactly one quote on import. The old pair was asymmetric - '=secret exported verbatim
        // then imported as =secret (silent corruption), and ''=x lost a quote through the
        // unconditional '\'' strip branch that had no export-side producer.
        var entry = MakeEntry("q", "网站", "https://q.example.com", new List<EntryField>
        {
            new() { Label = "网址", Value = "https://q.example.com", CanCopy = true },
            new() { Label = "账号", Value = "'=secret", CanCopy = true },
            new() { Label = "密码", Value = "''=x", CanCopy = true },
            new() { Label = "备注", Value = "", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());
        Assert.Contains("''=secret", csv); // export escapes the leading quote

        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.Single(parsed.Entries);
        Assert.Equal("'=secret", FirstValue(parsed.Entries[0], "账号"));
        Assert.Equal("''=x", FirstValue(parsed.Entries[0], "密码"));
    }

    [Fact]
    public void RoundTrip_SpaceLeadingFormula_Symmetric()
    {
        // N4-48 escapes past leading whitespace on export; the import strip must probe the same way
        // (' followed by whitespace-then-'='), otherwise ' =SUM(A1) kept a stray leading quote.
        var entry = MakeEntry("ws", "网站", "https://w.example.com", new List<EntryField>
        {
            new() { Label = "网址", Value = "https://w.example.com", CanCopy = true },
            new() { Label = "账号", Value = " =SUM(A1)", CanCopy = true },
            new() { Label = "密码", Value = "plain", CanCopy = true },
            new() { Label = "备注", Value = "", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());

        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.Single(parsed.Entries);
        Assert.Equal(" =SUM(A1)", FirstValue(parsed.Entries[0], "账号"));
    }

    [Fact]
    public void Import_QuoteLeadingPlainText_Unchanged()
    {
        // The strip rule must stay narrow: a value whose post-quote content is not escape-worthy
        // (plain 'hello, a lone quote) never loses its quote.
        var entry = MakeEntry("n", "网站", "https://n.example.com", new List<EntryField>
        {
            new() { Label = "网址", Value = "https://n.example.com", CanCopy = true },
            new() { Label = "账号", Value = "'hello", CanCopy = true },
            new() { Label = "密码", Value = "'", CanCopy = true },
            new() { Label = "备注", Value = "", CanCopy = false },
        });
        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());

        var parsed = CsvImportExportService.ParseCsv(csv, "自定义");
        Assert.Single(parsed.Entries);
        Assert.Equal("'hello", FirstValue(parsed.Entries[0], "账号"));
        Assert.Equal("'", FirstValue(parsed.Entries[0], "密码"));
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

    // ==================== 9.3: browser / manager dialects ====================

    [Fact]
    public void ParseCsv_ChromeDialect_MapsNameUrlUsernamePassword()
    {
        var csv = "name,url,username,password\nGitHub,https://github.com,octocat,pass123";
        var parsed = CsvImportExportService.ParseCsv(csv, "网站");
        Assert.False(parsed.IsNovaraFormat);
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        Assert.Equal("GitHub", e.Name);
        Assert.Equal("网站", e.Type);
        Assert.Equal("https://github.com", FirstValue(e, "网址"));
        Assert.Equal("octocat", FirstValue(e, "账号"));
        Assert.Equal("pass123", FirstValue(e, "密码"));
    }

    [Fact]
    public void ParseCsv_FirefoxDialect_DerivesNameFromUrlHost()
    {
        var csv = "url,username,password,httpRealm,formActionOrigin\n" +
                  "https://example.com/login,user1,pw1,,https://example.com/login";
        var parsed = CsvImportExportService.ParseCsv(csv, "网站");
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        Assert.Equal("example.com", e.Name); // no name column - host is the name
        Assert.Equal("user1", FirstValue(e, "账号"));
        Assert.Equal("pw1", FirstValue(e, "密码"));
    }

    [Fact]
    public void ParseCsv_OnePasswordDialect_MapsTitleAndNotes()
    {
        var csv = "\"Title\",\"Url\",\"Username\",\"Password\",\"Notes\"\n" +
                  "\"GH\",\"https://github.com\",\"octocat\",\"pass123\",\"my note\"";
        var parsed = CsvImportExportService.ParseCsv(csv, "网站");
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        Assert.Equal("GH", e.Name);
        Assert.Equal("https://github.com", FirstValue(e, "网址"));
        Assert.Contains("my note", AllText(e));
    }

    [Fact]
    public void ParseCsv_ProtonPassDialect_MapsItemNameAndNote()
    {
        var csv = "\"Item Name\",\"Title\",\"Url\",\"Username\",\"Password\",\"Note\"\n" +
                  "\"GH\",\"login\",\"https://github.com\",\"octocat\",\"pass123\",\"proton note\"";
        var parsed = CsvImportExportService.ParseCsv(csv, "网站");
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        Assert.Equal("GH", e.Name);
        Assert.Equal("octocat", FirstValue(e, "账号"));
        Assert.Contains("proton note", AllText(e));
    }

    [Fact]
    public void ParseCsv_NovaraFormat_NotHijackedByBrowserDialects()
    {
        // Novara's own CSV lowercases to username/url keys through the ignore-case dictionary -
        // the browser branches must not strip its Notes-encoded extra fields.
        var csv = "\"Group\",\"Type\",\"Name\",\"URL\",\"Username\",\"Password\",\"Notes\"\n" +
                  "\"\",\"网站\",\"GH\",\"https://github.com\",\"octocat\",\"pass123\",\"备注: keep me\"";
        var parsed = CsvImportExportService.ParseCsv(csv, "网站");
        Assert.True(parsed.IsNovaraFormat);
        Assert.Single(parsed.Entries);
        var e = parsed.Entries[0];
        Assert.Equal("GH", e.Name);
        Assert.Equal("keep me", FirstValue(e, "备注")); // notes-encoded field must survive
    }
}

// N4A-07: tiny accessor so tests can read the raw Notes column round-tripped through fields.
file static class CsvTestExtensions
{
    public static string Notes0(this CsvImportResult r)
        => r.Entries.Count > 0 ? (r.Entries[0].Fields.FirstOrDefault(f => f.Label == "备注")?.Value ?? "") : "";
}
