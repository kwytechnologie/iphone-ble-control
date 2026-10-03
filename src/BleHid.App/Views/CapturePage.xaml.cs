using System.Windows.Controls;
using BleHid.App.Services;

namespace BleHid.App.Views;

public partial class CapturePage : Page
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public CapturePage()
    {
        InitializeComponent();
        DataContext = _service;
    }

    private async void OnToggleCapture(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_service.IsCapturing) await _service.StopCaptureAsync();
        else await _service.StartCaptureAsync();

        // IsChecked is bound one-way, so snap the switch back to whatever the session actually did.
        CaptureToggle.IsChecked = _service.IsCapturing;
    }

    private void OnRefreshMonitors(object sender, System.Windows.RoutedEventArgs e) => _service.RefreshMonitors();

    private async void OnShowWindowsDisplay(object sender, System.Windows.RoutedEventArgs e)
    {
        await _service.StopCaptureAsync();
        await _service.VirtualDisplay.ShowAsync();
        _service.RefreshMonitors();
    }

    private async void OnHideWindowsDisplay(object sender, System.Windows.RoutedEventArgs e)
    {
        await _service.StopCaptureAsync();
        await _service.VirtualDisplay.HideAsync();
        _service.RefreshMonitors();
    }

    private void OnOpenWindowsDisplaySettings(object sender, System.Windows.RoutedEventArgs e) =>
        _service.VirtualDisplay.OpenDisplaySettings();

    private void OnRecoverVirtualWindows(object sender, System.Windows.RoutedEventArgs e) =>
        _service.VirtualDisplay.RecoverWindows();

    private async void OnToggleScreenMode(object sender, System.Windows.RoutedEventArgs e) => await _service.ToggleScreenModeAsync();
}
