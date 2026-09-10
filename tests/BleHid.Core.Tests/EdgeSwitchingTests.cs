using Xunit;

namespace BleHid.Core.Tests;

public sealed class EdgeSwitchingTests
{
    [Fact]
    public async Task Hook_thread_samples_cursor_on_timer_without_requiring_mouse_callbacks()
    {
        // The fake monitor is outside the real desktop, so this test cannot select a host.
        // Pass-through stays on; no Bluetooth, cursor movement or input injection is used.
        var outside = new ScreenBounds(-100000, -100000, 1000, 1000);
        using var capture = new InputCapture
        {
            EdgeSwitch = new EdgeSwitchOptions("test-only", ScreenEdge.Right, outside, [outside])
        };
        capture.SetPassThrough(true);
        try
        {
            capture.Start();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (capture.EdgeSamples < 4 && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.True(capture.EdgeSamples >= 4, "The hook thread must poll independently of WM_MOUSEMOVE.");
            Assert.True(capture.PassThrough);
        }
        finally { capture.Stop(); }
        Assert.False(capture.IsRunning);
    }

    [Fact]
    public void Stationary_cursor_at_edge_triggers_after_periodic_samples()
    {
        var detector = Detector(ScreenEdge.Right);
        detector.Observe(900, 500, 0, false);
        for (var now = 25; now < 375; now += 25)
            Assert.False(detector.Observe(1919, 500, now, false));
        Assert.True(detector.Observe(1919, 500, 375, false));
        Assert.False(detector.Observe(1919, 500, 400, false));
    }

    private static readonly ScreenBounds Monitor = new(0, 0, 1920, 1080);
    private static EdgeSwitchDetector Detector(ScreenEdge side, params ScreenBounds[] others) =>
        new(new EdgeSwitchOptions("test-phone", side, Monitor, new[] { Monitor }.Concat(others).ToArray()));

    [Theory]
    [InlineData(ScreenEdge.Left, 0, 500)]
    [InlineData(ScreenEdge.Right, 1919, 500)]
    [InlineData(ScreenEdge.Top, 900, 0)]
    [InlineData(ScreenEdge.Bottom, 900, 1079)]
    public void All_four_edges_require_interior_then_dwell(ScreenEdge edge, int x, int y)
    {
        var detector = Detector(edge);
        Assert.False(detector.Observe(900, 500, 0, false));
        Assert.False(detector.Observe(x, y, 100, false));
        Assert.False(detector.Observe(x, y, 449, false));
        Assert.True(detector.Observe(x, y, 450, false));
        Assert.False(detector.Observe(x, y, 900, false));
    }

    [Fact]
    public void Starting_on_edge_does_not_take_control()
    {
        var detector = Detector(ScreenEdge.Right);
        Assert.False(detector.Observe(1919, 500, 0, false));
        Assert.False(detector.Observe(1919, 500, 5000, false));
    }

    [Fact]
    public void Held_key_or_mouse_button_disarms_until_cursor_reenters_interior()
    {
        var detector = Detector(ScreenEdge.Right);
        detector.Observe(900, 500, 0, false);
        detector.Observe(1919, 500, 100, false);
        Assert.False(detector.Observe(1919, 500, 450, true));
        Assert.False(detector.Observe(1919, 500, 1000, false));
        detector.Observe(900, 500, 1100, false);
        detector.Observe(1919, 500, 1200, false);
        Assert.True(detector.Observe(1919, 500, 1550, false));
    }

    [Theory]
    [InlineData(ScreenEdge.Right, 1920, 0, 1919, 500)]
    [InlineData(ScreenEdge.Left, -1920, 0, 0, 500)]
    [InlineData(ScreenEdge.Top, 0, -1080, 900, 0)]
    [InlineData(ScreenEdge.Bottom, 0, 1080, 900, 1079)]
    public void Does_not_intercept_seams_between_pc_monitors(ScreenEdge edge, int left, int top, int x, int y)
    {
        var detector = Detector(edge, new ScreenBounds(left, top, 1920, 1080));
        detector.Observe(900, 500, 0, false);
        Assert.False(detector.Observe(x, y, 100, false));
        Assert.False(detector.Observe(x, y, 1000, false));
    }

    [Fact]
    public void Exposed_part_of_a_partially_shared_edge_is_allowed()
    {
        var detector = Detector(ScreenEdge.Right, new ScreenBounds(1920, 600, 1920, 1080));
        detector.Observe(900, 500, 0, false);
        detector.Observe(1919, 500, 100, false);
        Assert.True(detector.Observe(1919, 500, 450, false));
    }

    [Theory]
    [InlineData(1919, 0)]
    [InlineData(1919, 1079)]
    [InlineData(0, 500)]
    [InlineData(2500, 500)]
    public void Corners_wrong_edge_and_outside_points_do_not_trigger(int x, int y)
    {
        var detector = Detector(ScreenEdge.Right);
        detector.Observe(900, 500, 0, false);
        Assert.False(detector.Observe(x, y, 100, false));
        Assert.False(detector.Observe(x, y, 1000, false));
    }

    [Fact]
    public void Leaving_edge_restarts_dwell()
    {
        var detector = Detector(ScreenEdge.Right);
        detector.Observe(900, 500, 0, false);
        detector.Observe(1919, 500, 100, false);
        detector.Observe(1910, 500, 400, false);
        Assert.False(detector.Observe(1919, 500, 450, false));
        Assert.True(detector.Observe(1919, 500, 800, false));
    }

    [Fact]
    public void Reset_after_local_recovery_requires_new_interior_observation()
    {
        var detector = Detector(ScreenEdge.Right);
        detector.Observe(900, 500, 0, false);
        detector.Observe(1919, 500, 100, false);
        detector.Reset();
        Assert.False(detector.Observe(1919, 500, 900, false));
        detector.Observe(1800, 500, 1000, false);
        detector.Observe(1919, 500, 1100, false);
        Assert.True(detector.Observe(1919, 500, 1450, false));
    }

    [Fact]
    public void Supports_negative_physical_coordinates()
    {
        var b = new ScreenBounds(-2560, -1440, 2560, 1440);
        var detector = new EdgeSwitchDetector(new EdgeSwitchOptions("phone", ScreenEdge.Left, b, [b]));
        detector.Observe(-1000, -700, 0, false);
        detector.Observe(-2560, -700, 100, false);
        Assert.True(detector.Observe(-2560, -700, 450, false));
    }

    [Fact]
    public void Bounds_exclude_right_and_bottom_pixel_for_neighbor_detection()
    {
        Assert.True(Monitor.Contains(0, 0));
        Assert.True(Monitor.Contains(1919, 1079));
        Assert.False(Monitor.Contains(1920, 500));
        Assert.False(Monitor.Contains(500, 1080));
        Assert.False(Monitor.Contains(-1, 500));
    }
}
