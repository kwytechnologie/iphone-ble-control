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
            MainWindow = new MainWindow
            {
                Width = 720,
                Height = 520,
                ShowActivated = false,
                ShowInTaskbar = false,
                Opacity = 0
            };
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

            var scrollResult = VerifySmokeCaptureScrolling(MainWindow);

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
                $"{DateTime.Now:s} OK: MainWindow e quatro páginas WPF carregadas. {scrollResult} Bluetooth e captura não iniciados.\n");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(smokeLog, $"{DateTime.Now:s} FAIL: {ex}\n"); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            Shutdown(1);
        }
    }

    // These helpers are used only by --smoke-test. Routed events remain in this process's
    // visual tree: they do not move the system pointer, inject input or contact a BLE host.
    private static string VerifySmokeCaptureScrolling(Window window)
    {
        PumpSmokeLayout(window);
        var navigation = (Wpf.Ui.Controls.NavigationView)window.FindName("RootNavigation");
        navigation.Transition = Wpf.Ui.Animations.Transition.None;
        navigation.TransitionDuration = 0;
        if (!navigation.Navigate(typeof(Views.CapturePage)))
            throw new InvalidOperationException("Smoke: não foi possível navegar para Controle.");
        PumpSmokeLayout(window);

        var capture = SmokeVisualDescendants<Views.CapturePage>(navigation).Single();
        var source = (FrameworkElement)capture.FindName("LayoutPanel");
        // Override this visual only; never toggle EdgeSwitchEnabled or change saved settings.
        source.Visibility = Visibility.Visible;
        PumpSmokeLayout(window);

        System.Windows.Controls.ScrollViewer? outer = null;
        for (var ancestor = System.Windows.Media.VisualTreeHelper.GetParent(capture);
             ancestor is not null;
             ancestor = System.Windows.Media.VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is System.Windows.Controls.ScrollViewer scrollViewer)
            {
                outer = scrollViewer;
                break;
            }
        }
        if (outer is null || outer.ScrollableHeight <= 0)
            throw new InvalidOperationException("Smoke: Controle não está dentro do scroller rolável da navegação.");

        outer.ScrollToTop();
        PumpSmokeLayout(window);
        var initial = outer.VerticalOffset;
        RaiseSmokeWheel(source, -120);
        PumpSmokeLayout(window);
        var down = outer.VerticalOffset;
        var expectedNotch = Math.Min(outer.ScrollableHeight,
            LocalPageScroll.Pixels(-120, SystemParameters.WheelScrollLines, outer.ViewportHeight));
        AssertSmokeOffset(down, expectedNotch, "roda inteira/sem duplicação");

        RaiseSmokeWheel(source, 120);
        PumpSmokeLayout(window);
        var up = outer.VerticalOffset;
        AssertSmokeOffset(up, 0, "retorno da roda");

        outer.ScrollToTop();
        PumpSmokeLayout(window);
        RaiseSmokeWheel(source, -30);
        PumpSmokeLayout(window);
        var quarter = outer.VerticalOffset;
        var expectedQuarter = Math.Min(outer.ScrollableHeight,
            LocalPageScroll.Pixels(-30, SystemParameters.WheelScrollLines, outer.ViewportHeight));
        AssertSmokeOffset(quarter, expectedQuarter, "delta de um quarto de roda");

        outer.ScrollToTop();
        PumpSmokeLayout(window);
        for (var index = 0; index < 4; index++) RaiseSmokeWheel(source, -30);
        PumpSmokeLayout(window);
        AssertSmokeOffset(outer.VerticalOffset, expectedNotch, "quatro deltas no mesmo frame");

        VerifySmokeNestedWheelControls(window, capture, outer);

        return $"Rolagem proporcional em Controle (720x520): {initial:0.##} -> {down:0.##} -> {up:0.##}; delta -30 -> {quarter:0.##}; quatro frações = uma roda; controles internos preservados.";
    }

    private static void AssertSmokeOffset(double actual, double expected, string label)
    {
        if (Math.Abs(actual - expected) > 0.1)
            throw new InvalidOperationException($"Smoke: {label}: esperado {expected}, encontrado {actual}.");
    }

    private static void VerifySmokeNestedWheelControls(Window window, Views.CapturePage capture,
        System.Windows.Controls.ScrollViewer outer)
    {
        var panel = (System.Windows.Controls.Panel)capture.Content;
        // Temporary controls live only in the test tree; none is bound to user preferences.
        foreach (var control in new FrameworkElement[]
                 {
                     new System.Windows.Controls.ComboBox(), new System.Windows.Controls.ListBox(),
                     new System.Windows.Controls.TextBox(), new System.Windows.Controls.PasswordBox(),
                     new System.Windows.Controls.Slider()
                 })
        {
            panel.Children.Add(control);
            PumpSmokeLayout(window);
            var args = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount, -30) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            control.RaiseEvent(args);
            if (args.Handled)
                throw new InvalidOperationException($"Smoke: roda de {control.GetType().Name} foi interceptada pela página.");
            panel.Children.Remove(control);
        }

        var nestedContent = new System.Windows.Controls.Border { Height = 1000 };
        var nested = new System.Windows.Controls.ScrollViewer { Height = 100, Content = nestedContent };
        panel.Children.Add(nested);
        PumpSmokeLayout(window);
        outer.ScrollToBottom();
        PumpSmokeLayout(window);
        var before = outer.VerticalOffset;
        RaiseSmokeWheel(nestedContent, -120);
        PumpSmokeLayout(window);
        AssertSmokeOffset(outer.VerticalOffset, before, "scroller interno não deve mover a página");
        if (SystemParameters.WheelScrollLines != 0 && nested.VerticalOffset <= 0)
            throw new InvalidOperationException("Smoke: scroller interno deixou de receber a roda.");
        panel.Children.Remove(nested);
    }

    private static void RaiseSmokeWheel(UIElement source, int delta)
    {
        // WPF input uses a preview/bubble pair sharing the handled flag. Reproduce that
        // routing without SendInput, physical mouse actions or global input hooks.
        var args = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount, delta) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(args);
        if (args.Handled) return;
        args.RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent;
        source.RaiseEvent(args);
    }

    private static void PumpSmokeLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static IEnumerable<T> SmokeVisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in SmokeVisualDescendants<T>(child)) yield return descendant;
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
        try { await PeripheralService.Instance.StopAsync(); }
        catch (Exception ex) { Report(ex); }
        finally
        {
            Tray?.Dispose();
            // Shutdown closes windows, so it must not run inside a close that is still unwinding.
            await Dispatcher.InvokeAsync(Shutdown);
        }
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
