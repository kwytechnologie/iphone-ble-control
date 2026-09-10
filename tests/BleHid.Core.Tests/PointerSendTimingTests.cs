using Xunit;

namespace BleHid.Core.Tests;

public sealed class PointerSendTimingTests
{
    [Theory]
    [InlineData(100, 100, 15, 15)]
    [InlineData(105, 100, 15, 10)]
    [InlineData(115, 100, 15, 0)]
    [InlineData(160, 100, 15, 0)]
    [InlineData(0, -1000, 15, 0)]
    [InlineData(110, 100, 30, 20)]
    public void Bluetooth_call_time_counts_toward_the_interval(long now, long previousStart, int interval, int expected)
    {
        Assert.Equal(expected, PointerSendTiming.RemainingDelay(now, previousStart, interval));
    }
}
