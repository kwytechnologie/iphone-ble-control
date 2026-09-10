using System.Collections.Concurrent;
using System.Diagnostics;

namespace BleHid.Core;

/// <summary>
/// Runs a capture session: local hooks in, paced HID reports out. Shared by the interactive
/// `capture` command, background mode, and the desktop UI.
/// </summary>
public static class CaptureSession
{
    private enum Queued { Report, SwitchHost, GoLocal, GoEdgeHost }

    /// <param name="stopEndsSession">
    /// Console mode ends on Ctrl+Alt+Q. Background mode has no console to return to, so the
    /// same hotkey drops to the local target instead and capture keeps running.
    /// </param>
    public static async Task<int> RunAsync(
        BleHidPeripheral peripheral,
        Action<string> log,
        bool verbose,
        int mouseIntervalMs,
        bool stopEndsSession,
        CancellationToken cancellationToken,
        EdgeSwitchOptions? edgeSwitch = null,
        RemoteReturnOptions? returnOptions = null)
    {
        using var capture = new InputCapture { Verbose = verbose, EdgeSwitch = edgeSwitch, ReturnOptions = returnOptions ?? new() };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? captureFailure = null;
        using var registration = cancellationToken.Register(() =>
        {
            capture.SetPassThrough(true);
            stopped.TrySetResult();
        });

        // Keystrokes must all be delivered, but pointer motion is coalesced: the hook produces
        // far more events than the BLE link can carry. Target changes travel through the same
        // queue so they take effect only after the key-release report has gone to the old host.
        var keyQueue = new ConcurrentQueue<(Queued Kind, KeyModifiers Modifiers, byte[]? Usages, int Epoch)>();
        var recoveryEpoch = 0;
        var mouseLock = new object();
        int pendingDx = 0, pendingDy = 0, pendingWheel = 0;
        var pendingButtons = MouseButtons.None;
        var mouseDirty = false;
        var sent = 0;

        // The radio interleaves connection events across every subscribed link, so a second host
        // starves the one we are notifying even when it receives nothing. Broadcast pays again on
        // top of that: measured, 2 hosts needed 40 ms rather than 20 ms.
        int PointerIntervalMs()
        {
            var hostIntervalMs = peripheral.MouseReportIntervalMs(mouseIntervalMs);
            var links = Math.Max(1, peripheral.SubscribedMouseClients);
            var broadcasting = peripheral.SelectedHostId is null && !peripheral.IsLocalTarget;
            return broadcasting ? hostIntervalMs * 2 * links : hostIntervalMs;
        }

        // Signal-driven rather than polled: Task.Delay has ~15 ms granularity on Windows,
        // which alone made pointer motion feel sluggish.
        using var signal = new SemaphoreSlim(0, 1);
        void Wake()
        {
            try { signal.Release(); }
            catch (SemaphoreFullException) { }
        }

        using var pumpCancellation = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            var iterations = 0;
            long lastMouseSend = -1000;
            if (verbose) log("  [pump] started");
            while (!pumpCancellation.IsCancellationRequested)
            {
                iterations++;
                try
                {
                    await signal.WaitAsync(pumpCancellation.Token);
                }
                catch (OperationCanceledException) { break; }

                try
                {
                    while (!pumpCancellation.IsCancellationRequested && keyQueue.TryDequeue(out var key))
                    {
                        if (key.Epoch != Volatile.Read(ref recoveryEpoch)) continue;
                        if (key.Kind == Queued.GoEdgeHost)
                        {
                            if (edgeSwitch is null || cancellationToken.IsCancellationRequested ||
                                !peripheral.IsLocalTarget) continue;
                            if (peripheral.SelectHostById(edgeSwitch.HostId))
                                log($"  [telas] Borda -> {peripheral.SelectedHostDisplay}");
                            else
                                log("  [telas] iPhone indisponível; controle mantido no PC.");
                            // Recovery can arrive while selecting a target; it always wins.
                            if (key.Epoch != Volatile.Read(ref recoveryEpoch) || cancellationToken.IsCancellationRequested)
                                peripheral.SelectLocal();
                            continue;
                        }
                        if (key.Kind is Queued.SwitchHost or Queued.GoLocal)
                        {
                            await ReleaseInputAsync(peripheral.ReleaseKeysAsync,
                                () => peripheral.SendMouseAsync(MouseButtons.None, 0, 0, 0), log);
                            lock (mouseLock)
                            {
                                pendingDx = pendingDy = pendingWheel = 0;
                                pendingButtons = MouseButtons.None;
                                mouseDirty = false;
                            }
                            await peripheral.RefreshHostNamesAsync();
                            if (key.Epoch != Volatile.Read(ref recoveryEpoch) || cancellationToken.IsCancellationRequested)
                            {
                                peripheral.SelectLocal();
                                continue;
                            }
                            var target = key.Kind == Queued.GoLocal
                                ? peripheral.SelectLocal()
                                : peripheral.SelectNextHost();
                            capture.SetPassThrough(peripheral.IsLocalTarget || cancellationToken.IsCancellationRequested || stopped.Task.IsCompleted);
                            log($"  [host] -> {target} (pointer interval {PointerIntervalMs()} ms)");
                            continue;
                        }

                        var started = clock.ElapsedMilliseconds;
                        await peripheral.SendKeyboardAsync(key.Modifiers, key.Usages!);
                        var elapsed = clock.ElapsedMilliseconds - started;
                        if (verbose && sent < 40) log($"  [pump] key notify #{sent} took {elapsed} ms");
                        Interlocked.Increment(ref sent);
                    }

                    pumpCancellation.Token.ThrowIfCancellationRequested();
                    bool hasMotion;
                    lock (mouseLock) hasMotion = mouseDirty;
                    if (!hasMotion) continue;

                    // NotifyValueAsync returns on queueing, so overshoot is invisible here and
                    // shows up as pointer drift after the user stops moving.
                    var interval = PointerIntervalMs();

                    var delay = PointerSendTiming.RemainingDelay(clock.ElapsedMilliseconds, lastMouseSend, interval);
                    if (delay > 0)
                        await Task.Delay(delay, pumpCancellation.Token);

                    int dx, dy, wheel;
                    MouseButtons buttons;
                    lock (mouseLock)
                    {
                        dx = pendingDx; dy = pendingDy; wheel = pendingWheel;
                        buttons = pendingButtons;
                        pendingDx = pendingDy = pendingWheel = 0;
                        mouseDirty = false;
                    }

                    {
                        var started = clock.ElapsedMilliseconds;
                        lastMouseSend = started;
                        await peripheral.SendMouseAsync(buttons, dx, dy, wheel);
                        if (verbose && sent < 40) log($"  [pump] mouse notify #{sent} ({dx},{dy}) took {clock.ElapsedMilliseconds - started} ms");
                        Interlocked.Increment(ref sent);
                    }
                }
                catch (OperationCanceledException) when (pumpCancellation.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Return control before any retry or remote key-release attempt.
                    capture.SetPassThrough(true);
                    captureFailure = new InvalidOperationException("Input forwarding failed; control has returned to this PC.", ex);
                    stopped.TrySetResult();
                    log($"  [pump] send error; input returned to this PC: {ex.Message}");
                    break;
                }
            }

            if (verbose) log($"  [pump] exited after {iterations} iterations, {clock.ElapsedMilliseconds} ms");
        });

        capture.Log += message => log(message);
        capture.Faulted += error =>
        {
            captureFailure = error;
            stopped.TrySetResult();
        };
        capture.KeyboardReport += (modifiers, usages) =>
        {
            keyQueue.Enqueue((Queued.Report, modifiers, usages, Volatile.Read(ref recoveryEpoch)));
            Wake();
        };
        capture.MouseReport += (buttons, dx, dy, wheel) =>
        {
            lock (mouseLock)
            {
                pendingDx += dx;
                pendingDy += dy;
                pendingWheel += wheel;
                pendingButtons = buttons;
                mouseDirty = true;
            }
            Wake();
        };
        capture.SwitchHostRequested += () =>
        {
            keyQueue.Enqueue((Queued.SwitchHost, KeyModifiers.None, null, Volatile.Read(ref recoveryEpoch)));
            Wake();
        };
        capture.EdgeSwitchRequested += () =>
        {
            keyQueue.Enqueue((Queued.GoEdgeHost, KeyModifiers.None, null, Volatile.Read(ref recoveryEpoch)));
            Wake();
        };
        capture.StopRequested += () =>
        {
            Interlocked.Increment(ref recoveryEpoch);
            capture.SetPassThrough(true);
            if (stopEndsSession)
            {
                stopped.TrySetResult();
                return;
            }

            // Local recovery must not wait behind a notification to a host that just vanished.
            // Keep GoLocal queued as well so a healthy host still receives key releases first.
            capture.SetPassThrough(true);
            keyQueue.Enqueue((Queued.GoLocal, KeyModifiers.None, null, Volatile.Read(ref recoveryEpoch)));
            Wake();
        };
        capture.ReturnLocalRequested += () =>
        {
            Interlocked.Increment(ref recoveryEpoch);
            capture.SetPassThrough(true);
            keyQueue.Enqueue((Queued.GoLocal, KeyModifiers.None, null, Volatile.Read(ref recoveryEpoch)));
            Wake();
        };

        // The UI can retarget mid-session, and the hook has to stop swallowing input when it does.
        void OnTargetChanged() => capture.SetPassThrough(
            peripheral.IsLocalTarget || cancellationToken.IsCancellationRequested || stopped.Task.IsCompleted);
        peripheral.TargetChanged += OnTargetChanged;

        try
        {
            await peripheral.RefreshHostNamesAsync();
            if (!peripheral.IsLocalTarget && peripheral.Hosts().Count == 0)
                peripheral.SelectLocal();
            capture.SetPassThrough(peripheral.IsLocalTarget);
            cancellationToken.ThrowIfCancellationRequested();
            log($"  pointer report interval: {PointerIntervalMs()} ms");
            log($"  sending to: {peripheral.SelectedHostDisplay}");
            log(stopEndsSession
                ? "  capturing - Ctrl+D+C switches target, Ctrl+Alt+Q stops."
                : "  capturing - Ctrl+D+C switches target, Ctrl+Alt+Q returns input to this PC.");

            capture.Start();
            await stopped.Task;
            if (captureFailure is not null) throw captureFailure;
        }
        finally
        {
            capture.Stop();
            peripheral.TargetChanged -= OnTargetChanged;
            pumpCancellation.Cancel();
            try { await pump; } catch (OperationCanceledException) { }
            await ReleaseInputAsync(peripheral.ReleaseKeysAsync,
                () => peripheral.SendMouseAsync(MouseButtons.None, 0, 0, 0), log);
            peripheral.SelectLocal();
        }

        log($"  capture stopped. keyboard events={capture.KeyboardEvents}, mouse events={capture.MouseEvents}, reports sent={sent}");
        return sent;
    }

    // A failed keyboard release must never prevent the mouse buttons being released.
    internal static async Task ReleaseInputAsync(Func<Task> releaseKeyboard, Func<Task> releaseMouse,
        Action<string> log)
    {
        try { await releaseKeyboard(); }
        catch (Exception ex) { log($"  [release] keyboard release failed: {ex.Message}"); }
        try { await releaseMouse(); }
        catch (Exception ex) { log($"  [release] mouse release failed: {ex.Message}"); }
    }
}
