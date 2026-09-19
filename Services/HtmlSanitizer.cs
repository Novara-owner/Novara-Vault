using AngleSharp.Html.Parser;
using DomNode = AngleSharp.Dom.INode;
using DomElement = AngleSharp.Dom.IElement;
using DomComment = AngleSharp.Dom.IComment;

namespace Novara.Services;

public static class HtmlSanitizer
{


    private const int MaxDepth = 200;

    public static string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html ?? "";
        html = NormalizeLegacyFontTags(html);
        var doc = new HtmlParser().ParseDocument(html);
        if (doc.Body == null) return html;
        SanitizeNode(doc.Body, 0);
        return doc.Body.InnerHtml;
    }







    private static string NormalizeLegacyFontTags(string html)
    {
        if (!html.Contains("font", StringComparison.OrdinalIgnoreCase)) return html;
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            "<font\\b[^>]*?color\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))[^>]*>",
            m =>
            {
                var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                var safe = System.Text.RegularExpressions.Regex.Replace(raw ?? "", "[^A-Za-z0-9#(),.%\\s-]", "");
                return "<span style=\"color:" + safe.Trim() + "\">";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "<font\\b[^>]*>", "<span>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, "</\\s*font\\s*>", "</span>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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




                if (depth >= MaxDepth) { el.Remove(); continue; }
                var tag = el.LocalName;
                if (DropTags.Contains(tag)) { el.Remove(); continue; }
                if (!AllowedTags.Contains(tag))
                {
                    var parent = el.ParentElement;
                    var nodes = el.ChildNodes.ToList();
                    foreach (var n in nodes) parent?.InsertBefore(n, el);
                    el.Remove();
                    foreach (var n in nodes) SanitizePromoted(n, depth + 1);
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








    private static void SanitizePromoted(DomNode node, int depth)
    {
        if (node is not DomElement el) return;
        if (depth >= MaxDepth) { el.Remove(); return; }
        var tag = el.LocalName;
        if (DropTags.Contains(tag)) { el.Remove(); return; }
        if (!AllowedTags.Contains(tag))
        {
            var parent = el.ParentElement;
            var nodes = el.ChildNodes.ToList();
            foreach (var n in nodes) parent?.InsertBefore(n, el);
            el.Remove();
            foreach (var n in nodes) SanitizePromoted(n, depth + 1);
            return;
        }
        SanitizeAttrs(el);
        SanitizeNode(el, depth + 1);
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
            else if (name == "style")
            {

                var v = SanitizeStyle(attr.Value ?? "");
                if (string.IsNullOrEmpty(v)) el.RemoveAttribute(attr.Name);
                else el.SetAttribute("style", v);
            }
        }
    }





    private static string SanitizeStyle(string style)
    {
        if (string.IsNullOrWhiteSpace(style)) return "";
        var kept = new System.Collections.Generic.List<string>();
        foreach (var raw in style.Split(';'))
        {
            var idx = raw.IndexOf(':');
            if (idx <= 0) continue;
            var prop = raw[..idx].Trim().ToLowerInvariant();
            var val = raw[(idx + 1)..].Trim();
            if (val.Contains("url(", StringComparison.OrdinalIgnoreCase)
                || val.Contains("expression", StringComparison.OrdinalIgnoreCase)
                || val.Contains("@import", StringComparison.OrdinalIgnoreCase))
                continue;
            if (prop is "color" or "background-color")
            {
                var safe = System.Text.RegularExpressions.Regex.Replace(val, "[^A-Za-z0-9#(),.%\\s-]", "");
                if (safe.Trim().Length > 0) kept.Add(prop + ":" + safe.Trim());
            }
            else if (prop == "text-align")
            {
                if (val is "left" or "right" or "center" or "justify") kept.Add(prop + ":" + val);
            }

        }
        return string.Join(";", kept);
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;





        var normalized = url.Replace('\\', '/');
        if (normalized.StartsWith("//")) return false;
        if (normalized.StartsWith('/') || normalized.StartsWith('#') || normalized.StartsWith("./") || normalized.StartsWith("../")) return true;
        if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {


            const string prefix = "data:image/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            var rest = normalized.Substring(prefix.Length);
            var end = rest.IndexOfAny(new[] { ';', ',' });
            var mime = end < 0 ? rest : rest.Substring(0, end);
            return mime.Equals("png", StringComparison.OrdinalIgnoreCase)
                || mime.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || mime.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                || mime.Equals("gif", StringComparison.OrdinalIgnoreCase)
                || mime.Equals("webp", StringComparison.OrdinalIgnoreCase);
        }
        var idx = normalized.IndexOf(':');
        if (idx < 0) return true;
        return AllowedSchemes.Contains(normalized[..idx]);
    }
}
