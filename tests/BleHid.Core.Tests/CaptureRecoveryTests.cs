using Xunit;

namespace BleHid.Core.Tests;

public sealed class CaptureRecoveryTests
{
    [Fact]
    public async Task Keyboard_release_failure_still_releases_mouse_buttons()
    {
        var releasedMouse = false;
        var messages = new List<string>();

        await CaptureSession.ReleaseInputAsync(
            () => Task.FromException(new InvalidOperationException("disconnected keyboard")),
            () => { releasedMouse = true; return Task.CompletedTask; },
            messages.Add);

        Assert.True(releasedMouse);
        Assert.Single(messages);
        Assert.Contains("keyboard release failed", messages[0]);
    }

    [Fact]
    public async Task Both_failed_releases_are_reported_without_masking_the_session_error()
    {
        var messages = new List<string>();

        await CaptureSession.ReleaseInputAsync(
            () => throw new InvalidOperationException("keyboard failure"),
            () => Task.FromException(new InvalidOperationException("mouse failure")),
            messages.Add);

        Assert.Equal(2, messages.Count);
        Assert.Contains("keyboard failure", messages[0]);
        Assert.Contains("mouse failure", messages[1]);
    }

    [Fact]
    public async Task Successful_cleanup_releases_keyboard_then_mouse_without_errors()
    {
        var calls = new List<string>();
        var messages = new List<string>();

        await CaptureSession.ReleaseInputAsync(
            () => { calls.Add("keyboard"); return Task.CompletedTask; },
            () => { calls.Add("mouse"); return Task.CompletedTask; },
            messages.Add);

        Assert.Equal(new[] { "keyboard", "mouse" }, calls);
        Assert.Empty(messages);
    }

    [Fact]
    public void Stopping_before_start_is_safe_and_restores_local_pass_through()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(false);

        capture.Stop();
        capture.Stop();

        Assert.True(capture.PassThrough);
        Assert.False(capture.IsRunning);
    }
}
