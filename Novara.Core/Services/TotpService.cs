using System.Security.Cryptography;
using System.Text;

namespace Novara.Services;

public sealed record TotpConfig(byte[] Key, string Algorithm, int Digits, int Period, string? Issuer, string Raw);

public static class TotpService
{
    public const int DefaultPeriod = 30;
    public const int DefaultDigits = 6;



    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";


    public static bool TryDecodeBase32(string input, out byte[] decoded)
    {
        decoded = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input)) return false;
        var bits = new StringBuilder();
        foreach (var c in input)
        {
            if (c == '=' || c == ' ' || c == '-') continue;
            var idx = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (idx < 0) return false;
            bits.Append(Convert.ToString(idx, 2).PadLeft(5, '0'));
        }
        var bitCount = bits.Length / 8 * 8;
        if (bitCount == 0) return false;
        decoded = new byte[bitCount / 8];
        for (int i = 0; i < decoded.Length; i++)
            decoded[i] = Convert.ToByte(bits.ToString(i * 8, 8), 2);
        return decoded.Length > 0;
    }

    public static bool IsValidBase32Secret(string input) => TryDecodeBase32(input, out _);





    public static bool TryParse(string input, out TotpConfig config)
    {
        config = new TotpConfig(Array.Empty<byte>(), "SHA1", DefaultDigits, DefaultPeriod, null, input?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();

        if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {

                var qIndex = trimmed.IndexOf('?');
                if (qIndex < 0) return false;
                var query = trimmed[(qIndex + 1)..];
                string? secret = null, algorithm = "SHA1", issuer = null;
                int digits = DefaultDigits, period = DefaultPeriod;
                foreach (var pair in query.Split('&'))
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = Uri.UnescapeDataString(pair[..eq]).Trim();
                    var v = Uri.UnescapeDataString(pair[(eq + 1)..]).Trim();
                    if (k.Equals("secret", StringComparison.OrdinalIgnoreCase)) secret = v;
                    else if (k.Equals("algorithm", StringComparison.OrdinalIgnoreCase)) algorithm = v.ToUpperInvariant();
                    else if (k.Equals("digits", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(v, out var d)) digits = d; }
                    else if (k.Equals("period", StringComparison.OrdinalIgnoreCase)) { if (int.TryParse(v, out var p)) period = p; }
                    else if (k.Equals("issuer", StringComparison.OrdinalIgnoreCase)) issuer = v;
                }
                if (secret == null || !TryDecodeBase32(secret, out var key)) return false;
                if (algorithm is not ("SHA1" or "SHA256" or "SHA512")) return false;
                if (digits is < 6 or > 10 || period is < 15 or > 120) return false;
                config = new TotpConfig(key, algorithm, digits, period, issuer, trimmed);
                return true;
            }
            catch { return false; }
        }


        if (TryDecodeBase32(trimmed, out var bareKey))
        {
            var normalized = new StringBuilder();
            foreach (var c in trimmed) if (c != ' ' && c != '-' && c != '=') normalized.Append(char.ToUpperInvariant(c));
            config = new TotpConfig(bareKey, "SHA1", DefaultDigits, DefaultPeriod, null, normalized.ToString());
            return true;
        }
        return false;
    }



    public static string ComputeCode(byte[] key, string algorithm, long counter, int digits)
    {
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--) { counterBytes[i] = (byte)(counter & 0xff); counter >>= 8; }
        using System.Security.Cryptography.HMAC hmac = algorithm switch
        {
            "SHA256" => new HMACSHA256(key),
            "SHA512" => new HMACSHA512(key),
            _ => new HMACSHA1(key),
        };
        var hs = hmac.ComputeHash(counterBytes);
        var offset = hs[^1] & 0x0f;
        var snum = ((hs[offset] & 0x7f) << 24) | (hs[offset + 1] << 16) | (hs[offset + 2] << 8) | hs[offset + 3];


        if (digits is < 1 or > 10) digits = 6;
        var mod = (long)Math.Pow(10, digits);
        return (snum % mod).ToString().PadLeft(digits, '0');
    }

    public static string ComputeCode(TotpConfig config, long unixNow)
        => ComputeCode(config.Key, config.Algorithm, unixNow / (config.Period > 0 ? config.Period : 30), config.Digits);

    public static string ComputeCode(TotpConfig config)
        => ComputeCode(config, DateTimeOffset.UtcNow.ToUnixTimeSeconds());


    public static int RemainingSeconds(int period, long unixNow)
    {
        if (period <= 0) period = 30;
        var rem = (int)(period - unixNow % period);
        return rem == 0 ? period : rem;
    }
}
