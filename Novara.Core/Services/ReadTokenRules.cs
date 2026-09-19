using System.Text;

namespace Novara.Services;





public static class ReadTokenRules
{

    public const int Length = 24;


    public const int GroupSize = 4;












    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(Length);
        foreach (var ch in raw)
        {
            var c = char.ToUpperInvariant(ch);
            if (c is 'I' or 'L') c = '1';
            else if (c == 'O') c = '0';
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z') sb.Append(c);
        }
        return sb.ToString();
    }


    public static string Display(string? token)
    {
        var text = Normalize(token);
        if (text.Length == 0) return "";

        var groups = new List<string>((text.Length + GroupSize - 1) / GroupSize);
        for (var i = 0; i < text.Length; i += GroupSize)
            groups.Add(text.Substring(i, Math.Min(GroupSize, text.Length - i)));
        return string.Join("-", groups);
    }


    public static bool IsWellFormed(string? token) => Normalize(token).Length == Length;
}
