using System.Text;
using Novara.Models;

namespace Novara.Services;

public static class ImageExportTemplates
{


    public const int WideWidth = 820;
    public const int NarrowWidth = 420;



    public const int MaxSliceHeight = 8000;




    public static string BuildMemoCollectionHtml(
        IReadOnlyList<MemoGroup> groups,
        IReadOnlyList<MemoEntry> entries,
        int width,
        DateTime exportedAt,
        string? logoSvg)
    {
        var live = entries.Where(e => !e.IsDeleted).ToList();
        var groupIds = groups.Select(g => g.Id).ToHashSet();
        var orderedGroups = groups
            .Where(g => live.Any(e => e.GroupId == g.Id))
            .OrderByDescending(g => g.IsPinned)
            .ThenBy(g => g.CreatedAt)
            .ToList();

        var ungrouped = live
            .Where(e => e.GroupId == null || !groupIds.Contains(e.GroupId.Value))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();

        var sb = new StringBuilder();
        AppendHtmlHead(sb, width, $"Novara {Loc.T("Export_Section_Memo")}", logoSvg, isWide: width >= 640);
        sb.AppendLine("<div class=\"header\">" + HeaderBrandHtml(logoSvg) + "</div>");
        sb.AppendLine("<p class=\"meta\">" + Encode(string.Format(Loc.T("Export_ExportedAt"), exportedAt.ToString("yyyy-MM-dd HH:mm"))) + "</p>");

        foreach (var g in orderedGroups)
        {
            sb.AppendLine("<div class=\"group\">");
            sb.AppendLine("<div class=\"group-title\">" + Encode(string.IsNullOrWhiteSpace(g.Name) ? Loc.T("Memo_Group_Uncategorized") : g.Name) + "</div>");
            AppendMemoGrid(sb, live.Where(e => e.GroupId == g.Id));
            sb.AppendLine("</div>");
        }
        if (ungrouped.Count > 0)
        {
            sb.AppendLine("<div class=\"group\">");
            sb.AppendLine("<div class=\"group-title\">" + Encode(Loc.T("Export_Section_Uncategorized")) + "</div>");
            AppendMemoGrid(sb, ungrouped);
            sb.AppendLine("</div>");
        }

        AppendFooter(sb);
        return sb.ToString();
    }




    public static string BuildDiaryHtml(
        string title,
        string sanitizedHtml,
        DateTime createdAt,
        DateTime modifiedAt,
        int width,
        DateTime exportedAt,
        string? logoSvg)
    {
        var sb = new StringBuilder();
        var isWide = width >= 640;
        AppendHtmlHead(sb, width, Encode(string.IsNullOrWhiteSpace(title) ? Loc.T("Export_Untitled") : title), logoSvg, isWide);
        sb.AppendLine("<div class=\"header\">" + HeaderBrandHtml(logoSvg) + "</div>");
        sb.AppendLine("<div class=\"title\">" + Encode(string.IsNullOrWhiteSpace(title) ? Loc.T("Export_Untitled") : title) + "</div>");
        sb.AppendLine("<div class=\"dmeta\">" + Encode(Loc.T("Search_Created")) + " " + createdAt.ToString("yyyy-MM-dd HH:mm")
            + " · " + Encode(Loc.T("Search_Modified")) + " " + modifiedAt.ToString("yyyy-MM-dd HH:mm") + "</div>");
        sb.AppendLine("<div class=\"content\">" + sanitizedHtml + "</div>");
        AppendFooter(sb);
        return sb.ToString();
    }



