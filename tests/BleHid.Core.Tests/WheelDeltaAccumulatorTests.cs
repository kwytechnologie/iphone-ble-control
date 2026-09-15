using Xunit;

namespace BleHid.Core.Tests;

public sealed class WheelDeltaAccumulatorTests
{
    [Theory]
    [InlineData(120, -1)]
    [InlineData(-120, 1)]
    [InlineData(240, -2)]
    [InlineData(0, 0)]
    public void Inversion_changes_only_the_remote_direction(int delta, int expected)
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(expected, wheel.Add(delta, invert: true));
    }

    [Fact]
    public void Inversion_preserves_touchpad_fractions_and_reset()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(-30, invert: true));
        Assert.Equal(0, wheel.Add(-30, invert: true));
        Assert.Equal(0, wheel.Add(-30, invert: true));
        Assert.Equal(1, wheel.Add(-30, invert: true));
        Assert.Equal(0, wheel.Add(-90, invert: true));
        wheel.Reset();
        Assert.Equal(0, wheel.Add(-30, invert: true));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(120, 1)]
    [InlineData(-120, -1)]
    [InlineData(360, 3)]
    [InlineData(-360, -3)]
    public void Full_wheel_steps_are_forwarded_without_delay(int delta, int expected)
    {
        Assert.Equal(expected, new WheelDeltaAccumulator().Add(delta));
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(-30, -1)]
    public void Small_touchpad_deltas_accumulate_until_a_complete_step(int delta, int expected)
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(delta));
        Assert.Equal(0, wheel.Add(delta));
        Assert.Equal(0, wheel.Add(delta));
        Assert.Equal(expected, wheel.Add(delta));
        Assert.Equal(0, wheel.Add(0));
    }

    [Theory]
    [InlineData(250, 2, 110, 1)]
    [InlineData(-250, -2, -110, -1)]
    public void Multiple_steps_preserve_the_remaining_fraction(int first, int firstSteps, int second, int secondSteps)
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(firstSteps, wheel.Add(first));
        Assert.Equal(secondSteps, wheel.Add(second));
        Assert.Equal(0, wheel.Add(0));
    }

    [Theory]
    [InlineData(90, -30, -180, -1)]
    [InlineData(-90, 30, 180, 1)]
    public void Reversing_direction_cancels_the_signed_fraction(int first, int reversed, int final, int expected)
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(first));
        Assert.Equal(0, wheel.Add(reversed));
        Assert.Equal(expected, wheel.Add(final));
        Assert.Equal(0, wheel.Add(0));
    }

    [Theory]
    [InlineData(90, 30)]
    [InlineData(-90, -30)]
    public void Reset_prevents_a_partial_step_leaking_to_the_next_destination(int oldDelta, int nextDelta)
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(oldDelta));
        wheel.Reset();
        wheel.Reset();
        Assert.Equal(0, wheel.Add(nextDelta));
        Assert.Equal(Math.Sign(oldDelta), wheel.Add(oldDelta));
    }

    [Fact]
    public void Extreme_delta_does_not_overflow_when_added_to_a_fraction()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(119));
        Assert.Equal((int)(((long)int.MaxValue + 119) / 120), wheel.Add(int.MaxValue));
        wheel.Reset();
        Assert.Equal(0, wheel.Add(-119));
        Assert.Equal((int)(((long)int.MinValue - 119) / 120), wheel.Add(int.MinValue));
    }
}
