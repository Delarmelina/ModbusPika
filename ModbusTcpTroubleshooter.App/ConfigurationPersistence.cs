using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public partial class MainViewModel
{
    private string? _configurationPath;

    public ApplicationConfiguration CaptureConfiguration() => new()
    {
        CaseName = CaseName, Mode = SelectedMode, SelectedClient = ClientSessions.IndexOf(SelectedClientSession),
        ClientStationName = ClientStationName, ServerName = ServerName,
        TestMode = TestMode, TestTargetIndexes = ClientSessions.Select((s, i) => (s, i)).Where(x => x.s.IncludeInFullTest).Select(x => x.i).ToList(),
        ServerAddress = LocalIp, ServerPort = ServerPort, ServerUnitId = ServerUnitId,
        Clients = ClientSessions.Select(x => new ClientSessionCase(x.Name, x.Address, x.Port, x.UnitId,
            x.ScanRateMs, x.KeepConnectionOpen, x.Rows.Select(r => new ClientBlockCase(
                r.Name, r.FunctionCode, r.StartAddress, r.Quantity, r.Enabled)).ToList())).ToList(),
        ServerRanges = ServerMapRanges.Select(r => new ServerRangeConfiguration(r.Enabled, r.Type,
            r.StartAddress, r.Quantity, r.InitialValue, r.IncrementValue, r.Writable, r.NamePrefix)).ToList(),
        ServerPoints = _serverMap.ToPoints().ToList()
    };

    public async Task RestoreConfigurationAsync(ApplicationConfiguration configuration)
    {
        ConfigurationFile.Validate(configuration);
        if (IsFullTestRunning || IsDeviceProbeRunning || IsServerRunning || IsNetworkCaptureRunning || ClientSessions.Any(x => x.IsScanning || x.IsReading))
            throw new InvalidOperationException("Pare o teste, o servidor, as leituras e a captura antes de abrir uma configuracao.");

        // Disconnect idle persistent sockets too; imported endpoints are never connected automatically.
        foreach (var session in ClientSessions.ToList())
        {
            await session.DisconnectAsync();
            session.Client.TrafficObserved -= OnTrafficObserved;
            session.FindingObserved -= OnTrafficObserved;
            session.PropertyChanged -= OnClientSessionPropertyChanged;
        }
        ClientSessions.Clear();
        _nextClientNumber = 0;
        foreach (var client in configuration.Clients)
        {
            var session = new ClientConnectionSession
            {
                Name = client.Name, Address = client.Address, Port = client.Port, UnitId = client.UnitId,
                ScanRateMs = client.ScanRateMs, KeepConnectionOpen = client.KeepConnectionOpen
            };
            foreach (var row in session.Rows.ToList()) session.RemoveRow(row);
            foreach (var block in client.Blocks)
                session.AddRow(new ClientMapRow { Name = block.Name, Enabled = block.Enabled,
                    Function = ClientMapRow.AvailableFunctions[block.FunctionCode - 1],
                    StartAddress = block.StartAddress, Quantity = block.Quantity });
            await session.Client.ConfigureConnectionModeAsync(client.KeepConnectionOpen);
            RegisterClientSession(session);
            _nextClientNumber++;
        }
        if (ClientSessions.Count == 0)
        {
            var empty = new ClientConnectionSession { Name = "Nenhum alvo", Address = "" };
            foreach (var row in empty.Rows.ToArray()) empty.RemoveRow(row);
            SelectedClientSession = empty;
        }
        else SelectedClientSession = ClientSessions[configuration.SelectedClient];
        CaseName = configuration.CaseName;
        ClientStationName = configuration.ClientStationName;
        ServerName = configuration.ServerName;
        TestMode = configuration.TestMode;
        SetFullTestTargets(configuration.TestTargetIndexes is null ? ClientSessions
            : configuration.TestTargetIndexes.Select(i => ClientSessions[i]));
        LocalIp = configuration.ServerAddress;
        ServerPort = configuration.ServerPort;
        ServerUnitId = configuration.ServerUnitId;
        ServerMapRanges.Clear();
        foreach (var range in configuration.ServerRanges)
            ServerMapRanges.Add(new ServerMapRange { Enabled = range.Enabled, Type = range.Type,
                StartAddress = range.StartAddress, Quantity = range.Quantity, InitialValue = range.InitialValue,
                IncrementValue = range.IncrementValue, Writable = range.Writable, NamePrefix = range.NamePrefix });
        SelectedServerMapRange = ServerMapRanges.FirstOrDefault();
        _serverMap.Clear();
        foreach (var point in configuration.ServerPoints)
            _serverMap.AddPoint(point.Type, point.Address, point.Value, point.IsWritable);
        RefreshServerPoints();
        SelectedMode = configuration.Mode;
        ResetDiagnosticSession();
        RefreshClientSessionBindings();
        Status = "Configuracao restaurada. Comunicacao parada; descoberta de rede somente ao executar o teste.";
    }

    [RelayCommand]
    private Task SaveConfigurationAsync() => SaveConfigurationFileAsync(false);

    [RelayCommand]
    private Task SaveConfigurationAsAsync() => SaveConfigurationFileAsync(true);

    private async Task SaveConfigurationFileAsync(bool saveAs)
    {
        try
        {
            if (IsFullTestRunning) throw new InvalidOperationException("Aguarde o termino do teste antes de salvar a configuracao.");
            var path = _configurationPath;
            if (saveAs || path is null)
            {
                var dialog = new SaveFileDialog { Filter = "Configuracao Modbus (*.ffd)|*.ffd", DefaultExt = ".ffd",
                    AddExtension = true, FileName = path is null ? "configuracao-modbus.ffd" : Path.GetFileName(path) };
                if (dialog.ShowDialog() != true) return;
                path = dialog.FileName;
            }
            var configuration = CaptureConfiguration();
            await ConfigurationFile.SaveAsync(path, configuration);
            _configurationPath = path;
            Status = $"Configuracao salva: {path}";
        }
        catch (Exception ex) { ShowConfigurationError(ex); }
    }

    [RelayCommand]
    private async Task OpenConfigurationAsync()
    {
        try
        {
            if (IsFullTestRunning || IsDeviceProbeRunning || IsServerRunning || IsNetworkCaptureRunning || ClientSessions.Any(x => x.IsScanning || x.IsReading))
                throw new InvalidOperationException("Pare o teste, o servidor, as leituras e a captura antes de abrir uma configuracao.");
            var dialog = new OpenFileDialog { Filter = "Configuracao Modbus (*.ffd)|*.ffd", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            var configuration = await ConfigurationFile.LoadAsync(dialog.FileName);
            if (ConfirmPendingDeviceEdits?.Invoke() == false) return;
            if (MessageBox.Show("Substituir os clientes e o mapa do servidor pela configuracao selecionada? Os resultados atuais serao limpos e a comunicacao permanecera parada.",
                "Abrir configuracao", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await RestoreConfigurationAsync(configuration);
            _configurationPath = dialog.FileName;
        }
        catch (Exception ex) { ShowConfigurationError(ex); }
    }

    private void ShowConfigurationError(Exception ex)
    {
        Status = $"Configuracao nao alterada/salva: {ex.Message}";
        MessageBox.Show(ex.Message, "Configuracao Modbus", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
