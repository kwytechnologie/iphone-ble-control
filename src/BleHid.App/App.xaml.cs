using System.IO;
using System.Windows;
using System.Windows.Threading;
using BleHid.App.Services;
using BleHid.Core;
using Wpf.Ui.Appearance;

namespace BleHid.App;

public partial class App : Application
{
    private static readonly string LogPath = AppPaths.InLogs("blehid-app.log");

    private Mutex? _instance;
    private EventWaitHandle? _stopRequest;
    private RegisteredWaitHandle? _stopRegistration;
    private bool _exiting;

    public new static App Current => (App)Application.Current;

    public TrayIconService? Tray { get; private set; }

    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            RunSmokeTest(e);
            return;
        }

        if (!TryTakeOwnership())
        {
            Shutdown();
            return;
        }

        // Lets `blehid --stop` shut this down the same way it shuts down background mode.
        _stopRequest = new EventWaitHandle(false, EventResetMode.ManualReset, SingleInstance.StopEventName);
        // A named event that still exists ignores the initial state above, so the stop request that
        // retired the previous owner would fire this one immediately.
        _stopRequest.Reset();
        _stopRegistration = ThreadPool.RegisterWaitForSingleObject(
            _stopRequest, (_, _) => Dispatcher.Invoke(ExitApplication), null, Timeout.Infinite,
            executeOnlyOnce: true);

        ApplicationThemeManager.ApplySystemTheme();
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception);
        base.OnStartup(e);

        Tray = new TrayIconService(ShowMainWindow, ExitApplication);

        var startHidden = e.Args.Contains(StartupService.TrayArgument, StringComparer.OrdinalIgnoreCase);
        MainWindow = new MainWindow();
        if (!startHidden) MainWindow.Show();

        // Advertising is opt-in; even a tray launch never captures input automatically.
        if (AppSettings.Instance.StartPeripheralOnLaunch) _ = PeripheralService.Instance.StartAsync();
    }

    // Checks WPF resources and all pages without taking Bluetooth ownership, enabling startup,
    // creating a tray icon, or installing an input hook. The exit code and log are automation-friendly.
    private void RunSmokeTest(StartupEventArgs e)
    {
        var smokeLog = AppPaths.InLogs("wpf-smoke-test.log");
        _exiting = true;
        try
        {
            ApplicationThemeManager.ApplySystemTheme();
            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
            MainWindow.UpdateLayout();
            foreach (var page in new FrameworkElement[]
                     {
                         new Views.StatusPage(), new Views.HostsPage(),
                         new Views.CapturePage(), new Views.SettingsPage()
                     })
            {
                page.Measure(new Size(720, 520));
                page.Arrange(new Rect(0, 0, 720, 520));
                page.UpdateLayout();
            }

            // Render our own WPF visual tree, not a desktop screenshot. Show the new panel
            // without changing saved preferences, pairing, input hooks or the running app.
            var preview = new Views.CapturePage();
            ((FrameworkElement)preview.FindName("LayoutPanel")).Visibility = Visibility.Visible;
            preview.Background = new System.Windows.Media.SolidColorBrush(
                ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
                    ? System.Windows.Media.Color.FromRgb(32, 32, 32)
                    : System.Windows.Media.Color.FromRgb(249, 249, 249));
            preview.Measure(new Size(720, 1400));
            preview.Arrange(new Rect(0, 0, 720, 1400));
            preview.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(720, 1400, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(preview);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(AppPaths.InLogs("screen-layout-preview.png"))) encoder.Save(stream);

            File.WriteAllText(smokeLog,
                $"{DateTime.Now:s} OK: MainWindow e quatro páginas WPF carregadas. Bluetooth e captura não iniciados.\n");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(smokeLog, $"{DateTime.Now:s} FAIL: {ex}\n"); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            Shutdown(1);
        }
    }

    /// <summary>
    /// The holder may be another copy of this app or the CLI's background service; either way it
    /// owns the radio and has to release it before this process can advertise.
    /// </summary>
    private bool TryTakeOwnership()
    {
        _instance = new Mutex(true, SingleInstance.MutexName, out var owned);
        if (owned) return true;

        var answer = MessageBox.Show(
            "O controle BLE HID já está aberto na bandeja ou como serviço de linha de comando."
            + "\n\nEncerrar a outra instância e continuar?",
            "iPhone BLE Control", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;

        try
        {
            using var stop = EventWaitHandle.OpenExisting(SingleInstance.StopEventName);
            stop.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            MessageBox.Show(
                "A instância aberta não aceita pedidos de encerramento. Feche-a manualmente.",
                "iPhone BLE Control", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        for (var attempt = 0; attempt < 50 && !owned; attempt++)
        {
            Thread.Sleep(100);
            _instance.Dispose();
            _instance = new Mutex(true, SingleInstance.MutexName, out owned);
        }

        if (!owned)
            MessageBox.Show("A instância aberta não encerrou a tempo.",
                "iPhone BLE Control", MessageBoxButton.OK, MessageBoxImage.Warning);

        return owned;
    }

    public void ShowMainWindow()
    {
        MainWindow ??= new MainWindow();
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        await PeripheralService.Instance.StopAsync();
        Tray?.Dispose();
        // Shutdown closes windows, so it must not run inside a close that is still unwinding.
        await Dispatcher.InvokeAsync(Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopRegistration?.Unregister(null);
        _stopRequest?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    // A XAML error on one page used to terminate the process with no output at all.
    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        MessageBox.Show($"{e.Exception.Message}\n\nDetalhes em {LogPath}",
            "iPhone BLE Control", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Report(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            // The tray build can run for weeks, so the log cannot grow without a bound.
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024) File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:s}  {ex}\n\n");
        }
        catch (IOException) { }
    }
}
