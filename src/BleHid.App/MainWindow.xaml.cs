using System.ComponentModel;
using BleHid.App.Services;
using BleHid.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BleHid.App;

public partial class MainWindow : FluentWindow
{
    private bool _warnedAboutTray;

    public MainWindow()
    {
        InitializeComponent();
        // Applies the Mica backdrop and keeps tracking light/dark changes made while running.
        SystemThemeWatcher.Watch(this);
        Loaded += (_, _) => RootNavigation.Navigate(
            Environment.GetCommandLineArgs().Contains("--screen-layout", StringComparer.OrdinalIgnoreCase)
                ? typeof(CapturePage) : typeof(StatusPage));
    }

    // Staying resident keeps the GATT attribute table alive, which is what hosts reconnect to.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.Current.IsExiting)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (!AppSettings.Instance.CloseToTray)
        {
            App.Current.ExitApplication();
            return;
        }

        Hide();
        if (_warnedAboutTray) return;
        _warnedAboutTray = true;
        App.Current.Tray?.ShowMessage("O aplicativo continua aberto. Clique com o botão direito no ícone da bandeja para sair. Ctrl+Alt+Q devolve o controle ao PC.");
    }
}
