using System.Text.RegularExpressions;

namespace Novara.Services;

public static class AuditSanitizer
{

    public const int MaxFieldChars = 300;


    public const int MaxTitleChars = 60;





    internal static readonly (string Prefix, string Vendor)[] VendorKeyPrefixes =
    {
        ("sk-ant-", "Anthropic"),
        ("AIza", "Google Gemini"),
        ("gsk_", "Groq"),
        ("xai-", "xAI Grok"),
        ("sk-or-", "OpenRouter"),
    };



    private static readonly Regex TokenRegex = BuildTokenRegex();

    private static Regex BuildTokenRegex()
    {



        var prefixes = VendorKeyPrefixes.Select(p => Regex.Escape(p.Prefix))
            .OrderByDescending(p => p.Length)
            .Append(Regex.Escape("sk-"));
        return new Regex("(" + string.Join("|", prefixes) + @")[A-Za-z0-9_\-\.]{4,}", RegexOptions.Compiled);
    }

    private static readonly Regex AuthHeaderRegex = new(@"((?:bearer|x-api-key|api-key)\s*[:=]?\s*)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);








    private static readonly Regex KeyParamRegex = new(@"((?:key|token|secret|password|passwd|otp)\s*=)([^\s&]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);






    private static readonly Regex UrlUserInfoRegex = new(@"([a-zA-Z][a-zA-Z0-9+.\-]*://)([^/@\s]+)@", RegexOptions.Compiled);





    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text;



        s = KeyParamRegex.Replace(s, m => m.Groups[1].Value + MaskParamValue(m.Groups[2].Value));
        s = TokenRegex.Replace(s, "$1***");
        s = AuthHeaderRegex.Replace(s, "$1***");
        s = UrlUserInfoRegex.Replace(s, "$1***@");
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > MaxFieldChars ? s.Substring(0, MaxFieldChars) + "…" : s;
    }


    private static string MaskParamValue(string value)
    {
        foreach (var prefix in VendorKeyPrefixes.Select(p => p.Prefix)
                     .Append("sk-")
                     .OrderByDescending(p => p.Length))
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return prefix + "***";
        }
        return "***";
    }


    public static string TruncateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = title.Trim();
        return t.Length <= MaxTitleChars ? t : t.Substring(0, MaxTitleChars) + "…";
    }
}
