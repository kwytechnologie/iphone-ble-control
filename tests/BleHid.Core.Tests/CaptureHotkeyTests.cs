using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public class CaptureHotkeyTests
{
    [Theory]
    [InlineData(KeyModifiers.LeftControl | KeyModifiers.LeftAlt)]
    [InlineData(KeyModifiers.RightControl | KeyModifiers.LeftAlt)]
    [InlineData(KeyModifiers.LeftControl | KeyModifiers.LeftAlt | KeyModifiers.LeftShift)]
    public void Ctrl_left_alt_q_still_returns_control_to_pc(KeyModifiers modifiers)
    {
        Assert.True(InputCapture.IsStopShortcut(0x51, true, modifiers));
        Assert.False(InputCapture.IsStopShortcut(0x51, false, modifiers));
        Assert.False(InputCapture.IsStopShortcut(0x41, true, modifiers));
    }

    [Theory]
    [InlineData(KeyModifiers.LeftControl | KeyModifiers.RightAlt)]
    [InlineData(KeyModifiers.RightAlt)]
    [InlineData(KeyModifiers.LeftControl | KeyModifiers.LeftAlt | KeyModifiers.RightAlt)]
    [InlineData(KeyModifiers.LeftControl)]
    [InlineData(KeyModifiers.LeftAlt)]
    [InlineData(KeyModifiers.None)]
    public void Altgr_and_incomplete_chords_do_not_stop_capture(KeyModifiers modifiers)
    {
        Assert.False(InputCapture.IsStopShortcut(0x51, true, modifiers));
    }
}
