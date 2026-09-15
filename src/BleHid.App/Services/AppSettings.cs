using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BleHid.App.Services;

/// <summary>
/// User preferences, persisted next to the background log so the CLI and the UI agree on a home.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly string Path = System.IO.Path.Combine(
        BleHid.Core.AppPaths.Root, "app-settings.json");
    private static bool _loading;

    public static AppSettings Instance { get; } = Load();

    // Explicit opt-in: connecting this profile may route the phone's audio to the PC.
    private bool _companionAudioEnabled;
    public bool CompanionAudioEnabled
    {
        get => _companionAudioEnabled;
        set { if (Set(ref _companionAudioEnabled, value)) Save(); }
    }

    private string _companionAudioDeviceId = "";
    public string CompanionAudioDeviceId
    {
        get => _companionAudioDeviceId;
        set { if (Set(ref _companionAudioDeviceId, value ?? "")) Save(); }
    }

    private bool _invertScroll = true;
    public bool InvertScroll
    {
        get => _invertScroll;
        set { if (Set(ref _invertScroll, value)) Save(); }
    }

    private bool _edgeSwitchEnabled;
    private bool _middleClickReturn = true;
    public bool MiddleClickReturn
    {
        get => _middleClickReturn;
        set { if (Set(ref _middleClickReturn, value)) Save(); }
    }
    private bool _estimatedEdgeReturn;
    public bool EstimatedEdgeReturn
    {
        get => _estimatedEdgeReturn;
        set { if (Set(ref _estimatedEdgeReturn, value)) Save(); }
    }
    private int _estimatedTravel = 600;
    public int EstimatedTravel
    {
        get => _estimatedTravel;
        set { if (Set(ref _estimatedTravel, Math.Clamp(value,100,3000))) Save(); }
    }
    public bool EdgeSwitchEnabled
    {
        get => _edgeSwitchEnabled;
        set { if (Set(ref _edgeSwitchEnabled, value)) Save(); }
    }

    private BleHid.Core.ScreenEdge _edgePosition = BleHid.Core.ScreenEdge.Right;
    public BleHid.Core.ScreenEdge EdgePosition
    {
        get => _edgePosition;
        set
        {
            if (Enum.IsDefined(value) && Set(ref _edgePosition, value)) Save();
        }
    }

    private string _edgeMonitorId = "";
    public string EdgeMonitorId
    {
        get => _edgeMonitorId;
        set { if (Set(ref _edgeMonitorId, value ?? "")) Save(); }
    }

    private string _edgeHostId = "";
    public string EdgeHostId
    {
        get => _edgeHostId;
        set { if (Set(ref _edgeHostId, value ?? "")) Save(); }
    }

    private bool _closeToTray = true;
    public bool CloseToTray
    {
        get => _closeToTray;
        set { if (Set(ref _closeToTray, value)) Save(); }
    }

    private bool _startPeripheralOnLaunch;
    public bool StartPeripheralOnLaunch
    {
        get => _startPeripheralOnLaunch;
        set { if (Set(ref _startPeripheralOnLaunch, value)) Save(); }
    }

    private static AppSettings Load()
    {
        _loading = true;
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file is not worth blocking startup over.
        }
        finally { _loading = false; }

        return new AppSettings();
    }

    private void Save()
    {
        if (_loading) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
