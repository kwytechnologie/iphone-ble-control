using System.Runtime.InteropServices;
using System.Text;
using BleHid.Core;
using Screen = System.Windows.Forms.Screen;

namespace BleHid.App.Services;

/// <summary>Display topology only. Never enables, disables or reconfigures physical adapters.</summary>
internal static class WindowsDisplayLayout
{
    public static Screen? FindPhone()
    {
        foreach (var screen in Screen.AllScreens)
            if (IsPhoneDevice(screen.DeviceName)) return screen;
        return null;
    }

    private static bool IsPhoneDevice(string deviceName)
    {
        var foundOwnedMonitor = false;
        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            // With the adapter's GDI name and EDD_GET_DEVICE_INTERFACE_NAME, DeviceID
            // is the documented GUID_DEVINTERFACE_MONITOR symbolic link, not an
            // assumed PnP instance ID. Resolve it through SetupAPI; never parse it.
            if (!EnumDisplayDevices(deviceName, index, ref device, 1)) return foundOwnedMonitor;
            if ((device.State & 1) == 0) continue; // DISPLAY_DEVICE_ACTIVE
            // A cloned GDI view may contain more than one monitor. Do not reposition
            // it if ANY active monitor is physical, third-party, or unidentifiable.
            if (string.IsNullOrEmpty(device.Id) || !HasOwnedAncestor(device.Id)) return false;
            foundOwnedMonitor = true;
        }
    }

    private static bool HasOwnedAncestor(string monitorInterface)
    {
        var informationSet = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (informationSet == IntPtr.Zero || informationSet == new IntPtr(-1)) return false;
        try
        {
            var deviceInterface = new DeviceInterfaceData { Size = (uint)Marshal.SizeOf<DeviceInterfaceData>() };
            if (!SetupDiOpenDeviceInterface(informationSet, monitorInterface, 0, ref deviceInterface)) return false;
            var deviceInfo = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            // Documented DevInfo-only query: NULL detail/zero length returns
            // ERROR_INSUFFICIENT_BUFFER while still supplying the owning devnode.
            var success = SetupDiGetDeviceInterfaceDetail(informationSet, ref deviceInterface,
                IntPtr.Zero, 0, out _, ref deviceInfo);
            if (!success && Marshal.GetLastWin32Error() != 122) return false;

            var current = deviceInfo.DevInst;
            var visited = new HashSet<uint>();
            for (var depth = 0; depth < 32 && visited.Add(current); depth++)
            {
                var instanceId = new StringBuilder(1024);
                if (CM_Get_Device_ID(current, instanceId, (uint)instanceId.Capacity, 0) != 0) return false;
                if (instanceId.ToString().StartsWith(@"SWD\BleHidVirtualDisplay\", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (CM_Get_Parent(out var parent, current, 0) != 0) return false;
                current = parent;
            }
            return false;
        }
        finally
        {
            // Frees the local information set and its interface elements; does not
            // remove, disable or otherwise alter any Windows device.
            SetupDiDestroyDeviceInfoList(informationSet);
        }
    }

    public static ScreenBounds Bounds(Screen screen)
    {
        // Screen.AllScreens can retain an earlier position during a display change.
        // Current DEVMODE coordinates are desktop pixels, matching the input hook.
        var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() };
        if (!EnumDisplaySettings(screen.DeviceName, -1, ref mode) || mode.Width == 0 || mode.Height == 0 ||
            mode.Width > int.MaxValue || mode.Height > int.MaxValue)
            throw new InvalidOperationException("Não foi possível ler a posição atual de um monitor.");
        return new ScreenBounds(mode.X, mode.Y, (int)mode.Width, (int)mode.Height);
    }

    public static string CurrentSignature()
    {
        var parts = new List<string>();
        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.State & 1) == 0) continue;
            var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() };
            if (EnumDisplaySettings(device.Name, -1, ref mode))
                parts.Add($"{device.Name}:{device.State & 4}:{mode.X},{mode.Y},{mode.Width},{mode.Height}");
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    public static bool PlacePhone(Screen phone, Screen physical, ScreenEdge side)
    {
        // A cached Screen's GDI name can be recycled after hot-plug. Recheck ownership
        // immediately before changing topology, rather than trusting the caller's snapshot.
        if (phone.Primary || phone.DeviceName == physical.DeviceName || !IsPhoneDevice(phone.DeviceName)) return false;
        var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() };
        if (!EnumDisplaySettings(phone.DeviceName, -1, ref mode)) return false;
        mode.Fields = 0x20; // DM_POSITION only: preserve mode, orientation and primary display.
        // DEVMODE requires these fields to be zero when their corresponding flags are absent.
        mode.Orientation = 0;
        mode.FixedOutput = 0;
        mode.DriverExtra = 0; // This marshalled buffer contains no driver-private trailing data.
        mode.X = side switch
        {
            ScreenEdge.Left => physical.Bounds.Left - phone.Bounds.Width,
            ScreenEdge.Right => physical.Bounds.Right,
            _ => physical.Bounds.Left
        };
        mode.Y = side switch
        {
            ScreenEdge.Top => physical.Bounds.Top - phone.Bounds.Height,
            ScreenEdge.Bottom => physical.Bounds.Bottom,
            _ => physical.Bounds.Top
        };
        // On first use only; later arrangements are owned by Windows Settings.
        return ChangeDisplaySettingsEx(phone.DeviceName, ref mode, IntPtr.Zero, 1, IntPtr.Zero) == 0;
    }

    public static int RecoverWindows(Screen phone)
    {
        var physical = Screen.AllScreens.Where(s => s.DeviceName != phone.DeviceName).ToArray();
        var destination = physical.FirstOrDefault(s => s.Primary) ?? physical.FirstOrDefault();
        if (destination is null) return 0;
        var moved = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window) || !GetWindowRect(window, out var rect)) return true;
            var area = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (area.Width <= 0 || area.Height <= 0 || !area.IntersectsWith(phone.Bounds) ||
                physical.Any(s => area.IntersectsWith(s.Bounds))) return true;
            var position = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(window, ref position) || position.Show != 1) return true;
            if (SetWindowPos(window, IntPtr.Zero, destination.WorkingArea.Left + 40,
                    destination.WorkingArea.Top + 40, 0, 0, 0x0015)) moved++;
            return true;
        }, IntPtr.Zero);
        return moved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int X, Y;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent,
            MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement
    {
        public int Length; public uint Flags, Show; public Point Min, Max; public Rect Normal;
    }
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public uint Size; public Guid InterfaceClass; public uint Flags; public UIntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size; public Guid DeviceClass; public uint DevInst; public UIntPtr Reserved;
    }
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr deviceClass, IntPtr parentWindow);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInterface(IntPtr informationSet, string path,
        uint flags, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr informationSet,
        ref DeviceInterfaceData deviceInterface, IntPtr detail, uint detailSize,
        out uint requiredSize, ref DeviceInfoData data);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr informationSet);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID(uint devInst, StringBuilder instanceId, uint capacity, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint number, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string name, int mode, ref DeviceMode settings);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string name, ref DeviceMode settings, IntPtr window, uint flags, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
