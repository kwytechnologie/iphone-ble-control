using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class CapturedKeyboardStateTests
{
    [Fact]
    public void Numpad_release_uses_original_usage_after_num_lock_changes_virtual_key()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x61, 0x4F, false, true); // Numpad 1
        Assert.Equal(new byte[] { 0x59 }, state.Usages());

        state.Update(0x23, 0x4F, false, false); // Same key now reports End
        Assert.Empty(state.Usages());
    }

    [Fact]
    public void Auto_repeat_does_not_remap_a_held_key_after_virtual_key_changes()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x61, 0x4F, false, true);
        state.Update(0x23, 0x4F, false, true);
        state.Update(0x23, 0x4F, false, true);
        Assert.Equal(new byte[] { 0x59 }, state.Usages());

        state.Update(0x23, 0x4F, false, false);
        Assert.Empty(state.Usages());
        state.Update(0x23, 0x4F, false, true);
        Assert.Equal(new byte[] { 0x4D }, state.Usages());
    }

    [Fact]
    public void Main_and_keypad_enter_have_separate_physical_identities()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x0D, 0x1C, false, true);
        state.Update(0x0D, 0x1C, true, true);
        Assert.Equal(new byte[] { 0x28, 0x58 }, state.Usages());

        state.Update(0x0D, 0x1C, false, false);
        Assert.Equal(new byte[] { 0x58 }, state.Usages());
        state.Update(0x0D, 0x1C, true, false);
        Assert.Empty(state.Usages());
    }

    [Fact]
    public void Shared_usage_stays_down_until_both_physical_keys_are_released()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x24, 0x47, false, true); // Keypad Home, Num Lock off
        state.Update(0x24, 0x47, true, true); // Dedicated Home
        Assert.Equal(new byte[] { 0x4A }, state.Usages());

        state.Update(0x24, 0x47, false, false);
        Assert.Equal(new byte[] { 0x4A }, state.Usages());
        state.Update(0x24, 0x47, true, false);
        Assert.Empty(state.Usages());
    }

    [Fact]
    public void Unknown_virtual_keys_and_unicode_packets_do_not_create_pressed_usages()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x07, 0x28, false, true);
        state.Update(0xE7, 0x28, false, true);
        state.Update(0xE7, 0x28, false, false);
        Assert.Empty(state.Usages());

        state.Update(0xDE, 0x28, false, true);
        state.Update(0xE7, 0x28, false, false);
        Assert.Equal(new byte[] { 0x34 }, state.Usages());
    }

    [Fact]
    public void Scanless_input_falls_back_to_virtual_key_without_colliding_with_scan_codes()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x41, 0, false, true);
        state.Update(0x77, 0x41, false, true); // F8: scan happens to equal VK_A
        state.Update(0x42, 0, true, true);
        Assert.Equal(new byte[] { 0x04, 0x41, 0x05 }, state.Usages());

        state.Update(0x41, 0, true, false); // Scanless fallback uses VK, not extended
        Assert.Equal(new byte[] { 0x41, 0x05 }, state.Usages());
    }

    [Fact]
    public void Clear_forgets_pressed_keys_and_accepts_a_fresh_mapping()
    {
        var state = new CapturedKeyboardState();
        state.Update(0x61, 0x4F, false, true);
        state.Clear();
        state.Clear();
        Assert.Empty(state.Usages());

        state.Update(0x23, 0x4F, false, true);
        Assert.Equal(new byte[] { 0x4D }, state.Usages());
    }

    [Fact]
    public void Reports_have_at_most_six_usages_without_forgetting_additional_held_keys()
    {
        var state = new CapturedKeyboardState();
        for (var i = 0; i < 8; i++) state.Update(0x41 + i, 0, false, true);
        Assert.Equal(6, state.Usages().Length);

        state.Update(0x41, 0, false, false);
        state.Update(0x42, 0, false, false);
        Assert.Equal(new byte[] { 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B }, state.Usages());
    }
}
