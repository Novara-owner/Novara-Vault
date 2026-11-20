using System.Text;

namespace Novara.Services;

public static class DestructiveConfirm
{



    public static bool Matches(string? typed, string? expected)
    {
        var a = Normalize(typed);
        return a.Length > 0 && string.Equals(a, Normalize(expected), StringComparison.Ordinal);
    }




    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var folded = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
