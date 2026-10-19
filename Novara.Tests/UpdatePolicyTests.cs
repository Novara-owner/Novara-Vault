using Novara.Services;
using Xunit;

namespace Novara.Tests;







public class UpdatePolicyTests
{


    [Fact]
    public async Task ReadCappedAsync_EnforcesTheSafetyLimit()
    {
        using var ok = new StringContent(new string('a', 100));
        Assert.Equal(100, (await Novara.Services.UpdateHttp.ReadCappedAsync(ok, 1024, CancellationToken.None)).Length);

        using var over = new StringContent(new string('a', 3000));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Novara.Services.UpdateHttp.ReadCappedAsync(over, 1024, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Novara.Services.UpdateHttp.ReadCappedAsync(null!, 1024, CancellationToken.None));
    }

    [Theory]
    [InlineData("novara12", "novara11", true)]
    [InlineData("novara11.R1", "novara11", true)]
    [InlineData("novara11.R2", "novara11.R1", true)]
    [InlineData("novara11", "novara11", false)]
    [InlineData("novara11", "novara11.R1", false)]
    [InlineData("novara11.R1", "novara11.R2", false)]
    [InlineData("novara11", "novara12", false)]
    [InlineData("10.3.0", "novara11", false)]
    [InlineData("v11", "novara11", false)]
    [InlineData("Novara11", "novara11", false)]
    [InlineData("novara11.RX", "novara11", false)]
    [InlineData("", "novara11", false)]
    [InlineData("novara11", "", false)]
    [InlineData("  novara11.R1 ", "novara11", true)]
    [InlineData("novara11.R0", "novara11", false)]
    [InlineData("11.0.0", "10.3.0", true)]
    [InlineData("11.0.0", "novara11", false)]
    [InlineData("11.1.0", "novara11", true)]
    [InlineData("11.1.0", "novara11.R1", false)]
    [InlineData("11.1.0", "11.0.0", true)]
    [InlineData("10.3.0", "11.0.0", false)]
    [InlineData("abc", "novara11", false)]
    [InlineData("novara11", "abc", false)]
    [InlineData("novara99999999999", "novara11", false)]
    [InlineData("novara11.R99999999999", "novara11", false)]
    [InlineData("11.0.0.0", "11.0.0", false)]
    [InlineData("11.0.0.0", "novara11", false)]
    [InlineData("11.1.0.0", "novara11", true)]
    [InlineData("11.0", "11.0.0", false)]
    [InlineData("11.0.0.7", "11.0.0", false)]
    [InlineData("novara11.R10", "novara11.R9", true)]
    [InlineData("novara11.R01", "novara11.R1", false)]
    public void IsNewer_OnlyStrictlyNewerVersionsPrompt(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_NullInputsNeverPrompt()
    {
        Assert.False(UpdatePolicy.IsNewer(null, "novara11"));
        Assert.False(UpdatePolicy.IsNewer("novara11.R1", null));
        Assert.False(UpdatePolicy.IsNewer(null, null));
    }

    [Theory]
    [InlineData(11, 0, "novara11")]
    [InlineData(11, 1, "novara11.R1")]
    [InlineData(12, 0, "novara12")]
    [InlineData(12, 3, "novara12.R3")]
    public void FormatDisplayVersion_MapsMachineVersionToDisplay(int major, int minor, string expected)
    {
        Assert.Equal(expected, UpdatePolicy.FormatDisplayVersion(new Version(major, minor, 0)));
    }

    [Fact]
    public void FormatDisplayVersion_IgnoresPatch_PatchMustStayZeroByDiscipline()
    {



        Assert.Equal("novara11", UpdatePolicy.FormatDisplayVersion(new Version(11, 0, 1)));
    }

    [Theory]
    [InlineData("11.0.0", "novara11")]
    [InlineData("11.1.0", "novara11.R1")]
    [InlineData(" 11.0.0 ", "novara11")]
    [InlineData("novara11", "novara11")]
    [InlineData("novara11.R2", "novara11.R2")]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    public void FormatDisplayVersionFromString_MapsMachineStringToDisplay(string input, string expected)
    {
        Assert.Equal(expected, UpdatePolicy.FormatDisplayVersion(input));
    }

    [Fact]
    public void FormatDisplayVersionFromString_NullReturnsEmpty()
    {
        Assert.Equal("", UpdatePolicy.FormatDisplayVersion((string?)null));
    }

    [Theory]
    [InlineData("novara11", 11, 0)]
    [InlineData("novara11.R1", 11, 1)]
    [InlineData("novara12.R25", 12, 25)]
    public void TryParseDisplayVersion_RoundTripsTheFormat(string text, int major, int revision)
    {
        Assert.True(UpdatePolicy.TryParseDisplayVersion(text, out var m, out var r));
        Assert.Equal(major, m);
        Assert.Equal(revision, r);
    }

    [Theory]
    [InlineData("10.3.0")]
    [InlineData("novara")]
    [InlineData("novara0")]
    [InlineData("novara011")]
    [InlineData("novara11R1")]
    [InlineData("novara11.")]
    [InlineData("novara11.R1.2")]
    public void TryParseDisplayVersion_RejectsOldAndMalformedInput(string text)
    {
        Assert.False(UpdatePolicy.TryParseDisplayVersion(text, out _, out _));
    }



    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void IsAutoCheckDue_FiresOnlyWhenNowReachesDue()
    {
        var due = T0.AddHours(6);
        Assert.False(UpdatePolicy.IsAutoCheckDue(due, T0));
        Assert.False(UpdatePolicy.IsAutoCheckDue(due, due.AddTicks(-1)));
        Assert.True(UpdatePolicy.IsAutoCheckDue(due, due));
        Assert.True(UpdatePolicy.IsAutoCheckDue(due, due.AddMinutes(90)));
    }

    [Fact]
    public void IsAutoCheckDue_WallClockSemantics_AreSafeUnderClockShifts()
    {

        Assert.False(UpdatePolicy.IsAutoCheckDue(T0.AddHours(6), T0.AddHours(3)));

        Assert.True(UpdatePolicy.IsAutoCheckDue(T0.AddHours(6), T0.AddHours(30)));
    }

    [Theory]
    [InlineData("novara12", "", true)]
    [InlineData("novara12", "novara11", true)]
    [InlineData("novara12", "novara12", false)]
    [InlineData("novara11", "novara12", false)]
    [InlineData("novara11", "novara11", false)]
    [InlineData("10.3.0", "", false)]
    [InlineData("novara99999999999", "", false)]
    public void ShouldPromptForVersion_PromptsOncePerVersion(string latest, string pending, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.ShouldPromptForVersion(latest, pending, "novara11"));
    }

