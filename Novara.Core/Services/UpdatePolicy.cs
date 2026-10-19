using System;
using System.Text.RegularExpressions;

namespace Novara.Services;















public static class UpdatePolicy
{


    public static string FormatDisplayVersion(Version v)
        => v.Minor > 0 ? $"novara{v.Major}.R{v.Minor}" : $"novara{v.Major}";





    public static string FormatDisplayVersion(string? versionText)
        => TryParseAnyVersion(versionText, out var v)
            ? FormatDisplayVersion(v)
            : (versionText?.Trim() ?? "");






    public static bool TryParseDisplayVersion(string? text, out int major, out int revision)
    {
        major = revision = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Regex.Match(text.Trim(), "^novara([1-9][0-9]*)(?:\\.R([0-9]+))?$");
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out major)) return false;
        if (m.Groups[2].Success && !int.TryParse(m.Groups[2].Value, out revision)) return false;
        return true;
    }

    public static bool IsNewer(string? latestVersion, string? currentVersion)
    {
        var okL = TryParseAnyVersion(latestVersion, out var latest);
        var okC = TryParseAnyVersion(currentVersion, out var current);
        return okL && okC && latest.CompareTo(current) > 0;
    }



    public static bool IsWellFormedVersion(string? text) => TryParseAnyVersion(text, out _);






    private static bool TryParseAnyVersion(string? text, out Version parsed)
    {
        if (TryParseDisplayVersion(text, out var major, out var revision))
        {
            parsed = new Version(major, revision, 0);
            return true;
        }
        if (Version.TryParse(text?.Trim(), out var raw))
        {
            parsed = new Version(raw.Major, raw.Minor, raw.Build >= 0 ? raw.Build : 0);
            return true;
        }
        parsed = null!;
        return false;
    }







    public static bool IsAutoCheckDue(DateTimeOffset dueAt, DateTimeOffset now) => now >= dueAt;







    public static bool ShouldPromptForVersion(string? latestVersion, string? pendingVersion, string? currentVersion)
    {
        if (!IsNewer(latestVersion, currentVersion)) return false;
        var latest = (latestVersion ?? "").Trim();
        var pending = (pendingVersion ?? "").Trim();
        if (pending.Length == 0) return true;
        if (string.Equals(latest, pending, StringComparison.OrdinalIgnoreCase)) return false;
        if (TryParseAnyVersion(latest, out var a) && TryParseAnyVersion(pending, out var b))
            return a.CompareTo(b) != 0;
        return true;
    }
}
