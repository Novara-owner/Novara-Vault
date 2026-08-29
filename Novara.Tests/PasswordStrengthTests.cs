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
}
