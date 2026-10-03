using System.Windows;
using System.Windows.Controls;
using BleHid.App.Services;

namespace BleHid.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = AppSettings.Instance;
        CompanionAudioPanel.DataContext = PeripheralService.Instance;
        Loaded += async (_, _) =>
        {
            if (!App.Current.IsExiting) await RefreshAudioDevicesAsync();
        };

        var available = StartupService.Command is not null;
        DevBar.IsOpen = !available;
        AutoStartToggle.IsEnabled = available;
        AutoStartToggle.IsChecked = available && StartupService.IsEnabled();
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e) =>
        StartupService.Set(AutoStartToggle.IsChecked == true);

    private void OnExit(object sender, RoutedEventArgs e) => App.Current.ExitApplication();

    private async void OnRefreshAudioDevices(object sender, RoutedEventArgs e) =>
        await RefreshAudioDevicesAsync();

    private async void OnReconnectAudio(object sender, RoutedEventArgs e)
    {
        if (!PeripheralService.Instance.ReconnectCompanionAudio())
        {
            ReconnectAudioHint.Text = "Inicie o periférico, habilite a conexão auxiliar e selecione o iPhone. Se já fez isso, aguarde alguns segundos ou confira o status acima.";
            return;
        }
        ReconnectAudioHint.Text = "Reconexão solicitada. Confira o status acima e selecione o PC como saída de áudio no iPhone, se necessário. Sua automação pode desligar e ligar o AssistiveTouch.";
        ReconnectAudioButton.IsEnabled = false;
        try { await Task.Delay(5_000); }
        finally { ReconnectAudioButton.IsEnabled = true; }
    }

    private async Task RefreshAudioDevicesAsync()
    {
        RefreshAudioDevicesButton.IsEnabled = false;
        try { await PeripheralService.Instance.RefreshCompanionAudioDevicesAsync(); }
        finally { RefreshAudioDevicesButton.IsEnabled = true; }
    }
}
