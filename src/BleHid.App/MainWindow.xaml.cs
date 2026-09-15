using System.ComponentModel;
using BleHid.App.Services;
using BleHid.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BleHid.App;

public partial class MainWindow : FluentWindow
{
    private bool _warnedAboutTray;
    private System.Windows.Controls.ScrollViewer? _pendingPageScroll;
    private double _pendingPagePixels;
    private bool _pageScrollScheduled;

    public MainWindow()
    {
        InitializeComponent();
        RootNavigation.PreviewMouseWheel += OnPageMouseWheel;
        // Applies the Mica backdrop and keeps tracking light/dark changes made while running.
        SystemThemeWatcher.Watch(this);
        Loaded += (_, _) => RootNavigation.Navigate(
            Environment.GetCommandLineArgs().Contains("--screen-layout", StringComparer.OrdinalIgnoreCase)
                ? typeof(CapturePage) : typeof(StatusPage));
    }

    private void OnPageMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
            return;
        var scroll = FindPageScroller(e.OriginalSource as System.Windows.DependencyObject);
        if (scroll is null) return;

        // WPF treats every delta as a full notch. Keep high-resolution wheel/touchpad
        // movement proportional, only on the navigation page (never the BLE reports).
        var pixels = BleHid.Core.LocalPageScroll.Pixels(e.Delta,
            System.Windows.SystemParameters.WheelScrollLines, scroll.ViewportHeight);
        e.Handled = true; // The normal bubbling handler must not scroll a second time.
        if (_pendingPageScroll is not null && !ReferenceEquals(_pendingPageScroll, scroll))
            FlushPageWheel();
        _pendingPageScroll = scroll;
        _pendingPagePixels += pixels;
        if (_pageScrollScheduled) return;
        _pageScrollScheduled = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(FlushPageWheel));
    }

    private void FlushPageWheel()
    {
        var scroll = _pendingPageScroll;
        var pixels = _pendingPagePixels;
        _pendingPageScroll = null;
        _pendingPagePixels = 0;
        _pageScrollScheduled = false;
        if (scroll is null || !scroll.IsLoaded) return;
        scroll.ScrollToVerticalOffset(Math.Clamp(scroll.VerticalOffset + pixels, 0, scroll.ScrollableHeight));
        // Commit once per batch so following events see the applied offset. Without this,
        // several deltas in one UI frame can overwrite each other's queued scroll command.
        scroll.UpdateLayout();
    }

    private System.Windows.Controls.ScrollViewer? FindPageScroller(System.Windows.DependencyObject? source)
    {
        System.Windows.Controls.ScrollViewer? candidate = null;
        var insidePagePresenter = false;
        for (var current = source; current is not null; current = WheelParent(current))
        {
            if (ReferenceEquals(current, RootNavigation)) return insidePagePresenter ? candidate : null;
            // Controls with their own wheel behavior keep it, including dropdowns, editors,
            // sliders and lists. Nested scrollers (such as the activity log) stay independent.
            if (current is System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.Primitives.RangeBase)
                return null;
            if (current is System.Windows.Controls.ScrollViewer scroll)
            {
                if (candidate is not null) return null;
                candidate = scroll;
            }
            if (current is NavigationViewContentPresenter) insidePagePresenter = true;
        }
        return null;
    }

    private static System.Windows.DependencyObject? WheelParent(System.Windows.DependencyObject child)
    {
        if (child is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            return System.Windows.Media.VisualTreeHelper.GetParent(child);
        if (child is System.Windows.FrameworkContentElement content) return content.Parent;
        return System.Windows.LogicalTreeHelper.GetParent(child);
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
