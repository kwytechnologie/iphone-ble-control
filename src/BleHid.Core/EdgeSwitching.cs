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
    IReadOnlyList<ScreenBounds> Monitors, ScreenBounds? VirtualMonitor = null);

internal sealed record VirtualMonitorEntry(ScreenBounds Monitor, ScreenEdge Edge, int ReturnX, int ReturnY);

/// <summary>Geometry only. The virtual screen may be included in Monitors, but must not overlap
/// a physical screen. Entry is accepted only across an actual shared edge, never a gap or corner.</summary>
internal sealed class VirtualMonitorGeometry
{
    private readonly ScreenBounds _virtual;
    private readonly ScreenBounds[] _physical;
    private readonly ScreenBounds _preferred;
    internal bool IsValid { get; }

    internal VirtualMonitorGeometry(EdgeSwitchOptions options)
    {
        _virtual = options.VirtualMonitor!.Value;
        _preferred = options.Monitor;
        _physical = options.Monitors.Append(options.Monitor)
            .Where(m => m != _virtual && ValidBounds(m)).Distinct().ToArray();
        IsValid = ValidBounds(_virtual) && _physical.Length > 0 &&
            _physical.All(m => !Overlaps(m, _virtual)) &&
            !_physical.Where((m, i) => _physical.Skip(i + 1).Any(other => Overlaps(m, other))).Any();
    }

    internal bool IsPhysicalPoint(int x, int y) => _physical.Any(m => m.Contains(x, y));

    internal bool TryEntry(int fromX, int fromY, int x, int y, out VirtualMonitorEntry? entry)
    {
        entry = null;
        if (!IsValid || !_virtual.Contains(x, y)) return false;
        foreach (var monitor in _physical)
        {
            if (!monitor.Contains(fromX, fromY)) continue;
            ScreenEdge side;
            double crossing;
            int low, high;
            if (monitor.Right == _virtual.Left && x > fromX)
            {
                side = ScreenEdge.Right;
                crossing = fromY + ((double)y - fromY) * (monitor.Right - (double)fromX) / ((double)x - fromX);
                low = Math.Max(monitor.Top, _virtual.Top); high = Math.Min(monitor.Bottom, _virtual.Bottom);
            }
            else if (monitor.Left == _virtual.Right && x < fromX)
            {
                side = ScreenEdge.Left;
                crossing = fromY + ((double)y - fromY) * (monitor.Left - (double)fromX) / ((double)x - fromX);
                low = Math.Max(monitor.Top, _virtual.Top); high = Math.Min(monitor.Bottom, _virtual.Bottom);
            }
            else if (monitor.Bottom == _virtual.Top && y > fromY)
            {
                side = ScreenEdge.Bottom;
                crossing = fromX + ((double)x - fromX) * (monitor.Bottom - (double)fromY) / ((double)y - fromY);
                low = Math.Max(monitor.Left, _virtual.Left); high = Math.Min(monitor.Right, _virtual.Right);
            }
            else if (monitor.Top == _virtual.Bottom && y < fromY)
            {
                side = ScreenEdge.Top;
                crossing = fromX + ((double)x - fromX) * (monitor.Top - (double)fromY) / ((double)y - fromY);
                low = Math.Max(monitor.Left, _virtual.Left); high = Math.Min(monitor.Right, _virtual.Right);
            }
            else continue;

            // A jump around the end of the shared seam is not a crossing from this monitor.
            if (crossing <= low || crossing >= high) return false;
            var point = ClampInside(monitor, x, y);
            entry = new(monitor, side, point.X, point.Y);
            return true;
        }
        return false;
    }

    internal (int X, int Y)? SafeLocalPoint(int x, int y)
    {
        if (_physical.Length == 0) return null;
        var monitor = _physical.FirstOrDefault(m => m.Contains(x, y));
        if (!ValidBounds(monitor))
            monitor = _physical.Contains(_preferred) ? _preferred : _physical[0];
        return ClampInside(monitor, x, y);
    }

    internal static (int X, int Y) ClampInside(ScreenBounds monitor, int x, int y)
    {
        var marginX = Math.Min(32, (monitor.Width - 1) / 2);
        var marginY = Math.Min(32, (monitor.Height - 1) / 2);
        return (Math.Clamp(x, monitor.Left + marginX, monitor.Right - marginX - 1),
            Math.Clamp(y, monitor.Top + marginY, monitor.Bottom - marginY - 1));
    }

    private static bool ValidBounds(ScreenBounds b) => b.Width > 0 && b.Height > 0 &&
        (long)b.Left + b.Width <= int.MaxValue && (long)b.Top + b.Height <= int.MaxValue;
    private static bool Overlaps(ScreenBounds a, ScreenBounds b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}

/// <summary>
/// Pure geometry/state: no hooks or Bluetooth. Requires an interior observation followed by
/// deliberate pressure at an exposed edge in legacy mode; virtual mode requires a physical
/// origin followed by a crossing of the actual shared seam. Other PC screens remain local.
/// </summary>
internal sealed class EdgeSwitchDetector(EdgeSwitchOptions options)
{
    internal const int DwellMs = 350;
    private bool _armed;
    private long? _since;
    private (int X, int Y)? _previous;
    internal VirtualMonitorGeometry? VirtualGeometry { get; } = options.VirtualMonitor.HasValue
        ? new VirtualMonitorGeometry(options) : null;
    internal VirtualMonitorEntry? LastEntry { get; private set; }

    public bool Observe(int x, int y, long nowMs, bool inputHeld)
    {
        var b = options.Monitor;
        if (inputHeld)
        {
            Reset();
            return false;
        }

        if (VirtualGeometry is { } geometry)
        {
            var previous = _previous;
            _previous = geometry.IsPhysicalPoint(x, y) ? (x, y) : null;
            LastEntry = null;
            if (previous is not { } origin || !geometry.TryEntry(origin.X, origin.Y, x, y, out var entry))
                return false;
            LastEntry = entry;
            return true;
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

    public void Reset() { _armed = false; _since = null; _previous = null; LastEntry = null; }
}
