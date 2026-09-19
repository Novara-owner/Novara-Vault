using System.Text.RegularExpressions;

namespace Novara.Services;

public static class AuditSanitizer
{

    public const int MaxFieldChars = 300;


    public const int MaxTitleChars = 60;


    private static readonly Regex SkRegex = new(@"sk-[A-Za-z0-9_-]{4,}", RegexOptions.Compiled);
    private static readonly Regex AuthHeaderRegex = new(@"((?:bearer|x-api-key|api-key)\s*[:=]?\s*)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);






    private static readonly Regex KeyParamRegex = new(@"((?:key|token|secret|password|passwd|otp)\s*=)[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);






    private static readonly Regex UrlUserInfoRegex = new(@"([a-zA-Z][a-zA-Z0-9+.\-]*://)([^/@\s]+)@", RegexOptions.Compiled);





    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = SkRegex.Replace(text, "sk-***");
        s = AuthHeaderRegex.Replace(s, "$1***");
        s = KeyParamRegex.Replace(s, "$1***");
        s = UrlUserInfoRegex.Replace(s, "$1***@");
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > MaxFieldChars ? s.Substring(0, MaxFieldChars) + "…" : s;
    }


    public static string TruncateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var t = title.Trim();
        return t.Length <= MaxTitleChars ? t : t.Substring(0, MaxTitleChars) + "…";
    }
}
