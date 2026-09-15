using Xunit;

namespace BleHid.Core.Tests;

public class CaptureWheelTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    public void Capture_uses_selected_remote_direction(bool invert, int expected)
    {
        using var capture = new InputCapture { InvertScroll = invert };
        Assert.Equal(expected, capture.AccumulateWheelDelta(120));
    }

    [Fact]
    public void Direct_remote_target_change_does_not_carry_a_partial_wheel_step()
    {
        using var capture = new InputCapture();
        Assert.Equal(0, capture.AccumulateWheelDelta(90));
        capture.SetPassThrough(false); // remote -> remote; no hook is started by this test
        Assert.Equal(0, capture.AccumulateWheelDelta(30));
        Assert.Equal(1, capture.AccumulateWheelDelta(90));
    }

    [Fact]
    public void Going_local_and_back_discards_partial_scroll()
    {
        using var capture = new InputCapture { InvertScroll = true };
        Assert.Equal(0, capture.AccumulateWheelDelta(-90));
        capture.SetPassThrough(true);
        capture.SetPassThrough(false);
        Assert.Equal(0, capture.AccumulateWheelDelta(-30));
        Assert.Equal(1, capture.AccumulateWheelDelta(-90));
    }
}
