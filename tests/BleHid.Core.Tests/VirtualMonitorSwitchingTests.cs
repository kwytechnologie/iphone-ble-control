using Xunit;

namespace BleHid.Core.Tests;

public sealed class VirtualMonitorSwitchingTests
{
    private static readonly ScreenBounds Pc = new(0, 0, 1920, 1080);
    private static readonly ScreenBounds Phone = new(1920, 0, 1080, 1920);
    private static EdgeSwitchDetector Detector(ScreenBounds phone, params ScreenBounds[] real) =>
        new(new EdgeSwitchOptions("phone", ScreenEdge.Right, Pc,
            new[] { Pc, phone }.Concat(real).ToArray(), phone));

    [Theory]
    [InlineData(ScreenEdge.Right, 1920, 0, 1900, 500, 1960, 500)]
    [InlineData(ScreenEdge.Left, -1080, 0, 20, 500, -40, 500)]
    [InlineData(ScreenEdge.Top, 0, -1920, 500, 20, 500, -40)]
    [InlineData(ScreenEdge.Bottom, 0, 1080, 500, 1060, 500, 1120)]
    public void Crossing_any_shared_edge_is_immediate_and_uses_actual_direction(
        ScreenEdge expected, int left, int top, int fromX, int fromY, int x, int y)
    {
        var phone = new ScreenBounds(left, top, 1080, 1920);
        var detector = Detector(phone);
        Assert.False(detector.Observe(fromX, fromY, 10, false));
        Assert.True(detector.Observe(x, y, 11, false));
        var entry = Assert.IsType<VirtualMonitorEntry>(detector.LastEntry);
        Assert.Equal(expected, entry.Edge);
        Assert.Equal(Pc, entry.Monitor);
        Assert.True(Pc.Contains(entry.ReturnX, entry.ReturnY));
        Assert.False(phone.Contains(entry.ReturnX, entry.ReturnY));
        Assert.False(detector.Observe(x, y, 1000, false));
    }

    [Fact]
    public void Entering_from_secondary_monitor_records_that_monitor_and_its_actual_side()
    {
        var secondary = new ScreenBounds(1920, -1080, 1080, 1080);
        var detector = Detector(Phone, secondary);
        detector.Observe(2000, -10, 0, false);
        Assert.True(detector.Observe(2000, 10, 1, false));
        var entry = Assert.IsType<VirtualMonitorEntry>(detector.LastEntry);
        Assert.Equal(secondary, entry.Monitor);
        Assert.Equal(ScreenEdge.Bottom, entry.Edge);
        Assert.Equal((2000, -33), (entry.ReturnX, entry.ReturnY));

        var estimate = new RemoteEdgeEstimate(entry.Edge, 600);
        estimate.Reset(0);
        Assert.False(estimate.Move(-350, 0, false, 800));
        Assert.True(estimate.Move(0, -350, false, 801));
    }

    [Fact]
    public void Traveling_to_another_real_monitor_does_not_capture()
    {
        var secondary = new ScreenBounds(1920, -1080, 1080, 1080);
        var detector = Detector(Phone, secondary);
        detector.Observe(1900, 10, 0, false);
        Assert.False(detector.Observe(2000, -10, 1, false));
        Assert.True(detector.Observe(2000, 10, 2, false));
        Assert.Equal(secondary, detector.LastEntry!.Monitor);
    }

    [Fact]
    public void Starting_inside_virtual_monitor_or_at_stationary_physical_edge_does_not_capture()
    {
        var detector = Detector(Phone);
        Assert.False(detector.Observe(2000, 500, 0, false));
        Assert.False(detector.Observe(2000, 500, 10000, false));
        Assert.False(detector.Observe(1919, 500, 10001, false));
        Assert.False(detector.Observe(1919, 500, 20000, false));
        Assert.True(detector.Observe(1920, 500, 20001, false));
    }

    [Fact]
    public void Held_input_disarms_crossing_and_release_inside_virtual_does_not_capture()
    {
        var detector = Detector(Phone);
        detector.Observe(1800, 500, 0, false);
        Assert.False(detector.Observe(1940, 500, 1, true));
        Assert.False(detector.Observe(1980, 500, 2, false));
        Assert.Null(detector.LastEntry);
        Assert.False(detector.Observe(1800, 500, 3, false));
        Assert.True(detector.Observe(1940, 500, 4, false));
    }

    [Fact]
    public void Held_observation_on_physical_monitor_must_be_rearmed_before_crossing()
    {
        var detector = Detector(Phone);
        Assert.False(detector.Observe(1800, 500, 0, true));
        Assert.False(detector.Observe(1940, 500, 1, false));
    }

