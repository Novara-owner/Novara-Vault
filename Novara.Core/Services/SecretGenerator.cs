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

        var slots = Enumerable.Range(0, Math.Min(len, perClass.Count)).OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();
        for (int i = 0; i < slots.Count; i++)
            chars[slots[i]] = perClass[i][RandomNumberGenerator.GetInt32(perClass[i].Length)];

        for (int i = len - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }


    public static string GenerateUuid() => Guid.NewGuid().ToString("D");


    public static string GenerateToken(int byteCount)
    {
        var n = Math.Clamp(byteCount, 16, 64);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(n)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }


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
