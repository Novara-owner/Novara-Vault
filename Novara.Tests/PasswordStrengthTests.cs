using Novara.Services;
using Xunit;

namespace Novara.Tests;


[Collection("CoreSequential")]
public class PasswordStrengthTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1234567")]
    [InlineData("abcdefgh")]
    [InlineData("123456789012")]
    [InlineData("Ab1!")]
    public void Rate_Weak(string? pw) => Assert.Equal(BackupPasswordStrength.Weak, PasswordStrength.Rate(pw));

    [Theory]
    [InlineData("Abc123")]
    [InlineData("Abcdefg1")]
    [InlineData("abcdefg1!")]
    public void Rate_Medium(string? pw) => Assert.Equal(BackupPasswordStrength.Medium, PasswordStrength.Rate(pw));

    [Theory]
    [InlineData("Abcdefg12345")]
    [InlineData("Correct-Horse-99")]
    public void Rate_Strong(string? pw) => Assert.Equal(BackupPasswordStrength.Strong, PasswordStrength.Rate(pw));





    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("1234567")]
    [InlineData("abc123!")]
    public void RateLockPassword_Weak(string? pw) => Assert.Equal(BackupPasswordStrength.Weak, PasswordStrength.RateLockPassword(pw));

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("abcdefg1")]
    public void RateLockPassword_Medium(string? pw) => Assert.Equal(BackupPasswordStrength.Medium, PasswordStrength.RateLockPassword(pw));

    [Theory]
    [InlineData("abc1234567")]
    [InlineData("13800138000abc")]
    public void RateLockPassword_Strong(string? pw) => Assert.Equal(BackupPasswordStrength.Strong, PasswordStrength.RateLockPassword(pw));
}
