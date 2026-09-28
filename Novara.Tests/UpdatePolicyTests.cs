using Novara.Services;
using Xunit;

namespace Novara.Tests;




public class UpdatePolicyTests
{
    [Theory]
    [InlineData("9.1.0", "9.0.0", true)]
    [InlineData("9.0.0", "9.0.0", false)]
    [InlineData("9.0.0", "9.1.0", false)]
    [InlineData("v9.1.0", "9.0.0", false)]
    [InlineData("", "9.0.0", false)]
    [InlineData("9.1.0", "", false)]
    [InlineData("10.0", "9.0.9", true)]
    [InlineData(" 9.2.0 ", "9.1.0", true)]
    public void IsNewer_OnlyStrictlyNewerVersionsPrompt(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_NullInputsNeverPrompt()
    {
        Assert.False(UpdatePolicy.IsNewer(null, "9.0.0"));
        Assert.False(UpdatePolicy.IsNewer("9.1.0", null));
        Assert.False(UpdatePolicy.IsNewer(null, null));
    }
}
