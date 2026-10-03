using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BleHid.VirtualDisplayHost;

/// <summary>A module-backed callback entry point; owns no devices or global callback state.</summary>
internal sealed class NativeCallbackBridge : IDisposable
{
    private IntPtr _library;
    public IntPtr EntryPoint { get; }

    public NativeCallbackBridge()
    {
        // Fixed absolute companion path, with dependencies restricted to its directory
        // and System32. No working-directory/PATH search or caller-supplied DLL path.
        var path = Path.Combine(AppContext.BaseDirectory, "BleHid.NativeCallbacks.dll");
        _library = NativeLibrary.Load(path, typeof(NativeCallbackBridge).Assembly,
            DllImportSearchPath.System32 | DllImportSearchPath.UseDllDirectoryForDependencies);
        try
        {
            EntryPoint = NativeLibrary.GetExport(_library, "BleHidSwDeviceCreated");
            // Match the check Windows performs on the callback. Keep our own library
            // reference; UNCHANGED_REFCOUNT means this borrowed handle must not be freed.
            if (!GetModuleHandleEx(0x04 | 0x02, EntryPoint, out var module))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CALLBACK_MODULE_NOT_FOUND");
            if (module != _library)
                throw new InvalidOperationException("CALLBACK_MODULE_MISMATCH");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // The owner must close the software-device handle BEFORE disposing this bridge.
    public void Dispose()
    {
        var library = Interlocked.Exchange(ref _library, IntPtr.Zero);
        if (library != IntPtr.Zero) NativeLibrary.Free(library);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
}
