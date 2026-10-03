using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using BleHid.Core;

namespace BleHid.App.Services;

/// <summary>
/// Owns the peripheral for the lifetime of the app so every page observes one shared state.
/// </summary>
public sealed class PeripheralService : INotifyPropertyChanged
{
    private const int MaxLogLines = 500;

    public static PeripheralService Instance { get; } = new();

    private BleHidPeripheral? _peripheral;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private readonly DisplayLayoutRefreshGate _layoutRefresh = new();
    private CancellationTokenSource? _layoutRefreshDelay;
    private string _captureLayoutSignature = "";
    private readonly DispatcherTimer _poll;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    private PeripheralService()
    {
        CompanionAudio.Log += Append;
        VirtualDisplay.Changed += () =>
        {
            if (!VirtualDisplay.IsVisible) CancelDisplayLayoutRefresh();
            RefreshMonitors();
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.CompanionAudioEnabled) or nameof(AppSettings.CompanionAudioDeviceId))
                ApplyCompanionAudio();
            if (e.PropertyName is nameof(AppSettings.UseWindowsDisplayLayout) or nameof(AppSettings.EdgeSwitchEnabled))
                CancelDisplayLayoutRefresh();
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (!App.Current.IsExiting) ReconnectCompanionAudio();
            });
        };
        RefreshMonitors();
        _poll = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _poll.Tick += async (_, _) =>
        {
            RefreshCounters();
            // Fallback for missed/coalesced display notifications. Read native current
            // positions, not Screen's cached bounds; never poll in the mouse hook.
            if (IsCapturing && Settings.UseWindowsDisplayLayout && !IsUpdatingDisplayLayout &&
                _captureLayoutSignature.Length != 0 &&
                WindowsDisplayLayout.CurrentSignature() != _captureLayoutSignature)
                await RefreshDisplayLayoutAsync();
            await TryStartScreenModeAsync();
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
            _dispatcher.BeginInvoke(async () => await RefreshDisplayLayoutAsync());
    }

    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<HostTarget> Hosts { get; } = [];
    public ObservableCollection<TargetOption> Targets { get; } = [];
    public AppSettings Settings => AppSettings.Instance;
    public ClassicAudioService CompanionAudio { get; } = new();
    public VirtualDisplayService VirtualDisplay { get; } = new();
    private bool _stoppingPeripheral;
    private Task? _stopTask;

    public bool IsUpdatingDisplayLayout => _layoutRefresh.IsPending;

    private void NotifyDisplayLayoutRefresh()
    {
        OnPropertyChanged(nameof(IsUpdatingDisplayLayout));
        OnPropertyChanged(nameof(ScreenModeButtonText));
        OnPropertyChanged(nameof(ScreenModeStatus));
        OnPropertyChanged(nameof(CanToggleCapture));
    }

    private void CancelDisplayLayoutRefresh()
    {
        _layoutRefresh.Cancel();
        _layoutRefreshDelay?.Cancel();
        NotifyDisplayLayoutRefresh();
    }

    private async Task RefreshDisplayLayoutAsync()
    {
        if (App.Current.IsExiting || _stoppingPeripheral) return;
        _layoutRefreshDelay?.Cancel();
        var delay = new CancellationTokenSource();
        _layoutRefreshDelay = delay;
        var wasCapturing = IsCapturing;
        var windowsMode = Settings.UseWindowsDisplayLayout && Settings.EdgeSwitchEnabled;
        // A manual Stop leaves IsCapturing true until cleanup completes. It must
        // not become a new resume request if a display event arrives meanwhile.
        var captureRequested = wasCapturing && _captureCancellation?.IsCancellationRequested == false;
        var revision = _layoutRefresh.Request(captureRequested, windowsMode);
        NotifyDisplayLayoutRefresh();
        try
        {
            // Release keys/buttons and stop the old hooks before reading/rearming.
            // This internal pause preserves intent; public/manual Stop cancels it.
            if (wasCapturing || _captureTask is { IsCompleted: false })
                await StopCaptureCoreAsync();
            await Task.Delay(500, delay.Token);
            if (!_layoutRefresh.IsCurrent(revision)) return;
            RefreshMonitors();
            RefreshCounters();
            var wantedResume = IsUpdatingDisplayLayout;
            var allowed = !App.Current.IsExiting && !_stoppingPeripheral && IsRunning && !IsBusy &&
                Settings.UseWindowsDisplayLayout && Settings.EdgeSwitchEnabled && VirtualDisplay.IsVisible &&
                KeyboardSubscribers > 0 && MouseSubscribers > 0;
            var resume = _layoutRefresh.TryTakeResume(revision, allowed);
            NotifyDisplayLayoutRefresh();
            if (resume)
            {
                await StartCaptureAsync(); // Relocalizes the cursor; does not immediately capture the iPhone.
                if (IsCapturing) Append("[telas] Posição do Windows atualizada automaticamente; passagem pela borda reativada.");
            }
            else if (wantedResume)
            {
                CaptureError = "A posição mudou, mas a tela virtual ou o iPhone não está disponível. O controle permanece no PC.";
            }
            else if (wasCapturing)
            {
                Append("[telas] Monitores alterados; controle devolvido ao PC. No modo tradicional, confira a posição e ative novamente.");
            }
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_layoutRefresh.IsCurrent(revision))
            {
                _layoutRefresh.Cancel();
                CaptureError = $"Não foi possível atualizar a posição. O controle permanece no PC. {ex.Message}";
                NotifyDisplayLayoutRefresh();
            }
        }
        finally
        {
            if (ReferenceEquals(_layoutRefreshDelay, delay)) _layoutRefreshDelay = null;
            delay.Dispose();
        }
    }

    // A refresh temporarily clears the ComboBox selection; never erase a saved endpoint for that.
    public string CompanionAudioDeviceId
    {
        get => Settings.CompanionAudioDeviceId;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            Settings.CompanionAudioDeviceId = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshCompanionAudioDevicesAsync()
    {
        await CompanionAudio.RefreshDevicesAsync();
        OnPropertyChanged(nameof(CompanionAudioDeviceId));
    }

    private void ApplyCompanionAudio()
    {
        // Constructors and smoke tests must never enable any Bluetooth profile.
        if (IsRunning && !_stoppingPeripheral && !App.Current.IsExiting && Settings.CompanionAudioEnabled &&
            !string.IsNullOrWhiteSpace(Settings.CompanionAudioDeviceId))
            CompanionAudio.Start(Settings.CompanionAudioDeviceId);
        else
            _ = CompanionAudio.StopAsync();
    }

    public bool ReconnectCompanionAudio() =>
        IsRunning && !_stoppingPeripheral && !App.Current.IsExiting &&
        Settings.CompanionAudioEnabled && CompanionAudio.Reconnect();
    public sealed record MonitorOption(string Id, string Title);
    public ObservableCollection<MonitorOption> Monitors { get; } = [];
    public string EdgeMonitorId
    {
        get => string.IsNullOrEmpty(Settings.EdgeMonitorId) ? Monitors.FirstOrDefault()?.Id ?? "" : Settings.EdgeMonitorId;
        set { if (!string.IsNullOrEmpty(value)) Settings.EdgeMonitorId = value; OnPropertyChanged(); }
    }
    public string EdgeHostId
    {
        get => Settings.EdgeHostId;
        set
        {
            // ComboBox clears its selection when subscriptions refresh; preserve the chosen ID.
            if (!string.IsNullOrEmpty(value)) Settings.EdgeHostId = value;
            OnPropertyChanged();
        }
    }

    public void RefreshMonitors()
    {
        Monitors.Clear();
        var phone = WindowsDisplayLayout.FindPhone();
        var screens = System.Windows.Forms.Screen.AllScreens
            .Where(s => s.DeviceName != phone?.DeviceName).OrderByDescending(s => s.Primary).ToArray();
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            Monitors.Add(new MonitorOption(s.DeviceName,
                $"Monitor {i + 1}{(s.Primary ? " (principal)" : "")} · {s.Bounds.Width} × {s.Bounds.Height}"));
        }
        OnPropertyChanged(nameof(EdgeMonitorId));
    }

    public bool IsPhoneLeft { get => Settings.EdgePosition == ScreenEdge.Left; set { if (value) SetEdge(ScreenEdge.Left); } }
    public bool IsPhoneRight { get => Settings.EdgePosition == ScreenEdge.Right; set { if (value) SetEdge(ScreenEdge.Right); } }
    public bool IsPhoneTop { get => Settings.EdgePosition == ScreenEdge.Top; set { if (value) SetEdge(ScreenEdge.Top); } }
    public bool IsPhoneBottom { get => Settings.EdgePosition == ScreenEdge.Bottom; set { if (value) SetEdge(ScreenEdge.Bottom); } }
    private void SetEdge(ScreenEdge edge)
    {
        Settings.EdgePosition = edge;
        foreach (var property in new[] { nameof(IsPhoneLeft), nameof(IsPhoneRight), nameof(IsPhoneTop), nameof(IsPhoneBottom) })
            OnPropertyChanged(property);
    }

    private string _captureError = "";
    public string CaptureError { get => _captureError; private set => Set(ref _captureError, value); }
    public bool HasCaptureError => !string.IsNullOrEmpty(CaptureError);
    private bool _waitingForScreenHost;
    public bool WaitingForScreenHost { get => _waitingForScreenHost; private set => Set(ref _waitingForScreenHost, value); }
    private bool _edgeMonitorReady;
    public bool EdgeMonitorReady { get => _edgeMonitorReady; private set => Set(ref _edgeMonitorReady, value); }
    public string ScreenModeButtonText => IsUpdatingDisplayLayout ? "Cancelar atualização" : IsCapturing ? "Desativar modo telas" : WaitingForScreenHost ? "Cancelar espera" : "Ativar modo telas";
    public string ScreenModeStatus => IsUpdatingDisplayLayout
        ? "Atualizando a posição do Windows… O controle fica no PC e a passagem será retomada automaticamente."
        : IsCapturing
        ? !EdgeMonitorReady ? "Iniciando a detecção da borda…"
            : _peripheral?.IsLocalTarget != false
                ? Settings.UseWindowsDisplayLayout
                    ? "Pronto: passe o cursor para a tela virtual na posição definida no Windows."
                    : "Pronto: leve o cursor até a borda escolhida e aguarde 0,35 s."
                : "Controlando o iPhone. Ctrl+Alt+Q volta ao PC."
        : WaitingForScreenHost ? "Aguardando o iPhone conectar teclado e mouse. O controle continua no PC."
        : IsBusy ? "Iniciando Bluetooth…"
        : "Modo telas desligado. Para começar, clique em Ativar modo telas.";
    public bool CanToggleCapture => IsUpdatingDisplayLayout || IsCapturing || CanCapture;
    public string CaptureHelp => Settings.EdgeSwitchEnabled
        ? Settings.UseWindowsDisplayLayout
            ? "Passe para a tela virtual sem segurar teclas ou botões. Ctrl+Alt+Q volta ao PC; desative o modo para encerrar. Não transfere janelas ou arquivos."
            : "Modo telas: encoste na borda escolhida e aguarde 0,35 s. Ctrl+Alt+Q volta ao PC; a passagem pela borda continua ativada. Desative o modo para encerrar."
        : "Ao controlar o iPhone, esta janela deixa de receber teclado e mouse. Ctrl+Alt+Q encerra o controle e devolve a entrada ao PC. Ctrl+D+C alterna o destino.";

    private bool _applyingSelection;
    private bool _refreshingHosts;
    private string _hostSignature = "";

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private bool _isCapturing;
    public bool IsCapturing { get => _isCapturing; private set => Set(ref _isCapturing, value); }

    public bool RequireEncryption => true;

    private int _pointerIntervalMs = 10;
    public int PointerIntervalMs { get => _pointerIntervalMs; set => Set(ref _pointerIntervalMs, value); }

    public string ComputerName => Environment.MachineName;

    private string _startupError = "";
    public string StartupError { get => _startupError; private set => Set(ref _startupError, value); }
    public bool HasStartupError => !string.IsNullOrEmpty(StartupError);

    private string _advertisementStatus = "Parado";
    public string AdvertisementStatus { get => _advertisementStatus; private set => Set(ref _advertisementStatus, value); }

    private int _keyboardSubscribers;
    public int KeyboardSubscribers { get => _keyboardSubscribers; private set => Set(ref _keyboardSubscribers, value); }

    private int _mouseSubscribers;
    public int MouseSubscribers { get => _mouseSubscribers; private set => Set(ref _mouseSubscribers, value); }

    private string _target = "Nenhum dispositivo selecionado";
    public string Target { get => _target; private set => Set(ref _target, value); }

    /// <summary>Capture needs a subscribed host, so it stays disabled until one appears.</summary>
    public bool CanCapture => IsRunning && KeyboardSubscribers > 0;

    public async Task StartAsync()
    {
        if (IsRunning || IsBusy || _stoppingPeripheral || App.Current.IsExiting) return;
        IsBusy = true;
        StartupError = "";
        BleHidPeripheral? peripheral = null;
        try
        {
            peripheral = new BleHidPeripheral(RequireEncryption);
            peripheral.Log += Append;
            await peripheral.StartAsync();
            _peripheral = peripheral;
            IsRunning = true;
            // Without this the peripheral starts in broadcast mode and every report is duplicated
            // to all subscribed hosts, which is both surprising and slow.
            peripheral.SelectLocal();
            _poll.Start();
            RefreshCounters();
            await RefreshHostsAsync();
            ApplyCompanionAudio();
        }
        catch (Exception ex)
        {
            _poll.Stop();
            StartupError = $"Não foi possível iniciar o Bluetooth LE. {ex.Message}";
            Append(StartupError);
            if (peripheral is not null)
            {
                try
                {
                    await peripheral.DisposeAsync();
                }
                catch (Exception cleanupError)
                {
                    Append($"Falha ao liberar o periférico: {cleanupError.Message}");
                }
                finally
                {
                    peripheral.Log -= Append;
                }
            }
            _peripheral = null;
            IsRunning = false;
            Hosts.Clear();
            Targets.Clear();
            _hostSignature = "";
            AdvertisementStatus = "Falha ao iniciar";
            KeyboardSubscribers = MouseSubscribers = 0;
            Target = "Nenhum dispositivo selecionado";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // A window exit during a pending Stop must await the same cleanup, not skip it.
    public Task StopAsync() => _stopTask is { IsCompleted: false } ? _stopTask : _stopTask = StopCoreAsync();

    private async Task StopCoreAsync()
    {
        CancelDisplayLayoutRefresh();
        WaitingForScreenHost = false;
        _stoppingPeripheral = true;
        // Return local input immediately even if the audio provider takes time to close.
        _captureCancellation?.Cancel();
        // Always release the companion, including failed or still-starting BLE sessions.
        await CompanionAudio.StopAsync();
        if (!IsRunning || IsBusy)
        {
            _stoppingPeripheral = false;
            return;
        }
        IsBusy = true;
        try
        {
            await StopCaptureAsync();
            _poll.Stop();
            if (_peripheral is not null)
            {
                await _peripheral.DisposeAsync();
                _peripheral.Log -= Append;
            }
            _peripheral = null;
            IsRunning = false;
            Hosts.Clear();
            Targets.Clear();
            _hostSignature = "";
            AdvertisementStatus = "Parado";
            KeyboardSubscribers = MouseSubscribers = 0;
            Target = "Nenhum dispositivo selecionado";
            Append("Periférico parado");
        }
        finally
        {
            IsBusy = false;
            _stoppingPeripheral = false;
        }
    }

    public async Task RefreshHostsAsync()
    {
        if (_peripheral is null || _refreshingHosts) return;
        _refreshingHosts = true;
        try
        {
            await _peripheral.RefreshHostNamesAsync();
            if (_peripheral is null) return;
            Hosts.Clear();
            foreach (var host in _peripheral.Hosts()) Hosts.Add(host);
            OnPropertyChanged(nameof(EdgeHostId));
            _hostSignature = Signature(_peripheral);
            RebuildTargets();
            Target = DisplayTarget(_peripheral);
        }
        finally
        {
            _refreshingHosts = false;
        }
    }

    public void Select(TargetOption option)
    {
        if (_peripheral is null || _applyingSelection) return;

        switch (option.Kind)
        {
            case TargetKind.Local:
                _peripheral.SelectLocal();
                break;
            default:
                // Indexes shift as hosts subscribe and drop, so the device id is the only stable handle.
                var index = IndexOf(option.DeviceId);
                if (index < 0)
                {
                    Append($"[dispositivo] {option.Title} não está mais conectado");
                    _ = RefreshHostsAsync();
                    return;
                }

                _peripheral.SelectHost(index);
                break;
        }

        Target = DisplayTarget(_peripheral);
        Append($"[dispositivo] -> {Target}");
        SyncSelection();
    }

    private int IndexOf(string? deviceId)
    {
        if (_peripheral is null || deviceId is null) return -1;
        var hosts = _peripheral.Hosts();
        for (var i = 0; i < hosts.Count; i++)
            if (string.Equals(hosts[i].DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string Signature(BleHidPeripheral peripheral) =>
        string.Join('|', peripheral.Hosts().Select(h => h.DeviceId));

    private static string DisplayTarget(BleHidPeripheral peripheral) =>
        peripheral.IsLocalTarget ? "Este PC (controle local)" : peripheral.SelectedHostDisplay;

    private void RebuildTargets()
    {
        if (_peripheral is null) return;

        Targets.Clear();
        var hosts = _peripheral.Hosts();
        for (var i = 0; i < hosts.Count; i++)
        {
            Targets.Add(new TargetOption
            {
                Title = hosts[i].Name is { Length: > 0 } name ? name : hosts[i].Address,
                Detail = hosts[i].Address,
                Kind = TargetKind.Host,
                DeviceId = hosts[i].DeviceId
            });
        }

        Targets.Add(new TargetOption
        {
            Title = "Este PC",
            Detail = "O controle permanece ativado, mas o teclado e o mouse funcionam neste PC.",
            Kind = TargetKind.Local
        });

        SyncSelection();
    }

    private void SyncSelection()
    {
        if (_peripheral is null) return;

        // Writing IsSelected re-enters through the RadioButton's Checked event, so suppress it.
        _applyingSelection = true;
        try
        {
            foreach (var option in Targets)
            {
                option.IsSelected = option.Kind switch
                {
                    TargetKind.Local => _peripheral.IsLocalTarget,
                    _ => !_peripheral.IsLocalTarget && _peripheral.SelectedHostId == option.DeviceId
                };
            }
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    public async Task ToggleScreenModeAsync()
    {
        if (IsCapturing || WaitingForScreenHost || IsUpdatingDisplayLayout)
        {
            WaitingForScreenHost = false;
            await StopCaptureAsync();
            return;
        }
        if (IsBusy) return;
        Settings.EdgeSwitchEnabled = true;
        CaptureError = "";
        WaitingForScreenHost = true;
        Append("[telas] Ativação solicitada. Iniciando Bluetooth e aguardando o iPhone.");
        if (!IsRunning) await StartAsync();
        if (!IsRunning)
        {
            WaitingForScreenHost = false;
            CaptureError = StartupError;
            return;
        }
        await TryStartScreenModeAsync();
    }

    private async Task TryStartScreenModeAsync()
    {
        if (!WaitingForScreenHost || IsBusy || IsCapturing || !IsRunning) return;
        if (!Settings.EdgeSwitchEnabled) { WaitingForScreenHost = false; return; }
        if (KeyboardSubscribers == 0 || MouseSubscribers == 0) return;
        WaitingForScreenHost = false;
        await StartCaptureAsync();
    }

    /// <param name="resident">
    /// Background behaviour, as in the CLI: arms before any host has subscribed and treats
    /// Ctrl+Alt+Q as "return input to this PC" rather than "end the session", because a hidden
    /// window leaves no way to switch it back on.
    /// </param>
    public async Task StartCaptureAsync(bool resident = false)
    {
        CancelDisplayLayoutRefresh();
        if (_peripheral is null || IsBusy || IsCapturing || _captureTask is { IsCompleted: false }) return;
        if (!resident && !CanCapture) return;

        CaptureError = "";
        EdgeSwitchOptions? edgeSwitch = null;
        if (Settings.EdgeSwitchEnabled)
        {
            var hosts = _peripheral.Hosts();
            var hostId = Settings.EdgeHostId;
            if (string.IsNullOrEmpty(hostId))
                hostId = _peripheral.SelectedHostId ?? (hosts.Count == 1 ? hosts[0].DeviceId : "");
            if (!hosts.Any(h => string.Equals(h.DeviceId, hostId, StringComparison.OrdinalIgnoreCase)))
            {
                CaptureError = "Selecione o iPhone conectado em Destino da borda. Nenhuma entrada foi capturada.";
                return;
            }
            var screens = System.Windows.Forms.Screen.AllScreens;
            var monitor = screens.FirstOrDefault(s => s.DeviceName == EdgeMonitorId);
            if (monitor is null)
            {
                CaptureError = "O monitor escolhido não está disponível. Atualize a lista e escolha o monitor novamente.";
                return;
            }
            static ScreenBounds Bounds(System.Windows.Forms.Screen s) => WindowsDisplayLayout.Bounds(s);
            ScreenBounds? virtualBounds = null;
            try
            {
                var layoutSignature = Settings.UseWindowsDisplayLayout ? WindowsDisplayLayout.CurrentSignature() : "";
                if (Settings.UseWindowsDisplayLayout)
                {
                    var phone = WindowsDisplayLayout.FindPhone();
                    if (!VirtualDisplay.IsVisible || phone is null || phone.Primary)
                    {
                        CaptureError = "Mostre a tela virtual primeiro, mantenha o monitor físico como principal e use Estender nas configurações do Windows.";
                        return;
                    }
                    virtualBounds = Bounds(phone);
                }
                edgeSwitch = new EdgeSwitchOptions(hostId, Settings.EdgePosition, Bounds(monitor), screens.Select(Bounds).ToArray(), virtualBounds);
                if (virtualBounds.HasValue && layoutSignature != WindowsDisplayLayout.CurrentSignature())
                    throw new InvalidOperationException("A disposição mudou durante a leitura; aguarde a estabilização das telas.");
                _captureLayoutSignature = layoutSignature;
            }
            catch (InvalidOperationException ex)
            {
                CaptureError = $"O Windows ainda está alterando as telas. O controle permanece no PC. {ex.Message}";
                return;
            }
            Settings.EdgeHostId = hostId;
            OnPropertyChanged(nameof(EdgeHostId));
            // Arming the edge must never take input away from the PC immediately.
            _peripheral.SelectLocal();
            Target = DisplayTarget(_peripheral);
            SyncSelection();
        }
        OnPropertyChanged(nameof(CaptureHelp));

        var cancellation = new CancellationTokenSource();
        _captureCancellation = cancellation;
        EdgeMonitorReady = false;
        IsCapturing = true;

        var peripheral = _peripheral;
        var token = cancellation.Token;
        var interval = PointerIntervalMs;
        var returnOptions = new RemoteReturnOptions(Settings.MiddleClickReturn, Settings.EstimatedEdgeReturn, Settings.EstimatedTravel);
        var invertScroll = Settings.InvertScroll;

        // Ctrl+Alt+Q ends the session, so the toggle has to follow the hotkey rather than the click.
        _captureTask = Task.Run(async () =>
        {
            try
            {
                await CaptureSession.RunAsync(peripheral, Append, verbose: false, interval,
                    stopEndsSession: !resident && edgeSwitch is null, token, edgeSwitch, returnOptions, invertScroll);
            }
            catch (Exception ex)
            {
                Append($"Erro no controle: {ex.Message}");
                await _dispatcher.InvokeAsync(() => CaptureError = $"O controle voltou ao PC. {ex.Message}");
            }
            finally
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_captureCancellation, cancellation))
                        _captureCancellation = null;
                    cancellation.Dispose();
                    IsCapturing = false;
                    EdgeMonitorReady = false;
                    _captureLayoutSignature = "";
                    Target = DisplayTarget(peripheral);
                    SyncSelection();
                });
            }
        });

        await Task.CompletedTask;
    }

    public async Task StopCaptureAsync()
    {
        CancelDisplayLayoutRefresh();
        await StopCaptureCoreAsync();
    }

    private async Task StopCaptureCoreAsync()
    {
        WaitingForScreenHost = false;
        var captureTask = _captureTask;
        if (captureTask is null) return;
        _captureCancellation?.Cancel();
        // Wait for key/button release and hook cleanup before disposing the BLE provider.
        // Await yields to the dispatcher used by the session's completion callback.
        await captureTask;
        if (ReferenceEquals(_captureTask, captureTask)) _captureTask = null;
    }

    private void RefreshCounters()
    {
        if (_peripheral is null) return;
        AdvertisementStatus = _peripheral.AdvertisementStatus.ToString() switch
        {
            "Created" => "Criado",
            "Stopped" => "Parado",
            "Started" => "Ativo",
            "Aborted" => "Interrompido",
            "StartedWithoutAllAdvertisementData" => "Ativo com dados limitados",
            var status => status
        };
        KeyboardSubscribers = _peripheral.SubscribedKeyboardClients;
        MouseSubscribers = _peripheral.SubscribedMouseClients;
        // Ctrl+D+C changes the target without going through the UI, so mirror it back.
        Target = DisplayTarget(_peripheral);

        // Hosts subscribe long after the peripheral starts, so the list cannot be built only once.
        if (Signature(_peripheral) != _hostSignature) _ = RefreshHostsAsync();
        else SyncSelection();

        OnPropertyChanged(nameof(CanCapture));
    }

    private void Append(string message)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Append(message));
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss}  {message.TrimEnd()}";
        if (message.StartsWith("[telas] Monitoramento ativo:", StringComparison.Ordinal)) EdgeMonitorReady = true;
        Log.Add(line);
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
        try
        {
            // Technical events only: normal capture runs with per-report logging disabled.
            var path = AppPaths.InLogs("iphone-activity.log");
            if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 2 * 1024 * 1024)
                System.IO.File.Move(path, AppPaths.InLogs("iphone-activity.previous.log"), overwrite: true);
            System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // An unavailable log directory must not interfere with local input recovery.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
        if (name is nameof(IsRunning) or nameof(KeyboardSubscribers)) OnPropertyChanged(nameof(CanCapture));
        if (name == nameof(StartupError)) OnPropertyChanged(nameof(HasStartupError));
        if (name == nameof(CaptureError)) OnPropertyChanged(nameof(HasCaptureError));
        if (name is nameof(IsCapturing) or nameof(IsRunning) or nameof(KeyboardSubscribers))
            OnPropertyChanged(nameof(CanToggleCapture));
        if (name is nameof(IsCapturing) or nameof(IsRunning) or nameof(IsBusy) or nameof(WaitingForScreenHost) or nameof(EdgeMonitorReady) or nameof(Target))
        {
            OnPropertyChanged(nameof(ScreenModeButtonText));
            OnPropertyChanged(nameof(ScreenModeStatus));
        }
    }
}
