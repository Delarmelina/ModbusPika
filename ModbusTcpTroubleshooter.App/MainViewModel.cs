using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ModbusTcpTroubleshooter.Core;
using Serilog;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ModbusDataMap _serverMap = new();
    private readonly DiagnosticsEngine _diagnostics = new();
    private readonly ModbusTcpClientProbe _client = new();
    private readonly NetworkCaptureService _networkCapture = new();
    private readonly ConcurrentQueue<TcpTimelineRow> _passivePacketQueue = new();
    private readonly DispatcherTimer _passivePacketFlushTimer;
    private ModbusTcpServer? _server;
    private CancellationTokenSource? _serverCts;
    private CancellationTokenSource? _scanCts;
    private readonly Dictionary<string, DateTimeOffset> _lastRequestBySignature = [];
    private readonly Dictionary<string, int> _exceptionCounts = [];
    private readonly Dictionary<string, int> _outOfMapCounts = [];

    [ObservableProperty] private string caseName = "Troubleshoot Modbus TCP";
    [ObservableProperty] private string selectedMode = "Client";
    [ObservableProperty] private string localIp = "0.0.0.0";
    [ObservableProperty] private string targetIp = "127.0.0.1";
    [ObservableProperty] private int port = 1502;
    [ObservableProperty] private byte unitId = 1;
    [ObservableProperty] private ushort writeAddress;
    [ObservableProperty] private ushort writeValue = 1234;
    [ObservableProperty] private int scanRateMs = 1000;
    [ObservableProperty] private string status = "Pronto.";
    [ObservableProperty] private bool isServerRunning;
    [ObservableProperty] private bool isClientScanning;
    [ObservableProperty] private bool isNetworkCaptureRunning;
    [ObservableProperty] private ClientMapRow? selectedClientMapRow;
    [ObservableProperty] private ServerMapRange? selectedServerMapRange;
    [ObservableProperty] private CaptureDeviceOption? selectedCaptureDevice;
    [ObservableProperty] private string selectedCaptureProtocol = "Todos";
    [ObservableProperty] private string captureIp = "";
    [ObservableProperty] private string selectedCaptureIpDirection = "Origem ou destino";
    [ObservableProperty] private string capturePort = "";
    [ObservableProperty] private string selectedCapturePortDirection = "Origem ou destino";
    [ObservableProperty] private string generatedCaptureFilter = "tcp or udp or arp or icmp";
    [ObservableProperty] private string tcpViewFilter = "";
    [ObservableProperty] private string sourceColumnFilter = "";
    [ObservableProperty] private string destinationColumnFilter = "";
    [ObservableProperty] private string protocolColumnFilter = "";
    [ObservableProperty] private string infoColumnFilter = "";
    [ObservableProperty] private int queuedPassivePackets;

    public ObservableCollection<string> Modes { get; } = ["Client", "Server"];
    public ObservableCollection<string> CaptureProtocols { get; } = ["Todos", "TCP", "UDP", "ARP", "ICMP", "Modbus TCP"];
    public ObservableCollection<string> CaptureDirections { get; } = ["Origem ou destino", "Somente origem", "Somente destino"];
    public ObservableCollection<CaptureDeviceOption> CaptureDevices { get; } = [];
    public ObservableCollection<ModbusPoint> ServerPoints { get; } = [];
    public ObservableCollection<ServerMapRange> ServerMapRanges { get; } = [];
    public ObservableCollection<ClientMapRow> ClientMapRows { get; } = [];
    public ObservableCollection<TrafficEvent> Traffic { get; } = [];
    public ObservableCollection<TcpTimelineRow> TcpTimeline { get; } = [];
    public ObservableCollection<TcpTimelineRow> FilteredTcpTimeline { get; } = [];
    public ObservableCollection<DiagnosticFinding> Diagnostics { get; } = [];
    public ObservableCollection<ImportantWarningSummary> ImportantWarnings { get; } = [];
    public ObservableCollection<VerificationCheck> VerificationChecks { get; } = [];

    public bool IsClientMode => SelectedMode == "Client";
    public bool IsServerMode => SelectedMode == "Server";

    public MainViewModel()
    {
        _serverMap.LoadDefaults();
        LoadDefaultServerRanges();
        ApplyServerMapRanges();
        RefreshServerPoints();
        LoadDefaultClientMap();
        LoadVerificationChecks();
        LoadCaptureDevices();
        _client.TrafficObserved += OnTrafficObserved;
        _networkCapture.PacketCaptured += OnPassivePacketCaptured;
        _passivePacketFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _passivePacketFlushTimer.Tick += (_, _) => FlushPassivePackets();
        _passivePacketFlushTimer.Start();
    }

    [RelayCommand]
    private void AddClientMapRow()
    {
        var nextAddress = ClientMapRows.Count == 0 ? 0 : ClientMapRows.Max(x => x.StartAddress + x.Quantity);
        var row = new ClientMapRow
        {
            Name = $"Leitura {ClientMapRows.Count + 1}",
            Function = "FC03 Holding Registers",
            StartAddress = (ushort)Math.Min(ushort.MaxValue, nextAddress),
            Quantity = 1,
            Enabled = true
        };

        ClientMapRows.Add(row);
        SelectedClientMapRow = row;
    }

    [RelayCommand]
    private void RemoveClientMapRow()
    {
        if (SelectedClientMapRow is null)
        {
            return;
        }

        ClientMapRows.Remove(SelectedClientMapRow);
        SelectedClientMapRow = ClientMapRows.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanEditServerMap))]
    private void AddServerRange()
    {
        var row = new ServerMapRange
        {
            Type = ModbusPointType.HoldingRegister,
            StartAddress = 0,
            Quantity = 1,
            InitialValue = 0,
            NamePrefix = $"Range {ServerMapRanges.Count + 1}",
            Writable = true
        };

        ServerMapRanges.Add(row);
        SelectedServerMapRange = row;
        ApplyServerMapRanges();
    }

    [RelayCommand(CanExecute = nameof(CanEditServerMap))]
    private void RemoveServerRange()
    {
        if (SelectedServerMapRange is null)
        {
            return;
        }

        ServerMapRanges.Remove(SelectedServerMapRange);
        SelectedServerMapRange = ServerMapRanges.FirstOrDefault();
        ApplyServerMapRanges();
    }

    [RelayCommand(CanExecute = nameof(CanEditServerMap))]
    private void ApplyServerMap()
    {
        ApplyServerMapRanges();
        Status = $"Mapa do server aplicado: {ServerPoints.Count} ponto(s).";
    }

    [RelayCommand(CanExecute = nameof(CanStartServer))]
    private async Task StartServerAsync()
    {
        _serverCts = new CancellationTokenSource();
        _server = new ModbusTcpServer(_serverMap);
        _server.TrafficObserved += OnTrafficObserved;
        IsServerRunning = true;
        Status = $"Servidor escutando em {LocalIp}:{Port}.";

        try
        {
            var address = IPAddress.Parse(LocalIp);
            await _server.StartAsync(address, Port, _serverCts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Servidor parado.";
        }
        catch (Exception ex)
        {
            Status = $"Falha ao iniciar servidor: {ex.Message}";
            Log.Error(ex, "Falha ao iniciar servidor");
        }
        finally
        {
            IsServerRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsServerRunning))]
    private void StopServer()
    {
        _serverCts?.Cancel();
        _server?.Stop();
        IsServerRunning = false;
        Status = "Servidor parado.";
    }

    [RelayCommand(CanExecute = nameof(CanReadOnce))]
    private async Task ReadOnceAsync()
    {
        await ReadEnabledClientRowsAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanStartClientScan))]
    private async Task StartClientScanAsync()
    {
        _scanCts = new CancellationTokenSource();
        IsClientScanning = true;
        Status = $"Scan iniciado a cada {ScanRateMs} ms.";

        try
        {
            while (!_scanCts.IsCancellationRequested)
            {
                await ReadEnabledClientRowsAsync(_scanCts.Token);
                await Task.Delay(Math.Max(100, ScanRateMs), _scanCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Scan parado.";
        }
        finally
        {
            IsClientScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsClientScanning))]
    private void StopClientScan()
    {
        _scanCts?.Cancel();
        IsClientScanning = false;
        Status = "Scan parado.";
    }

    [RelayCommand]
    private async Task WriteRegisterAsync()
    {
        try
        {
            await _client.WriteSingleRegisterAsync(TargetIp, Port, UnitId, WriteAddress, WriteValue, CancellationToken.None);
            Status = $"Escrita OK: HR {WriteAddress} = {WriteValue}";
        }
        catch (Exception ex)
        {
            AddSystemFinding($"Falha/timeout na escrita: {ex.Message}");
            Status = $"Falha na escrita: {ex.Message}";
            Log.Error(ex, "Falha na escrita Modbus");
        }
    }

    [RelayCommand]
    private async Task SaveCaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Caso Modbus (*.json)|*.json",
            FileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm}-modbus-case.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var troubleshootCase = new TroubleshootCase
        {
            Name = CaseName,
            LocalIp = LocalIp,
            TargetIp = TargetIp,
            Port = Port,
            UnitId = UnitId,
            Map = ServerPoints.ToList(),
            Traffic = Traffic.ToList(),
            Diagnostics = Diagnostics.ToList()
        };

        await CaseStorage.SaveAsync(troubleshootCase, dialog.FileName);
        Status = $"Caso exportado: {dialog.FileName}";
    }

    [RelayCommand]
    private void ClearTimeline()
    {
        Traffic.Clear();
        TcpTimeline.Clear();
        FilteredTcpTimeline.Clear();
        while (_passivePacketQueue.TryDequeue(out _))
        {
        }
        QueuedPassivePackets = 0;
        Diagnostics.Clear();
        ImportantWarnings.Clear();
        _lastRequestBySignature.Clear();
        _exceptionCounts.Clear();
        _outOfMapCounts.Clear();
        LoadVerificationChecks();
        Status = "Timeline limpa.";
    }

    [RelayCommand]
    private void RefreshCaptureDevices()
    {
        LoadCaptureDevices();
    }

    [RelayCommand(CanExecute = nameof(CanStartNetworkCapture))]
    private void StartNetworkCapture()
    {
        if (SelectedCaptureDevice is null)
        {
            Status = "Nenhuma interface de captura selecionada.";
            return;
        }

        try
        {
            GeneratedCaptureFilter = BuildCaptureFilter();
            _networkCapture.Start(SelectedCaptureDevice, GeneratedCaptureFilter);
            IsNetworkCaptureRunning = true;
            Status = $"Captura TCP iniciada: {SelectedCaptureDevice.Description}";
        }
        catch (Exception ex)
        {
            Status = $"Falha ao iniciar captura. Verifique Npcap/permissao/admin: {ex.Message}";
            Log.Error(ex, "Falha ao iniciar captura passiva");
        }
    }

    [RelayCommand(CanExecute = nameof(IsNetworkCaptureRunning))]
    private void StopNetworkCapture()
    {
        _networkCapture.Stop();
        IsNetworkCaptureRunning = false;
        Status = "Captura TCP parada.";
    }

    [RelayCommand]
    private void ApplyCaptureFilter()
    {
        try
        {
            GeneratedCaptureFilter = BuildCaptureFilter();
            if (IsNetworkCaptureRunning)
            {
                _networkCapture.UpdateFilter(GeneratedCaptureFilter);
                Status = $"Filtro atualizado: {GeneratedCaptureFilter}";
                return;
            }

            Status = $"Filtro preparado: {GeneratedCaptureFilter}";
        }
        catch (Exception ex)
        {
            Status = $"Falha ao atualizar filtro: {ex.Message}";
            Log.Error(ex, "Falha ao atualizar filtro de captura");
        }
    }

    private async Task ReadEnabledClientRowsAsync(CancellationToken cancellationToken)
    {
        var rows = ClientMapRows.Where(x => x.Enabled).ToList();
        if (rows.Count == 0)
        {
            Status = "Nenhuma linha habilitada no mapa do client.";
            return;
        }

        foreach (var row in rows)
        {
            try
            {
                var functionCode = row.FunctionCode;
                if (functionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs)
                {
                    var values = await _client.ReadBitsAsync(TargetIp, Port, UnitId, functionCode, row.StartAddress, row.Quantity, cancellationToken);
                    row.LastValue = string.Join(", ", values.Select(x => x ? "1" : "0"));
                }
                else
                {
                    var values = await _client.ReadRegistersAsync(TargetIp, Port, UnitId, functionCode, row.StartAddress, row.Quantity, cancellationToken);
                    row.LastValue = string.Join(", ", values);
                }

                row.LastStatus = "OK";
                row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
            }
            catch (Exception ex)
            {
                row.LastStatus = ex.Message;
                row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
                AddSystemFinding($"Falha/timeout na leitura '{row.Name}': {ex.Message}");
                Log.Error(ex, "Falha na linha de mapa client {MapRow}", row.Name);
            }
        }

        Status = $"Leitura finalizada: {rows.Count} linha(s).";
    }

    private bool CanStartServer() => !IsServerRunning;
    private bool CanReadOnce() => !IsClientScanning;
    private bool CanStartClientScan() => !IsClientScanning;
    private bool CanEditServerMap() => !IsServerRunning;
    private bool CanStartNetworkCapture() => !IsNetworkCaptureRunning && SelectedCaptureDevice is not null;

    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsClientMode));
        OnPropertyChanged(nameof(IsServerMode));
    }

    partial void OnIsServerRunningChanged(bool value)
    {
        StartServerCommand.NotifyCanExecuteChanged();
        StopServerCommand.NotifyCanExecuteChanged();
        AddServerRangeCommand.NotifyCanExecuteChanged();
        RemoveServerRangeCommand.NotifyCanExecuteChanged();
        ApplyServerMapCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsClientScanningChanged(bool value)
    {
        ReadOnceCommand.NotifyCanExecuteChanged();
        StartClientScanCommand.NotifyCanExecuteChanged();
        StopClientScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsNetworkCaptureRunningChanged(bool value)
    {
        StartNetworkCaptureCommand.NotifyCanExecuteChanged();
        StopNetworkCaptureCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCaptureDeviceChanged(CaptureDeviceOption? value)
    {
        StartNetworkCaptureCommand.NotifyCanExecuteChanged();
    }

    partial void OnTcpViewFilterChanged(string value)
    {
        ApplyTcpViewFilter();
    }

    partial void OnSourceColumnFilterChanged(string value)
    {
        ApplyTcpViewFilter();
    }

    partial void OnDestinationColumnFilterChanged(string value)
    {
        ApplyTcpViewFilter();
    }

    partial void OnProtocolColumnFilterChanged(string value)
    {
        ApplyTcpViewFilter();
    }

    partial void OnInfoColumnFilterChanged(string value)
    {
        ApplyTcpViewFilter();
    }

    partial void OnSelectedCaptureProtocolChanged(string value)
    {
        GeneratedCaptureFilter = BuildCaptureFilter();
    }

    partial void OnCaptureIpChanged(string value)
    {
        GeneratedCaptureFilter = BuildCaptureFilter();
    }

    partial void OnSelectedCaptureIpDirectionChanged(string value)
    {
        GeneratedCaptureFilter = BuildCaptureFilter();
    }

    partial void OnCapturePortChanged(string value)
    {
        GeneratedCaptureFilter = BuildCaptureFilter();
    }

    partial void OnSelectedCapturePortDirectionChanged(string value)
    {
        GeneratedCaptureFilter = BuildCaptureFilter();
    }

    private void LoadDefaultClientMap()
    {
        ClientMapRows.Add(new ClientMapRow { Name = "Holding 0-9", Function = "FC03 Holding Registers", StartAddress = 0, Quantity = 10, Enabled = true });
        ClientMapRows.Add(new ClientMapRow { Name = "Input 0-9", Function = "FC04 Input Registers", StartAddress = 0, Quantity = 10, Enabled = false });
        SelectedClientMapRow = ClientMapRows[0];
    }

    private void LoadDefaultServerRanges()
    {
        ServerMapRanges.Add(new ServerMapRange { Type = ModbusPointType.Coil, StartAddress = 0, Quantity = 20, InitialValue = 1, NamePrefix = "Coil", Writable = true });
        ServerMapRanges.Add(new ServerMapRange { Type = ModbusPointType.DiscreteInput, StartAddress = 0, Quantity = 20, InitialValue = 0, NamePrefix = "Discrete", Writable = false });
        ServerMapRanges.Add(new ServerMapRange { Type = ModbusPointType.HoldingRegister, StartAddress = 0, Quantity = 20, InitialValue = 1000, NamePrefix = "HR", Writable = true });
        ServerMapRanges.Add(new ServerMapRange { Type = ModbusPointType.InputRegister, StartAddress = 0, Quantity = 20, InitialValue = 2000, NamePrefix = "IR", Writable = false });
        SelectedServerMapRange = ServerMapRanges[0];
    }

    private void ApplyServerMapRanges()
    {
        _serverMap.Clear();
        foreach (var range in ServerMapRanges.Where(x => x.Enabled))
        {
            for (var i = 0; i < range.Quantity; i++)
            {
                var address = (ushort)(range.StartAddress + i);
                var value = range.IncrementValue ? (ushort)(range.InitialValue + i) : range.InitialValue;
                _serverMap.AddPoint(range.Type, address, value);
            }
        }

        RefreshServerPoints();
    }

    private void RefreshServerPoints()
    {
        ServerPoints.Clear();
        foreach (var point in _serverMap.ToPoints())
        {
            ServerPoints.Add(point);
        }
    }

    private void LoadCaptureDevices()
    {
        CaptureDevices.Clear();
        foreach (var device in _networkCapture.GetDevices())
        {
            CaptureDevices.Add(device);
        }

        SelectedCaptureDevice = CaptureDevices.FirstOrDefault();
        if (CaptureDevices.Count == 0)
        {
            Status = "Nenhuma interface de captura encontrada. Instale Npcap para captura passiva.";
        }
    }

    private void OnTrafficObserved(object? sender, TrafficEvent e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Traffic.Insert(0, e);
            while (Traffic.Count > 500)
            {
                Traffic.RemoveAt(Traffic.Count - 1);
            }

            var finding = _diagnostics.Analyze(e);
            Diagnostics.Insert(0, finding);
            while (Diagnostics.Count > 200)
            {
                Diagnostics.RemoveAt(Diagnostics.Count - 1);
            }

            if (finding.Severity is "Alerta" or "Atencao" or "Erro")
            {
                UpsertImportantWarning(e, finding);
            }

            UpdateVerificationChecks(e, finding);
            AnalyzeCommunicationPattern(e);
            RefreshServerPoints();
        });
    }

    private void OnPassivePacketCaptured(object? sender, TcpTimelineRow row)
    {
        _passivePacketQueue.Enqueue(row);
    }

    private void FlushPassivePackets()
    {
        const int maxRowsPerFlush = 250;
        var processed = 0;

        while (processed < maxRowsPerFlush && _passivePacketQueue.TryDequeue(out var row))
        {
            AddTcpTimelineRow(row);
            processed++;
        }

        QueuedPassivePackets = _passivePacketQueue.Count;
        if (QueuedPassivePackets > 5000)
        {
            UpsertImportantWarning(
                "captura-tcp-alta-taxa",
                "Atencao",
                "Captura TCP em alta taxa",
                $"Fila de pacotes pendentes: {QueuedPassivePackets}.",
                "Aplique filtros de captura por protocolo, IP ou porta para reduzir a carga da interface.",
                DateTimeOffset.Now);
        }
    }

    private void AddTcpTimelineRow(TcpTimelineRow row)
    {
        TcpTimeline.Insert(0, row);
        if (MatchesTcpFilter(row))
        {
            FilteredTcpTimeline.Insert(0, row);
        }

        while (TcpTimeline.Count > 2000)
        {
            TcpTimeline.RemoveAt(TcpTimeline.Count - 1);
        }
        while (FilteredTcpTimeline.Count > 2000)
        {
            FilteredTcpTimeline.RemoveAt(FilteredTcpTimeline.Count - 1);
        }
    }

    private void ApplyTcpViewFilter()
    {
        FilteredTcpTimeline.Clear();
        foreach (var row in TcpTimeline.Where(MatchesTcpFilter))
        {
            FilteredTcpTimeline.Add(row);
        }
    }

    private bool MatchesTcpFilter(TcpTimelineRow row)
    {
        if (!Contains(row.Source, SourceColumnFilter)
            || !Contains(row.Destination, DestinationColumnFilter)
            || !Contains(row.Protocol, ProtocolColumnFilter)
            || !Contains(row.Info, InfoColumnFilter))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TcpViewFilter))
        {
            return true;
        }

        var filter = TcpViewFilter.Trim();
        return row.Source.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Destination.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Protocol.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Info.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Length.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Number.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildCaptureFilter()
    {
        var parts = new List<string>();

        var protocol = SelectedCaptureProtocol switch
        {
            "TCP" => "tcp",
            "UDP" => "udp",
            "ARP" => "arp",
            "ICMP" => "icmp",
            "Modbus TCP" => string.IsNullOrWhiteSpace(CapturePort) ? "tcp port 502" : "tcp",
            _ => "tcp or udp or arp or icmp"
        };
        parts.Add($"({protocol})");

        if (!string.IsNullOrWhiteSpace(CaptureIp))
        {
            var ipClause = SelectedCaptureIpDirection switch
            {
                "Somente origem" => $"src host {CaptureIp.Trim()}",
                "Somente destino" => $"dst host {CaptureIp.Trim()}",
                _ => $"host {CaptureIp.Trim()}"
            };
            parts.Add($"({ipClause})");
        }

        if (!string.IsNullOrWhiteSpace(CapturePort))
        {
            var portClause = SelectedCapturePortDirection switch
            {
                "Somente origem" => $"src port {CapturePort.Trim()}",
                "Somente destino" => $"dst port {CapturePort.Trim()}",
                _ => $"port {CapturePort.Trim()}"
            };
            parts.Add($"({portClause})");
        }

        return string.Join(" and ", parts);
    }

    private static bool Contains(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void AddSystemFinding(string message)
    {
        var trafficEvent = new TrafficEvent(DateTimeOffset.Now, TrafficDirection.System, "local", null, null, null, null, null, message, string.Empty);
        OnTrafficObserved(this, trafficEvent);
    }

    private void LoadVerificationChecks()
    {
        VerificationChecks.Clear();
        VerificationChecks.Add(new VerificationCheck("Socket TCP", "Aguardando", "Ainda sem conexao ou tentativa de comunicacao."));
        VerificationChecks.Add(new VerificationCheck("Resposta Modbus", "Aguardando", "Ainda sem resposta Modbus valida."));
        VerificationChecks.Add(new VerificationCheck("Transaction ID", "Aguardando", "Ainda sem par request/response para correlacionar."));
        VerificationChecks.Add(new VerificationCheck("Function Code", "Aguardando", "Ainda sem function code avaliado."));
        VerificationChecks.Add(new VerificationCheck("Mapa / range", "Aguardando", "Ainda sem validacao contra mapa."));
        VerificationChecks.Add(new VerificationCheck("Escritas", "Aguardando", "Nenhuma escrita observada."));
        VerificationChecks.Add(new VerificationCheck("Padrao de polling", "Aguardando", "Ainda sem repeticao suficiente para estimar taxa."));
        VerificationChecks.Add(new VerificationCheck("Exceptions repetidas", "Aguardando", "Nenhuma exception repetida observada."));
    }

    private void UpdateVerificationChecks(TrafficEvent trafficEvent, DiagnosticFinding finding)
    {
        if (trafficEvent.Direction is TrafficDirection.ClientToServer or TrafficDirection.ServerToClient)
        {
            SetCheck("Socket TCP", "OK", $"Comunicacao observada com {trafficEvent.Endpoint}.");
        }

        if (trafficEvent.Direction == TrafficDirection.ServerToClient && trafficEvent.FunctionCode is not null)
        {
            SetCheck("Resposta Modbus", "OK", $"Resposta recebida para FC{trafficEvent.FunctionCode}.");
        }

        if (trafficEvent.TransactionId is not null)
        {
            SetCheck("Transaction ID", "OK", $"Ultimo TID observado: {trafficEvent.TransactionId}.");
        }

        if (trafficEvent.FunctionCode is not null)
        {
            var fcStatus = trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase) ? "Falha" : "OK";
            SetCheck("Function Code", fcStatus, $"Ultimo FC avaliado: {trafficEvent.FunctionCode}. {trafficEvent.Summary}");
        }

        if (trafficEvent.StartAddress is not null)
        {
            var rangeStatus = trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase) ? "Falha" : "OK";
            SetCheck("Mapa / range", rangeStatus, $"Endereco {trafficEvent.StartAddress}, quantidade {trafficEvent.Quantity}.");
        }

        if (trafficEvent.FunctionCode is 5 or 6 or 15 or 16)
        {
            SetCheck("Escritas", "Atencao", $"Escrita observada: FC{trafficEvent.FunctionCode}, endereco {trafficEvent.StartAddress}.");
        }

        if (finding.Severity == "Erro")
        {
            SetCheck("Resposta Modbus", "Falha", finding.Message);
        }
    }

    private void AnalyzeCommunicationPattern(TrafficEvent trafficEvent)
    {
        if (trafficEvent.FunctionCode is null)
        {
            return;
        }

        if (trafficEvent.Direction == TrafficDirection.ClientToServer && trafficEvent.StartAddress is not null)
        {
            var signature = $"{trafficEvent.Endpoint}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
            if (_lastRequestBySignature.TryGetValue(signature, out var lastSeen))
            {
                var intervalMs = (trafficEvent.Timestamp - lastSeen).TotalMilliseconds;
                var status = intervalMs < 100 ? "Atencao" : "OK";
                SetCheck("Padrao de polling", status, $"FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress} qty {trafficEvent.Quantity}: intervalo ~{intervalMs:0} ms.");

                if (intervalMs < 100)
                {
                    UpsertImportantWarning(
                        "polling-rapido",
                        "Atencao",
                        "Polling muito rapido observado",
                        $"Requisicoes repetidas abaixo de 100 ms para FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}.",
                        "Verifique se a taxa de scan do cliente/PLC esta adequada para o equipamento e para a rede.",
                        trafficEvent.Timestamp);
                }
            }

            _lastRequestBySignature[signature] = trafficEvent.Timestamp;
        }

        if (trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
            _exceptionCounts[key] = _exceptionCounts.GetValueOrDefault(key) + 1;
            SetCheck("Exceptions repetidas", _exceptionCounts[key] >= 3 ? "Falha" : "Atencao", $"{_exceptionCounts[key]} exception(s) em {key}.");
        }

        if (trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
            _outOfMapCounts[key] = _outOfMapCounts.GetValueOrDefault(key) + 1;
            SetCheck("Mapa / range", "Falha", $"{_outOfMapCounts[key]} acesso(s) fora do mapa em {key}.");
        }
    }

    private void UpsertImportantWarning(TrafficEvent trafficEvent, DiagnosticFinding finding)
    {
        var key = BuildWarningKey(trafficEvent, finding);
        UpsertImportantWarning(key, finding.Severity, BuildWarningTitle(trafficEvent, finding), finding.Message, finding.Recommendation, finding.Timestamp);
    }

    private void UpsertImportantWarning(string key, string severity, string title, string latestDetail, string recommendation, DateTimeOffset timestamp)
    {
        var existing = ImportantWarnings.FirstOrDefault(x => x.Key == key);
        if (existing is null)
        {
            ImportantWarnings.Insert(0, new ImportantWarningSummary(key, severity, title, latestDetail, recommendation, timestamp));
            return;
        }

        existing.Count++;
        existing.Severity = MergeSeverity(existing.Severity, severity);
        existing.LatestDetail = latestDetail;
        existing.Recommendation = recommendation;
        existing.LastSeenAt = timestamp;

        ImportantWarnings.Move(ImportantWarnings.IndexOf(existing), 0);
    }

    private static string BuildWarningKey(TrafficEvent trafficEvent, DiagnosticFinding finding)
    {
        if (trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase))
        {
            return $"fora-mapa|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
        }

        if (trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return $"exception|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
        }

        if (trafficEvent.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return $"timeout|{trafficEvent.Endpoint}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}";
        }

        if (trafficEvent.FunctionCode is 5 or 6 or 15 or 16)
        {
            return $"write|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}";
        }

        return $"{finding.Severity}|{finding.Recommendation}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}";
    }

    private static string BuildWarningTitle(TrafficEvent trafficEvent, DiagnosticFinding finding)
    {
        if (trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase))
        {
            return $"Acesso fora do mapa em FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}";
        }

        if (trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            return $"Exception Modbus em FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}";
        }

        if (trafficEvent.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return $"Timeout comunicando com {trafficEvent.Endpoint}";
        }

        if (trafficEvent.FunctionCode is 5 or 6 or 15 or 16)
        {
            return $"Escrita observada em FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}";
        }

        return finding.Message;
    }

    private static string MergeSeverity(string current, string next)
    {
        static int Rank(string severity) => severity switch
        {
            "Erro" => 3,
            "Falha" => 3,
            "Alerta" => 2,
            "Atencao" => 1,
            _ => 0
        };

        return Rank(next) > Rank(current) ? next : current;
    }

    private void SetCheck(string name, string status, string detail)
    {
        var check = VerificationChecks.FirstOrDefault(x => x.Name == name);
        if (check is null)
        {
            return;
        }

        check.Status = status;
        check.Detail = detail;
        check.LastCheckedAt = DateTime.Now.ToString("HH:mm:ss");
    }
}

