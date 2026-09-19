using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;






public class MemoFieldMaskTests
{
    [Theory]
    [InlineData("密码", true)]
    [InlineData("邮箱密码", true)]
    [InlineData("CVV", true)]
    [InlineData("API Key", true)]
    [InlineData("卡号", true)]
    [InlineData("证件号", true)]
    [InlineData("账号", false)]
    [InlineData("邮箱地址", false)]
    [InlineData("网址", false)]
    [InlineData("网络名", false)]
    [InlineData("备注", false)]
    [InlineData("信息", false)]
    [InlineData("", false)]
    [InlineData(null, false)]

    [InlineData("password", true)]
    [InlineData("Password", true)]
    [InlineData("PASSWORD", true)]
    [InlineData("passwd", true)]
    [InlineData("apikey", true)]
    [InlineData("api_key", true)]
    [InlineData("api key", true)]
    [InlineData("密钥", true)]

    [InlineData("key", false)]
    [InlineData("token", false)]
    [InlineData("secret", false)]
    [InlineData("自定义字段", false)]
    public void IsMaskedLabel_ClassifiesEveryStoredLabel(string? label, bool expected)
    {
        Assert.Equal(expected, MemoFieldMask.IsMaskedLabel(label));
    }

    [Theory]
    [InlineData("银行卡", true)]
    [InlineData("证件", true)]
    [InlineData("邮箱", false)]
    [InlineData("账户", false)]
    [InlineData("网站", false)]
    [InlineData("WiFi", false)]
    [InlineData("API Key", false)]
    [InlineData("自定义", false)]
    public void IsMaskedKeyInfo_CoversTheCollapsedSubtitle(string type, bool expected)
    {
        Assert.Equal(expected, MemoFieldMask.IsMaskedKeyInfo(type));
    }

    [Fact]
    public void KeyInfoLabelFor_MirrorsTheStoredLabels()
    {
        Assert.Equal("卡号", MemoFieldMask.KeyInfoLabelFor("银行卡"));
        Assert.Equal("证件号", MemoFieldMask.KeyInfoLabelFor("证件"));
        Assert.Equal("URL", MemoFieldMask.KeyInfoLabelFor("API Key"));
        Assert.Equal("信息", MemoFieldMask.KeyInfoLabelFor("未知类型"));
    }

    [Fact]
    public void Mask_IsFixedLength_SoTheValueLengthDoesNotLeak()
    {
        Assert.Equal(8, MemoFieldMask.Mask.Length);
    }







    [Fact]
    public void McpSensitiveLabels_CoverEveryLabelTheUiMasks()
    {
        foreach (var label in MemoFieldMask.MaskedLabelNames)
            Assert.True(McpLogic.IsSensitiveLabel(label), $"MCP 未把 UI 的掩码 label「{label}」判为敏感");
    }

    [Fact]
    public void McpSensitiveLabels_AddTheCredentialAliasesMcpItselfAccepts()
    {
        foreach (var alias in new[] { "password", "Password", "密钥", "secret", "token", "key",
                                      "api key", "API Key", "apikey", "api_key", "passwd", "cvv", "CVV", "totp" })
            Assert.True(McpLogic.IsSensitiveLabel(alias), $"MCP 未把凭据别名「{alias}」判为敏感");
    }

    [Fact]
    public void McpSensitivityIsASuperset_NotTheUiJudgement()
    {


        Assert.True(MemoFieldMask.IsMaskedLabel("Password"));
        Assert.True(McpLogic.IsSensitiveLabel("Password"));
        Assert.True(MemoFieldMask.IsMaskedLabel("密钥"));
        Assert.True(McpLogic.IsSensitiveLabel("密钥"));

        Assert.False(MemoFieldMask.IsMaskedLabel("key"));
        Assert.True(McpLogic.IsSensitiveLabel("key"));
        Assert.False(MemoFieldMask.IsMaskedLabel("token"));
        Assert.True(McpLogic.IsSensitiveLabel("token"));

        Assert.False(MemoFieldMask.IsMaskedLabel("账号"));
        Assert.False(McpLogic.IsSensitiveLabel("账号"));
    }

    [Fact]
    public void UiAndMcpMaskingAgreeOnNormalisation_IncludingWhitespace()
    {



        foreach (var label in new[] { "密码 ", " 密码", "Password ", "  API Key  ", "卡号\t" })
        {
            Assert.True(MemoFieldMask.IsMaskedLabel(label), $"UI 未掩码带空白的「{label}」");
            Assert.True(McpLogic.IsSensitiveLabel(label), $"MCP 未判敏感：{label}");
        }
    }



    public static IEnumerable<object[]> AllMemoTypes() => MemoEntryTypes.All.Select(t => new object[] { t });

