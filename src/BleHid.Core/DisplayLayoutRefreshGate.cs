namespace BleHid.Core;

/// <summary>
/// Preserves automatic-resume intent across a burst of Windows display-layout changes.
/// All members must be called on the UI thread; this class is not thread-safe.
/// </summary>
public sealed class DisplayLayoutRefreshGate
{
    private long _revision;

    public bool IsPending { get; private set; }

    public long Request(bool captureActive, bool windowsLayout)
    {
        _revision++;
        IsPending = windowsLayout && (captureActive || IsPending);
        return _revision;
    }

    public bool IsCurrent(long revision) => revision == _revision;

    public bool TryTakeResume(long revision, bool allowed)
    {
        if (!IsCurrent(revision)) return false;
        var resume = IsPending && allowed;
        IsPending = false;
        return resume;
    }

    public void Cancel()
    {
        _revision++;
        IsPending = false;
    }
}
