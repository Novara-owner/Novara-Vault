using Novara.Services;
using Xunit;

namespace Novara.Tests;

public class SealPasswordPolicyTests
{


    [Theory]
    [InlineData("Ab3kLm9Qz7Rt")]
    [InlineData("Tr0ub4dor&3xyZ")]
    [InlineData("Xk9#mQ2$vL7%")]
    [InlineData("nU7!bG2@wQ9#")]
    [InlineData("Pz8$Lm3^Rt6&")]
    [InlineData("Kj4%Nb8^Vc2*")]
    [InlineData("Qw3!eR7@tY1#")]
    [InlineData("abcd1234EFGH")]
    [InlineData("这是一个很长的中文密码短语2026!")]
    [InlineData("紫罗兰色的鲸鱼在2026年跳过一次!")]
    [InlineData("长密码需要至少十二个字符才行2026!")]
    public void Accepts(string password)
    {
        var (ok, issue) = SealPasswordPolicy.Evaluate(password);
        Assert.True(ok, $"expected acceptance, got {issue}");
        Assert.Equal(SealPasswordIssue.None, issue);
    }



    [Theory]
    [InlineData("", SealPasswordIssue.TooShort)]
    [InlineData("Ab3!xY", SealPasswordIssue.TooShort)]
    [InlineData("Ab3!xY9@kLm", SealPasswordIssue.TooShort)]
    [InlineData("短密码2026!", SealPasswordIssue.TooShort)]
    public void TooShort_IsRefused(string password, SealPasswordIssue expected)
    {
        var (ok, issue) = SealPasswordPolicy.Evaluate(password);
        Assert.False(ok);
        Assert.Equal(expected, issue);
    }

    [Theory]
    [InlineData("P@ssw0rd123456")]
    [InlineData("MyNovara2000!")]
    [InlineData("Qwerty!2026Aa")]
    [InlineData("Abc123XYZ!@#9")]
    [InlineData("ILoveYou4Ever!!")]
    public void CommonFragments_AreRefused(string password)
    {
        var (ok, issue) = SealPasswordPolicy.Evaluate(password);
        Assert.False(ok);
        Assert.Equal(SealPasswordIssue.WeakFragment, issue);
    }

    [Theory]
    [InlineData("abcdefghijkL")]
    [InlineData("908172635490")]
    [InlineData("这是一个很长的中文密码短语")]
    [InlineData("Абвгдежзийклмн")]
    public void TooFewClasses_IsRefused(string password)
    {
        var (ok, issue) = SealPasswordPolicy.Evaluate(password);
        Assert.False(ok);
        Assert.Equal(SealPasswordIssue.TooFewClasses, issue);
    }

    [Theory]
    [InlineData("Abcdefgh1234!")]
    [InlineData("ZYXWVUT9!abc")]
    [InlineData("Qw!987654Zx#")]
    public void LongRuns_AreRefused(string password)
    {
        var (ok, issue) = SealPasswordPolicy.Evaluate(password);
        Assert.False(ok);
        Assert.Equal(SealPasswordIssue.Sequence, issue);
    }




    [Fact]
    public void LengthBoundary_IsTwelve()
    {
        Assert.True(SealPasswordPolicy.Evaluate("Ab3kLm9Qz7Rt").Ok);
        Assert.False(SealPasswordPolicy.Evaluate("Ab3kLm9Qz7R").Ok);
    }

    [Fact]
    public void Null_IsRefusedAsTooShort()
    {
        Assert.Equal((false, SealPasswordIssue.TooShort), SealPasswordPolicy.Evaluate(null));
    }






    [Fact]
    public void Evaluate_IsPure()
    {
        var first = SealPasswordPolicy.Evaluate("Ab3kLm9Qz7Rt");
        var second = SealPasswordPolicy.Evaluate("Ab3kLm9Qz7Rt");
        Assert.Equal(first, second);
    }


    [Fact]
    public void Rating_UsesTheSharedMeter()
    {
        Assert.Equal(PasswordStrength.Rate("Ab3kLm9Qz7Rt"), SealPasswordPolicy.Rate("Ab3kLm9Qz7Rt"));
        Assert.Equal(BackupPasswordStrength.Strong, SealPasswordPolicy.Rate("Ab3kLm9Qz7Rt"));
        Assert.Equal(BackupPasswordStrength.Weak, SealPasswordPolicy.Rate("short"));
    }
}
