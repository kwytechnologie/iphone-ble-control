namespace BleHid.Core;

/// <summary>Converts Windows wheel deltas to HID wheel steps without dropping partial steps.</summary>
internal sealed class WheelDeltaAccumulator
{
    private const int DeltaPerStep = 120;
    private int _remainder;

    /// <summary>Returns complete steps and retains the signed fraction for the next event.</summary>
    public int Add(int delta, bool invert = false)
    {
        // Use a wide sum so even an unusually large delta cannot overflow with the remainder.
        var total = (long)_remainder + delta;
        var steps = (int)(total / DeltaPerStep);
        _remainder = (int)(total % DeltaPerStep);
        return invert ? -steps : steps;
    }

    /// <summary>Discard a partial step when starting capture or changing the input destination.</summary>
    public void Reset() => _remainder = 0;
}
