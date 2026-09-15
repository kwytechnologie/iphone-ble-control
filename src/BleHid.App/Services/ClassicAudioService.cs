using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BleHid.App.Services;

public sealed record AudioDeviceOption(string Id, string Name);

/// <summary>
/// Owns one optional A2DP connection. This never changes pairing, drivers or radio state.
/// An open audio profile is not proof that an iPhone automation has run, and releasing
/// this reference cannot disconnect profiles owned by Windows or other applications.
/// </summary>
public sealed class ClassicAudioService : INotifyPropertyChanged
{
    private readonly Dispatcher _dispatcher;
    private readonly object _sync = new();
    private CancellationTokenSource? _sessionCancellation;
    private Task _sessionTask = Task.CompletedTask;
    private string? _desiredDeviceId;
    private bool _releaseFailed;
    private long _generation;
    private int _refreshInFlight;
    private string _status = "Conexão auxiliar desligada.";
    private bool _isConnected;
    private bool _isRefreshing;

    // Construct on the application's UI thread. Construction does no device I/O.
    public ClassicAudioService() =>
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public ObservableCollection<AudioDeviceOption> Devices { get; } = [];
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? Log;

    /// <summary>Enumerates audio endpoints only; never enables or opens a connection.</summary>
    public async Task RefreshDevicesAsync()
    {
        if (Interlocked.Exchange(ref _refreshInFlight, 1) != 0) return;
        Post(() => IsRefreshing = true);
        try
        {
            var devices = await Task.Run(() => WithDeadlineAsync(
                token => DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector())
                    .AsTask(token), TimeSpan.FromSeconds(10), CancellationToken.None))
                .ConfigureAwait(false);
            var options = devices.Where(device => device.IsEnabled)
                .Select(device => new AudioDeviceOption(device.Id, device.Name))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            // Do not clear/repopulate until a complete enumeration succeeds.
            await _dispatcher.InvokeAsync(() =>
            {
                Devices.Clear();
                foreach (var option in options) Devices.Add(option);
            }).Task.ConfigureAwait(false);
            WriteLog($"Endpoints de áudio disponíveis: {options.Length}.");
        }
        catch (Exception exception)
        {
            WriteError("Falha ao listar os dispositivos de áudio", exception);
        }
        finally
        {
            Post(() => IsRefreshing = false);
            Interlocked.Exchange(ref _refreshInFlight, 0);
        }
    }

    /// <summary>Requests a single explicit target without blocking the caller.</summary>
    public void Start(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        CancellationTokenSource? previousCancellation;
        long generation;
        lock (_sync)
        {
            if (_desiredDeviceId == deviceId && !_sessionTask.IsCompleted) return;
            previousCancellation = _sessionCancellation;
            var previousTask = _sessionTask;
            var cancellation = new CancellationTokenSource();
            _sessionCancellation = cancellation;
            _desiredDeviceId = deviceId;
            generation = ++_generation;
            // Replacement sessions must wait for the old handle to be released. If
            // Windows cleanup stalls, do not create overlapping connections instead.
            _sessionTask = Task.Run(async () =>
            {
                try
                {
                    await previousTask.ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    await RunSessionAsync(deviceId, generation, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception exception) { WriteError("Sessão auxiliar encerrada", exception); }
                finally { cancellation.Dispose(); }
            });
        }
        RequestCancellation(previousCancellation);
        SetSessionStatus(generation, "Preparando conexão auxiliar…", false);
    }

    /// <summary>
    /// Stops only this service's audio reference. Awaiting this never awaits the UI
    /// dispatcher; it returns after six seconds if Windows has not finished cleanup.
    /// </summary>
    public async Task StopAsync()
    {
        Task task;
        CancellationTokenSource? cancellation;
        long generation;
        lock (_sync)
        {
            cancellation = _sessionCancellation;
            _sessionCancellation = null;
            _desiredDeviceId = null;
            task = _sessionTask;
            generation = ++_generation;
        }
        SetSessionStatus(generation, "Liberando conexão auxiliar…", false);
        RequestCancellation(cancellation);
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            SetStoppedStatus(generation);
        }
        catch (TimeoutException)
        {
            SetSessionStatus(generation, "Aguardando o Windows liberar a conexão…", false);
            WriteLog("Liberação ainda pendente; nenhuma nova conexão será aberta antes da limpeza.");
            _ = CompleteStopAsync(task, generation);
        }
    }

    private async Task CompleteStopAsync(Task task, long generation)
    {
        try
        {
            await task.ConfigureAwait(false);
            SetStoppedStatus(generation);
        }
        catch (Exception exception) { WriteError("Falha ao concluir a liberação", exception); }
    }

    private async Task RunSessionAsync(string deviceId, long generation, CancellationToken cancellation)
    {
        var retry = 0;
        while (!cancellation.IsCancellationRequested)
        {
            if (HasReleaseFailure())
            {
                SetStoppedStatus(generation);
                return;
            }
            AudioPlaybackConnection? connection = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                SetSessionStatus(generation, "Conectando Bluetooth auxiliar…", false);
                connection = AudioPlaybackConnection.TryCreateFromId(deviceId)
                    ?? throw new InvalidOperationException("Endpoint indisponível.");
                await WithDeadlineAsync(async token =>
                {
                    await connection.StartAsync().AsTask(token).ConfigureAwait(false);
                    return true;
                }, TimeSpan.FromSeconds(20), cancellation).ConfigureAwait(false);
                var result = await WithDeadlineAsync(
                    token => connection.OpenAsync().AsTask(token),
                    TimeSpan.FromSeconds(30), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
                {
                    WriteLog($"Abertura de áudio: {result.Status}; HRESULT={result.ExtendedError?.HResult:X8}.");
                }
                else
                {
                    SetSessionStatus(generation, "Bluetooth auxiliar conectado; áudio pode sair no PC.", true);
                    WriteLog("Perfil de áudio aberto; a automação do iPhone deve ser verificada no aparelho.");
                    var connectedAt = Environment.TickCount64;
                    // Polling avoids retaining WinRT event handlers after cancellation.
                    // Require a second Closed observation to debounce transient changes.
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
                        if (connection.State != AudioPlaybackConnectionState.Closed) continue;
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
                        if (connection.State == AudioPlaybackConnectionState.Closed) break;
                    }
                    // Brief connect/disconnect loops retain their increasing backoff.
                    if (Environment.TickCount64 - connectedAt >= 60_000) retry = 0;
                    WriteLog("Perfil de áudio fechado; reconexão será espaçada.");
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception exception) { WriteError("Tentativa de conexão auxiliar falhou", exception); }
            finally
            {
                if (connection is not null)
                {
                    try { connection.Dispose(); }
                    catch (Exception exception)
                    {
                        // Do not create another handle when ownership of the previous
                        // one is uncertain, including in an already queued replacement.
                        lock (_sync) _releaseFailed = true;
                        WriteError("Falha ao liberar referência de áudio; novas tentativas bloqueadas", exception);
                    }
                }
            }

            cancellation.ThrowIfCancellationRequested();
            if (HasReleaseFailure())
            {
                SetStoppedStatus(generation);
                return;
            }
            var seconds = retry++ switch { 0 => 15, 1 => 30, _ => 60 };
            SetSessionStatus(generation, $"Aguardando o iPhone; nova tentativa em {seconds} s.", false);
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation).ConfigureAwait(false);
        }
    }

    private static async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        var pending = operation(deadline.Token);
        // Observe a fault even if the WinRT provider outlives the caller's deadline.
        _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try { return await pending.WaitAsync(timeout + TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false); }
        finally { deadline.Cancel(); }
    }

    private static void RequestCancellation(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        // WinRT cancellation can invoke native code; never perform it on the UI thread.
        _ = Task.Run(() =>
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { }
        });
    }

    private bool HasReleaseFailure()
    {
        lock (_sync) return _releaseFailed;
    }

    private void SetStoppedStatus(long generation) => SetSessionStatus(generation,
        HasReleaseFailure()
            ? "Não foi possível liberar o áudio; reinicie o aplicativo."
            : "Conexão auxiliar desligada.", false);

    private void SetSessionStatus(long generation, string status, bool connected) => Post(() =>
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            Status = status;
            IsConnected = connected;
        }
    });

    private void WriteError(string context, Exception exception) =>
        WriteLog($"{context}: {exception.GetType().Name}, HRESULT={exception.HResult:X8}.");

    private void WriteLog(string message) => Post(() =>
    {
        // Observers cannot take down connection cleanup. Never log endpoint IDs or
        // exception messages, which may contain device addresses and personal names.
        if (Log is null) return;
        foreach (Action<string> observer in Log.GetInvocationList())
        {
            try { observer(message); }
            catch { /* A UI/log subscriber must not break lifecycle management. */ }
        }
    });

    private void Post(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        if (_dispatcher.CheckAccess()) action();
        else
        {
            try { _dispatcher.BeginInvoke(action); }
            catch (InvalidOperationException) { /* Dispatcher is shutting down. */ }
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