public sealed partial class ClientMapRow : ObservableObject
{
    public static IReadOnlyList<string> AvailableFunctions { get; } =
    [
        "FC01 Coils",
        "FC02 Discrete Inputs",
        "FC03 Holding Registers",
        "FC04 Input Registers"
    ];

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string function = "FC03 Holding Registers";
    [ObservableProperty] private ushort startAddress;
    [ObservableProperty] private ushort quantity = 1;
    [ObservableProperty] private string lastValue = "";
    [ObservableProperty] private string lastStatus = "Nao lido";
    [ObservableProperty] private string lastReadAt = "";

    public IReadOnlyList<string> Functions => AvailableFunctions;

    public byte FunctionCode => Function switch
    {
        "FC01 Coils" => ModbusProtocol.ReadCoils,
        "FC02 Discrete Inputs" => ModbusProtocol.ReadDiscreteInputs,
        "FC04 Input Registers" => ModbusProtocol.ReadInputRegisters,
        _ => ModbusProtocol.ReadHoldingRegisters
    };
}

public sealed partial class ServerMapRange : ObservableObject
{
    public static IReadOnlyList<ModbusPointType> AvailableTypes { get; } =
    [
        ModbusPointType.Coil,
        ModbusPointType.DiscreteInput,
        ModbusPointType.HoldingRegister,
        ModbusPointType.InputRegister
    ];