    [Fact]
    public void Reset_clears_origin_and_return_entry()
    {
        var detector = Detector(Phone);
        detector.Observe(1800, 500, 0, false);
        detector.Reset();
        Assert.False(detector.Observe(1940, 500, 1, false));
        detector.Observe(1800, 500, 2, false);
        Assert.True(detector.Observe(1940, 500, 3, false));
        detector.Reset();
        Assert.Null(detector.LastEntry);
    }

    [Theory]
    [InlineData(1921, 0, 1940, 500)] // One-pixel gap.
    [InlineData(1920, 1080, 1940, 1100)] // Only a corner is shared.
    public void Gaps_and_corner_only_contact_fail_safe(int left, int top, int x, int y)
    {
        var detector = Detector(new(left, top, 1080, 1920));
        detector.Observe(1800, 500, 0, false);
        Assert.False(detector.Observe(x, y, 1, false));
        Assert.Null(detector.LastEntry);
    }

    [Fact]
    public void Diagonal_jump_around_shared_seam_is_not_entry()
    {
        var phone = new ScreenBounds(1920, 600, 1080, 1920);
        var detector = Detector(phone);
        detector.Observe(1910, 500, 0, false);
        // Destination is virtual, but at x=1920 the path is still above y=600.
        Assert.False(detector.Observe(2500, 700, 1, false));
        detector.Observe(1910, 800, 2, false);
        Assert.True(detector.Observe(1950, 900, 3, false));
    }

    [Fact]
    public void Overlapping_virtual_or_real_monitors_disable_entry()
    {
        var overlap = Detector(new(1910, 0, 1080, 1920));
        Assert.False(overlap.VirtualGeometry!.IsValid);
        overlap.Observe(1800, 500, 0, false);
        Assert.False(overlap.Observe(1950, 500, 1, false));

        var realOverlap = Detector(Phone, new ScreenBounds(10, 10, 200, 200));
        Assert.False(realOverlap.VirtualGeometry!.IsValid);
    }

    [Fact]
    public void Invalid_virtual_configuration_fails_before_installing_hooks()
    {
        using var capture = new InputCapture
        {
            EdgeSwitch = new("phone", ScreenEdge.Right, Pc, [Pc], new(1900, 0, 1080, 1920))
        };
        capture.SetPassThrough(true);
        Assert.Throws<InvalidOperationException>(capture.Start);
        Assert.False(capture.IsRunning);
        Assert.True(capture.PassThrough);
    }

    [Fact]
    public void Virtual_display_may_be_omitted_from_or_duplicated_in_inventory()
    {
        var omitted = new VirtualMonitorGeometry(new("phone", ScreenEdge.Right, Pc, [Pc], Phone));
        var duplicate = new VirtualMonitorGeometry(new("phone", ScreenEdge.Right, Pc, [Pc, Pc, Phone, Phone], Phone));
        Assert.True(omitted.IsValid);
        Assert.True(duplicate.IsValid);
        Assert.True(omitted.TryEntry(1800, 500, 1950, 500, out _));
        Assert.True(duplicate.TryEntry(1800, 500, 1950, 500, out _));
    }

    [Fact]
    public void Safe_return_clamps_to_real_screen_even_when_cursor_is_already_virtual()
    {
        var geometry = new VirtualMonitorGeometry(new("phone", ScreenEdge.Right, Pc, [Pc, Phone], Phone));
        Assert.Equal((1887, 500), geometry.SafeLocalPoint(2000, 500)!.Value);
        Assert.False(geometry.IsPhysicalPoint(2000, 500));
        Assert.True(geometry.IsPhysicalPoint(1887, 500));
    }

    [Fact]
    public void Negative_coordinates_and_small_physical_screens_have_valid_return_points()
    {
        var pc = new ScreenBounds(-60, -50, 60, 50);
        var phone = new ScreenBounds(0, -50, 100, 100);
        var geometry = new VirtualMonitorGeometry(new("phone", ScreenEdge.Right, pc, [pc, phone], phone));
        Assert.True(geometry.TryEntry(-1, -25, 5, -25, out var entry));
        Assert.True(pc.Contains(entry!.ReturnX, entry.ReturnY));
        Assert.Equal((-30, -25), (entry.ReturnX, entry.ReturnY));
        Assert.Equal((0, 0), VirtualMonitorGeometry.ClampInside(new(0, 0, 1, 1), 2000, -2000));
    }

    [Theory]
    [InlineData(0, 1920)]
    [InlineData(1080, 0)]
    [InlineData(-1, 1920)]
    public void Invalid_screen_sizes_disable_geometry(int width, int height)
    {
        var geometry = new VirtualMonitorGeometry(new("phone", ScreenEdge.Right, Pc, [Pc], new(1920, 0, width, height)));
        Assert.False(geometry.IsValid);
    }
}
