using Novara.Services;
using Xunit;

namespace Novara.Tests;



[Collection("CoreSequential")]
public class TotpServiceTests
{
    // RFC 4226 App. D: secret = ASCII "12345678901234567890", 6 digits, HOTP counters 0..9
    private static readonly long[] HotpCounters = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    private static readonly string[] HotpExpected =
        { "755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489" };

    public static IEnumerable<object[]> HotpVectors()
        => HotpCounters.Zip(HotpExpected, (c, e) => new object[] { c, e });

    [Theory]
    [MemberData(nameof(HotpVectors))]
    public void Hotp_Rfc4226_AppendixD_Vectors(long counter, string expected)
    {
        var key = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, TotpService.ComputeCode(key, "SHA1", counter, 6));
    }

    // RFC 6238 App. B: secret = ASCII "12345678901234567890" (SHA1), 8 digits, T = unix time
    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Totp_Rfc6238_AppendixB_Sha1_Vectors(long unixTime, string expected)
    {
        var key = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal(expected, TotpService.ComputeCode(key, "SHA1", unixTime / 30, 8));
    }

    // RFC 6238 App. B: SHA256 uses the 32-byte secret "12345678901234567890123456789012"
    
    [Theory]
    [InlineData(59L, "46119246")]
    [InlineData(1111111109L, "68084774")]
    [InlineData(1111111111L, "67062674")]
    [InlineData(1234567890L, "91819424")]
    [InlineData(2000000000L, "90698825")]
    [InlineData(20000000000L, "77737706")]
    public void Totp_Rfc6238_AppendixB_Sha256_Vectors(long unixTime, string expected)
    {
        var key = System.Text.Encoding.ASCII.GetBytes("12345678901234567890123456789012");
        Assert.Equal(expected, TotpService.ComputeCode(key, "SHA256", unixTime / 30, 8));
    }

    // ---------- Base32 ----------

    [Fact]
    public void Base32_RoundTrip_And_Tolerance()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        var b32 = ToBase32(data);
        Assert.True(TotpService.TryDecodeBase32(b32, out var decoded));
        Assert.Equal(data, decoded);
        
        Assert.True(TotpService.TryDecodeBase32(b32.ToLowerInvariant().Replace("A", "a ").Insert(8, "-"), out var tolerated));
        Assert.Equal(data, tolerated);
        Assert.False(TotpService.TryDecodeBase32("not!valid@base32", out _)); 
        Assert.False(TotpService.TryDecodeBase32("", out _));
    }

    private static string ToBase32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(data.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i + 5 <= bits.Length; i += 5)
            sb.Append(alphabet[Convert.ToInt32(bits.Substring(i, 5), 2)]);
        var pad = (8 - sb.Length % 8) % 8;
        return sb.ToString().PadRight(sb.Length + pad, '=');
    }

    // ---------- otpauth URI ----------

    private const string TestSecret = "JBSWY3DPEHPK3PXP";

    [Fact]
    public void ParseOtpAuthUri_FullParameters()
    {
        var uri = $"otpauth://totp/Acme:alice@acme.com?secret={TestSecret}&issuer=Acme&algorithm=SHA1&digits=6&period=30";
        Assert.True(TotpService.TryParse(uri, out var cfg));
        Assert.Equal("Acme", cfg.Issuer);
        Assert.Equal(6, cfg.Digits);
        Assert.Equal(30, cfg.Period);
        Assert.Equal("SHA1", cfg.Algorithm);
        TotpService.TryDecodeBase32(TestSecret, out var expectedKey);
        Assert.Equal(expectedKey, cfg.Key);
    }

    [Fact]
    public void ParseOtpAuthUri_Minimal_And_BareSecret()
    {
        Assert.True(TotpService.TryParse($"otpauth://totp/x?secret={TestSecret.ToLowerInvariant()}", out var minimal));
        Assert.Equal("SHA1", minimal.Algorithm);
        Assert.Equal(6, minimal.Digits);
        Assert.Equal(30, minimal.Period);

        Assert.True(TotpService.TryParse(TestSecret, out var bare)); 
        TotpService.TryDecodeBase32(TestSecret, out var expectedKey);
        Assert.Equal(expectedKey, bare.Key);
    }

    [Theory]
    [InlineData("otpauth://totp/x")]                       
    [InlineData("otpauth://totp/x?secret=!!")]             
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")] 
    [InlineData("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=4")]      
    public void ParseOtpAuthUri_Invalid_Rejected(string input)
        => Assert.False(TotpService.TryParse(input, out _));

    

    [Fact]
    public void RemainingSeconds_RollsOver()
    {
        Assert.Equal(30, TotpService.RemainingSeconds(30, 60)); 
        Assert.Equal(1, TotpService.RemainingSeconds(30, 89));  
        Assert.Equal(1, TotpService.RemainingSeconds(30, 59));  
        Assert.Equal(30, TotpService.RemainingSeconds(30, 30)); 
    }
}
