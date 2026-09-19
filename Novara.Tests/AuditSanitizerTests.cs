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
    public void Clean_MasksTokenShapedSecrets(string input, string expected)
    {
        Assert.Equal(expected, AuditSanitizer.Clean(input));
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
