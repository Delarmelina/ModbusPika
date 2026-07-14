using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly Dictionary<string, Queue<double>> _pollingIntervalsBySignature = [];
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
    [ObservableProperty] private bool isFullTestRunning;
    [ObservableProperty] private FullTestStep? selectedFullTestStep;
    [ObservableProperty] private string fullTestReport = "";
    [ObservableProperty] private string fullTestOverallStatus = "Aguardando";
    [ObservableProperty] private string fullTestScore = "0/0";
    [ObservableProperty] private string fullTestNetworkSummary = "Sem varredura executada.";
    [ObservableProperty] private string fullTestModbusSummary = "Sem descoberta Modbus executada.";
    [ObservableProperty] private string fullTestRouteSummary = "Sem analise de rota executada.";
    [ObservableProperty] private string fullTestBandwidthSummary = "Sem medicao de banda executada.";
    [ObservableProperty] private bool enableActiveSubnetScan = true;
    [ObservableProperty] private int activeScanTimeoutMs = 250;
    [ObservableProperty] private int activeScanConcurrency = 48;
    [ObservableProperty] private int passiveObservationSeconds = 12;

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
    public ObservableCollection<FullTestStep> FullTestSteps { get; } = [];
    public ObservableCollection<NetworkDiscoveryRow> NetworkDiscoveryRows { get; } = [];

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
        _pollingIntervalsBySignature.Clear();
        _exceptionCounts.Clear();
        _outOfMapCounts.Clear();
        LoadVerificationChecks();
        Status = "Timeline limpa.";
    }

    [RelayCommand(CanExecute = nameof(CanStartFullTest))]
    private async Task StartFullTestAsync()
    {
        IsFullTestRunning = true;
        FullTestReport = "";
        FullTestOverallStatus = "Executando";
        FullTestScore = "0/0";
        FullTestNetworkSummary = "Coletando dados...";
        FullTestModbusSummary = "Coletando dados...";
        FullTestRouteSummary = "Coletando dados...";
        FullTestBandwidthSummary = "Coletando dados...";
        FullTestSteps.Clear();
        NetworkDiscoveryRows.Clear();

        var steps = new List<(FullTestStep Step, Func<CancellationToken, Task<FullTestStepResult>> Action)>
        {
            CreateFullTestStep("Contexto do teste", "Registra alvo, modo, porta, interface, filtro e premissas de seguranca.", RunFullTestContextAsync),
            CreateFullTestStep("Interfaces e rotas IP", "Mapeia placas ativas, gateways, mascara, velocidade nominal e rotas do Windows.", RunIpRouteAnalysisAsync),
            CreateFullTestStep("Inventario passivo TCP", "Resume endpoints e protocolos vistos na captura TCP atual.", RunPassiveInventoryAsync),
            CreateFullTestStep("Tabela ARP local", "Consulta ARP do Windows para descobrir dispositivos ja resolvidos na rede.", RunArpSnapshotAsync),
            CreateFullTestStep("Varredura de hosts", "Testa hosts candidatos da sub-rede e consolida dispositivos possivelmente ativos.", RunHostDiscoveryAsync),
            CreateFullTestStep("Descoberta Modbus", "Procura servidores Modbus/TCP nos hosts descobertos e no alvo configurado.", RunModbusDiscoveryAsync),
            CreateFullTestStep("Conectividade TCP", "Testa abertura de socket TCP no alvo e porta configurados.", RunTcpConnectivityAsync),
            CreateFullTestStep("Banda e carga", "Mede contadores de interface e analisa taxa aproximada de pacotes capturados.", RunTrafficLoadAsync),
            CreateFullTestStep("Topologia inferida", "Infere gateway, possiveis switches/infraestrutura e lacunas de visibilidade.", RunTopologyInferenceAsync),
            CreateFullTestStep("Mapa Modbus", "Valida todas as linhas habilitadas do mapa configurado por leitura real.", RunClientMapValidationAsync),
            CreateFullTestStep("Envio e recebimento", "Executa uma transacao Modbus read-only para confirmar request/response.", RunSendReceiveValidationAsync),
            CreateFullTestStep("Falhas observadas", "Consolida avisos importantes e checks automaticos ja detectados.", RunObservedFailuresAsync),
            CreateFullTestStep("Conclusao", "Gera parecer final com proximas acoes de troubleshooting.", RunFullTestConclusionAsync)
        };

        foreach (var (step, action) in steps)
        {
            await ExecuteFullTestStepAsync(step, action, CancellationToken.None);
        }

        FullTestReport = BuildFullTestReport();
        UpdateFullTestSummaryCards();
        Status = "Teste completo finalizado. Relatorio gerado.";
        IsFullTestRunning = false;
    }

    [RelayCommand]
    private async Task SaveFullTestReportAsync()
    {
        if (string.IsNullOrWhiteSpace(FullTestReport))
        {
            Status = "Nenhum relatorio de teste completo gerado ainda.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Relatorio Markdown (*.md)|*.md|Texto (*.txt)|*.txt",
            FileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm}-teste-completo-modbus.md"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, FullTestReport, Encoding.UTF8);
        Status = $"Relatorio exportado: {dialog.FileName}";
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
    private bool CanStartFullTest() => !IsFullTestRunning && !IsClientScanning;

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
        StartFullTestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFullTestRunningChanged(bool value)
    {
        StartFullTestCommand.NotifyCanExecuteChanged();
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
        RefreshGeneratedCaptureFilterPreview();
    }

    partial void OnCaptureIpChanged(string value)
    {
        RefreshGeneratedCaptureFilterPreview();
    }

    partial void OnSelectedCaptureIpDirectionChanged(string value)
    {
        RefreshGeneratedCaptureFilterPreview();
    }

    partial void OnCapturePortChanged(string value)
    {
        RefreshGeneratedCaptureFilterPreview();
    }

    partial void OnSelectedCapturePortDirectionChanged(string value)
    {
        RefreshGeneratedCaptureFilterPreview();
    }

    private void RefreshGeneratedCaptureFilterPreview()
    {
        try
        {
            GeneratedCaptureFilter = BuildCaptureFilter();
        }
        catch (Exception ex)
        {
            GeneratedCaptureFilter = ex.Message;
        }
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
            "Modbus TCP" => "tcp",
            _ => "tcp or udp or arp or icmp"
        };
        parts.Add($"({protocol})");

        if (!string.IsNullOrWhiteSpace(CaptureIp))
        {
            if (!IPAddress.TryParse(CaptureIp.Trim(), out _))
            {
                throw new InvalidOperationException($"IP invalido para filtro BPF: {CaptureIp}");
            }

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
            if (!int.TryParse(CapturePort.Trim(), out var filterPort) || filterPort < 1 || filterPort > 65535)
            {
                throw new InvalidOperationException($"Porta invalida para filtro BPF: {CapturePort}");
            }

            var portClause = SelectedCapturePortDirection switch
            {
                "Somente origem" => $"src port {filterPort}",
                "Somente destino" => $"dst port {filterPort}",
                _ => $"port {filterPort}"
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

    private (FullTestStep Step, Func<CancellationToken, Task<FullTestStepResult>> Action) CreateFullTestStep(
        string name,
        string objective,
        Func<CancellationToken, Task<FullTestStepResult>> action)
    {
        var step = new FullTestStep(FullTestSteps.Count + 1, name, objective);
        FullTestSteps.Add(step);
        return (step, action);
    }

    private async Task ExecuteFullTestStepAsync(FullTestStep step, Func<CancellationToken, Task<FullTestStepResult>> action, CancellationToken cancellationToken)
    {
        SelectedFullTestStep = step;
        step.Status = "Executando";
        step.StartedAt = DateTimeOffset.Now;
        Status = $"Teste completo: {step.Name}...";

        try
        {
            var result = await action(cancellationToken);
            step.Status = result.Status;
            step.Result = result.Detail;
            step.Recommendation = result.Recommendation;
        }
        catch (Exception ex)
        {
            step.Status = "Falha";
            step.Result = ex.Message;
            step.Recommendation = "Investigue permissao, interface selecionada, IP/porta e disponibilidade do dispositivo antes de repetir o teste.";
            Log.Error(ex, "Falha na etapa de teste completo {StepName}", step.Name);
        }
        finally
        {
            step.FinishedAt = DateTimeOffset.Now;
            UpdateFullTestSummaryCards();
        }
    }

    private Task<FullTestStepResult> RunFullTestContextAsync(CancellationToken cancellationToken)
    {
        var interfaceName = SelectedCaptureDevice?.Description ?? "Nenhuma interface selecionada";
        var enabledRows = ClientMapRows.Count(x => x.Enabled);
        var serverRanges = ServerMapRanges.Count(x => x.Enabled);
        var details = string.Join(Environment.NewLine, [
            $"Modo: {SelectedMode}",
            $"Local IP: {LocalIp}",
            $"Alvo: {TargetIp}:{Port}",
            $"Unit ID: {UnitId}",
            $"Interface de captura: {interfaceName}",
            $"Filtro BPF atual: {GeneratedCaptureFilter}",
            $"Captura passiva ativa: {(IsNetworkCaptureRunning ? "sim" : "nao")}",
            $"Linhas habilitadas no mapa client: {enabledRows}",
            $"Faixas habilitadas no server simulado: {serverRanges}",
            "Seguranca: o teste completo executa apenas leituras Modbus. Escritas sao puladas por padrao para nao alterar PLC/equipamento."
        ]);

        var status = string.IsNullOrWhiteSpace(TargetIp) ? "Falha" : "OK";
        var recommendation = status == "OK"
            ? "Confirme se o IP/porta correspondem ao dispositivo que sera testado."
            : "Configure IP alvo, porta e Unit ID antes de executar o teste completo.";

        return Task.FromResult(new FullTestStepResult(status, details, recommendation));
    }

    private async Task<FullTestStepResult> RunIpRouteAnalysisAsync(CancellationToken cancellationToken)
    {
        var profiles = GetNetworkProfiles();
        var routeOutput = await RunProcessAsync("route", "print -4", cancellationToken);
        var activeProfiles = profiles.Where(x => x.IsOperational).ToList();

        var profileLines = activeProfiles.Count == 0
            ? "Nenhuma interface IPv4 operacional encontrada."
            : string.Join(Environment.NewLine, activeProfiles.Select(x =>
                $"{x.Name} | IP {x.Address}/{x.PrefixLength} | Gateway {x.Gateway} | Speed {FormatBitsPerSecond(x.SpeedBitsPerSecond)} | MAC {x.MacAddress}"));

        FullTestRouteSummary = activeProfiles.Count == 0
            ? "Sem interface IPv4 operacional."
            : string.Join(" | ", activeProfiles.Take(3).Select(x => $"{x.Address}/{x.PrefixLength} via {x.Gateway}"));

        var defaultRouteFound = routeOutput.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase);
        var status = activeProfiles.Count == 0 ? "Falha" : defaultRouteFound ? "OK" : "Atencao";
        var details = string.Join(Environment.NewLine, [
            "Interfaces operacionais:",
            profileLines,
            "",
            "Amostra de rotas IPv4:",
            string.Join(Environment.NewLine, routeOutput.Split(Environment.NewLine).Take(40))
        ]);
        var recommendation = status == "OK"
            ? "Rotas e interfaces basicas foram detectadas. Confira se a interface usada e a mesma conectada a rede industrial."
            : "Verifique IP, mascara, gateway, VLAN e se a interface industrial esta ativa antes de diagnosticar Modbus.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunPassiveInventoryAsync(CancellationToken cancellationToken)
    {
        var rows = await CollectPassiveTrafficSampleAsync(minNewPackets: 10, cancellationToken);
        if (rows.Count == 0)
        {
            return new FullTestStepResult(
                "Atencao",
                $"Nenhum pacote TCP/UDP/ARP/ICMP foi observado na timeline TCP apos {Math.Clamp(PassiveObservationSeconds, 3, 60)} s de observacao. Captura ativa: {(IsNetworkCaptureRunning ? "sim" : "nao")}. Fila pendente UI: {QueuedPassivePackets}.",
                "Inicie a captura passiva sem filtro ou com filtro amplo, confirme a interface correta/Npcap e repita o teste. Se voce ve pacotes na aba TCP, use Atualizar filtro e confira se eles nao estao apenas na visao filtrada antiga.");
        }

        var endpoints = rows
            .SelectMany(x => new[] { ExtractHost(x.Source), ExtractHost(x.Destination) })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        foreach (var endpoint in endpoints)
        {
            UpsertDiscovery(endpoint, "", "Captura TCP", GuessRole(endpoint), "", "Host observado passivamente na timeline TCP.");
        }

        var protocols = rows
            .GroupBy(x => x.Protocol)
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key}: {x.Count()}")
            .Take(8);

        var modbusRows = rows.Count(x => x.Protocol.Contains("Modbus", StringComparison.OrdinalIgnoreCase));
        var details = string.Join(Environment.NewLine, [
            $"Pacotes observados: {rows.Count}",
            $"Endpoints unicos: {endpoints.Count}",
            $"Endpoints: {string.Join(", ", endpoints.Take(25))}{(endpoints.Count > 25 ? " ..." : "")}",
            $"Protocolos: {string.Join("; ", protocols)}",
            $"Pacotes identificados como Modbus/TCP: {modbusRows}",
            "Switches: identificacao direta exige SNMP/LLDP/porta espelhada documentada. Nesta etapa o sistema infere apenas hosts vistos em trafego/ARP."
        ]);

        var status = endpoints.Count < 2 ? "Atencao" : "OK";
        var recommendation = status == "OK"
            ? "Compare a lista de endpoints com a topologia esperada da celula/linha."
            : "A captura tem pouca amostra. Verifique porta espelhada/SPAN, interface correta e filtros de captura.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunArpSnapshotAsync(CancellationToken cancellationToken)
    {
        var output = await RunProcessAsync("arp", "-a", cancellationToken);
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var arpEntries = ParseArpEntries(output);

        foreach (var entry in arpEntries)
        {
            UpsertDiscovery(entry.Ip, entry.Mac, "ARP", GuessRole(entry.Ip), "", $"Entrada ARP {entry.Type}.");
        }

        var entries = arpEntries.Count;
        var targetSeen = arpEntries.Any(x => x.Ip.Equals(TargetIp, StringComparison.OrdinalIgnoreCase));
        var status = entries == 0 ? "Atencao" : targetSeen || TargetIp is "127.0.0.1" or "localhost" ? "OK" : "Atencao";

        var details = string.Join(Environment.NewLine, [
            $"Entradas ARP encontradas: {entries}",
            $"Alvo aparece na tabela ARP: {(targetSeen ? "sim" : "nao")}",
            "Amostra ARP:",
            string.Join(Environment.NewLine, lines.Take(20))
        ]);
        var recommendation = status == "OK"
            ? "ARP local encontrou entradas; valide se MAC/vendor batem com o inventario industrial."
            : "Se o alvo deveria estar no mesmo segmento, gere comunicacao/ping permitido ou revise VLAN, gateway, mascara e porta do switch.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunHostDiscoveryAsync(CancellationToken cancellationToken)
    {
        var candidates = BuildDiscoveryCandidates();
        if (candidates.Count == 0)
        {
            FullTestNetworkSummary = "Nenhum host candidato.";
            return new FullTestStepResult("Atencao", "Nenhum host candidato foi encontrado por ARP, captura passiva, alvo configurado ou sub-rede local.", "Configure o IP alvo e/ou inicie uma captura passiva antes da varredura.");
        }

        var timeoutMs = Math.Clamp(ActiveScanTimeoutMs, 100, 3000);
        var concurrency = Math.Clamp(ActiveScanConcurrency, 4, 128);
        var semaphore = new SemaphoreSlim(concurrency);
        var results = new ConcurrentBag<HostProbeResult>();

        var tasks = candidates.Select(async ip =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var pingOk = await TryPingAsync(ip, timeoutMs);
                var configuredPortOpen = await TryTcpConnectAsync(ip, Port, timeoutMs, cancellationToken);
                var modbusPortOpen = Port == 502 ? configuredPortOpen : await TryTcpConnectAsync(ip, 502, timeoutMs, cancellationToken);
                results.Add(new HostProbeResult(ip, pingOk, configuredPortOpen, modbusPortOpen));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var active = results
            .Where(x => x.PingOk || x.ConfiguredPortOpen || x.ModbusPortOpen)
            .OrderBy(x => IpSortKey(x.Ip))
            .ToList();

        foreach (var result in active)
        {
            var openPorts = new List<string>();
            if (result.ConfiguredPortOpen)
            {
                openPorts.Add(Port.ToString());
            }
            if (result.ModbusPortOpen && Port != 502)
            {
                openPorts.Add("502");
            }

            UpsertDiscovery(
                result.Ip,
                "",
                "Varredura ativa",
                GuessRole(result.Ip),
                string.Join(", ", openPorts.Distinct()),
                $"Ping: {(result.PingOk ? "OK" : "sem resposta")}; porta configurada {Port}: {(result.ConfiguredPortOpen ? "aberta" : "fechada/filtrada")}; porta 502: {(result.ModbusPortOpen ? "aberta" : "fechada/filtrada")}.");
        }

        FullTestNetworkSummary = $"{active.Count}/{candidates.Count} hosts candidatos responderam; {active.Count(x => x.ModbusPortOpen || x.ConfiguredPortOpen)} com porta Modbus/configurada aberta.";

        var status = active.Count == 0 ? "Atencao" : "OK";
        var details = string.Join(Environment.NewLine, [
            $"Candidatos testados: {candidates.Count}",
            $"Timeout por tentativa: {timeoutMs} ms",
            $"Concorrencia: {concurrency}",
            $"Hosts ativos/provaveis: {active.Count}",
            active.Count == 0 ? "Nenhum host respondeu a ping ou TCP." : string.Join(Environment.NewLine, active.Take(80).Select(x => $"{x.Ip} | ping={x.PingOk} | port {Port}={x.ConfiguredPortOpen} | port 502={x.ModbusPortOpen}"))
        ]);
        var recommendation = status == "OK"
            ? "Compare hosts encontrados com a topologia esperada; qualquer IP desconhecido em rede industrial deve ser investigado."
            : "Se a rede bloqueia ICMP/TCP probe, use captura passiva com porta espelhada e ARP para inventario.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunModbusDiscoveryAsync(CancellationToken cancellationToken)
    {
        var hosts = BuildDiscoveryCandidates()
            .Concat(NetworkDiscoveryRows.Select(x => x.Ip))
            .Where(IsIPv4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToList();

        if (!hosts.Contains(TargetIp) && IsIPv4(TargetIp))
        {
            hosts.Insert(0, TargetIp);
        }

        var ports = new[] { Port, 502 }.Distinct().Where(x => x > 0 && x <= 65535).ToList();
        var modbusCandidates = new ConcurrentBag<(string Ip, int Port, bool Open)>();
        var timeoutMs = Math.Clamp(ActiveScanTimeoutMs, 100, 3000);
        var semaphore = new SemaphoreSlim(Math.Clamp(ActiveScanConcurrency, 4, 128));

        var tasks = hosts.SelectMany(ip => ports.Select(async port =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var open = await TryTcpConnectAsync(ip, port, timeoutMs, cancellationToken);
                modbusCandidates.Add((ip, port, open));
            }
            finally
            {
                semaphore.Release();
            }
        }));

        await Task.WhenAll(tasks);

        var openEndpoints = modbusCandidates
            .Where(x => x.Open)
            .OrderBy(x => IpSortKey(x.Ip))
            .ThenBy(x => x.Port)
            .ToList();

        foreach (var endpoint in openEndpoints)
        {
            UpsertDiscovery(endpoint.Ip, "", "Descoberta Modbus", endpoint.Port == 502 ? "Possivel server Modbus" : "Porta TCP configurada aberta", endpoint.Port.ToString(), $"Porta TCP {endpoint.Port} aceitou conexao.");
        }

        var passiveModbusHosts = TcpTimeline
            .Where(x => x.Protocol.Contains("Modbus", StringComparison.OrdinalIgnoreCase) || x.Info.Contains("Modbus", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => new[] { ExtractHost(x.Source), ExtractHost(x.Destination) })
            .Where(IsIPv4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        FullTestModbusSummary = $"{openEndpoints.Count} endpoint(s) TCP Modbus/configurado aberto(s); {passiveModbusHosts.Count} host(s) Modbus vistos passivamente.";

        var status = openEndpoints.Count > 0 || passiveModbusHosts.Count > 0 ? "OK" : "Atencao";
        var details = string.Join(Environment.NewLine, [
            $"Hosts avaliados: {hosts.Count}",
            $"Portas avaliadas: {string.Join(", ", ports)}",
            $"Endpoints com porta aberta: {openEndpoints.Count}",
            openEndpoints.Count == 0 ? "Nenhuma porta Modbus/configurada aberta encontrada." : string.Join(Environment.NewLine, openEndpoints.Take(80).Select(x => $"{x.Ip}:{x.Port} aberto")),
            $"Hosts Modbus na captura passiva: {(passiveModbusHosts.Count == 0 ? "nenhum" : string.Join(", ", passiveModbusHosts))}"
        ]);
        var recommendation = status == "OK"
            ? "Valide se todos os servidores Modbus encontrados pertencem ao inventario esperado da rede."
            : "Se deveria haver Modbus na rede, revise filtros de captura, porta usada pelo equipamento, firewall e segmentacao/VLAN.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunTcpConnectivityAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TargetIp))
        {
            return new FullTestStepResult("Falha", "IP alvo vazio.", "Configure o IP do PLC/server Modbus antes do teste.");
        }

        var started = Stopwatch.StartNew();
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        await client.ConnectAsync(TargetIp, Port, timeout.Token);
        started.Stop();

        return new FullTestStepResult(
            "OK",
            $"Socket TCP abriu em {started.ElapsedMilliseconds} ms para {TargetIp}:{Port}.",
            "Conectividade TCP basica OK. Se Modbus falhar, investigue Unit ID, mapa, function code, gateway ou resposta de aplicacao.");
    }

    private async Task<FullTestStepResult> RunTrafficLoadAsync(CancellationToken cancellationToken)
    {
        var before = TakeInterfaceSnapshots();
        var rows = await CollectPassiveTrafficSampleAsync(minNewPackets: 20, cancellationToken);
        var after = TakeInterfaceSnapshots();
        var bandwidthLines = new List<string>();
        var observationSeconds = Math.Clamp(PassiveObservationSeconds, 3, 60);

        foreach (var end in after)
        {
            if (!before.TryGetValue(end.Key, out var start))
            {
                continue;
            }

            var rxBytesPerSecond = Math.Max(0, end.Value.BytesReceived - start.BytesReceived) / (double)observationSeconds;
            var txBytesPerSecond = Math.Max(0, end.Value.BytesSent - start.BytesSent) / (double)observationSeconds;
            bandwidthLines.Add($"{end.Value.Name}: RX {FormatBytesPerSecond(rxBytesPerSecond)}, TX {FormatBytesPerSecond(txBytesPerSecond)}, nominal {FormatBitsPerSecond(end.Value.SpeedBitsPerSecond)}");
        }

        if (rows.Count < 2)
        {
            FullTestBandwidthSummary = bandwidthLines.Count == 0 ? "Sem contadores de interface." : string.Join(" | ", bandwidthLines.Take(3));
            return new FullTestStepResult(
                "Atencao",
                string.Join(Environment.NewLine, [
                    $"Amostra TCP insuficiente para carga por protocolo. Pacotes: {rows.Count}. Fila pendente UI: {QueuedPassivePackets}.",
                    $"Janela de observacao: {observationSeconds} s. Captura ativa: {(IsNetworkCaptureRunning ? "sim" : "nao")}.",
                    "Contadores de interface:",
                    bandwidthLines.Count == 0 ? "Nenhum contador coletado." : string.Join(Environment.NewLine, bandwidthLines)
                ]),
                "Aumente a janela de observacao, confirme a interface de captura e mantenha trafego ativo durante o teste para estimar carga por protocolo.");
        }

        var minTime = rows.Min(x => x.RelativeTime);
        var maxTime = rows.Max(x => x.RelativeTime);
        var span = Math.Max(0.001, maxTime - minTime);
        var packetsPerSecond = rows.Count / span;
        var bytesPerSecond = rows.Sum(x => x.Length) / span;
        var topTalkers = rows
            .GroupBy(x => $"{ExtractHost(x.Source)} -> {ExtractHost(x.Destination)}")
            .OrderByDescending(x => x.Count())
            .Take(5)
            .Select(x => $"{x.Key}: {x.Count()} pkt");

        var status = QueuedPassivePackets > 5000 || packetsPerSecond > 5000 ? "Atencao" : "OK";
        FullTestBandwidthSummary = $"{packetsPerSecond:0.0} pkt/s capturados | {(bandwidthLines.Count == 0 ? "sem contador de interface" : bandwidthLines[0])}";
        var details = string.Join(Environment.NewLine, [
            $"Contadores de interface em janela de {observationSeconds}s:",
            bandwidthLines.Count == 0 ? "Nenhum contador coletado." : string.Join(Environment.NewLine, bandwidthLines),
            "",
            $"Janela analisada: {span:0.0} s",
            $"Pacotes analisados: {rows.Count}",
            $"Taxa aproximada: {packetsPerSecond:0.0} pkt/s, {bytesPerSecond / 1024:0.0} KB/s",
            $"Fila pendente na UI: {QueuedPassivePackets}",
            $"Top conversas: {string.Join("; ", topTalkers)}"
        ]);
        var recommendation = status == "OK"
            ? "Carga observada parece tratavel pela UI; compare com baseline da rede industrial."
            : "Ha sinal de alto volume ou backlog. Filtre por VLAN/IP/porta, valide broadcast storm, multicast excessivo ou porta espelhada muito ampla.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private Task<FullTestStepResult> RunTopologyInferenceAsync(CancellationToken cancellationToken)
    {
        var profiles = GetNetworkProfiles().Where(x => x.IsOperational).ToList();
        var gateways = profiles.Select(x => x.Gateway).Where(IsIPv4).Distinct().ToList();
        var knownHosts = NetworkDiscoveryRows.ToList();
        var modbusHosts = knownHosts.Where(x => x.ModbusStatus.Contains("aberta", StringComparison.OrdinalIgnoreCase)
            || x.RoleGuess.Contains("Modbus", StringComparison.OrdinalIgnoreCase)).ToList();
        var passiveOnly = knownHosts.Where(x => x.Source.Contains("Captura", StringComparison.OrdinalIgnoreCase)).ToList();
        var gatewayRows = knownHosts.Where(x => gateways.Contains(x.Ip)).ToList();

        foreach (var gateway in gateways)
        {
            UpsertDiscovery(gateway, "", "Rota IP", "Gateway / possivel roteador industrial", "", "Gateway padrao detectado nas interfaces IPv4.");
        }

        var possibleInfrastructure = knownHosts
            .Where(x => gateways.Contains(x.Ip)
                || x.Notes.Contains("ARP", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(x.ModbusStatus))
            .Take(20)
            .ToList();

        var details = string.Join(Environment.NewLine, [
            $"Gateways detectados: {(gateways.Count == 0 ? "nenhum" : string.Join(", ", gateways))}",
            $"Dispositivos conhecidos na tabela visual: {knownHosts.Count}",
            $"Possiveis servidores Modbus: {modbusHosts.Count}",
            $"Hosts vistos apenas passivamente: {passiveOnly.Count}",
            $"Gateways tambem vistos em ARP/captura: {gatewayRows.Count}",
            "Possivel infraestrutura:",
            possibleInfrastructure.Count == 0 ? "Sem candidatos claros." : string.Join(Environment.NewLine, possibleInfrastructure.Select(x => $"{x.Ip} | {x.RoleGuess} | {x.Source} | {x.Notes}")),
            "",
            "Limite tecnico: switch L2 puro normalmente nao aparece como hop IP. Para identificar switch, porta fisica, VLAN e fabricante com confianca, o proximo passo e SNMP/LLDP/CDP ou leitura do switch gerenciavel."
        ]);

        var status = gateways.Count == 0 ? "Atencao" : "OK";
        var recommendation = status == "OK"
            ? "Use gateway + ARP + captura passiva como mapa inicial. Para topologia fisica real, adicionar SNMP/LLDP e cadastro de switches."
            : "Sem gateway claro. Verifique se a maquina esta na VLAN industrial correta ou se a rede e isolada sem roteamento.";

        return Task.FromResult(new FullTestStepResult(status, details, recommendation));
    }

    private async Task<FullTestStepResult> RunClientMapValidationAsync(CancellationToken cancellationToken)
    {
        var rows = ClientMapRows.Where(x => x.Enabled).ToList();
        if (rows.Count == 0)
        {
            return new FullTestStepResult("Atencao", "Nenhuma linha habilitada no mapa client.", "Configure o mapa real do PLC/equipamento antes de validar ranges.");
        }

        var ok = 0;
        var failures = new List<string>();

        foreach (var row in rows)
        {
            try
            {
                if (row.FunctionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs)
                {
                    var values = await _client.ReadBitsAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
                    row.LastValue = string.Join(", ", values.Select(x => x ? "1" : "0"));
                }
                else
                {
                    var values = await _client.ReadRegistersAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
                    row.LastValue = string.Join(", ", values);
                }

                row.LastStatus = "OK";
                row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
                ok++;
            }
            catch (Exception ex)
            {
                row.LastStatus = ex.Message;
                row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
                failures.Add($"{row.Name} FC{row.FunctionCode} addr={row.StartAddress} qty={row.Quantity}: {ex.Message}");
            }
        }

        var status = failures.Count == 0 ? "OK" : ok > 0 ? "Atencao" : "Falha";
        var details = string.Join(Environment.NewLine, [
            $"Linhas testadas: {rows.Count}",
            $"OK: {ok}",
            $"Falhas: {failures.Count}",
            failures.Count == 0 ? "Todas as linhas habilitadas responderam." : string.Join(Environment.NewLine, failures.Take(20))
        ]);
        var recommendation = status == "OK"
            ? "Mapa configurado respondeu com sucesso. Mantenha esse mapa como referencia do caso."
            : "Revise function code, endereco base zero/um, quantidade, Unit ID e ranges realmente publicados pelo equipamento.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunSendReceiveValidationAsync(CancellationToken cancellationToken)
    {
        var row = ClientMapRows.FirstOrDefault(x => x.Enabled) ?? new ClientMapRow
        {
            Name = "Teste minimo FC03",
            Function = "FC03 Holding Registers",
            StartAddress = 0,
            Quantity = 1,
            Enabled = true
        };

        if (row.FunctionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs)
        {
            var values = await _client.ReadBitsAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
            return new FullTestStepResult(
                "OK",
                $"Request/response read-only OK em {TargetIp}:{Port} UID {UnitId}, FC{row.FunctionCode}, addr {row.StartAddress}, qty {row.Quantity}. Valores: {string.Join(", ", values.Select(x => x ? "1" : "0"))}",
                "Envio e recebimento Modbus confirmados sem escrita. Para teste de escrita, configure futuramente um ponto seguro de teste.");
        }

        var registers = await _client.ReadRegistersAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
        return new FullTestStepResult(
            "OK",
            $"Request/response read-only OK em {TargetIp}:{Port} UID {UnitId}, FC{row.FunctionCode}, addr {row.StartAddress}, qty {row.Quantity}. Valores: {string.Join(", ", registers)}",
            "Envio e recebimento Modbus confirmados sem escrita. Para teste de escrita, configure futuramente um ponto seguro de teste.");
    }

    private Task<FullTestStepResult> RunObservedFailuresAsync(CancellationToken cancellationToken)
    {
        var warnings = ImportantWarnings.ToList();
        var badChecks = VerificationChecks
            .Where(x => x.Status is "Falha" or "Atencao" or "Erro")
            .ToList();

        var status = warnings.Any(x => x.Severity is "Falha" or "Erro") || badChecks.Any(x => x.Status is "Falha" or "Erro")
            ? "Falha"
            : warnings.Count > 0 || badChecks.Count > 0 ? "Atencao" : "OK";

        var details = string.Join(Environment.NewLine, [
            $"Avisos importantes consolidados: {warnings.Count}",
            warnings.Count == 0 ? "Sem avisos importantes." : string.Join(Environment.NewLine, warnings.Take(12).Select(x => $"{x.Severity} x{x.Count}: {x.Title} - {x.LatestDetail}")),
            $"Checks com atencao/falha: {badChecks.Count}",
            badChecks.Count == 0 ? "Checklist automatico sem falhas atuais." : string.Join(Environment.NewLine, badChecks.Select(x => $"{x.Name}: {x.Status} - {x.Detail}"))
        ]);
        var recommendation = status == "OK"
            ? "Nenhuma falha consolidada foi detectada nesta janela de teste."
            : "Priorize itens com Falha/Erro, depois valide polling rapido, exceptions Modbus e acessos fora de mapa.";

        return Task.FromResult(new FullTestStepResult(status, details, recommendation));
    }

    private Task<FullTestStepResult> RunFullTestConclusionAsync(CancellationToken cancellationToken)
    {
        var completed = FullTestSteps.Where(x => x.Name != "Conclusao").ToList();
        var failures = completed.Where(x => x.Status is "Falha" or "Erro").ToList();
        var warnings = completed.Where(x => x.Status == "Atencao").ToList();
        var status = failures.Count > 0 ? "Falha" : warnings.Count > 0 ? "Atencao" : "OK";

        var details = string.Join(Environment.NewLine, [
            $"Etapas OK: {completed.Count(x => x.Status == "OK")}",
            $"Etapas com atencao: {warnings.Count}",
            $"Etapas com falha: {failures.Count}",
            failures.Count == 0 ? "Sem falhas criticas no teste completo." : $"Falhas: {string.Join(", ", failures.Select(x => x.Name))}",
            warnings.Count == 0 ? "Sem alertas adicionais." : $"Atencoes: {string.Join(", ", warnings.Select(x => x.Name))}"
        ]);
        var recommendation = status == "OK"
            ? "Use o relatorio como baseline. Para troubleshooting prolongado, deixe a captura ativa e reexecute o teste apos a falha ocorrer."
            : "Corrija primeiro conectividade TCP/ARP e mapa Modbus; depois repita o teste com captura passiva ativa para confirmar estabilidade.";

        return Task.FromResult(new FullTestStepResult(status, details, recommendation));
    }

    private string BuildFullTestReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Relatorio - Teste completo Modbus TCP");
        builder.AppendLine();
        builder.AppendLine($"- Caso: {CaseName}");
        builder.AppendLine($"- Gerado em: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"- Modo: {SelectedMode}");
        builder.AppendLine($"- Alvo: {TargetIp}:{Port}");
        builder.AppendLine($"- Unit ID: {UnitId}");
        builder.AppendLine($"- Interface captura: {SelectedCaptureDevice?.Description ?? "Nao selecionada"}");
        builder.AppendLine($"- Filtro BPF: {GeneratedCaptureFilter}");
        builder.AppendLine($"- Status geral: {FullTestOverallStatus}");
        builder.AppendLine($"- Score: {FullTestScore}");
        builder.AppendLine($"- Rede: {FullTestNetworkSummary}");
        builder.AppendLine($"- Modbus: {FullTestModbusSummary}");
        builder.AppendLine($"- Rotas: {FullTestRouteSummary}");
        builder.AppendLine($"- Banda: {FullTestBandwidthSummary}");
        builder.AppendLine();

        foreach (var step in FullTestSteps)
        {
            builder.AppendLine($"## {step.Order}. {step.Name} - {step.Status}");
            builder.AppendLine();
            builder.AppendLine(step.Objective);
            builder.AppendLine();
            builder.AppendLine("Resultado:");
            builder.AppendLine("```text");
            builder.AppendLine(step.Result);
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine($"Recomendacao: {step.Recommendation}");
            builder.AppendLine();
        }

        builder.AppendLine("## Dispositivos / rede");
        builder.AppendLine();
        if (NetworkDiscoveryRows.Count == 0)
        {
            builder.AppendLine("Nenhum dispositivo consolidado.");
        }
        else
        {
            builder.AppendLine("| IP | MAC | Fonte | Papel provavel | Portas/Modbus | Notas |");
            builder.AppendLine("|---|---|---|---|---|---|");
            foreach (var row in NetworkDiscoveryRows.OrderBy(x => IpSortKey(x.Ip)))
            {
                builder.AppendLine($"| {row.Ip} | {row.Mac} | {EscapeMarkdownTable(row.Source)} | {EscapeMarkdownTable(row.RoleGuess)} | {EscapeMarkdownTable(row.ModbusStatus)} | {EscapeMarkdownTable(row.Notes)} |");
            }
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownTable(string value)
    {
        return value.Replace("|", "\\|").Replace(Environment.NewLine, " ");
    }

    private void UpdateFullTestSummaryCards()
    {
        var completed = FullTestSteps.ToList();
        var ok = completed.Count(x => x.Status == "OK");
        var warnings = completed.Count(x => x.Status == "Atencao");
        var failures = completed.Count(x => x.Status is "Falha" or "Erro");

        FullTestScore = $"{ok}/{completed.Count} OK";
        FullTestOverallStatus = failures > 0 ? "Falha" : warnings > 0 ? "Atencao" : "OK";
    }

    private async Task<List<TcpTimelineRow>> CollectPassiveTrafficSampleAsync(int minNewPackets, CancellationToken cancellationToken)
    {
        FlushPassivePackets();
        var initialCount = TcpTimeline.Count;
        var observationSeconds = Math.Clamp(PassiveObservationSeconds, 3, 60);
        var deadline = DateTimeOffset.Now.AddSeconds(observationSeconds);

        while (DateTimeOffset.Now < deadline && TcpTimeline.Count - initialCount < minNewPackets)
        {
            FlushPassivePackets();
            await Task.Delay(500, cancellationToken);
        }

        FlushPassivePackets();
        return TcpTimeline.ToList();
    }

    private List<string> BuildDiscoveryCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsIPv4(TargetIp))
        {
            candidates.Add(TargetIp);
        }

        foreach (var row in TcpTimeline)
        {
            var source = ExtractHost(row.Source);
            var destination = ExtractHost(row.Destination);
            if (IsIPv4(source))
            {
                candidates.Add(source);
            }
            if (IsIPv4(destination))
            {
                candidates.Add(destination);
            }
        }

        foreach (var row in NetworkDiscoveryRows)
        {
            if (IsIPv4(row.Ip))
            {
                candidates.Add(row.Ip);
            }
        }

        if (EnableActiveSubnetScan)
        {
            foreach (var profile in GetNetworkProfiles().Where(x => x.IsOperational))
            {
                foreach (var ip in EnumerateSubnetHosts(profile.Address, profile.PrefixLength, 512))
                {
                    candidates.Add(ip);
                }
            }
        }

        return candidates
            .Where(IsIPv4)
            .OrderBy(IpSortKey)
            .Take(512)
            .ToList();
    }

    private void UpsertDiscovery(string ip, string mac, string source, string roleGuess, string modbusStatus, string notes)
    {
        if (!IsIPv4(ip))
        {
            return;
        }

        var row = NetworkDiscoveryRows.FirstOrDefault(x => x.Ip.Equals(ip, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            NetworkDiscoveryRows.Add(new NetworkDiscoveryRow
            {
                Ip = ip,
                Mac = mac,
                Source = source,
                RoleGuess = roleGuess,
                ModbusStatus = modbusStatus,
                Notes = notes
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(mac))
        {
            row.Mac = mac;
        }
        row.Source = MergeText(row.Source, source);
        row.RoleGuess = PreferLonger(row.RoleGuess, roleGuess);
        row.ModbusStatus = MergeText(row.ModbusStatus, modbusStatus);
        row.Notes = MergeText(row.Notes, notes);
    }

    private static string MergeText(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }
        if (string.IsNullOrWhiteSpace(current))
        {
            return next;
        }
        return current.Contains(next, StringComparison.OrdinalIgnoreCase) ? current : $"{current}; {next}";
    }

    private static string PreferLonger(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }
        return string.IsNullOrWhiteSpace(current) || next.Length > current.Length ? next : current;
    }

    private List<NetworkInterfaceProfile> GetNetworkProfiles()
    {
        var profiles = new List<NetworkInterfaceProfile>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = networkInterface.GetIPProperties();
            var unicast = properties.UnicastAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x.Address));

            if (unicast is null)
            {
                continue;
            }

            var gateway = properties.GatewayAddresses
                .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";

            profiles.Add(new NetworkInterfaceProfile(
                networkInterface.Name,
                networkInterface.Description,
                unicast.Address.ToString(),
                PrefixLengthFromMask(unicast.IPv4Mask),
                gateway,
                networkInterface.GetPhysicalAddress().ToString(),
                networkInterface.Speed,
                networkInterface.OperationalStatus == OperationalStatus.Up));
        }

        return profiles;
    }

    private Dictionary<string, InterfaceTrafficSnapshot> TakeInterfaceSnapshots()
    {
        var snapshots = new Dictionary<string, InterfaceTrafficSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            try
            {
                var stats = networkInterface.GetIPv4Statistics();
                snapshots[networkInterface.Id] = new InterfaceTrafficSnapshot(
                    networkInterface.Name,
                    stats.BytesReceived,
                    stats.BytesSent,
                    networkInterface.Speed);
            }
            catch
            {
                // Some virtual adapters throw while counters are being queried.
            }
        }

        return snapshots;
    }

    private static List<ArpEntry> ParseArpEntries(string output)
    {
        var entries = new List<ArpEntry>();
        var regex = new Regex(@"(?<ip>(?:\d{1,3}\.){3}\d{1,3})\s+(?<mac>[0-9a-fA-F:-]{17})\s+(?<type>\S+)", RegexOptions.Compiled);

        foreach (Match match in regex.Matches(output))
        {
            var ip = match.Groups["ip"].Value;
            if (!IsIPv4(ip))
            {
                continue;
            }

            entries.Add(new ArpEntry(ip, match.Groups["mac"].Value, match.Groups["type"].Value));
        }

        return entries;
    }

    private static IEnumerable<string> EnumerateSubnetHosts(string address, int prefixLength, int maxHosts)
    {
        if (!IsIPv4(address) || prefixLength < 16 || prefixLength > 30)
        {
            yield break;
        }

        var ip = IpToUInt32(address);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        var network = ip & mask;
        var broadcast = network | ~mask;
        var count = Math.Min(maxHosts, Math.Max(0, (int)Math.Min(uint.MaxValue, broadcast - network - 1)));

        for (var i = 1u; i <= count; i++)
        {
            yield return UInt32ToIp(network + i);
        }
    }

    private static async Task<bool> TryTcpConnectAsync(string ip, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
            await client.ConnectAsync(ip, port, timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryPingAsync(string ip, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static string GuessRole(string ip)
    {
        if (!IsIPv4(ip))
        {
            return "";
        }

        return ip switch
        {
            "127.0.0.1" => "Loopback/local",
            _ => "Host industrial ou infraestrutura"
        };
    }

    private static bool IsIPv4(string value)
    {
        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static int PrefixLengthFromMask(IPAddress? mask)
    {
        if (mask is null)
        {
            return 24;
        }

        return mask.GetAddressBytes().Sum(b => Convert.ToString(b, 2).Count(bit => bit == '1'));
    }

    private static uint IpSortKey(string ip)
    {
        return IsIPv4(ip) ? IpToUInt32(ip) : uint.MaxValue;
    }

    private static uint IpToUInt32(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static string UInt32ToIp(uint value)
    {
        return $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
    }

    private static string FormatBytesPerSecond(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024 / 1024:0.00} MB/s";
        }
        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0.00} KB/s";
        }
        return $"{bytesPerSecond:0} B/s";
    }

    private static string FormatBitsPerSecond(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "desconhecida";
        }
        if (bitsPerSecond >= 1_000_000_000)
        {
            return $"{bitsPerSecond / 1_000_000_000.0:0.0} Gbps";
        }
        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000.0:0.0} Mbps";
        }
        return $"{bitsPerSecond / 1_000.0:0.0} Kbps";
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        return string.IsNullOrWhiteSpace(error) ? output : $"{output}{Environment.NewLine}{error}";
    }

    private static string ExtractHost(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "";
        }

        var value = endpoint.Trim();
        var lastColon = value.LastIndexOf(':');
        if (lastColon > 0 && lastColon < value.Length - 1 && int.TryParse(value[(lastColon + 1)..], out _))
        {
            return value[..lastColon];
        }

        return value;
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
                var intervals = GetPollingIntervals(signature);
                if (intervalMs >= 10)
                {
                    intervals.Enqueue(intervalMs);
                    while (intervals.Count > 12)
                    {
                        intervals.Dequeue();
                    }
                }

                if (intervals.Count < 3)
                {
                    SetCheck(
                        "Padrao de polling",
                        "Aguardando",
                        $"Coletando amostras para FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}. Ultimo intervalo bruto: {intervalMs:0} ms; amostras validas: {intervals.Count}/3.");
                }
                else
                {
                    var medianMs = Median(intervals);
                    var status = medianMs < 100 ? "Atencao" : "OK";
                    SetCheck("Padrao de polling", status, $"FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress} qty {trafficEvent.Quantity}: mediana ~{medianMs:0} ms em {intervals.Count} amostras. Ultimo intervalo bruto: {intervalMs:0} ms.");

                    if (medianMs < 100)
                    {
                        UpsertImportantWarning(
                            "polling-rapido",
                            "Atencao",
                            "Polling muito rapido observado",
                            $"Mediana de requisicoes abaixo de 100 ms para FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}: {medianMs:0} ms em {intervals.Count} amostras.",
                            "Verifique se a taxa de scan do cliente/PLC esta adequada para o equipamento e para a rede.",
                            trafficEvent.Timestamp);
                    }
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

    private Queue<double> GetPollingIntervals(string signature)
    {
        if (!_pollingIntervalsBySignature.TryGetValue(signature, out var intervals))
        {
            intervals = new Queue<double>();
            _pollingIntervalsBySignature[signature] = intervals;
        }

        return intervals;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(x => x).ToList();
        if (ordered.Count == 0)
        {
            return 0;
        }

        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
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

public sealed partial class FullTestStep : ObservableObject
{
    public FullTestStep(int order, string name, string objective)
    {
        Order = order;
        Name = name;
        Objective = objective;
    }

    public int Order { get; }
    public string Name { get; }
    public string Objective { get; }
    [ObservableProperty] private string status = "Pendente";
    [ObservableProperty] private string statusColor = "#9AA39C";
    [ObservableProperty] private string result = "";
    [ObservableProperty] private string recommendation = "";
    [ObservableProperty] private DateTimeOffset? startedAt;
    [ObservableProperty] private DateTimeOffset? finishedAt;

    partial void OnStatusChanged(string value)
    {
        StatusColor = value switch
        {
            "OK" => "#2E7D32",
            "Atencao" => "#C58A00",
            "Falha" or "Erro" => "#B3261E",
            "Executando" => "#1E6BD6",
            _ => "#9AA39C"
        };
    }
}

public sealed record FullTestStepResult(string Status, string Detail, string Recommendation);

public sealed partial class NetworkDiscoveryRow : ObservableObject
{
    [ObservableProperty] private string ip = "";
    [ObservableProperty] private string mac = "";
    [ObservableProperty] private string source = "";
    [ObservableProperty] private string roleGuess = "";
    [ObservableProperty] private string modbusStatus = "";
    [ObservableProperty] private string notes = "";
}

internal sealed record NetworkInterfaceProfile(
    string Name,
    string Description,
    string Address,
    int PrefixLength,
    string Gateway,
    string MacAddress,
    long SpeedBitsPerSecond,
    bool IsOperational);

internal sealed record InterfaceTrafficSnapshot(string Name, long BytesReceived, long BytesSent, long SpeedBitsPerSecond);

internal sealed record ArpEntry(string Ip, string Mac, string Type);

internal sealed record HostProbeResult(string Ip, bool PingOk, bool ConfiguredPortOpen, bool ModbusPortOpen);

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
