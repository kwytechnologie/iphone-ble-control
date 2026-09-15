namespace BleHid.Core;

/// <summary>Pixel distance for the PC application's pages; not used by remote HID scrolling.</summary>
public static class LocalPageScroll
{
    public static double Pixels(int wheelDelta, int scrollLines, double viewportHeight)
    {
        var perNotch = scrollLines switch
        {
            -1 => double.IsFinite(viewportHeight) ? Math.Max(0, viewportHeight) : 0,
            > 0 => scrollLines * 16.0,
            _ => 0
        };
        return -(wheelDelta / 120.0) * perNotch;
    }
}
