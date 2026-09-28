using Novara.Models;
using Novara.Services;
using Xunit;

namespace Novara.Tests;





public class ImageExportTemplateTests
{
    private static MemoGroup Group(string name, bool pinned = false, int daysAgo = 10)
        => new() { Id = Guid.NewGuid(), Name = name, IsPinned = pinned, CreatedAt = DateTime.UtcNow.AddDays(-daysAgo) };

    private static MemoEntry Entry(Guid? groupId, string name, params EntryField[] fields)
        => new() { Id = Guid.NewGuid(), GroupId = groupId, Name = name, Fields = fields.ToList(), CreatedAt = DateTime.UtcNow };

    [Fact]
    public void MemoHtml_RendersGroupsEntriesFields_AndSkipsEmptyGroup()
    {
        var g = Group("工作");
        var groups = new List<MemoGroup> { g, Group("空组") };
        var entries = new List<MemoEntry>
        {
            Entry(g.Id, "站点A", new EntryField { Label = "网址", Value = "https://a.example" }),
        };

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.Contains("工作", html);
        Assert.Contains("站点A", html);
        Assert.Contains("网址", html);
        Assert.Contains("https://a.example", html);

        Assert.DoesNotContain("空组", html);

        Assert.DoesNotContain("Export_Section_Uncategorized", html);
    }

    [Fact]
    public void MemoHtml_UncategorizedSectionComesLast()
    {
        var g = Group("甲组");
        var groups = new List<MemoGroup> { g };
        var entries = new List<MemoEntry>
        {
            Entry(null, "未归类条目"),
            Entry(g.Id, "组内条目"),
        };

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.True(html.IndexOf("甲组", StringComparison.Ordinal) < html.IndexOf("Export_Section_Uncategorized", StringComparison.Ordinal));
        Assert.True(html.IndexOf("组内条目", StringComparison.Ordinal) < html.IndexOf("未归类条目", StringComparison.Ordinal));
    }

    [Fact]
    public void MemoHtml_OrphanGroupId_FallsIntoUncategorized()
    {
        var orphan = Guid.NewGuid();
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry> { Entry(orphan, "孤儿条目") };

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.Contains("孤儿条目", html);
        Assert.Contains("Export_Section_Uncategorized", html);
    }

    [Fact]
    public void MemoHtml_SoftDeletedFiltered_TotpSkipped_EmptyFieldSkipped()
    {
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry>
        {
            Entry(null, "正常条目",
                new EntryField { Label = "TOTP", Value = "JBSWY3DPEHPK3PXP" },
                new EntryField { Label = "totp", Value = "ALSO-SECRET" },
                new EntryField { Label = "账号", Value = "user@example" },
                new EntryField { Label = "备注", Value = "" }),
            Entry(null, "已删条目", new EntryField { Label = "账号", Value = "ghost" }),
        };
        entries[1].IsDeleted = true;

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.Contains("正常条目", html);
        Assert.Contains("user@example", html);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", html);
        Assert.DoesNotContain("ALSO-SECRET", html);

        Assert.DoesNotContain(">备注</span>", html);
        Assert.DoesNotContain("已删条目", html);
        Assert.DoesNotContain("ghost", html);
    }

    [Fact]
    public void MemoHtml_UserValuesAreHtmlEncoded()
    {
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry>
        {
            Entry(null, "<b>名字</b>", new EntryField { Label = "账号", Value = "<script>alert(1)</script>" }),
        };

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<b>名字</b>", html);
        Assert.Contains("&lt;b&gt;名字&lt;/b&gt;", html);
    }

    [Fact]
    public void MemoHtml_WideUsesTwoColumns_NarrowUsesSingle()
    {
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry> { Entry(null, "条目") };

        var wide = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);
        var narrow = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.NarrowWidth, DateTime.Now, null);

        Assert.Contains("1fr 1fr", wide);
        Assert.DoesNotContain("1fr 1fr", narrow);
        Assert.Contains("width:" + ImageExportTemplates.WideWidth + "px", wide);
        Assert.Contains("width:" + ImageExportTemplates.NarrowWidth + "px", narrow);
    }

    [Fact]
    public void DiaryHtml_KeepsTitleMetaAndSanitizedContent()
    {
        var created = new DateTime(2026, 9, 1, 8, 0, 0);
        var modified = new DateTime(2026, 9, 25, 9, 30, 0);
        var content = "<p>正文<b>加粗</b></p><img src=\"data:image/png;base64,AAAA\"/>";

        var html = ImageExportTemplates.BuildDiaryHtml("我的日记", content, created, modified, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.Contains("我的日记", html);
        Assert.Contains(content, html);
        Assert.Contains("2026-09-01 08:00", html);
        Assert.Contains("2026-09-25 09:30", html);
    }

    [Fact]
    public void DiaryHtml_EmptyTitleFallsBackToLocalizedPlaceholder()
    {
        var html = ImageExportTemplates.BuildDiaryHtml("", "<p>x</p>", DateTime.Now, DateTime.Now, ImageExportTemplates.NarrowWidth, DateTime.Now, null);


        Assert.Contains("Export_Untitled", html);
    }

    [Fact]
    public void MemoHtml_LogoFallback_WhenSvgMissing()
    {
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry> { Entry(null, "条目") };

        var html = ImageExportTemplates.BuildMemoCollectionHtml(groups, entries, ImageExportTemplates.WideWidth, DateTime.Now, null);

        Assert.Contains(">N</span>", html);
    }
}
