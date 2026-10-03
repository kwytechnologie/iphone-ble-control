using System.Runtime.InteropServices;
using BleHid.VirtualDisplayHost;
using Xunit;

namespace BleHid.VirtualDisplayHost.Tests;

// No SwDeviceCreate, UAC, driver loading, Bluetooth, or input hooks in these tests.
public sealed class NativeCallbackBridgeTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate void Callback(IntPtr device, int result, IntPtr context,
        [MarshalAs(UnmanagedType.LPWStr)] string? instanceId);

    [Theory]
    [InlineData(0, @"SWD\BleHidVirtualDisplay\teste-ç")]
    [InlineData(unchecked((int)0x8007007E), null)]
    public void Native_export_belongs_to_module_and_forwards_callback(int result, string? instanceId)
    {
        using var bridge = new NativeCallbackBridge(); // Also validates PE module ownership.
        var invoke = Marshal.GetDelegateForFunctionPointer<Callback>(bridge.EntryPoint);
        var calls = 0;
        (IntPtr Device, int Result, IntPtr Context, string? Id) received = default;
        Callback target = (device, hr, context, id) =>
        {
            received = (device, hr, context, id);
            Interlocked.Increment(ref calls);
        };
        var thunk = Marshal.GetFunctionPointerForDelegate(target);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        invoke(new IntPtr(1234), result, thunk, instanceId);
        Assert.Equal(1, calls);
        Assert.Equal((new IntPtr(1234), result, IntPtr.Zero, instanceId), received);
        GC.KeepAlive(target);
    }

    [Fact]
    public void Null_context_does_not_call_a_delegate()
    {
        using var bridge = new NativeCallbackBridge();
        var invoke = Marshal.GetDelegateForFunctionPointer<Callback>(bridge.EntryPoint);
        invoke(IntPtr.Zero, 0, IntPtr.Zero, null);
    }

    [Fact]
    public void Separate_contexts_do_not_share_callback_state()
    {
        using var first = new NativeCallbackBridge();
        using var second = new NativeCallbackBridge();
        var invoke = Marshal.GetDelegateForFunctionPointer<Callback>(second.EntryPoint);
        var firstCalls = 0;
        var secondCalls = 0;
        Callback firstTarget = (_, _, _, _) => Interlocked.Increment(ref firstCalls);
        Callback secondTarget = (_, _, _, _) => Interlocked.Increment(ref secondCalls);
        var firstThunk = Marshal.GetFunctionPointerForDelegate(firstTarget);
        var secondThunk = Marshal.GetFunctionPointerForDelegate(secondTarget);
        Parallel.For(0, 20, i => invoke(IntPtr.Zero, 0, i % 2 == 0 ? firstThunk : secondThunk, null));
        Assert.Equal(10, firstCalls);
        Assert.Equal(10, secondCalls);
        first.Dispose(); // One owner's release cannot unload another owner's reference.
        invoke(IntPtr.Zero, 0, secondThunk, null);
        Assert.Equal(11, secondCalls);
        GC.KeepAlive(firstTarget);
        GC.KeepAlive(secondTarget);
    }
}
