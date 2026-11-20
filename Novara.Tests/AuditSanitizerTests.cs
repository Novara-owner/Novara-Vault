using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class AuditSanitizerTests
{
    [Theory]
    [InlineData("sk-abcdefghijkl", "sk-***")]
    [InlineData("Bearer abcdefgh12345", "Bearer ***")]
    [InlineData("x-api-key: abcdefgh12345", "x-api-key: ***")]
    [InlineData("api-key=abcdefgh12345", "api-key=***")]
    [InlineData("https://api.example.com/v1?key=abcdefgh12345", "https://api.example.com/v1?key=***")]
    [InlineData("token=abcdefgh12345", "token=***")]


    [InlineData("secret=JBSWY3DPEHPK3PXP", "secret=***")]
    [InlineData("password=Sup3rSecret12", "password=***")]
    [InlineData("otp=JBSWY3DPEHPK3P", "otp=***")]


    [InlineData("password=correct horse", "password=*** horse")]
    [InlineData("passwd=pa$$w0rd!", "passwd=***")]
    [InlineData("token=abc+defghij", "token=***")]
    [InlineData("key=abc/def?ghi=jkl", "key=***")]
    public void Clean_MasksTokenShapedSecrets(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
    }

    [Theory]


    [InlineData("gsk_live_abcdefgh123", "gsk_***")]
    [InlineData("AIzaSyA1234567890abcdef", "AIza***")]
    [InlineData("xai-abcdefgh1234", "xai-***")]
    [InlineData("sk-or-v1-abcdefgh1234", "sk-or-***")]
    public void Clean_MasksVendorPrefixedKeys(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
        Assert.NotEqual("", ApiProbeService.SuggestVendorForKey(input) ?? "");
    }

    [Theory]


    [InlineData("https://alice:s3cr3t@novara.example.com:8443",
                "https://***@novara.example.com:8443")]
    [InlineData("http://alice:s3cr3t@127.0.0.1:5000", "http://***@127.0.0.1:5000")]

    [InlineData("https://ghp_abcdefghijklmnop@github.com", "https://***@github.com")]
    public void Clean_MasksUrlUserInfo(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
    }

    [Theory]

    [InlineData("https://novara.example.com/path@2x", "https://novara.example.com/path@2x")]
    [InlineData("mail alice@example.com", "mail alice@example.com")]
    [InlineData("https://novara.example.com:8443", "https://novara.example.com:8443")]
    public void Clean_LeavesOrdinaryTextAlone(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
    }

    [Theory]


    [InlineData("error writing C:\\Users\\x\\AppData\\Local\\Novara\\seal",
                "error writing <seal>")]
    [InlineData("open /home/x/.local/share/Novara/seal failed", "open <seal> failed")]

    [InlineData("open /home/x/.local/share/Novara/seal/index.nseal failed", "open <seal> failed")]
    [InlineData("seal C:\\x\\Novara\\seal\\items\\a.novaseal done", "seal <seal> done")]
    public void Clean_MasksSealRootsBareOrNested(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
    }

    [Theory]


    [InlineData("seal the envelope before noon", "seal the envelope before noon")]
    [InlineData("the report is at C:\\docs\\seal-report.txt", "the report is at C:\\docs\\seal-report.txt")]
    [InlineData("we unsealed the shipment", "we unsealed the shipment")]
    public void Clean_LeavesPlainSealWordsAlone(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
    }

    [Fact]
    public void Clean_FlattensNewlinesAndCapsTheLength()
    {

        Assert.Equal("a b c", AuditSanitizer.Clean("a\nb\rc"));

        var long_ = new string('x', AuditSanitizer.MaxFieldChars + 50);
        var cleaned = AuditSanitizer.Clean(long_);
        Assert.Equal(AuditSanitizer.MaxFieldChars + 1, cleaned.Length);
        Assert.EndsWith("…", cleaned);
        Assert.Equal("", AuditSanitizer.Clean(null));
        Assert.Equal("", AuditSanitizer.Clean(""));
    }

    [Fact]
    public void TruncateTitle_TrimsAndCaps()
    {
        Assert.Equal("标题", AuditSanitizer.TruncateTitle("  标题  "));
        Assert.Equal("", AuditSanitizer.TruncateTitle("   "));
        Assert.Equal("", AuditSanitizer.TruncateTitle(null));
        var longTitle = new string('标', AuditSanitizer.MaxTitleChars + 10);
        var snapped = AuditSanitizer.TruncateTitle(longTitle);
        Assert.Equal(AuditSanitizer.MaxTitleChars + 1, snapped.Length);
        Assert.EndsWith("…", snapped);
    }
}