    [Fact]
    public void MemoEntryTypes_ListAndTemplatesCoverTheSameTypes()
    {
        Assert.Equal(8, MemoEntryTypes.All.Count);
        Assert.Equal(MemoEntryTypes.All.Count, MemoEntryTypes.All.Distinct().Count());

        Assert.Equal(MemoEntryTypes.All.OrderBy(t => t, StringComparer.Ordinal),
                     MemoEntryTypes.FieldLabels.Keys.OrderBy(t => t, StringComparer.Ordinal));
        Assert.True(MemoEntryTypes.IsKnown("银行卡"));
        Assert.False(MemoEntryTypes.IsKnown("第 9 个新类型"));
        Assert.Empty(MemoEntryTypes.FieldLabelsFor("第 9 个新类型"));
    }





    [Theory]
    [MemberData(nameof(AllMemoTypes))]
    public void KeyInfoLabelFor_IsConsistentWithTheTypeTemplate(string type)
    {
        var label = MemoFieldMask.KeyInfoLabelFor(type);
        var template = MemoEntryTypes.FieldLabels[type];
        if (type == MemoEntryTypes.Custom)
        {

            Assert.Empty(template);
            Assert.Equal("信息", label);
        }
        else
        {
            Assert.Contains(label, template);
        }
        Assert.Equal(MemoFieldMask.IsMaskedLabel(label), MemoFieldMask.IsMaskedKeyInfo(type));
    }



    [Fact]
    public void KeyInfoLabelFor_HasNoKnownTypeFallingIntoTheDefaultArm()
    {
        foreach (var type in MemoEntryTypes.All.Where(t => t != MemoEntryTypes.Custom))
            Assert.NotEqual("信息", MemoFieldMask.KeyInfoLabelFor(type));
        Assert.Equal("信息", MemoFieldMask.KeyInfoLabelFor(MemoEntryTypes.Custom));
        Assert.Equal("信息", MemoFieldMask.KeyInfoLabelFor("第 9 个新类型"));
    }




    [Theory]
    [MemberData(nameof(AllMemoTypes))]
    public void CsvImport_LandsKeyInfoFromTheSingleSourceLabel(string type)
    {
        var labels = MemoEntryTypes.FieldLabels[type];
        var fields = labels.Length == 0
            ? new List<EntryField> { new() { Label = "信息", Value = "随手记", CanCopy = true } }
            : labels.Select((l, i) => new EntryField { Label = l, Value = $"v{i}", CanCopy = true }).ToList();
        var entry = new MemoEntry { Name = "n", Type = type, KeyInfo = "ignored", Fields = fields };

        var csv = CsvImportExportService.ExportMemoEntriesToCsv(new List<MemoEntry> { entry }, new List<MemoGroup>());
        var parsed = CsvImportExportService.ParseCsv(csv, type);
        var imported = Assert.Single(parsed.Entries);

        var kiLabel = MemoFieldMask.KeyInfoLabelFor(type);
        Assert.Equal(imported.Fields.FirstOrDefault(f => f.Label == kiLabel)?.Value ?? "", imported.KeyInfo);
        Assert.False(string.IsNullOrEmpty(imported.KeyInfo));
    }



    [Theory]
    [InlineData("银行卡", "6222020200112233445", "••••••••")]
    [InlineData("证件", "110101199001011234", "••••••••")]
    [InlineData("账户", "alice", "alice")]
    [InlineData("API Key", "https://api.example.com", "https://api.example.com")]
    [InlineData("自定义", "随手记", "随手记")]
    [InlineData("银行卡", "", "")]
    [InlineData("银行卡", null, "")]
    [InlineData("证件", "   ", "")]
    public void MaskedKeyInfo_DoesNotMaskAnEmptyValue(string type, string? keyInfo, string expected)
    {
        Assert.Equal(expected, MemoFieldMask.MaskedKeyInfo(type, keyInfo));
    }



    [Fact]
    public void MaskedKeyInfo_EmptyKeyInfoWithoutFallbackYieldsEmptyString()
    {
        Assert.Equal("", MemoFieldMask.MaskedKeyInfo("银行卡", ""));
        Assert.Equal("", MemoFieldMask.MaskedKeyInfo("证件", null));
    }




    [Fact]
    public void MaskedKeyInfo_FallsBackToTheFirstNonEmptyField()
    {
        var idFields = new (string Label, string Value)[] { ("证件号", ""), ("姓名", "张三"), ("备注", "x") };
        Assert.Equal("张三", MemoFieldMask.MaskedKeyInfo("证件", "", idFields));
        Assert.Equal("张三", MemoFieldMask.MaskedKeyInfo("证件", null, idFields));


        Assert.Equal(MemoFieldMask.Mask,
            MemoFieldMask.MaskedKeyInfo("账户", "", new (string, string)[] { ("密码", "p@ss") }));


        Assert.Equal("alice",
            MemoFieldMask.MaskedKeyInfo("账户", "alice", new (string, string)[] { ("密码", "p@ss") }));


        Assert.Equal("", MemoFieldMask.MaskedKeyInfo("银行卡", "", new (string, string)[] { ("卡号", "") }));
    }
}
