namespace BleHid.Core;

/// <summary>
/// Tracks the HID usage chosen on key-down until the same physical key is released.
/// Windows can change a key's virtual code while it is held (for example, Num Lock).
/// Owned by the input-hook thread; callers must not update it concurrently.
/// </summary>
internal sealed class CapturedKeyboardState
{
    private readonly record struct KeyIdentity(int Code, bool Extended, bool HasScanCode);

    private readonly Dictionary<KeyIdentity, byte> _pressed = [];

    public void Update(int virtualKey, int scanCode, bool extended, bool isDown)
    {
        // VK_PACKET carries Unicode rather than a hardware scan code. Never let an
        // injected character masquerade as a physical key, including on key-up.
        if (virtualKey is < 1 or > 254 or 0xE7 || scanCode is < 0 or > 0xFF) return;

        var identity = scanCode == 0
            ? new KeyIdentity(virtualKey, false, false)
            : new KeyIdentity(scanCode, extended, true);

        if (!isDown)
        {
            _pressed.Remove(identity);
            return;
        }

        // Keep the original mapping across auto-repeat, even if Windows now reports
        // a different virtual key because Num Lock, Shift or the layout changed.
        if (_pressed.ContainsKey(identity)) return;
        if (VirtualKeyMap.TryGetUsage(virtualKey, scanCode, extended, out var usage))
            _pressed.Add(identity, usage);
    }

    public void Clear() => _pressed.Clear();

    // Distinct physical keys may share a usage. Releasing either one must not release
    // the other; retain all physical keys internally even beyond the six HID slots.
    public byte[] Usages() => _pressed.Values.Distinct().Take(6).ToArray();
}
