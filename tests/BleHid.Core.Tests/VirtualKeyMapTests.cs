using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

/// <summary>
/// Capture translates Windows virtual-key codes into HID usages, so an error here sends the
/// wrong character to the host with no local symptom at all.
/// </summary>
public class VirtualKeyMapTests
{
    [Theory]
    [InlineData(0x41, 0x04)] // A
    [InlineData(0x5A, 0x1D)] // Z
    [InlineData(0x31, 0x1E)] // 1
    [InlineData(0x39, 0x26)] // 9
    [InlineData(0x30, 0x27)] // 0
    [InlineData(0x0D, 0x28)] // Enter
    [InlineData(0x1B, 0x29)] // Escape
    [InlineData(0x08, 0x2A)] // Backspace
    [InlineData(0x09, 0x2B)] // Tab
    [InlineData(0x20, 0x2C)] // Space
    [InlineData(0x70, 0x3A)] // F1
    [InlineData(0x7B, 0x45)] // F12
    [InlineData(0xE2, 0x64)] // ABNT/ISO extra backslash
    [InlineData(0x60, 0x62)] // Numpad 0
    [InlineData(0x61, 0x59)] // Numpad 1
    [InlineData(0x69, 0x61)] // Numpad 9
    [InlineData(0x6F, 0x54)] // Numpad /
    [InlineData(0x6A, 0x55)] // Numpad *
    [InlineData(0x6D, 0x56)] // Numpad -
    [InlineData(0x6B, 0x57)] // Numpad +
    [InlineData(0x6E, 0x63)] // Numpad decimal
    [InlineData(0x90, 0x53)] // Num Lock
    [InlineData(0x5D, 0x65)] // Menu
    public void Maps_known_virtual_keys_to_usage_ids(int virtualKey, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, out var usage));
        Assert.Equal(expected, usage);
    }

    [Theory]
    [InlineData(0x0D, 0x1C, false, 0x28)] // Main Enter
    [InlineData(0x0D, 0x1C, true, 0x58)]  // Numpad Enter
    [InlineData(0x6F, 0x35, true, 0x54)]  // Numpad slash, not main slash
    [InlineData(0xE2, 0x56, false, 0x64)] // Extra ABNT/ISO key
    [InlineData(0x24, 0x47, false, 0x4A)] // Num Lock off: preserve Windows Home semantics
    [InlineData(0x24, 0x47, true, 0x4A)]  // Dedicated Home
    public void Capture_distinguishes_extended_keys(int virtualKey, int scanCode, bool extended, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, scanCode, extended, out var usage));
        Assert.Equal(expected, usage);
        Assert.InRange(usage, (byte)0, (byte)101);
    }

    [Theory]
    [InlineData(0xDB, 0x1A, 0x2F)] // ABNT acute accent
    [InlineData(0xDE, 0x28, 0x34)] // ABNT tilde: do not swap blindly for another host layout
    [InlineData(0xBA, 0x27, 0x33)] // ABNT cedilla
    [InlineData(0xC0, 0x29, 0x35)] // ABNT apostrophe
    public void Brazilian_punctuation_retains_its_physical_hid_position(int virtualKey, int scanCode, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, scanCode, false, out var usage));
        Assert.Equal(expected, usage);
    }

    [Theory]
    [InlineData(0xE7, 0x28)] // VK_PACKET is Unicode input, not a physical accent key
    [InlineData(0xC1, 0x73)] // ABNT C1 needs HID 0x87, outside the existing report map
    [InlineData(0xC2, 0x7E)] // ABNT C2 needs HID 0x85, outside the existing report map
    public void Does_not_emit_unsupported_or_unicode_keys(int virtualKey, int scanCode)
    {
        Assert.False(VirtualKeyMap.TryGetUsage(virtualKey, scanCode, false, out _));
    }

    [Fact]
    public void Function_keys_are_contiguous_from_f1_to_f12()
    {
        for (var i = 0; i < 12; i++)
        {
            Assert.True(VirtualKeyMap.TryGetUsage(0x70 + i, out var usage));
            Assert.Equal(0x3A + i, usage);
        }
    }

    [Fact]
    public void Digits_one_through_nine_are_contiguous()
    {
        for (var i = 0; i < 9; i++)
        {
            Assert.True(VirtualKeyMap.TryGetUsage(0x31 + i, out var usage));
            Assert.Equal(0x1E + i, usage);
        }
    }

    [Fact]
    public void Zero_follows_nine_rather_than_preceding_one()
    {
        VirtualKeyMap.TryGetUsage(0x39, out var nine);
        VirtualKeyMap.TryGetUsage(0x30, out var zero);

        Assert.Equal(nine + 1, zero);
    }

    [Fact]
    public void Distinct_virtual_keys_never_share_a_usage()
    {
        var seen = new Dictionary<byte, int>();

        for (var vk = 0; vk <= 0xFF; vk++)
        {
            if (!VirtualKeyMap.TryGetUsage(vk, out var usage) || usage == 0) continue;
            Assert.False(seen.ContainsKey(usage),
                $"usage 0x{usage:X2} is produced by both VK 0x{seen.GetValueOrDefault(usage):X2} and VK 0x{vk:X2}");
            seen[usage] = vk;
        }
    }

    [Fact]
    public void Unmapped_virtual_keys_are_rejected()
    {
        Assert.False(VirtualKeyMap.TryGetUsage(0x07, out _));
    }

    /// <summary>Anything the map emits must fit the 6-slot key array's logical maximum of 101.</summary>
    [Fact]
    public void Every_mapped_usage_is_within_the_declared_logical_range()
    {
        for (var vk = 0; vk <= 0xFF; vk++)
            if (VirtualKeyMap.TryGetUsage(vk, out var usage))
                Assert.InRange(usage, (byte)0, (byte)101);
    }
}
