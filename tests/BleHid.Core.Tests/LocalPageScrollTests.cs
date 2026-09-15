using Xunit;

namespace BleHid.Core.Tests;

public sealed class LocalPageScrollTests
{
    [Theory]
    [InlineData(-120, 3, 500, 48)]
    [InlineData(120, 3, 500, -48)]
    [InlineData(-30, 3, 500, 12)]
    [InlineData(30, 3, 500, -12)]
    [InlineData(-1, 3, 500, 0.4)]
    [InlineData(-120, 1, 500, 16)]
    [InlineData(-240, 3, 500, 96)]
    [InlineData(-120, 0, 500, 0)]
    [InlineData(0, 3, 500, 0)]
    [InlineData(-120, -1, 500, 500)]
    [InlineData(-30, -1, 500, 125)]
    [InlineData(120, -1, 500, -500)]
    [InlineData(-120, -1, -5, 0)]
    [InlineData(-120, -2, 500, 0)]
    public void Wheel_distance_is_proportional_and_respects_Windows_scroll_setting(
        int delta, int lines, double viewport, double expected)
    {
        Assert.Equal(expected, LocalPageScroll.Pixels(delta, lines, viewport), 8);
    }

    [Fact]
    public void Four_fractional_events_equal_one_notch_without_amplification()
    {
        Assert.Equal(LocalPageScroll.Pixels(-120, 3, 500),
            Enumerable.Repeat(-30, 4).Sum(delta => LocalPageScroll.Pixels(delta, 3, 500)));
    }
}
