namespace BleHid.Core;

public enum ScreenEdge { Left, Right, Top, Bottom }

/// <summary>Physical desktop pixels, including negative coordinates on secondary monitors.</summary>
public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public sealed record EdgeSwitchOptions(string HostId, ScreenEdge Edge, ScreenBounds Monitor,
    IReadOnlyList<ScreenBounds> Monitors);

/// <summary>
/// Pure geometry/state: no hooks or Bluetooth. Requires an interior observation followed by
/// deliberate pressure at an exposed edge. Never crosses a seam into another PC monitor.
/// </summary>
internal sealed class EdgeSwitchDetector(EdgeSwitchOptions options)
{
    internal const int DwellMs = 350;
    private bool _armed;
    private long? _since;

    public bool Observe(int x, int y, long nowMs, bool inputHeld)
    {
        var b = options.Monitor;
        if (inputHeld)
        {
            Reset();
            return false;
        }

        if (x >= b.Left + 12 && x < b.Right - 12 && y >= b.Top + 12 && y < b.Bottom - 12)
        {
            _armed = true;
            _since = null;
            return false;
        }

        var alongVertical = y >= b.Top + 20 && y < b.Bottom - 20;
        var alongHorizontal = x >= b.Left + 20 && x < b.Right - 20;
        var atEdge = options.Edge switch
        {
            ScreenEdge.Left => x >= b.Left - 1 && x <= b.Left + 1 && alongVertical,
            ScreenEdge.Right => x >= b.Right - 2 && x <= b.Right && alongVertical,
            ScreenEdge.Top => y >= b.Top - 1 && y <= b.Top + 1 && alongHorizontal,
            ScreenEdge.Bottom => y >= b.Bottom - 2 && y <= b.Bottom && alongHorizontal,
            _ => false
        };
        var outside = options.Edge switch
        {
            ScreenEdge.Left => (b.Left - 1, y),
            ScreenEdge.Right => (b.Right, y),
            ScreenEdge.Top => (x, b.Top - 1),
            _ => (x, b.Bottom)
        };
        if (!atEdge || options.Monitors.Any(m => m.Contains(outside.Item1, outside.Item2)))
        {
            _since = null;
            return false;
        }

        if (!_armed) return false;
        _since ??= nowMs;
        if (nowMs - _since.Value < DwellMs) return false;
        Reset();
        return true;
    }

    public void Reset() { _armed = false; _since = null; }
}
