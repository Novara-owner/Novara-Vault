using Novara.Services;
using Xunit;

namespace Novara.Tests;



public class CardGesturePolicyTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(749, false)]
    [InlineData(750, true)]
    [InlineData(1500, true)]
    public void IsLongPress_UsesInclusive750MsBoundary(long elapsed, bool expected)
        => Assert.Equal(expected, CardGesturePolicy.IsLongPress(elapsed));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(16, 0, false)]
    [InlineData(0, 16, false)]
    [InlineData(12, 12, true)]
    [InlineData(11, 11, false)]
    [InlineData(-16.1, 0, true)]
    public void ExceedsMoveTolerance_ComparesSquaredDistance(double dx, double dy, bool expected)
        => Assert.Equal(expected, CardGesturePolicy.ExceedsMoveTolerance(dx, dy));

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(749, false, true)]
    [InlineData(750, false, false)]
    [InlineData(500, true, false)]
    public void IsClick_RequiresShortPressAndNoMovement(long elapsed, bool moved, bool expected)
        => Assert.Equal(expected, CardGesturePolicy.IsClick(elapsed, moved));
}