    [Fact]
    public void ShouldPromptForVersion_NormalizesBothVersionFormats()
    {

        Assert.False(UpdatePolicy.ShouldPromptForVersion("11.0.0", "novara11", "novara11"));
        Assert.False(UpdatePolicy.ShouldPromptForVersion("novara11", "11.0.0", "novara11"));

        Assert.True(UpdatePolicy.ShouldPromptForVersion("11.1.0", "novara11", "novara11"));
        Assert.True(UpdatePolicy.ShouldPromptForVersion("novara11.R1", "11.0.0", "novara11"));

        Assert.False(UpdatePolicy.ShouldPromptForVersion("11.0.0.0", "11.0.0", "10.3.0"));

        Assert.True(UpdatePolicy.ShouldPromptForVersion("novara11.R10", "novara11.R9", "novara11"));
        Assert.False(UpdatePolicy.ShouldPromptForVersion("novara11.R01", "novara11.R1", "novara11"));
    }

    [Fact]
    public void ShouldPromptForVersion_UnparsablePending_FallsBackToStringCompare()
    {

        Assert.True(UpdatePolicy.ShouldPromptForVersion("novara12", "garbage", "novara11"));
        Assert.False(UpdatePolicy.ShouldPromptForVersion("novara12", "novara12 ", "novara11"));
    }
}
