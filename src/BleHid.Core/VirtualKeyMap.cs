namespace BleHid.Core;

/// <summary>Maps Windows virtual-key codes to HID keyboard usage IDs.</summary>
public static class VirtualKeyMap
{
    // Both Enter keys have VK_RETURN. Only the extended scan distinguishes the keypad.
    public static bool TryGetUsage(int virtualKey, int scanCode, bool extended, out byte usage)
    {
        if (virtualKey == 0x0D && scanCode == 0x1C && extended)
        {
            usage = 0x58;
            return true;
        }
        return TryGetUsage(virtualKey, out usage);
    }

    public static bool TryGetUsage(int virtualKey, out byte usage)
    {
        switch (virtualKey)
        {
            case >= 0x41 and <= 0x5A: // A-Z
                usage = (byte)(0x04 + (virtualKey - 0x41));
                return true;
            case >= 0x31 and <= 0x39: // 1-9
                usage = (byte)(0x1E + (virtualKey - 0x31));
                return true;
            case 0x30: usage = 0x27; return true; // 0
            case >= 0x70 and <= 0x7B: // F1-F12
                usage = (byte)(0x3A + (virtualKey - 0x70));
                return true;
            case >= 0x61 and <= 0x69: // Numpad 1-9 (Num Lock on)
                usage = (byte)(0x59 + (virtualKey - 0x61));
                return true;
        }

        usage = virtualKey switch
        {
            0x0D => 0x28, // Enter
            0x1B => 0x29, // Escape
            0x08 => 0x2A, // Backspace
            0x09 => 0x2B, // Tab
            0x20 => 0x2C, // Space
            0xBD => 0x2D, // -
            0xBB => 0x2E, // =
            0xDB => 0x2F, // [
            0xDD => 0x30, // ]
            0xDC => 0x31, // \
            0xBA => 0x33, // ;
            0xDE => 0x34, // '
            0xC0 => 0x35, // `
            0xBC => 0x36, // ,
            0xBE => 0x37, // .
            0xBF => 0x38, // /
            0xE2 => 0x64, // OEM_102: extra ISO/ABNT backslash key, not the US backslash
            0x14 => 0x39, // Caps Lock
            0x2C => 0x46, // Print Screen
            0x91 => 0x47, // Scroll Lock
            0x13 => 0x48, // Pause
            0x2D => 0x49, // Insert
            0x24 => 0x4A, // Home
            0x21 => 0x4B, // Page Up
            0x2E => 0x4C, // Delete
            0x23 => 0x4D, // End
            0x22 => 0x4E, // Page Down
            0x27 => 0x4F, // Right
            0x25 => 0x50, // Left
            0x28 => 0x51, // Down
            0x26 => 0x52, // Up
            0x90 => 0x53, // Num Lock
            0x6F => 0x54, // Numpad /
            0x6A => 0x55, // Numpad *
            0x6D => 0x56, // Numpad -
            0x6B => 0x57, // Numpad +
            0x60 => 0x62, // Numpad 0
            0x6E => 0x63, // Numpad decimal separator (interpreted by the host's layout)
            0x5D => 0x65, // Application/menu key
            _ => 0
        };

        return usage != 0;
    }
}