    public IReadOnlyList<ModbusPointType> TypeOptions => AvailableTypes;

    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private ModbusPointType type = ModbusPointType.HoldingRegister;
    [ObservableProperty] private ushort startAddress;
    [ObservableProperty] private ushort quantity = 1;
    [ObservableProperty] private ushort initialValue;
    [ObservableProperty] private bool incrementValue = true;
    [ObservableProperty] private bool writable = true;
    [ObservableProperty] private string namePrefix = "Point";
}

public sealed partial class VerificationCheck : ObservableObject
{
    public VerificationCheck(string name, string status, string detail)
    {
        Name = name;
        Status = status;
        Detail = detail;
    }

    public string Name { get; }
    [ObservableProperty] private string status;
    [ObservableProperty] private string detail;
    [ObservableProperty] private string lastCheckedAt = "";
}

public sealed partial class ImportantWarningSummary : ObservableObject
{
    public ImportantWarningSummary(string key, string severity, string title, string latestDetail, string recommendation, DateTimeOffset timestamp)
    {
        Key = key;
        Severity = severity;
        Title = title;
        LatestDetail = latestDetail;
        Recommendation = recommendation;
        FirstSeenAt = timestamp;
        LastSeenAt = timestamp;
    }

    public string Key { get; }
    public DateTimeOffset FirstSeenAt { get; }
    [ObservableProperty] private DateTimeOffset lastSeenAt;
    [ObservableProperty] private int count = 1;
    [ObservableProperty] private string severity;
    [ObservableProperty] private string title;
    [ObservableProperty] private string latestDetail;
    [ObservableProperty] private string recommendation;
}

public sealed class TcpTimelineRow
{
    public int Number { get; init; }
    public double RelativeTime { get; init; }
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Protocol { get; init; } = "";
    public int Length { get; init; }
    public string Info { get; init; } = "";

    public string Details => string.Join(Environment.NewLine, new[]
    {
        $"No.: {Number}",
        $"Time: {RelativeTime:0.000000}",
        $"Source: {Source}",
        $"Destination: {Destination}",
        $"Protocol: {Protocol}",
        $"Length: {Length}",
        $"Info: {Info}"
    });
}
