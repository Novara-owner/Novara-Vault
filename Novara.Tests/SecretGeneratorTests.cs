using Novara.Services;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class SecretGeneratorTests
{
    private static PasswordOptions Opt(int len = 16, bool up = true, bool low = true, bool dig = true, bool sym = true, bool exAmb = true)
        => new(len, up, low, dig, sym, exAmb);

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    public void GeneratePassword_LengthClamped(int len)
        => Assert.Equal(len, SecretGenerator.GeneratePassword(Opt(len)).Length);

    [Fact]
    public void GeneratePassword_LengthBelowMin_ClampsTo8()
        => Assert.Equal(8, SecretGenerator.GeneratePassword(Opt(3)).Length);

    [Fact]
    public void GeneratePassword_OnlyLowercase_OutputContainsLowercaseOnly()
    {
        var pw = SecretGenerator.GeneratePassword(Opt(32, up: false, dig: false, sym: false));
        Assert.All(pw, c => Assert.True(char.IsLower(c)));
    }

    [Fact]
    public void GeneratePassword_ExcludeAmbiguous_NeverContainsThem()
    {
        for (int i = 0; i < 20; i++)
        {
            var pw = SecretGenerator.GeneratePassword(Opt(64));
            Assert.All(pw, c => Assert.DoesNotContain(c, "0O1lI"));
        }
    }

    [Fact]
    public void GeneratePassword_CoverageGuarantee_EverySelectedClassPresent()
    {
        
        for (int i = 0; i < 10; i++)
        {
            var pw = SecretGenerator.GeneratePassword(Opt(16, exAmb: false));
            Assert.Contains(pw, c => char.IsUpper(c));
            Assert.Contains(pw, c => char.IsLower(c));
            Assert.Contains(pw, c => char.IsDigit(c));
            Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void GeneratePassword_NoClassSelected_Throws()
        => Assert.Throws<ArgumentException>(() => SecretGenerator.GeneratePassword(Opt(16, up: false, low: false, dig: false, sym: false)));

    [Fact]
    public void EntropyBits_Formula()
    {
        
        var opt = Opt(16);
        int pool = SecretGenerator.UpperChars.Count(c => !SecretGenerator.AmbiguousChars.Contains(c))
                 + SecretGenerator.LowerChars.Count(c => !SecretGenerator.AmbiguousChars.Contains(c))
                 + SecretGenerator.DigitChars.Count(c => !SecretGenerator.AmbiguousChars.Contains(c))
                 + SecretGenerator.SymbolChars.Length;
        Assert.Equal(16 * Math.Log2(pool), SecretGenerator.EntropyBits(opt), 5);
        Assert.Equal(0.0, SecretGenerator.EntropyBits(Opt(16, up: false, low: false, dig: false, sym: false)));
    }

    [Fact]
    public void GenerateUuid_CanonicalV4Format()
    {
        var u = SecretGenerator.GenerateUuid();
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", u);
        Assert.NotEqual(u, SecretGenerator.GenerateUuid()); 
    }

    [Theory]
    [InlineData(16, 22)] 
    [InlineData(32, 43)] 
    [InlineData(48, 64)]
    public void GenerateToken_Base64Url_Length(int bytes, int expectedLen)
    {
        var t = SecretGenerator.GenerateToken(bytes);
        Assert.Equal(expectedLen, t.Length);
        Assert.DoesNotContain(t, "+");
        Assert.DoesNotContain(t, "/");
        Assert.DoesNotContain(t, "=");
    }
}
