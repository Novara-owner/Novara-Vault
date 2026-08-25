

using AngleSharp.Html.Parser;
using DomNode = AngleSharp.Dom.INode;
using DomElement = AngleSharp.Dom.IElement;
using DomComment = AngleSharp.Dom.IComment;

namespace Novara.Services;

public static class HtmlSanitizer
{
    /// <summary>N4C-04: recursion cap - adversarial deep nesting (tens of thousands of levels) made the
    /// unwrap recursion overflow the stack; StackOverflowException is uncatchable and kills the process.</summary>
    private const int MaxDepth = 200;

    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = NormalizeLegacyFontTags(html); // N4D-07: legacy <font> must survive Tiptap's schema on next load
        var doc = new HtmlParser().ParseDocument(html);
        if (doc.Body == null) return html;
        SanitizeNode(doc.Body, 0);
        return doc.Body.InnerHtml;
    }

    /// <summary>
    /// N4D-07: pre-5.0 diaries may contain <c>&lt;font color&gt;</c>. Tiptap has no Font extension, so
    /// setContent would silently drop those nodes and the next save would lose the colors for good.
    /// Convert to <c>span style="color:..."</c> up front - that form survives both this whitelist and
    /// Tiptap parsing. Color values are restricted to safe CSS color characters before embedding.
    /// </summary>
    private static string NormalizeLegacyFontTags(string html)
    {
        if (!html.Contains("font", StringComparison.OrdinalIgnoreCase)) return html;
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            "<font\\b[^>]*?color\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))[^>]*>",
            m =>
            {
                var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value; // N5D-04: third alt covers unquoted values
                var safe = System.Text.RegularExpressions.Regex.Replace(raw ?? "", "[^A-Za-z0-9#(),.%\\s-]", "");
                return "<span style=\"color:" + safe.Trim() + "\">";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "<font\\b[^>]*>", "<span>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "</\\s*font\\s*>", "</span>", System.Text.RegularExpressions.RegexOptions.IgnoreCase); // N5D-03: tolerate whitespace before '>'
        return html;
    }

    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b","strong","i","em","u","s","span","font","div","br","p","img","ul","ol","li",
        "a","pre","code","hr","blockquote","h1","h2","h3","h4","h5","h6"
    };
    private static readonly HashSet<string> DropTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script","iframe","object","embed","svg","style","link","meta","form","input",
        "button","textarea","select","option","base","noscript","template","math","head","html","body"
    };
    private static readonly HashSet<string> GlobalAttrs = new(StringComparer.OrdinalIgnoreCase) { "style", "class" };
    private static readonly Dictionary<string, HashSet<string>> TagAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "title", "target", "rel" },
        ["img"] = new(StringComparer.OrdinalIgnoreCase) { "src", "width", "height", "alt", "title" },
        ["font"] = new(StringComparer.OrdinalIgnoreCase) { "color", "face", "size" },
    };
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    { "http", "https", "mailto", "ftp" };

    private static void SanitizeNode(DomNode node, int depth)
    {
        foreach (var child in node.ChildNodes.ToList())
        {
            if (child is DomElement el)
            {
                // N5D-01: over-deep subtrees are REMOVED, not skipped - a plain `return` left them
                // in the DOM fully unsanitized (on*/protocol/script intact) and they leaked into
                // exported HTML collections. Dropping also bounds Body.InnerHtml serialization
                // depth, closing the residual stack-overflow vector on legal deep nesting.
                if (depth >= MaxDepth) { el.Remove(); continue; }
                var tag = el.LocalName;
                if (DropTags.Contains(tag)) { el.Remove(); continue; }
                if (!AllowedTags.Contains(tag))
                {
                    var parent = el.ParentElement;
                    var nodes = el.ChildNodes.ToList();
                    foreach (var n in nodes) parent?.InsertBefore(n, el);
                    el.Remove();
                    foreach (var n in nodes) SanitizeNode(n, depth + 1);
                    continue;
                }
                SanitizeAttrs(el);
                SanitizeNode(el, depth + 1);
            }
            else if (child is DomComment)
            {
                node.RemoveChild(child);
            }
        }
    }

    private static void SanitizeAttrs(DomElement el)
    {
        var tag = el.LocalName;
        var allowed = TagAttrs.TryGetValue(tag, out var t) ? t : GlobalAttrs;
        foreach (var attr in el.Attributes.ToList())
        {
            var name = attr.Name.ToLowerInvariant();
            if (name.StartsWith("on", StringComparison.Ordinal)) { el.RemoveAttribute(attr.Name); continue; }
            if (!GlobalAttrs.Contains(name) && !allowed.Contains(name)) { el.RemoveAttribute(attr.Name); continue; }
            if (name is "href" or "src")
            {
                var v = attr.Value?.Trim() ?? "";
                if (!IsSafeUrl(v)) el.RemoveAttribute(attr.Name);
            }
        }
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (url.StartsWith('/') || url.StartsWith('#') || url.StartsWith("./") || url.StartsWith("../")) return true;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                && (url.Contains("png", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("jpg", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("gif", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("webp", StringComparison.OrdinalIgnoreCase));
        var idx = url.IndexOf(':');
        if (idx < 0) return true;
        return AllowedSchemes.Contains(url[..idx]);
    }
}
