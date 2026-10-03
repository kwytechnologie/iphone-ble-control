using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BleHid.VirtualDisplayHost;

internal static class Program
{
    private const string Enumerator = "BleHidVirtualDisplay";
    private const string Instance = "iPhone";
    private const string StopPrefix = @"Local\BleHid.VirtualDisplay.Stop.";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BleHid", "logs", "virtual-display-host.log");

    // No default action and no self-elevation: the application must explicitly start
    // this companion with RunAs. Never installs a package or changes display topology.
    public static int Main(string[] args)
    {
        if (!TryParse(args, out var parentId, out var parentStartTicks, out var stopName))
        {
            Console.Error.WriteLine("Usage: BleHid.VirtualDisplayHost --parent-pid N --parent-start-ticks UTC_TICKS --stop-event Local\\BleHid.VirtualDisplay.Stop.GUID_N");
            Log("ERROR reason=INVALID_ARGUMENTS exit=2");
            return 2;
        }

        IntPtr device = IntPtr.Zero;
        Native.CreateCallback? callback = null;
        NativeCallbackBridge? callbackBridge = null;
        var stageExitCode = 1;
        try
        {
            stageExitCode = 10;
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("ADMIN_REQUIRED");

            stageExitCode = 11;
            using var parent = Process.GetProcessById(parentId);
            stageExitCode = 12;
            ValidateParent(parent, parentStartTicks);
            // Open, never create: a typo or missing parent event must fail closed.
            stageExitCode = 14;
            using var stop = EventWaitHandle.OpenExisting(stopName);
            stageExitCode = 15;
            using var singleton = new Mutex(true, @"Global\BleHid.VirtualDisplayHost.iPhone", out var ownsSingleton);
            if (!ownsSingleton)
                throw new InvalidOperationException("HOST_ALREADY_EXISTS");

            stageExitCode = 1;
            using var cancelled = new ManualResetEvent(false);
            using var enumerated = new ManualResetEvent(false);
            ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancelled.Set(); };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                if (MustStop(parent, stop, cancelled)) return 0;
                var createResult = unchecked((int)0x80004005);
                string? instanceId = null;
                callback = (_, result, _, nativeInstanceId) =>
                {
                    // Native callbacks must not throw or touch a dispatcher. SwDeviceClose
                    // waits for them; retain the delegate until after the handle is closed.
                    createResult = result;
                    instanceId = nativeInstanceId;
                    enumerated.Set();
                };

                var info = new Native.CreateInfo
                {
                    Size = (uint)Marshal.SizeOf<Native.CreateInfo>(),
                    InstanceId = Instance,
                    HardwareIds = "MttVDD\0\0",
                    CapabilityFlags = 0x01 | 0x08, // Removable | DriverRequired, visible in Device Manager.
                    Description = "iPhone BLE Control"
                };
                stageExitCode = 19;
                callbackBridge = new NativeCallbackBridge();
                var managedCallback = Marshal.GetFunctionPointerForDelegate(callback);
                Log("CALLBACK_READY source=native-module");
                Log("CREATING");
                stageExitCode = 20;
                var hr = Native.SwDeviceCreate(Enumerator, @"HTREE\ROOT\0", ref info,
                    0, IntPtr.Zero, callbackBridge.EntryPoint, managedCallback, out device);
                Log($"CREATE_RETURN hresult=0x{hr:X8} handlePresent={device != IntPtr.Zero}");
                Marshal.ThrowExceptionForHR(hr);
                if (device == IntPtr.Zero) throw new InvalidOperationException("NO_DEVICE_HANDLE");

                stageExitCode = 22;
                var deadline = Stopwatch.StartNew();
                while (!enumerated.WaitOne(200))
                {
                    if (MustStop(parent, stop, cancelled)) return 0;
                    if (deadline.Elapsed > TimeSpan.FromSeconds(30))
                        throw new TimeoutException("ENUMERATION_TIMEOUT");
                }
                Marshal.ThrowExceptionForHR(createResult);
                // Default lifetime is Handle; explicitly require it after enumeration so
                // this feature cannot outlive its owning process.
                stageExitCode = 23;
                Marshal.ThrowExceptionForHR(Native.SwDeviceSetLifetime(device, 0));
                stageExitCode = 24;
                if (string.IsNullOrEmpty(instanceId) ||
                    !instanceId.StartsWith(@"SWD\BleHidVirtualDisplay\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("UNEXPECTED_DEVICE_INSTANCE");
                if (MustStop(parent, stop, cancelled)) return 0;

                // Enumeration confirms the devnode, NOT driver startup or display arrival.
                // The parent must observe the corresponding display before routing input.
                Log($"ENUMERATED instance={instanceId}");
                stageExitCode = 1;
                while (!MustStop(parent, stop, cancelled))
                    WaitHandle.WaitAny([stop, cancelled], 250);
                Log("STOP_REQUESTED");
                return 0;
            }
            finally
            {
                // Close before disposing callback state and releasing the global mutex.
                if (device != IntPtr.Zero)
                {
                    Native.SwDeviceClose(device);
                    device = IntPtr.Zero;
                    Log("DEVICE_RELEASED removal=pending");
                }
                GC.KeepAlive(callback);
                Console.CancelKeyPress -= cancelHandler;
                singleton.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            // Exception messages/paths can contain private information. Keep diagnostics bounded.
            var exitCode = ex switch
            {
                InvalidOperationException { Message: "PARENT_LOCATION_MISMATCH" } => 13,
                TimeoutException { Message: "ENUMERATION_TIMEOUT" } => 21,
                _ => stageExitCode
            };
            Log($"ERROR stage={exitCode} type={ex.GetType().Name} hresult=0x{ex.HResult:X8}");
            if (exitCode == 20 && ex is FileNotFoundException or DllNotFoundException)
            {
                // Local diagnostics only: distinguish an import failure from Windows
                // returning ERROR_MOD_NOT_FOUND inside SwDeviceCreate.
                var file = ex is FileNotFoundException missing ? missing.FileName : null;
                Log($"CREATE_FAILURE message={ex.Message.Replace('\r', ' ').Replace('\n', ' ')} file={file ?? "(not supplied)"}");
            }
            // Preserve the native creation failure for the UI even if log output is
            // redirected/unavailable. Process.ExitCode exposes this signed HRESULT.
            if (exitCode == 20 && ex.HResult < 0) return ex.HResult;
            return exitCode;
        }
        finally
        {
            if (device != IntPtr.Zero) Native.SwDeviceClose(device);
            GC.KeepAlive(callback);
            callbackBridge?.Dispose();
        }
    }

    private static bool MustStop(Process parent, WaitHandle stop, WaitHandle cancelled) =>
        stop.WaitOne(0) || cancelled.WaitOne(0) || parent.HasExited;

    private static void ValidateParent(Process parent, long expectedStartTicks)
    {
        if (parent.Id == Environment.ProcessId || parent.HasExited ||
            parent.StartTime.ToUniversalTime().Ticks != expectedStartTicks ||
            parent.SessionId != Process.GetCurrentProcess().SessionId)
            throw new InvalidOperationException("INVALID_PARENT");
        // Opening/retaining the process handle also protects against PID reuse.
        _ = parent.Handle;
        var parentPath = parent.MainModule?.FileName;
        var helperPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(helperPath) ||
            !string.Equals(Path.GetFileName(parentPath), "BleHid.App.exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(parentPath)),
                Path.GetDirectoryName(Path.GetFullPath(helperPath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PARENT_LOCATION_MISMATCH");
    }

    private static bool TryParse(string[] args, out int pid, out long ticks, out string stopName)
    {
        pid = 0;
        ticks = 0;
        stopName = "";
        if (args.Length != 6) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (!values.TryAdd(args[i], args[i + 1])) return false;
        return values.TryGetValue("--parent-pid", out var pidText) &&
            int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0 &&
            values.TryGetValue("--parent-start-ticks", out var tickText) &&
            long.TryParse(tickText, NumberStyles.None, CultureInfo.InvariantCulture, out ticks) &&
            ticks > 0 && ticks <= DateTime.MaxValue.Ticks &&
            values.TryGetValue("--stop-event", out stopName!) &&
            stopName.StartsWith(StopPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(stopName[StopPrefix.Length..], "N", out _);
    }

    private static void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}";
        try { Console.WriteLine(line); } catch (IOException) { }
        try
        {
            // Fixed destination only. Reject reparse points (including ancestor directories)
            // rather than following a user-created link from an elevated process.
            var directory = Path.GetDirectoryName(LogPath)!;
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            Directory.CreateDirectory(directory);
            if (File.Exists(LogPath) && File.GetAttributes(LogPath).HasFlag(FileAttributes.ReparsePoint)) return;
            // MSIX can legitimately redirect LocalAppData without a reparse point.
            // Resolve the fixed directory through its handle, then require the file
            // to be inside that exact directory. Never accept an arbitrary final path.
            using var directoryHandle = Native.CreateFile(directory, 0, 7, IntPtr.Zero, 3,
                0x02200000, IntPtr.Zero); // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
            if (directoryHandle.IsInvalid ||
                !Native.GetFileInformationByHandle(directoryHandle.DangerousGetHandle(), out var directoryInfo) ||
                (directoryInfo.Attributes & 0x400) != 0) return;
            var finalDirectory = FinalPath(directoryHandle);
            if (finalDirectory is null) return;
            using var stream = new FileStream(LogPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (!Native.GetFileInformationByHandle(stream.SafeFileHandle.DangerousGetHandle(), out var fileInfo) ||
                fileInfo.NumberOfLinks != 1 || (fileInfo.Attributes & 0x400) != 0) return;
            var finalFile = FinalPath(stream.SafeFileHandle);
            if (!string.Equals(finalFile, finalDirectory.TrimEnd('\\') + "\\" + Path.GetFileName(LogPath),
                    StringComparison.OrdinalIgnoreCase) && !IsRedirectedLocalLog(finalFile)) return;
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Logging must never prevent device release.
        }
    }

    private static string? FinalPath(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768);
        var length = Native.GetFinalPathNameByHandle(handle.DangerousGetHandle(), path, (uint)path.Capacity, 0);
        return length > 0 && length < path.Capacity ? path.ToString() : null;
    }

    private static bool IsRedirectedLocalLog(string? finalFile)
    {
        // Some desktop execution environments redirect the FILE into a package cache
        // but return the original DIRECTORY, even without a current package identity.
        // Allow only this exact log suffix, under this user's LocalAppData/Packages,
        // with precisely one package-directory component. No caller-supplied paths.
        if (finalFile is null) return false;
        var packageRoot = @"\\?\" + Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages") + "\\";
        const string suffix = @"\LocalCache\Local\BleHid\logs\virtual-display-host.log";
        if (!finalFile.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) ||
            !finalFile.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var package = finalFile[packageRoot.Length..^suffix.Length];
        if (string.IsNullOrWhiteSpace(package) || package is "." or ".." ||
            package.IndexOfAny(['\\', '/', ':']) >= 0) return false;
        for (var current = new DirectoryInfo(Path.GetDirectoryName(finalFile[4..])!);
             current != null; current = current.Parent)
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        return true;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct CreateInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string InstanceId;
            [MarshalAs(UnmanagedType.LPWStr)] public string HardwareIds;
            [MarshalAs(UnmanagedType.LPWStr)] public string? CompatibleIds;
            public IntPtr ContainerId;
            public uint CapabilityFlags;
            [MarshalAs(UnmanagedType.LPWStr)] public string Description;
            [MarshalAs(UnmanagedType.LPWStr)] public string? Location;
            public IntPtr SecurityDescriptor;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        internal delegate void CreateCallback(IntPtr device, int result, IntPtr context,
            [MarshalAs(UnmanagedType.LPWStr)] string? deviceInstanceId);

        [DllImport("Cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int SwDeviceCreate(string enumerator, string parent, ref CreateInfo info,
            uint propertyCount, IntPtr properties, IntPtr callback, IntPtr context, out IntPtr device);

        [DllImport("Cfgmgr32.dll", ExactSpelling = true)]
        internal static extern int SwDeviceSetLifetime(IntPtr device, int lifetime);

        [DllImport("Cfgmgr32.dll", ExactSpelling = true)]
        internal static extern void SwDeviceClose(IntPtr device);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
            public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(IntPtr file, out FileInformation info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint sharing,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint length, uint flags);
    }
}
