namespace BleHid.Core;

public sealed record RemoteReturnOptions(bool MiddleClick = false, bool EstimatedEdge = false, int Travel = 600);

internal enum ReturnGesture { None, Consume, ReturnLocal }

internal sealed class MiddleReturnGesture
{
    private bool _releasePending;
    public ReturnGesture Handle(int message, bool remote, bool enabled)
    {
        if (message == 0x0208 && _releasePending)
        {
            _releasePending = false;
            return ReturnGesture.Consume;
        }
        if (message != 0x0207 || !remote || !enabled) return ReturnGesture.None;
        _releasePending = true;
        return ReturnGesture.ReturnLocal;
    }
}

/// <summary>Relative-motion estimate, NOT an iOS cursor position. Each entry starts at
/// the middle of a virtual travel range. Acceleration/touch can invalidate the estimate.</summary>
internal sealed class RemoteEdgeEstimate(ScreenEdge phoneSide, int travel)
{
    private readonly int _travel = Math.Clamp(travel, 100, 3000);
    private double _depth;
    private long _entry;
    private int _outward;
    public void Reset(long now)
    {
        _depth = _travel / 2.0;
        _entry = now;
        _outward = 0;
    }

    public bool Move(int dx, int dy, bool inputHeld, long now)
    {
        var delta = phoneSide switch
        {
            ScreenEdge.Right => dx, ScreenEdge.Left => -dx,
            ScreenEdge.Bottom => dy, _ => -dy
        };
        var next = _depth + delta;
        _depth = Math.Clamp(next, 0, _travel);
        if (inputHeld || now - _entry < 700 || delta >= 0)
        {
            _outward = 0;
            return false;
        }
        if (next < 0) _outward += (int)Math.Min(-next, 3000);
        if (_outward < 40) return false;
        _outward = 0;
        return true;
    }
}
