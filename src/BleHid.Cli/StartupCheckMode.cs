using System.Text;
using BleHid.Core;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BleHid.Cli;

/// <summary>Tests the encrypted HID service without capturing or sending user input.</summary>
internal static class StartupCheckMode
{
    public static async Task<int> RunAsync()
    {
        using var instance = new Mutex(true, SingleInstance.MutexName, out var created);
        if (!created)
        {
            Console.Error.WriteLine("Feche o iPhone BLE Control antes deste teste de inicialização.");
            return 2;
        }

        var report = new StringBuilder();
        void Emit(string text)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {text}";
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        var result = 1;
        await using var peripheral = new BleHidPeripheral(requireEncryption: true);
        peripheral.Log += Emit;
        try
        {
            Emit($"iPhone BLE Control — teste de inicialização — {DateTime.Now:yyyy-MM-dd}");
            Emit(await BluetoothDiagnostics.DescribeEnvironmentAsync());
            peripheral.SelectLocal();
            await peripheral.StartAsync();
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (peripheral.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Started)
                throw new InvalidOperationException($"Publicidade não permaneceu ativa: {peripheral.AdvertisementStatus}");

            Emit("PASS: serviço HID criptografado e publicidade BLE ativos.");
            Emit("Nenhuma captura ou tecla enviada. Pareamento e controle no iPhone ainda precisam ser testados.");
            result = 0;
        }
        catch (Exception ex)
        {
            Emit($"FAIL: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                var path = AppPaths.InLogs($"startup-check-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                await File.WriteAllTextAsync(path, report.ToString());
                Console.WriteLine($"Relatório local: {path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Não foi possível salvar o relatório: {ex.Message}");
            }
        }
        return result;
    }
}
