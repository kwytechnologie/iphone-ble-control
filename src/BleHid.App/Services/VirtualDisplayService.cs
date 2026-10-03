using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using BleHid.Core;
using Screen = System.Windows.Forms.Screen;

namespace BleHid.App.Services;

/// <summary>Owns one ephemeral software display through a narrowly scoped elevated helper.
/// No driver installation, radio access or certificate-store changes occur here.</summary>
public sealed class VirtualDisplayService : INotifyPropertyChanged
{
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current.Dispatcher;
    private Process? _host;
    private EventWaitHandle? _stop;
    private bool _busy, _visible;
    private string _status = "Tela virtual desligada. O modo tradicional continua disponível.";
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public bool IsVisible { get => _visible; private set => Set(ref _visible, value); }
    public bool CanRemove => IsVisible || _host is not null;
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            Set(ref _status, value);
            try { File.AppendAllText(AppPaths.InLogs("virtual-display-app.log"), $"{DateTimeOffset.Now:O} {value}{Environment.NewLine}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public async Task ShowAsync()
    {
        if (IsBusy || IsVisible || App.Current.IsExiting) return;
        if (_host is not null) { Status = "Remova o componente anterior antes de criar outra tela."; return; }
        IsBusy = true;
        try
        {
            if (!File.Exists(@"C:\VirtualDisplayDriver\blehid-owner.txt"))
                throw new InvalidOperationException("O driver dedicado ainda não foi preparado. Consulte TELAS-WINDOWS.md.");
            var helper = Path.Combine(AppContext.BaseDirectory, "BleHid.VirtualDisplayHost.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("Componente de monitor virtual não encontrado.");
            if (WindowsDisplayLayout.FindPhone() is not null)
                throw new InvalidOperationException("Já existe uma tela deste app aberta por outra instância. Feche-a antes de continuar.");
            var physical = Screen.AllScreens.FirstOrDefault(s => s.Primary);
            var eventName = @"Local\BleHid.VirtualDisplay.Stop." + Guid.NewGuid().ToString("N");
            _stop = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            using var current = Process.GetCurrentProcess();
            Status = "Aguardando a permissão do Windows para criar a tela…";
            var start = new ProcessStartInfo(helper)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--parent-pid {current.Id} --parent-start-ticks {current.StartTime.ToUniversalTime().Ticks} --stop-event {eventName}"
            };
            // ShellExecute may wait for the UAC prompt; leave the app responsive.
            _host = await Task.Run(() => Process.Start(start)) ?? throw new InvalidOperationException("O componente não iniciou.");
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (_host.HasExited) throw new InvalidOperationException(DescribeHostFailure(_host.ExitCode));
                if (App.Current.IsExiting) throw new OperationCanceledException();
                var phone = WindowsDisplayLayout.FindPhone();
                if (phone is not null)
                {
                    if (phone.Primary) throw new InvalidOperationException("A tela virtual virou principal. Removendo para preservar o monitor físico.");
                    if (!AppSettings.Instance.WindowsDisplayWasPositioned && physical is not null)
                    {
                        if (WindowsDisplayLayout.PlacePhone(phone, physical, AppSettings.Instance.EdgePosition))
                            AppSettings.Instance.WindowsDisplayWasPositioned = true;
                    }
                    IsVisible = true;
                    AppSettings.Instance.UseWindowsDisplayLayout = true;
                    AppSettings.Instance.EdgeSwitchEnabled = true;
                    Status = "iPhone disponível em Sistema → Tela. Ative o modo telas uma vez; depois, ao arrastar o retângulo no Windows e clicar em Aplicar, a passagem acompanhará a posição automaticamente.";
                    Changed?.Invoke();
                    _ = WatchHostAsync(_host);
                    return;
                }
                await Task.Delay(250);
            }
            throw new TimeoutException("A tela virtual não apareceu. Nenhum monitor físico foi alterado.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (await ReleaseHostAsync()) Status = "Permissão cancelada; tela virtual não criada.";
        }
        catch (Exception ex)
        {
            if (await ReleaseHostAsync()) Status = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task HideAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            RecoverWindows();
            if (!await ReleaseHostAsync()) return;
            for (var attempt = 0; attempt < 20 && WindowsDisplayLayout.FindPhone() is not null; attempt++)
                await Task.Delay(100);
            IsVisible = WindowsDisplayLayout.FindPhone() is not null;
            Status = IsVisible ? "Aguardando o Windows remover a tela virtual. Tente Remover novamente."
                : "Tela virtual removida. Nenhum monitor físico foi desligado.";
            Changed?.Invoke();
        }
        finally { IsBusy = false; }
    }

    public void RecoverWindows()
    {
        if (!IsVisible || WindowsDisplayLayout.FindPhone() is not { } phone) return;
        var moved = WindowsDisplayLayout.RecoverWindows(phone);
        Status = $"Janelas recuperadas para o monitor físico: {moved}. Janelas protegidas por outro nível de permissão podem exigir recuperação manual.";
    }

    public void OpenDisplaySettings() => Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });

    private static string DescribeHostFailure(int code)
    {
        if (code < 0)
            return $"A criação da tela falhou: HRESULT 0x{code:X8}. O detalhe foi registrado no diagnóstico do monitor virtual.";
        var reason = code switch
        {
            2 => "Argumentos do componente inválidos",
            10 => "O componente não recebeu permissão de administrador",
            11 => "Não foi possível acessar o aplicativo principal",
            12 => "A identidade ou sessão do aplicativo principal não confere",
            13 => "O Windows informou pastas diferentes para o aplicativo e o componente",
            14 => "O componente não conseguiu abrir o canal de encerramento do aplicativo",
            15 => "Não foi possível reservar o componente; outra instância pode estar aberta",
            19 => "A ponte nativa do monitor está ausente ou inválida; reinstale o pacote completo do aplicativo",
            20 => "O Windows recusou a criação do dispositivo virtual",
            21 => "O Windows não enumerou o dispositivo dentro do prazo",
            22 => "O Windows retornou uma falha ao enumerar o dispositivo",
            23 => "Não foi possível limitar o dispositivo à duração do aplicativo",
            24 => "O Windows retornou uma identidade de dispositivo inesperada",
            _ => "O componente encerrou antes de disponibilizar a tela"
        };
        return $"{reason} (código {code}). Consulte o registro do monitor virtual.";
    }

    private async Task<bool> ReleaseHostAsync()
    {
        _stop?.Set();
        var host = _host;
        _host = null;
        if (host is not null)
        {
            try { await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException)
            {
                _host = host;
                Status = "O componente ainda está encerrando. Tente Remover novamente.";
                return false;
            }
            host.Dispose();
        }
        _stop?.Dispose();
        _stop = null;
        return true;
    }

    private async Task WatchHostAsync(Process host)
    {
        try
        {
            await host.WaitForExitAsync();
            await _dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_host, host)) return;
                _host = null;
                _stop?.Dispose(); _stop = null;
                IsVisible = false;
                Status = "Componente de tela encerrado; o Windows está removendo a tela virtual.";
                Changed?.Invoke();
                host.Dispose();
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or TaskCanceledException) { }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRemove)));
    }
}