    private static void AppendMemoGrid(StringBuilder sb, IEnumerable<MemoEntry> cards)
    {
        sb.AppendLine("<div class=\"grid\">");
        foreach (var e in cards)
        {
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine("<div class=\"card-name\">" + Encode(string.IsNullOrWhiteSpace(e.Name) ? Loc.T("Export_Untitled") : e.Name) + "</div>");
            if (!string.IsNullOrWhiteSpace(e.KeyInfo))
                sb.AppendLine("<div class=\"card-key\">" + Encode(e.KeyInfo) + "</div>");
            foreach (var f in e.Fields)
            {

                if (string.Equals(f.Label, "TOTP", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(f.Value)) continue;
                sb.AppendLine("<div class=\"fr\"><span class=\"fl\">" + Encode(f.Label) + "</span><span class=\"fv\">" + Encode(f.Value) + "</span></div>");
            }
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
    }

    private static string HeaderBrandHtml(string? logoSvg)
    {
        var logoHtml = string.IsNullOrEmpty(logoSvg) ? "<span class=\"logo\">N</span>" : logoSvg;
        return logoHtml + "<div><div class=\"brand\">Novara</div><div class=\"tagline\">" + Encode(Loc.T("Export_Html_Tagline")) + "</div></div>";
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("<div class=\"footer\"><div class=\"slogan\">" + Encode(Loc.T("Export_Html_Slogan")) + "</div><div>" + Encode(Loc.T("Export_Website")) + "novara.xin</div></div>");
        sb.AppendLine("</body></html>");
    }



    private static void AppendHtmlHead(StringBuilder sb, int width, string title, string? logoSvg, bool isWide)
    {
        var pad = isWide ? 28 : 20;
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head>");
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src * data:;\">");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<title>" + title + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"body{{margin:0;padding:{pad}px {pad}px 24px;background:#f6f7f9;font-family:-apple-system,'Segoe UI','Microsoft YaHei','PingFang SC',sans-serif;color:#1c1c22;box-sizing:border-box;width:{width}px;}}");
        sb.AppendLine(".header{display:flex;align-items:center;gap:12px;padding-bottom:14px;border-bottom:2px solid #7276ff;}");
        sb.AppendLine(".header svg{width:38px;height:38px;flex-shrink:0;}");
        sb.AppendLine(".logo{width:38px;height:38px;border-radius:10px;background:#7276ff;color:#fff;font-size:20px;font-weight:700;display:flex;align-items:center;justify-content:center;flex-shrink:0;}");
        sb.AppendLine(".brand{font-size:19px;font-weight:700;color:#7276ff;line-height:1.25;}");
        sb.AppendLine(".tagline{font-size:12px;color:#8a8a92;}");
        sb.AppendLine(".meta{color:#9a9aa2;font-size:12px;margin:10px 0 0;}");
        sb.AppendLine(".group{margin-top:22px;}");
        sb.AppendLine(".group-title{display:flex;align-items:center;gap:8px;font-size:16px;font-weight:700;margin-bottom:12px;}");
        sb.AppendLine(".group-title::before{content:'';width:4px;height:15px;border-radius:2px;background:#7276ff;flex-shrink:0;}");
        sb.AppendLine(isWide
            ? ".grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;align-items:start;}"
            : ".grid{display:grid;grid-template-columns:1fr;gap:12px;align-items:start;}");
        sb.AppendLine(".card{background:#fff;border:1px solid #ececf2;border-radius:12px;padding:13px 15px;break-inside:avoid;}");
        sb.AppendLine(".card-name{font-size:14.5px;font-weight:600;word-break:break-all;}");
        sb.AppendLine(".card-key{font-size:12px;color:#8a8a92;margin-top:3px;word-break:break-all;}");
        sb.AppendLine(".fr{display:flex;gap:8px;font-size:12.5px;line-height:1.55;margin-top:6px;}");
        sb.AppendLine(".fl{color:#8a8a92;flex-shrink:0;}");
        sb.AppendLine(".fv{word-break:break-all;min-width:0;}");
        sb.AppendLine(".title{font-size:" + (isWide ? "22px" : "19px") + ";font-weight:700;margin-top:16px;word-break:break-all;}");
        sb.AppendLine(".dmeta{color:#9a9aa2;font-size:12px;margin:4px 0 14px;}");
        sb.AppendLine(".content{font-size:14.5px;line-height:1.8;background:#fff;border:1px solid #ececf2;border-radius:12px;padding:18px 20px;word-break:break-word;}");
        sb.AppendLine(".content img{max-width:100%;height:auto;border-radius:8px;}");
        sb.AppendLine(".content h1,.content h2,.content h3{font-size:1.2em;margin:0.8em 0 0.4em;}");
        sb.AppendLine(".content h4,.content h5,.content h6{font-size:1.05em;margin:0.7em 0 0.35em;}");
        sb.AppendLine(".content p{margin:0.55em 0;}");
        sb.AppendLine(".content pre{white-space:pre-wrap;word-wrap:break-word;background:#f5f5f7;padding:12px;border-radius:8px;font-family:Consolas,'Courier New',monospace;font-size:12.5px;}");
        sb.AppendLine(".content blockquote{margin:0.6em 0;padding:2px 12px;border-left:3px solid #d9d9e3;color:#55555e;}");
        sb.AppendLine(".content a{color:#7276ff;text-decoration:none;}");
        sb.AppendLine(".footer{margin-top:30px;padding-top:14px;border-top:2px solid #7276ff;text-align:center;color:#9a9aa2;font-size:12px;}");
        sb.AppendLine(".footer .slogan{color:#66666e;margin-bottom:3px;}");
        sb.AppendLine("</style></head><body>");
    }

    private static string Encode(string? s)
        => System.Net.WebUtility.HtmlEncode(s ?? "");
}
