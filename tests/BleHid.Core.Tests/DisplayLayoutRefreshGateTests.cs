using Xunit;

namespace BleHid.Core.Tests;

public sealed class DisplayLayoutRefreshGateTests
{
    [Fact]
    public void Inactive_capture_never_starts_automatically()
    {
        var gate = new DisplayLayoutRefreshGate();
        var revision = gate.Request(captureActive: false, windowsLayout: true);
        Assert.False(gate.IsPending);
        Assert.True(gate.IsCurrent(revision));
        Assert.False(gate.TryTakeResume(revision, allowed: true));
    }

    [Fact]
    public void Traditional_mode_does_not_resume_automatically()
    {
        var gate = new DisplayLayoutRefreshGate();
        var revision = gate.Request(captureActive: true, windowsLayout: false);
        Assert.False(gate.IsPending);
        Assert.False(gate.TryTakeResume(revision, allowed: true));
    }

    [Fact]
    public void Active_windows_mode_resumes_exactly_once()
    {
        var gate = new DisplayLayoutRefreshGate();
        var revision = gate.Request(captureActive: true, windowsLayout: true);
        Assert.True(gate.IsPending);
        Assert.True(gate.TryTakeResume(revision, allowed: true));
        Assert.False(gate.IsPending);
        Assert.False(gate.TryTakeResume(revision, allowed: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bursts_preserve_intent_and_stale_callbacks_cannot_consume_it(bool staleAllowed)
    {
        var gate = new DisplayLayoutRefreshGate();
        var first = gate.Request(captureActive: true, windowsLayout: true);
        var second = gate.Request(captureActive: false, windowsLayout: true);
        var latest = gate.Request(captureActive: false, windowsLayout: true);

        Assert.True(first < second && second < latest);
        Assert.False(gate.IsCurrent(first));
        Assert.False(gate.IsCurrent(second));
        Assert.True(gate.IsCurrent(latest));
        Assert.False(gate.TryTakeResume(first, staleAllowed));
        Assert.False(gate.TryTakeResume(second, staleAllowed));
        Assert.True(gate.IsPending);
        Assert.True(gate.TryTakeResume(latest, allowed: true));
    }

    [Fact]
    public void Cancel_invalidates_callbacks_and_clears_intent()
    {
        var gate = new DisplayLayoutRefreshGate();
        var revision = gate.Request(captureActive: true, windowsLayout: true);
        gate.Cancel();
        Assert.False(gate.IsCurrent(revision));
        Assert.False(gate.IsPending);
        Assert.False(gate.TryTakeResume(revision, allowed: true));

        var next = gate.Request(captureActive: false, windowsLayout: true);
        Assert.True(next > revision);
        Assert.False(gate.TryTakeResume(next, allowed: true));
    }

    [Fact]
    public void Disallowed_current_callback_clears_intent_without_resuming()
    {
        var gate = new DisplayLayoutRefreshGate();
        var revision = gate.Request(captureActive: true, windowsLayout: true);
        Assert.False(gate.TryTakeResume(revision, allowed: false));
        Assert.False(gate.IsPending);
        Assert.False(gate.TryTakeResume(revision, allowed: true));
    }

    [Fact]
    public void Leaving_windows_mode_cancels_pending_intent()
    {
        var gate = new DisplayLayoutRefreshGate();
        var previous = gate.Request(captureActive: true, windowsLayout: true);
        var changedMode = gate.Request(captureActive: false, windowsLayout: false);
        Assert.False(gate.IsPending);
        Assert.False(gate.TryTakeResume(previous, allowed: true));
        Assert.False(gate.TryTakeResume(changedMode, allowed: true));

        var windowsAgain = gate.Request(captureActive: false, windowsLayout: true);
        Assert.False(gate.TryTakeResume(windowsAgain, allowed: true));
    }
}
