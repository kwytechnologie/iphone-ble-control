namespace BleHid.Core;

internal static class PointerSendTiming
{
    // Pace start-to-start. Time spent awaiting the Bluetooth call already counts toward
    // the interval; never add a second full interval after a slow notification.
    public static int RemainingDelay(long nowMs, long previousStartMs, int intervalMs) =>
        (int)Math.Clamp((long)Math.Max(1, intervalMs) - (nowMs - previousStartMs), 0, Math.Max(1, intervalMs));
}
