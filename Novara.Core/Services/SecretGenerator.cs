/* ========== SecretGenerator - Password/UUID/Token Generation ==========
Function: CSPRNG-based secret generation (design doc 9.3, 2026-08-29): random passwords with
          length/charset options (guaranteed one char per selected class), UUID v4, Base64Url
          tokens, and entropy estimation. Pure functions, no state - UI only renders results.
          Deliberately NO "API key" generation (9.3: random strings pretending to be vendor keys
          are meaningless).
Corresponding UI: BasicMemoPage entry-dialog generate button + generator dialog
Logic Range: Whole file
*/
using System.Security.Cryptography;
using System.Text;

namespace Novara.Services;

public sealed record PasswordOptions(int Length, bool Upper, bool Lower, bool Digits, bool Symbols, bool ExcludeAmbiguous);

public static class SecretGenerator
{
    public const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    public const string DigitChars = "0123456789";
    public const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.?/~";
    public const string AmbiguousChars = "0O1lI";
    public const int MinLength = 8;
    public const int MaxLength = 64;

    /// <summary>
    /// Random password from the selected character classes (CSPRNG). Each selected class is
    /// guaranteed at least one character (placed randomly, then the whole result is Fisher-Yates
    /// shuffled so the guarantee is not position-revealing). Throws ArgumentException when no
    /// class is selected - the UI keeps that state unconfirmable (M5).
    /// </summary>
    public static string GeneratePassword(PasswordOptions o)
    {
        var pool = new StringBuilder();
        var perClass = new List<string>();
        void Add(string set, bool on)
        {
            if (!on) return;
            var sb = new StringBuilder();
            foreach (var c in set)
            {
                if (o.ExcludeAmbiguous && AmbiguousChars.Contains(c)) continue;
                sb.Append(c);
            }
            if (sb.Length > 0) { pool.Append(sb); perClass.Add(sb.ToString()); }
        }
        Add(UpperChars, o.Upper);
        Add(LowerChars, o.Lower);
        Add(DigitChars, o.Digits);
        Add(SymbolChars, o.Symbols);
        if (pool.Length == 0) throw new ArgumentException("at least one character class must be selected");

        var len = Math.Clamp(o.Length, MinLength, MaxLength);
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        // guarantee: one char per selected class at random positions (cap at length)
        var slots = Enumerable.Range(0, Math.Min(len, perClass.Count)).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();
        for (int i = 0; i < slots.Count; i++)
            chars[slots[i]] = perClass[i][RandomNumberGenerator.GetInt32(perClass[i].Length)];
        // Fisher-Yates shuffle so guaranteed positions are not revealing
        for (int i = len - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    /// <summary>UUID v4 (random), canonical dashed lowercase form.</summary>
    public static string GenerateUuid() => Guid.NewGuid().ToString("D");

    /// <summary>Base64Url-encoded random token (same encoding the MCP token uses), 16-64 bytes.</summary>
    public static string GenerateToken(int byteCount)
    {
        var n = Math.Clamp(byteCount, 16, 64);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(n)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Entropy estimate in bits: length * log2(pool size). Advisory UI signal only.</summary>
    public static double EntropyBits(PasswordOptions o)
    {
        int pool = 0;
        foreach (var (set, on) in new[] { (UpperChars, o.Upper), (LowerChars, o.Lower), (DigitChars, o.Digits), (SymbolChars, o.Symbols) })
        {
            if (!on) continue;
            foreach (var c in set)
                if (!(o.ExcludeAmbiguous && AmbiguousChars.Contains(c))) pool++;
        }
        return pool == 0 ? 0 : Math.Clamp(o.Length, MinLength, MaxLength) * Math.Log2(pool);
    }
}
