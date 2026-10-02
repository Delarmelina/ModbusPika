using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
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
    private ModbusTcpClientProbe _client => SelectedClientSession.Client;
    private readonly NetworkCaptureService _networkCapture = new();
    private readonly ConcurrentQueue<TcpTimelineRow> _passivePacketQueue = new();
    private readonly DispatcherTimer _passivePacketFlushTimer;
    private ModbusTcpServer? _server;
    private CancellationTokenSource? _serverCts;
    private CancellationTokenSource? _fullTestCts;
    private DateTimeOffset _fullTestStartedAt;
    private long _droppedPassivePackets;
    private long _reportedDroppedPassivePackets;
    private bool _captureStartedForData;
    private readonly Dictionary<string, DateTimeOffset> _lastRequestBySignature = [];
    private readonly Dictionary<string, Queue<double>> _pollingIntervalsBySignature = [];
    private readonly Dictionary<string, int> _exceptionCounts = [];
    private readonly Dictionary<string, int> _outOfMapCounts = [];
    private readonly List<string> _fullTestStartupActions = [];
    private int _nextClientNumber = 1;
    private ClientConnectionSession[] _fullTestClients = [];
    private readonly Dictionary<string, ImportantWarningSummary> _runWarnings = [];
    private readonly HashSet<string> _fullTestSessionIds = [];

    [ObservableProperty] private string caseName = "Troubleshoot Modbus TCP";
    [ObservableProperty] private string selectedMode = "Client";
    [ObservableProperty] private string localIp = "0.0.0.0";
    [ObservableProperty] private ClientConnectionSession selectedClientSession = new();
    [ObservableProperty] private int serverPort = 1502;
    [ObservableProperty] private byte serverUnitId = 1;
    [ObservableProperty] private string serverConnectionStatus = "Parado";
    [ObservableProperty] private string status = "Pronto.";
    [ObservableProperty] private bool isServerRunning;
    [ObservableProperty] private bool isNetworkCaptureRunning;
    [ObservableProperty] private bool hasCompletedNetworkCapture;
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
    private string _fullTestDetailedReport = "";
    [ObservableProperty] private string fullTestOverallStatus = "Aguardando";
    [ObservableProperty] private string fullTestScore = "0/0";
    [ObservableProperty] private int fullTestCompletedSteps;
    [ObservableProperty] private int fullTestTotalSteps;
    [ObservableProperty] private int fullTestProgressPercent;
    [ObservableProperty] private string fullTestProgressLabel = "Aguardando início";
    [ObservableProperty] private int fullTestOkCount;
    [ObservableProperty] private int fullTestWarningCount;
    [ObservableProperty] private int fullTestFailureCount;
    [ObservableProperty] private string fullTestStartedAtText = "-";
    [ObservableProperty] private string fullTestFinishedAtText = "-";
    [ObservableProperty] private string fullTestNetworkSummary = "Sem varredura executada.";
    [ObservableProperty] private string fullTestModbusSummary = "Sem descoberta Modbus executada.";
    [ObservableProperty] private string fullTestRouteSummary = "Sem analise de rota executada.";
    [ObservableProperty] private string fullTestBandwidthSummary = "Sem medicao de banda executada.";
    [ObservableProperty] private bool enableActiveSubnetScan = true;
    [ObservableProperty] private bool enableMapDiscovery;
    [ObservableProperty] private bool enableMapDiscoveryUnitSweep;
    [ObservableProperty] private int activeScanTimeoutMs = 250;
    [ObservableProperty] private int activeScanConcurrency = 2;
    [ObservableProperty] private int passiveObservationSeconds = 12;
    [ObservableProperty] private int mapDiscoveryUnitIdStart = 1;
    [ObservableProperty] private int mapDiscoveryUnitIdEnd = 10;
    [ObservableProperty] private int mapDiscoveryMaxAddress = 120;
    [ObservableProperty] private int mapDiscoveryBlockSize = 10;
    [ObservableProperty] private bool mapDiscoveryFc01 = true;
    [ObservableProperty] private bool mapDiscoveryFc02 = true;
    [ObservableProperty] private bool mapDiscoveryFc03 = true;
    [ObservableProperty] private bool mapDiscoveryFc04 = true;
    [ObservableProperty] private bool enableMapDiscoveryPointFallback = true;

    public ObservableCollection<string> Modes { get; } = ["Client", "Server"];
    public ObservableCollection<string> CaptureProtocols { get; } = ["Todos", "TCP", "UDP", "ARP", "ICMP", "Modbus TCP", "LLDP", "CDP"];
    public ObservableCollection<string> CaptureDirections { get; } = ["Origem ou destino", "Somente origem", "Somente destino"];
    public ObservableCollection<CaptureDeviceOption> CaptureDevices { get; } = [];
    public ObservableCollection<ServerPointRow> ServerPoints { get; } = [];
    public ObservableCollection<ServerMapRange> ServerMapRanges { get; } = [];
    public ObservableCollection<ClientConnectionSession> ClientSessions { get; } = [];
    public ObservableCollection<ClientMapRow> ClientMapRows => SelectedClientSession.Rows;
    public ObservableCollection<ClientCommunicationPointRow> ClientCommunicationPoints => SelectedClientSession.Points;
    public ObservableCollection<ClientCommunicationPointRow> ClientHoldingRegisterPoints => SelectedClientSession.HoldingPoints;
    public ObservableCollection<ClientCommunicationPointRow> ClientInputRegisterPoints => SelectedClientSession.InputPoints;
    public ObservableCollection<ClientCommunicationPointRow> ClientCoilPoints => SelectedClientSession.CoilPoints;
    public ObservableCollection<ClientCommunicationPointRow> ClientDiscreteInputPoints => SelectedClientSession.DiscretePoints;
    public ObservableCollection<TrafficEvent> Traffic { get; } = [];
    public ObservableCollection<TcpTimelineRow> TcpTimeline { get; } = [];
    public ObservableCollection<TcpTimelineRow> FilteredTcpTimeline { get; } = [];
    public ObservableCollection<DiagnosticFinding> Diagnostics { get; } = [];
    public ObservableCollection<ImportantWarningSummary> ImportantWarnings { get; } = [];
    public ObservableCollection<VerificationCheck> VerificationChecks { get; } = [];
    public ObservableCollection<FullTestStep> FullTestSteps { get; } = [];
    public ObservableCollection<NetworkDiscoveryRow> NetworkDiscoveryRows { get; } = [];
    public ObservableCollection<MapDiscoveryRow> DiscoveredMapRows { get; } = [];

    public bool IsClientMode => SelectedMode == "Client";
    public bool IsServerMode => SelectedMode == "Server";
    public string SelectedModeLabel => IsClientMode ? "Cliente" : "Servidor";
    public string TargetIp { get => SelectedClientSession.Address; set => SelectedClientSession.Address = value; }
    public int Port { get => IsServerMode ? ServerPort : SelectedClientSession.Port; set { if (IsServerMode) ServerPort = value; else SelectedClientSession.Port = value; } }
    public byte UnitId { get => IsServerMode ? ServerUnitId : SelectedClientSession.UnitId; set { if (IsServerMode) ServerUnitId = value; else SelectedClientSession.UnitId = value; } }
    public int ScanRateMs { get => SelectedClientSession.ScanRateMs; set => SelectedClientSession.ScanRateMs = value; }
    public bool KeepClientConnectionOpen { get => SelectedClientSession.KeepConnectionOpen; set => SelectedClientSession.KeepConnectionOpen = value; }
    public bool IsClientScanning { get => SelectedClientSession.IsScanning; set => SelectedClientSession.IsScanning = value; }
    public int RunningClientCount => ClientSessions.Count(x => x.IsScanning);
    public bool AnyClientRunning => RunningClientCount > 0;
    public bool HasClientTargets => ClientSessions.Count > 0;
    public string ServerEndpoint => $"{LocalIp}:{ServerPort}";
    public string ServerStateColor => IsServerRunning ? "#2D9B62" : ServerConnectionStatus.StartsWith("Falha") ? "#CB5048" : "#8A99A5";
    public bool CanConfigureClient => ClientSessions.Contains(SelectedClientSession) && !IsClientScanning && !SelectedClientSession.IsReading && !IsFullTestRunning;
    public bool CanConfigureServer => !IsServerRunning && !IsFullTestRunning;
    public bool CanUseClientOperations => !IsFullTestRunning;
    public bool CanDisconnectClient => ClientSessions.Contains(SelectedClientSession) && !IsFullTestRunning;
    public bool CanRemoveClientSession => CanConfigureClient;
    public bool CanConfigureClientMap => HasClientTargets && !IsFullTestRunning;
    public bool CanConfigureFullTest => !IsFullTestRunning;
    public bool CanOpenFullTestReport => !string.IsNullOrWhiteSpace(FullTestReport);
    public string ActiveEndpoint => $"{(IsServerMode ? LocalIp : TargetIp)}:{Port}";

    public MainViewModel()
    {
        InitializeDiscoveryGroups();
        RefreshLocalIpv4Addresses();
        _serverMap.LoadDefaults();
        LoadDefaultServerRanges();
        ApplyServerMapRanges();
        RefreshServerPoints();
        RegisterClientSession(SelectedClientSession);
        LoadTestScopeSettings();
        SelectedClientMapRow = ClientMapRows.FirstOrDefault();
        LoadVerificationChecks();
        LoadCaptureDevices();
        CreateFullTestPlan();
        UpdateFullTestSummaryCards();
        _networkCapture.PacketCaptured += OnPassivePacketCaptured;
        _passivePacketFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _passivePacketFlushTimer.Tick += (_, _) => FlushPassivePackets();
        _passivePacketFlushTimer.Start();
    }

    public void StopAllOperations()
    {
        _passivePacketFlushTimer.Stop();
        foreach (var session in ClientSessions)
        {
            session.Client.TrafficObserved -= OnTrafficObserved;
            session.FindingObserved -= OnTrafficObserved;
            session.PropertyChanged -= OnClientSessionPropertyChanged;
            session.CancelScan();
            _ = session.DisconnectAsync();
        }
        _networkCapture.PacketCaptured -= OnPassivePacketCaptured;
        if (_server is not null) _server.TrafficObserved -= OnTrafficObserved;
        _fullTestCts?.Cancel();
        _serverCts?.Cancel();
        _server?.Stop();
        try { _networkCapture.Dispose(); }
        catch (Exception ex) { Log.Warning(ex, "Falha ao encerrar captura"); }
    }

    private void RegisterClientSession(ClientConnectionSession session)
    {
        session.Client.TrafficObserved += OnTrafficObserved;
        session.FindingObserved += OnTrafficObserved;
        session.PropertyChanged += OnClientSessionPropertyChanged;
        ClientSessions.Add(session);
        RefreshFullTestScope();
        OnPropertyChanged(nameof(HasClientTargets));
    }

    public ClientConnectionSession AddClientSession(string address, int endpointPort, byte id, int scanMs, bool persistent)
    {
        var session = new ClientConnectionSession
        {
            Name = $"Alvo {++_nextClientNumber}", Address = address, Port = endpointPort,
            UnitId = id, ScanRateMs = scanMs, KeepConnectionOpen = persistent
        };
        RegisterClientSession(session);
        SelectedClientSession = session;
        return session;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveClientSession))]
    private async Task RemoveClientSessionAsync()
    {
        var session = SelectedClientSession;
        await session.DisconnectAsync();
        session.Client.TrafficObserved -= OnTrafficObserved;
        session.FindingObserved -= OnTrafficObserved;
        session.PropertyChanged -= OnClientSessionPropertyChanged;
        ClientSessions.Remove(session);
        if (ClientSessions.Count == 0)
        {
            var empty = new ClientConnectionSession { Name = "Nenhum alvo", Address = "" };
            foreach (var row in empty.Rows.ToArray()) empty.RemoveRow(row);
            SelectedClientSession = empty;
            Status = "Nenhum servidor-alvo cadastrado. O servidor local e a descoberta continuam disponiveis.";
        }
        else SelectedClientSession = ClientSessions.First();
        OnPropertyChanged(nameof(HasClientTargets));
        RefreshFullTestScope();
    }

    private void OnClientSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedClientSession)) RefreshClientSessionBindings();
        OnPropertyChanged(nameof(RunningClientCount));
        OnPropertyChanged(nameof(AnyClientRunning));
        if (e.PropertyName is nameof(ClientConnectionSession.IncludeInFullTest) or nameof(ClientConnectionSession.Name)
            or nameof(ClientConnectionSession.Address) or nameof(ClientConnectionSession.Port) or nameof(ClientConnectionSession.UnitId))
            RefreshFullTestScope();
    }

    partial void OnSelectedClientSessionChanged(ClientConnectionSession value)
    {
        if (ClientSessions.Contains(value)) SelectedMode = "Client";
        SelectedClientMapRow = value.Rows.FirstOrDefault();
        RefreshClientSessionBindings();
    }

    private void RefreshClientSessionBindings()
    {
        foreach (var property in new[] { nameof(TargetIp), nameof(Port), nameof(UnitId), nameof(ScanRateMs),
            nameof(KeepClientConnectionOpen), nameof(IsClientScanning), nameof(ActiveEndpoint),
            nameof(ClientMapRows), nameof(ClientCommunicationPoints), nameof(ClientHoldingRegisterPoints),
            nameof(ClientInputRegisterPoints), nameof(ClientCoilPoints), nameof(ClientDiscreteInputPoints),
            nameof(CanConfigureClient), nameof(CanDisconnectClient), nameof(CanRemoveClientSession),
            nameof(CanConfigureClientMap), nameof(HasClientTargets) }) OnPropertyChanged(property);
        ReadOnceCommand.NotifyCanExecuteChanged();
        StartClientScanCommand.NotifyCanExecuteChanged();
        DisconnectClientCommand.NotifyCanExecuteChanged();
        RemoveClientSessionCommand.NotifyCanExecuteChanged();
        AddClientMapRowCommand.NotifyCanExecuteChanged();
        RemoveClientMapRowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfigureClientMap))]
    private void AddClientMapRow()
    {
        var nextAddress = ClientMapRows.Count == 0 ? 0 : ClientMapRows.Max(x => x.StartAddress + x.Quantity);
        var row = new ClientMapRow
        {
            Name = $"Bloco {ClientMapRows.Count + 1}",
            Function = "FC03 Holding Registers",
            StartAddress = (ushort)Math.Min(ushort.MaxValue, nextAddress),
            Quantity = 1,
            Enabled = true
        };

        AddClientMapRowToCollection(row);
        SelectedClientMapRow = row;
    }

    [RelayCommand(CanExecute = nameof(CanConfigureClientMap))]
    private void RemoveClientMapRow()
    {
        if (SelectedClientMapRow is null)
        {
            return;
        }

        SelectedClientSession.RemoveRow(SelectedClientMapRow);
        SelectedClientMapRow = ClientMapRows.FirstOrDefault();
        RefreshClientCommunicationMapFromConfiguration();
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
    private Task StartServerAsync()
    {
        SelectedMode = "Server";
        return StartServerRuntimeAsync();
    }

    private async Task StartServerRuntimeAsync()
    {
        _serverCts = new CancellationTokenSource();
        _server = new ModbusTcpServer(_serverMap);
        _server.TrafficObserved += OnTrafficObserved;
        IsServerRunning = true;
        ServerConnectionStatus = "Escutando";
        Status = $"Servidor escutando em {ServerEndpoint}.";

        try
        {
            var address = IPAddress.Parse(LocalIp);
            await _server.StartAsync(address, ServerPort, _serverCts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Servidor parado.";
            ServerConnectionStatus = "Parado";
        }
        catch (Exception ex)
        {
            Status = $"Falha ao iniciar servidor: {ex.Message}";
            ServerConnectionStatus = "Falha ao iniciar";
            Log.Error(ex, "Falha ao iniciar servidor");
        }
        finally
        {
            IsServerRunning = false;
            if (!ServerConnectionStatus.StartsWith("Falha")) ServerConnectionStatus = "Parado";
        }
    }

    [RelayCommand(CanExecute = nameof(IsServerRunning))]
    private void StopServer()
    {
        _serverCts?.Cancel();
        _server?.Stop();
        Status = "Encerrando servidor...";
    }

    [RelayCommand(CanExecute = nameof(CanReadOnce))]
    private async Task ReadOnceAsync()
    {
        SelectedMode = "Client";
        var session = SelectedClientSession;
        await session.ReadCycleAsync(CancellationToken.None);
        Status = $"{session.Endpoint}: {session.ConnectionState}";
    }

    [RelayCommand(CanExecute = nameof(CanStartClientScan), AllowConcurrentExecutions = true)]
    private async Task StartClientScanAsync()
    {
        SelectedMode = "Client";
        var session = SelectedClientSession;
        Status = $"Conectando a {session.Endpoint}; scan a cada {session.ScanRateMs} ms.";
        await session.ScanAsync();
        Status = $"{session.Endpoint}: {session.ConnectionState}";
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectClient))]
    private async Task DisconnectClientAsync()
    {
        var session = SelectedClientSession;
        await session.DisconnectAsync();
        Status = $"Cliente {session.Endpoint} desconectado; scan parado.";
        DisconnectClientCommand.NotifyCanExecuteChanged();
    }

    public async Task SetClientConnectionModeAsync(bool keepConnectionOpen)
    {
        var session = SelectedClientSession;
        await session.Client.ConfigureConnectionModeAsync(keepConnectionOpen);
        session.KeepConnectionOpen = keepConnectionOpen;
        Status = keepConnectionOpen
            ? "Modo Client configurado para manter uma conexao TCP."
            : "Modo Client configurado para reconectar a cada requisicao.";
    }

    public async Task WriteRangeFromMapAsync(ClientMapRow row, ushort address, ushort endAddress, ushort value)
    {
        var session = ClientSessions.FirstOrDefault(x => x.Rows.Contains(row)) ?? SelectedClientSession;
        var rangeEnd = row.StartAddress + Math.Max(1, (int)row.Quantity) - 1;
        if (address < row.StartAddress || endAddress > rangeEnd)
        {
            Status = $"Escrita bloqueada: range {address}-{endAddress} fora da linha {row.StartAddress}-{rangeEnd}.";
            return;
        }

        if (row.FunctionCode is not (ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadCoils))
        {
            Status = $"Escrita bloqueada: {FormatFunctionCode(row.FunctionCode)} nao e gravavel por este popup.";
            return;
        }

        try
        {
            var writes = 0;
            for (var current = (int)address; current <= endAddress; current++)
            {
                var currentAddress = (ushort)current;
                var displayValue = row.FunctionCode == ModbusProtocol.ReadHoldingRegisters
                    ? value
                    : value != 0 ? (ushort)1 : (ushort)0;

                if (row.FunctionCode == ModbusProtocol.ReadHoldingRegisters)
                {
                    await session.Client.WriteSingleRegisterAsync(session.Address, session.Port, session.UnitId, currentAddress, value, CancellationToken.None);
                }
                else
                {
                    await session.Client.WriteSingleCoilAsync(session.Address, session.Port, session.UnitId, currentAddress, value != 0, CancellationToken.None);
                }

                row.LastValue = ReplaceRegisterValue(row.LastValue, row.StartAddress, row.Quantity, currentAddress, displayValue);
                session.RecordValue(row, currentAddress, displayValue, "Escrita OK");
                writes++;
            }

            var writeFunction = row.FunctionCode == ModbusProtocol.ReadHoldingRegisters ? "FC06 HR" : "FC05 COIL";
            row.LastStatus = $"Escrita OK {writeFunction} {address}-{endAddress}";
            row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
            Status = $"Escrita OK: {writeFunction} {address}-{endAddress} = {value} ({writes} ponto(s)).";
        }
        catch (Exception ex)
        {
            AddSystemFinding($"Falha/timeout na escrita: {ex.Message}");
            row.LastStatus = ex.Message;
            row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
            Status = $"Falha na escrita: {ex.Message}";
            Log.Error(ex, "Falha na escrita Modbus");
        }
    }

    public async Task WritePointFromCommunicationPointAsync(ClientCommunicationPointRow point, ushort value)
    {
        var session = ClientSessions.FirstOrDefault(x => x.Points.Contains(point)) ?? SelectedClientSession;
        if (point.FunctionCode is not (ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadCoils))
        {
            Status = $"Escrita bloqueada: {point.Type} nao usa FC05/FC06.";
            return;
        }

        var row = session.Rows.FirstOrDefault(x =>
            x.FunctionCode == point.FunctionCode
            && x.StartAddress <= point.Address
            && point.Address <= x.StartAddress + Math.Max(1, (int)x.Quantity) - 1);
        if (row is not null)
        {
            await WriteRangeFromMapAsync(row, point.Address, point.Address, value);
            return;
        }

        try
        {
            if (point.FunctionCode == ModbusProtocol.ReadHoldingRegisters)
            {
                await session.Client.WriteSingleRegisterAsync(session.Address, session.Port, session.UnitId, point.Address, value, CancellationToken.None);
            }
            else
            {
                await session.Client.WriteSingleCoilAsync(session.Address, session.Port, session.UnitId, point.Address, value != 0, CancellationToken.None);
            }

            point.Value = point.FunctionCode == ModbusProtocol.ReadHoldingRegisters
                ? value
                : value != 0 ? (ushort)1 : (ushort)0;
            point.Quality = "Escrita OK";
            point.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
            Status = $"Escrita OK: {point.Type} {point.Address} = {value}";
            session.SyncViews();
        }
        catch (Exception ex)
        {
            AddSystemFinding($"Falha/timeout na escrita: {ex.Message}");
            point.Quality = ex.Message;
            point.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
            Status = $"Falha na escrita: {ex.Message}";
            Log.Error(ex, "Falha na escrita Modbus via mapa de comunicacao");
        }
    }

    private static string ReplaceRegisterValue(string currentValue, ushort startAddress, ushort quantity, ushort writtenAddress, ushort value)
    {
        var index = writtenAddress - startAddress;
        var values = currentValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (values.Count == 0)
        {
            return index == 0 ? value.ToString() : $"HR {writtenAddress}={value}";
        }

        if (index < values.Count)
        {
            values[index] = value.ToString();
            return string.Join(", ", values);
        }

        return quantity > 1
            ? $"{currentValue} | HR {writtenAddress}={value}"
            : value.ToString();
    }

    private void UpsertClientCommunicationPoints(ClientMapRow row, IReadOnlyList<ushort> values, string quality)
    {
        SelectedClientSession.RecordValues(row, values, quality);
    }

    private void MarkClientCommunicationRangeFailed(ClientMapRow row, string error)
    {
        SelectedClientSession.RecordFailure(row, error);
    }

    private void AddClientMapRowToCollection(ClientMapRow row)
    {
        SelectedClientSession.AddRow(row);
    }

    private void RefreshClientCommunicationMapFromConfiguration()
    {
        SelectedClientSession.RefreshMap();
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
            ServerPort = ServerPort,
            ServerUnitId = ServerUnitId,
            ClientSessions = ClientSessions.Select(x => new ClientSessionCase(x.Name, x.Address, x.Port, x.UnitId, x.ScanRateMs, x.KeepConnectionOpen,
                x.Rows.Select(row => new ClientBlockCase(row.Name, row.FunctionCode, row.StartAddress, row.Quantity, row.Enabled)).ToList())).ToList(),
            Map = ServerPoints.Select(x => x.ToModbusPoint()).ToList(),
            Traffic = Traffic.ToList(),
            Diagnostics = Diagnostics.ToList()
        };

        await CaseStorage.SaveAsync(troubleshootCase, dialog.FileName);
        Status = $"Caso exportado: {dialog.FileName}";
    }

    [RelayCommand(CanExecute = nameof(CanConfigureFullTest))]
    private void ClearTimelineHistory()
    {
        Traffic.Clear();
        TcpTimeline.Clear();
        FilteredTcpTimeline.Clear();
        while (_passivePacketQueue.TryDequeue(out _))
        {
        }
        QueuedPassivePackets = 0;
        HasCompletedNetworkCapture = false;
        Status = "Historico Modbus e TCP limpo. Avisos e resultados do teste preservados.";
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

    public void ResetDiagnosticSession()
    {
        ClearTimeline();
        ResetTopology();
        TopologySummary = "Sem coleta. Execute o teste completo.";
        NetworkDiscoveryRows.Clear();
        DiscoveredMapRows.Clear();
        FullTestReport = string.Empty;
        FullTestRunScope = "Nenhuma execucao realizada.";
        _fullTestClients = [];
        FullTestStartedAtText = "-";
        FullTestFinishedAtText = "-";
        FullTestOverallStatus = "Aguardando";
        FullTestProgressLabel = "Aguardando execução";
        FullTestCompletedSteps = 0;
        FullTestProgressPercent = 0;
        FullTestOkCount = 0;
        FullTestWarningCount = 0;
        FullTestFailureCount = 0;
        CreateFullTestPlan();
        Status = "Nova sessão de diagnóstico preparada.";
    }

    [RelayCommand(CanExecute = nameof(CanStartFullTest))]
    private async Task StartFullTestAsync()
    {
        if (!CanStartFullTest()) return;
        using var testCts = new CancellationTokenSource();
        _fullTestCts = testCts;
        PrepareFullTestScope();
        IsFullTestRunning = true;
        try
        {
        FullTestReport = "";
        _fullTestDetailedReport = "";
        FullTestOverallStatus = "Executando";
        FullTestScore = "0/0";
        FullTestCompletedSteps = 0;
        FullTestTotalSteps = 0;
        FullTestProgressPercent = 0;
        FullTestProgressLabel = "Preparando teste";
        FullTestOkCount = 0;
        FullTestWarningCount = 0;
        FullTestFailureCount = 0;
        FullTestStartedAtText = _fullTestStartedAt.ToString("HH:mm:ss");
        FullTestFinishedAtText = "-";
        FullTestNetworkSummary = "Coletando dados...";
        FullTestModbusSummary = "Coletando dados...";
        FullTestRouteSummary = "Coletando dados...";
        FullTestBandwidthSummary = "Coletando dados...";
        NetworkDiscoveryRows.Clear();
        DiscoveredMapRows.Clear();
        _fullTestStartupActions.Clear();

        var steps = CreateFullTestPlan();
        UpdateFullTestSummaryCards();

        await EnsureFullTestRuntimeAsync();

        foreach (var (step, action) in steps.Where(x => x.Step is not null))
        {
            testCts.Token.ThrowIfCancellationRequested();
            await ExecuteFullTestStepAsync(step!, action, testCts.Token);
        }

        _fullTestFinishedAt = DateTimeOffset.Now;
        UpdateFullTestSummaryCards();
        FullTestReport = BuildFullTestReport();
        _fullTestDetailedReport = BuildDetailedFullTestReport(FullTestReport);
        UpdateFullTestSummaryCards();
        FullTestFinishedAtText = DateTimeOffset.Now.ToString("HH:mm:ss");
        FullTestProgressLabel = "Teste concluído";
        Status = "Teste completo finalizado. Relatorio gerado.";
        }
        catch (OperationCanceledException) when (testCts.IsCancellationRequested)
        {
            FullTestOverallStatus = "Cancelado";
            FullTestFinishedAtText = DateTimeOffset.Now.ToString("HH:mm:ss");
            FullTestProgressLabel = "Teste cancelado";
            _fullTestFinishedAt = DateTimeOffset.Now;
            FullTestReport = BuildFullTestReport();
            _fullTestDetailedReport = BuildDetailedFullTestReport(FullTestReport);
            Status = "Teste cancelado. Relatorio parcial disponivel; scan e captura podem continuar ativos.";
        }
        catch (Exception ex)
        {
            FullTestOverallStatus = "Falha";
            FullTestFinishedAtText = DateTimeOffset.Now.ToString("HH:mm:ss");
            FullTestProgressLabel = "Teste interrompido por falha";
            _fullTestFinishedAt = DateTimeOffset.Now;
            Status = $"Falha no teste: {ex.Message}";
            FullTestReport = BuildFullTestReport();
            _fullTestDetailedReport = BuildDetailedFullTestReport(FullTestReport);
            Log.Error(ex, "Falha na execucao do teste completo");
        }
        finally
        {
            _fullTestCts = null;
            IsFullTestRunning = false;
        }
    }

    private void PrepareFullTestScope()
    {
        _fullTestMode = TestMode;
        _fullTestStartedAt = DateTimeOffset.Now;
        RefreshLocalIpv4Addresses();
        _runWarnings.Clear();
        _fullTestClients = TestMode == "Client" ? ConfiguredTestTargets() : [];
        FullTestRunScope = TestMode == "Client"
            ? _fullTestClients.Length == 0
                ? "Cliente / Mestre: descoberta automatica; nenhum alvo pre-configurado. Apenas servidores Modbus confirmados serao validados."
                : "Cliente / Mestre: " + string.Join("; ", _fullTestClients.Select(x => $"{x.Name} ({x.Endpoint}, ID {x.UnitId})"))
            : $"Servidor / Escravo: {ServerName} ({ServerEndpoint}, ID {ServerUnitId})";
        _fullTestSessionIds.Clear();
        foreach (var session in _fullTestClients) _fullTestSessionIds.Add(session.Id);
        if (FullTestIsServerMode) _fullTestSessionIds.Add("local-server");
        _lastRequestBySignature.Clear();
        _pollingIntervalsBySignature.Clear();
        _exceptionCounts.Clear();
        _outOfMapCounts.Clear();
        _baselineTraffic = _operationalTraffic = null;
        ResetTopology();
        _blockReadStatistics.Clear();
        _confirmedModbusEndpoints.Clear();
        _openDiscoveryEndpoints.Clear();
        _discoveredUnitIds.Clear();
        _connectionBaselines.Clear();
        foreach (var session in _fullTestClients) _connectionBaselines[session.Id] = (session.Client.ConnectionOpenCount, session.Client.ConnectionCloseCount);
        _serverPendingRequests.Clear();
        _serverBlocks.Clear();
        _serverWindowRequests = _serverWindowReplies = _serverWindowExceptions = _serverWindowUnmatched = 0;
        _nextProbeAt = DateTimeOffset.MinValue;
        _windowUiDrops = 0;
        _fullTestFinishedAt = null;
        FullTestCoverage = "Preparando coleta";
    }

    [RelayCommand(CanExecute = nameof(IsFullTestRunning))]
    private void CancelFullTest() => _fullTestCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSaveFullTestReport))]
    private Task SaveFullTestReportAsync() => ExportFullTestReportAsync(false);

    [RelayCommand(CanExecute = nameof(CanSaveFullTestReport))]
    private Task SaveDetailedFullTestReportAsync() => ExportFullTestReportAsync(true);

    private async Task ExportFullTestReportAsync(bool detailed)
    {
        if (string.IsNullOrWhiteSpace(FullTestReport))
        {
            Status = "Nenhum relatorio de teste completo gerado ainda.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = detailed ? "Exportar relatorio completo" : "Exportar relatorio resumido",
            Filter = "Relatorio Markdown (*.md)|*.md|Relatorio PDF (*.pdf)|*.pdf",
            DefaultExt = ".md",
            AddExtension = true,
            FileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm}-teste-modbus-{(detailed ? "completo" : "resumo")}"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var report = detailed ? _fullTestDetailedReport : FullTestReport;
        try
        {
            Status = "Exportando relatorio...";
            await ReportExporter.ExportAsync(dialog.FileName, report, _fullTestTopologySnapshot,
                NetworkDiscoveryRows.ToArray(), TopologyNeighbors.ToArray(), FullTestIsServerMode, _reportTrafficRows);
            Status = $"Relatorio exportado: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao exportar relatorio");
            Status = $"Falha ao exportar relatorio: {ex.Message}";
            System.Windows.MessageBox.Show($"Nao foi possivel exportar o relatorio.\n{ex.Message}",
                "Exportar relatorio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RefreshCaptureDevices()
    {
        LoadCaptureDevices();
    }

    private async Task EnsureFullTestRuntimeAsync()
    {
        await EnsureNetworkCaptureForFullTestAsync();
        _fullTestCts?.Token.ThrowIfCancellationRequested();
        _fullTestStartupActions.Add("Novas leituras/servidor serao iniciados apos a referencia passiva; operacoes previamente ativas continuam.");
    }

    private void StartModbusRuntimeAfterBaseline()
    {

        if (FullTestIsServerMode)
        {
            EnsureServerForFullTest();
        }
        else if (FullTestIsClientMode)
        {
            foreach (var session in _fullTestClients)
            {
                if (!session.IsScanning) _ = session.ScanAsync();
                _fullTestStartupActions.Add($"Cliente {session.Name}: leitura ativa solicitada em {session.Endpoint}, UID {session.UnitId}.");
            }
        }

    }

    private async Task EnsureNetworkCaptureForFullTestAsync()
    {
        if (IsNetworkCaptureRunning)
        {
            _fullTestStartupActions.Add("Captura passiva: ja estava ativa.");
            return;
        }

        if (CaptureDevices.Count == 0)
        {
            LoadCaptureDevices();
        }

        if (SelectedCaptureDevice is null)
        {
            _fullTestStartupActions.Add("Captura passiva: nao iniciada; nenhuma interface Npcap disponivel/selecionada.");
            return;
        }

        try
        {
            var selectedByTraffic = await SelectCaptureDeviceByTrafficAsync();
            if (selectedByTraffic is not null)
            {
                SelectedCaptureDevice = selectedByTraffic;
            }

            GeneratedCaptureFilter = BuildCaptureFilter();
            _networkCapture.Start(SelectedCaptureDevice, GeneratedCaptureFilter);
            IsNetworkCaptureRunning = true;
            _fullTestStartupActions.Add($"Captura passiva: iniciada automaticamente em '{SelectedCaptureDevice.Description}' com BPF '{GeneratedCaptureFilter}'.");
        }
        catch (Exception ex)
        {
            _fullTestStartupActions.Add($"Captura passiva: falha ao iniciar automaticamente ({ex.Message}).");
            Log.Error(ex, "Falha ao iniciar captura passiva automaticamente no teste completo");
        }
    }

    private async Task<CaptureDeviceOption?> SelectCaptureDeviceByTrafficAsync()
    {
        const string probeFilter = "tcp or udp or arp or icmp";
        var samples = await _networkCapture.SampleDeviceTrafficAsync(
            probeFilter,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        if (samples.Count == 0)
        {
            _fullTestStartupActions.Add("Selecao de interface: amostragem sem resultado; usando interface selecionada atual.");
            return SelectedCaptureDevice;
        }

        var sampleSummary = string.Join("; ", samples
            .OrderByDescending(x => x.PacketCount)
            .ThenByDescending(x => x.ByteCount)
            .Take(5)
            .Select(x => string.IsNullOrWhiteSpace(x.Error)
                ? $"{x.Device.Index}:{x.Device.Description}={x.PacketCount} pkt/{FormatBytes(x.ByteCount)}"
                : $"{x.Device.Index}:{x.Device.Description}=erro {x.Error}"));

        var loopbackOnly = FullTestIsClientMode
            ? _fullTestClients.Length > 0 && _fullTestClients.All(x => IPAddress.TryParse(x.Address, out var ip) && IPAddress.IsLoopback(ip))
            : IPAddress.TryParse(LocalIp, out var listenIp) && IPAddress.IsLoopback(listenIp);
        var loopbackSample = loopbackOnly && !EnableActiveSubnetScan ? samples.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Error)
            && (x.Device.Name.Contains("loopback", StringComparison.OrdinalIgnoreCase)
                || x.Device.Description.Contains("loopback", StringComparison.OrdinalIgnoreCase))) : null;
        var best = loopbackSample ?? samples
            .Where(x => string.IsNullOrWhiteSpace(x.Error))
            .Where(x => !EnableActiveSubnetScan || !Regex.IsMatch(x.Device.Description + " " + x.Device.Name, "Loopback|WFP|Filter|Pseudo", RegexOptions.IgnoreCase))
            .OrderByDescending(x => x.PacketCount)
            .ThenByDescending(x => x.ByteCount)
            .FirstOrDefault();

        if (best is null || (best.PacketCount == 0 && loopbackSample is null))
        {
            _fullTestStartupActions.Add($"Selecao de interface: nenhuma interface apresentou trafego em 2 s com BPF '{probeFilter}'. Amostras: {sampleSummary}. Usando interface selecionada atual.");
            return SelectedCaptureDevice;
        }

        var matchingDevice = CaptureDevices.FirstOrDefault(x => x.Index == best.Device.Index) ?? best.Device;
        var reason = loopbackSample is not null ? "todos os alvos do teste sao loopback" : "maior trafego em 2 s";
        _fullTestStartupActions.Add($"Selecao de interface: escolhida '{matchingDevice.Description}' por {reason} ({best.PacketCount} pkt, {FormatBytes(best.ByteCount)}). Amostras: {sampleSummary}.");
        return matchingDevice;
    }

    private void EnsureServerForFullTest()
    {
        if (IsServerRunning)
        {
            _fullTestStartupActions.Add("Servidor Modbus: ja estava ativo.");
            return;
        }

        ApplyServerMapRanges();
        _ = StartServerRuntimeAsync();
        _fullTestStartupActions.Add($"Servidor Modbus: start automatico solicitado em {ServerEndpoint}, Unit ID {ServerUnitId}.");
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


    private bool CanStartServer() => !IsServerRunning && !IsFullTestRunning;
    private bool CanReadOnce() => CanConfigureClient;
    private bool CanStartClientScan() => CanConfigureClient;
    private bool CanEditServerMap() => !IsServerRunning && !IsFullTestRunning;
    private bool CanStartNetworkCapture() => !IsNetworkCaptureRunning && SelectedCaptureDevice is not null;
    private bool CanStartFullTest() => !IsFullTestRunning && !IsDeviceProbeRunning;
    private bool CanSaveFullTestReport() => !string.IsNullOrWhiteSpace(FullTestReport) && !IsFullTestRunning;

    partial void OnFullTestReportChanged(string value)
    {
        SaveFullTestReportCommand.NotifyCanExecuteChanged();
        SaveDetailedFullTestReportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenFullTestReport));
    }

    partial void OnEnableMapDiscoveryChanged(bool value)
    {
        if (IsFullTestRunning || !string.IsNullOrWhiteSpace(FullTestReport)) return;

        CreateFullTestPlan();
        UpdateFullTestSummaryCards();
    }

    partial void OnSelectedModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsClientMode));
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(SelectedModeLabel));
        OnPropertyChanged(nameof(ActiveEndpoint));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(UnitId));
    }

    partial void OnLocalIpChanged(string value) { OnPropertyChanged(nameof(ActiveEndpoint)); OnPropertyChanged(nameof(ServerEndpoint)); }
    partial void OnServerPortChanged(int value) { OnPropertyChanged(nameof(Port)); OnPropertyChanged(nameof(ServerEndpoint)); OnPropertyChanged(nameof(ActiveEndpoint)); }
    partial void OnServerUnitIdChanged(byte value) => OnPropertyChanged(nameof(UnitId));
    partial void OnServerConnectionStatusChanged(string value) => OnPropertyChanged(nameof(ServerStateColor));

    partial void OnIsServerRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfigureClient));
        OnPropertyChanged(nameof(CanConfigureServer));
        OnPropertyChanged(nameof(CanUseClientOperations));
        OnPropertyChanged(nameof(CanDisconnectClient));
        OnPropertyChanged(nameof(ServerStateColor));
        StartServerCommand.NotifyCanExecuteChanged();
        ReadOnceCommand.NotifyCanExecuteChanged();
        StartClientScanCommand.NotifyCanExecuteChanged();
        StopServerCommand.NotifyCanExecuteChanged();
        AddServerRangeCommand.NotifyCanExecuteChanged();
        RemoveServerRangeCommand.NotifyCanExecuteChanged();
        ApplyServerMapCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFullTestRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(FullTestIsClientMode));
        OnPropertyChanged(nameof(FullTestIsServerMode));
        OnPropertyChanged(nameof(FullTestModeLabel));
        AddServerRangeCommand.NotifyCanExecuteChanged();
        RemoveServerRangeCommand.NotifyCanExecuteChanged();
        ApplyServerMapCommand.NotifyCanExecuteChanged();
        AddClientMapRowCommand.NotifyCanExecuteChanged();
        RemoveClientMapRowCommand.NotifyCanExecuteChanged();
        StartServerCommand.NotifyCanExecuteChanged();
        StartClientScanCommand.NotifyCanExecuteChanged();
        ReadOnceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConfigureClient));
        OnPropertyChanged(nameof(CanConfigureServer));
        OnPropertyChanged(nameof(CanDisconnectClient));
        OnPropertyChanged(nameof(CanUseClientOperations));
        OnPropertyChanged(nameof(CanRemoveClientSession));
        RemoveClientSessionCommand.NotifyCanExecuteChanged();
        StartFullTestCommand.NotifyCanExecuteChanged();
        CancelFullTestCommand.NotifyCanExecuteChanged();
        SaveFullTestReportCommand.NotifyCanExecuteChanged();
        SaveDetailedFullTestReportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConfigureFullTest));
        ClearTimelineHistoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsNetworkCaptureRunningChanged(bool value)
    {
        if (value)
        {
            _captureStartedForData = true;
            NetworkCaptureStartedAt = DateTimeOffset.Now;
            OnPropertyChanged(nameof(NetworkCaptureStartedAt));
            HasCompletedNetworkCapture = false;
        }
        else if (_captureStartedForData)
        {
            while (!_passivePacketQueue.IsEmpty)
            {
                FlushPassivePackets();
            }
            _captureStartedForData = false;
            HasCompletedNetworkCapture = true;
        }

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
        if (ServerMapRanges.Any(x => x.Enabled && (x.Quantity == 0 || (int)x.StartAddress + x.Quantity > 65536)))
        {
            Status = "Mapa nao aplicado: quantidade deve ser positiva e endereco final deve ser no maximo 65535.";
            return;
        }
        _serverMap.Clear();
        foreach (var range in ServerMapRanges.Where(x => x.Enabled))
        {
            for (var i = 0; i < range.Quantity; i++)
            {
                var address = (ushort)(range.StartAddress + i);
                var value = range.IncrementValue ? (ushort)(range.InitialValue + i) : range.InitialValue;
                _serverMap.AddPoint(range.Type, address, value, range.Writable);
            }
        }

        RefreshServerPoints();
    }

    private void RefreshServerPoints()
    {
        var points = _serverMap.ToPoints();
        if (ServerPoints.Count == points.Count && ServerPoints.Zip(points).All(x =>
            x.First.Type == x.Second.Type && x.First.Address == x.Second.Address && x.First.IsWritable == x.Second.IsWritable))
        {
            foreach (var (row, point) in ServerPoints.Zip(points))
            {
                if (row.Value == point.Value) continue;
                row.PropertyChanged -= OnServerPointChanged;
                row.Value = point.Value;
                row.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
                row.PropertyChanged += OnServerPointChanged;
            }
            return;
        }

        foreach (var row in ServerPoints) row.PropertyChanged -= OnServerPointChanged;
        ServerPoints.Clear();
        foreach (var point in points)
        {
            var row = new ServerPointRow(point);
            row.PropertyChanged += OnServerPointChanged;
            ServerPoints.Add(row);
        }
    }

    private void OnServerPointChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServerPointRow.Value) || sender is not ServerPointRow row)
        {
            return;
        }

        _serverMap.AddPoint(row.Type, row.Address, row.Value, row.IsWritable);
        row.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
        Status = $"Valor do server atualizado: {row.Type} {row.Address} = {row.Value}";
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
            var session = ClientSessions.FirstOrDefault(x => ReferenceEquals(x.Client, sender) || ReferenceEquals(x, sender));
            if (session is not null) e = e with { SessionId = session.Id, Origin = $"Cliente · {session.Name}" };
            else if (sender is ModbusTcpServer) e = e with { SessionId = "local-server", Origin = "Servidor local" };
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
            RecordServerWindowEvent(e);
            AnalyzeCommunicationPattern(e);
            if (sender is ModbusTcpServer && e.Direction == TrafficDirection.ServerToClient
                && e.FunctionCode is 5 or 6 or 15 or 16) RefreshServerPoints();
        });
    }

    private void OnPassivePacketCaptured(object? sender, TcpTimelineRow row)
    {
        row.CapturePhase = _capturePhase;
        _trafficWindow.Observe(row);
        ObserveTopologyPacket(row);
        if (_passivePacketQueue.Count >= 10000)
        {
            Interlocked.Increment(ref _droppedPassivePackets);
            return;
        }
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
        var dropped = Interlocked.Read(ref _droppedPassivePackets);
        if (dropped > _reportedDroppedPassivePackets)
        {
            _reportedDroppedPassivePackets = dropped;
            UpsertImportantWarning("captura-descarte-ui", "Atencao", "Captura com descarte na fila",
                $"{dropped} pacotes descartados desde a abertura do programa; limite da fila: 10000. Amostra incompleta.",
                "As taxas observadas nao representam o trafego integral. Reduza o escopo de captura.", DateTimeOffset.Now);
        }
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
        if (row.ModbusKind is "Response" or "Exception" && (!IsFullTestRunning || row.Timestamp >= _fullTestStartedAt))
            RegisterModbusDevice(row.Source, false, "ADU de resposta/exception Modbus estruturalmente reconhecido na captura; sem remontagem TCP.");
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

    public long PassivePacketsDropped => Interlocked.Read(ref _droppedPassivePackets);

    public DateTimeOffset? NetworkCaptureStartedAt { get; private set; }

    public (long Received, long Dropped, long InterfaceDropped)? ReadCaptureStatistics() =>
        _networkCapture.ReadStatistics();

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
            "LLDP" => "ether proto 0x88cc",
            "CDP" => "ether dst 01:00:0c:cc:cc:cc",
            "Modbus TCP" => "tcp",
            _ => SelectedCaptureDevice?.Description.Contains("loopback", StringComparison.OrdinalIgnoreCase) == true
                ? "tcp or udp or arp or icmp"
                : "tcp or udp or arp or icmp or ether proto 0x88cc or ether dst 01:00:0c:cc:cc:cc"
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

    private List<(FullTestStep? Step, Func<CancellationToken, Task<FullTestStepResult>> Action)> CreateFullTestPlan()
    {
        FullTestSteps.Clear();
        SelectedFullTestStep = null;
        List<(FullTestStep? Step, Func<CancellationToken, Task<FullTestStepResult>> Action)> plan =
        [
            CreateFullTestStep("Contexto do teste", "Registra alvo, modo, porta, interface, filtro e premissas de seguranca.", RunFullTestContextAsync),
            CreateFullTestStep("Interfaces e rotas IP", "Mapeia placas ativas, gateways, mascara, velocidade nominal e rotas do Windows.", RunIpRouteAnalysisAsync),
            CreateFullTestStep("Referencia passiva TCP", "Coleta janela completa antes das sondagens adicionais do teste.", RunPassiveInventoryAsync),
            CreateFullTestStep("Tabela ARP local", "Consulta ARP do Windows para descobrir dispositivos ja resolvidos na rede.", RunArpSnapshotAsync),
            CreateFullTestStep("Varredura de hosts", "Testa hosts candidatos da sub-rede e consolida dispositivos possivelmente ativos.", RunHostDiscoveryAsync),
            CreateFullTestStep("Descoberta Modbus", "Procura servidores Modbus/TCP nos hosts descobertos e nos alvos configurados.", RunModbusDiscoveryAsync),
            CreateOptionalMapDiscoveryStep(),
            CreateFullTestStep("Topologia inferida", "Mapeia conversas, rastreia rotas ICMP e identifica vizinhos LLDP/CDP anunciados; nao valida infraestrutura fisica.", RunTopologyInferenceAsync)
        ];
        var targets = IsFullTestRunning ? _fullTestClients
            : FullTestIsClientMode ? ConfiguredTestTargets() : [];
        if (FullTestIsClientMode && targets.Length == 0)
            plan.Add(CreateFullTestStep("Validacao dos servidores descobertos",
                "Repete leituras apenas em servidores Modbus confirmados; mapa nao descoberto limita a conclusao ao protocolo.",
                RunDiscoveredServersValidationAsync));
        if (FullTestIsServerMode)
        {
            plan.Add(CreateFullTestStep("Conectividade TCP", "Verifica servidor local.", t => RunTcpConnectivityAsync(t, null)));
            plan.Add(CreateFullTestStep("Mapa Modbus", "Verifica mapa local.", t => RunClientMapValidationAsync(t, null)));
        }
        foreach (var target in targets)
        {
            var label = $"{target.Name} · {target.Endpoint} · UID {target.UnitId}";
            plan.Add(CreateFullTestStep($"Conectividade TCP - {label}", "Testa abertura TCP deste alvo.", t => RunTcpConnectivityAsync(t, target)));
            plan.Add(CreateFullTestStep($"Mapa Modbus - {label}", "Valida as linhas habilitadas do mapa deste alvo, sem escrita.", t => RunClientMapValidationAsync(t, target)));
            plan.Add(CreateFullTestStep($"Envio e recebimento - {label}", "Confirma request/response deste alvo, sem escrita.", t => RunSendReceiveValidationAsync(t, target)));
        }
        plan.Add(CreateFullTestStep("Monitoramento TCP operacional", "Observa erros, sinais TCP e picos de carga pela duracao configurada, sem nova varredura.", RunTrafficLoadAsync));
        if (FullTestIsServerMode) plan.Add(CreateFullTestStep("Envio e recebimento", "Correlaciona transacoes externas coletadas durante a janela operacional.", t => RunSendReceiveValidationAsync(t, null)));
        plan.Add(CreateFullTestStep("Falhas observadas", "Consolida somente eventos dos alvos testados nesta execucao.", RunObservedFailuresAsync));
        plan.Add(CreateFullTestStep("Conclusao", "Agrega os resultados desta execucao.", RunFullTestConclusionAsync));
        return plan;
    }

    private (FullTestStep Step, Func<CancellationToken, Task<FullTestStepResult>> Action) CreateFullTestStep(
        string name,
        string objective,
        Func<CancellationToken, Task<FullTestStepResult>> action)
    {
        var step = new FullTestStep(FullTestSteps.Count + 1, name, objective);
        FullTestSteps.Add(step);
        SelectedFullTestStep ??= step;
        return (step, action);
    }

    private (FullTestStep? Step, Func<CancellationToken, Task<FullTestStepResult>> Action) CreateOptionalMapDiscoveryStep()
    {
        return EnableMapDiscovery
            ? CreateFullTestStep(
                "Descoberta de mapa",
                "Descobre ranges Modbus por leitura read-only em servers encontrados ou consolida ranges requisitados por clientes contra o server simulado.",
                RunMapDiscoveryAsync)
            : (null, RunMapDiscoveryAsync);
    }

    private async Task ExecuteFullTestStepAsync(FullTestStep step, Func<CancellationToken, Task<FullTestStepResult>> action, CancellationToken cancellationToken)
    {
        SelectedFullTestStep = step;
        step.Status = "Executando";
        step.StartedAt = DateTimeOffset.Now;
        _capturePhase = step.Name is "Varredura de hosts" or "Descoberta Modbus" or "Descoberta de mapa" ? "Sondagem"
            : step.Name.StartsWith("Mapa Modbus", StringComparison.Ordinal) ? "Validacao de mapa" : "Operacional";
        FullTestProgressLabel = $"Etapa {step.Order} de {FullTestSteps.Count}: {step.Name}";
        Status = $"Teste completo: {step.Name}...";

        try
        {
            var result = await action(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            step.Status = result.Status;
            step.Result = result.Detail;
            step.Recommendation = result.Recommendation;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            step.Status = "Cancelado";
            step.Result = "Etapa interrompida pelo operador. Resultados incompletos.";
            throw;
        }
        catch (Exception ex)
        {
            step.Status = "Falha";
            step.Result = ex.Message;
            step.Recommendation = "Verificacao tecnica: permissao de captura/socket, interface selecionada, IP/porta configurados e disponibilidade do endpoint.";
            Log.Error(ex, "Falha na etapa de teste completo {StepName}", step.Name);
        }
        finally
        {
            step.FinishedAt = DateTimeOffset.Now;
            if (_capturePhase == "Validacao de mapa")
            {
                _lastRequestBySignature.Clear();
                _pollingIntervalsBySignature.Clear();
            }
            _capturePhase = "Operacional";
            UpdateFullTestSummaryCards();
        }
    }

    private Task<FullTestStepResult> RunFullTestContextAsync(CancellationToken cancellationToken)
    {
        var interfaceName = SelectedCaptureDevice?.Description ?? "Nenhuma interface selecionada";
        var enabledRows = _fullTestClients.Sum(x => x.Rows.Count(r => r.Enabled));
        var serverRanges = ServerMapRanges.Count(x => x.Enabled);
        var details = string.Join(Environment.NewLine, [
            $"Papel do teste: {FullTestModeLabel}",
            $"Escopo desta execucao: {FullTestRunScope}",
            $"Clientes em leitura simultanea: {RunningClientCount}",
            $"Servidor local: {ServerEndpoint} ({ServerConnectionStatus})",
            $"Interface de captura: {interfaceName}",
            $"Filtro BPF atual: {GeneratedCaptureFilter}",
            $"Captura passiva ativa: {(IsNetworkCaptureRunning ? "sim" : "nao")}",
            $"Servidor ativo: {(IsServerRunning ? "sim" : "nao")}",
            $"Alvos incluidos em leitura: {_fullTestClients.Count(x => x.IsScanning)}",
            $"Linhas habilitadas no mapa client: {enabledRows}",
            $"Faixas habilitadas no server simulado: {serverRanges}",
            "Inicializacao automatica:",
            _fullTestStartupActions.Count == 0 ? "Nenhuma acao automatica registrada." : string.Join(Environment.NewLine, _fullTestStartupActions),
            "Seguranca: o teste completo executa apenas leituras Modbus. Escritas sao puladas por padrao para nao alterar PLC/equipamento."
        ]);

        var status = FullTestIsServerMode || _fullTestClients.Length > 0 || EnableActiveSubnetScan || IsNetworkCaptureRunning ? "OK" : "Atencao";
        var recommendation = status == "OK"
            ? _fullTestClients.Length == 0 && FullTestIsClientMode
                ? "Sem alvos cadastrados: a varredura autorizada e a captura determinam os servidores testaveis. Somente respostas Modbus confirmadas entram na validacao automatica."
                : "Escopo independente da selecao lateral. A execucao utiliza as configuracoes cadastradas dos alvos incluidos ou do servidor local."
            : "Sem alvo e sem sondagem ativa/captura disponivel; habilite uma fonte de descoberta para avaliar servidores remotos.";

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
            "Interpretacao:",
            InterpretRoutes(activeProfiles, defaultRouteFound),
            "",
            "Amostra de rotas IPv4:",
            string.Join(Environment.NewLine, routeOutput.Split(Environment.NewLine).Take(40))
        ]);
        var recommendation = status == "OK"
            ? "Validar correspondencia entre interface ativa, VLAN industrial e segmento IP do equipamento."
            : "Validar configuracao IPv4 local: estado da interface, mascara, gateway, VLAN e tabela de rotas.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private async Task<FullTestStepResult> RunPassiveInventoryAsync(CancellationToken cancellationToken)
    {
        var result = await ObserveTrafficWindowAsync(true, cancellationToken);
        StartModbusRuntimeAfterBaseline();
        return result;
    }

    private Task<FullTestStepResult> RunArpSnapshotAsync(CancellationToken cancellationToken) => RunScopedArpAsync(cancellationToken);


    private Task<FullTestStepResult> RunHostDiscoveryAsync(CancellationToken cancellationToken) => RunScopedHostDiscoveryAsync(cancellationToken);


    private Task<FullTestStepResult> RunModbusDiscoveryAsync(CancellationToken cancellationToken) => RunConfirmedModbusDiscoveryAsync(cancellationToken);


    private async Task<FullTestStepResult> RunMapDiscoveryAsync(CancellationToken cancellationToken)
    {
        DiscoveredMapRows.Clear();
        if (!FullTestIsServerMode) return await RunClientActiveMapDiscoveryAsync(cancellationToken);
        var requested = await RunServerRequestedMapDiscoveryAsync(cancellationToken);
        var active = await RunClientActiveMapDiscoveryAsync(cancellationToken);
        return new FullTestStepResult(active.Status == "OK" || requested.Status == "OK" ? "OK" : "Inconclusivo",
            "Clientes observados:\n" + requested.Detail + "\nServidores descobertos na rede:\n" + active.Detail,
            "Mapas de servidores obtidos por leitura; mapas de clientes limitados as requisicoes observadas.");
    }

    private async Task<FullTestStepResult> RunClientActiveMapDiscoveryAsync(CancellationToken cancellationToken)
    {
        var endpoints = BuildMapDiscoveryEndpoints();
        if (endpoints.Count == 0)
        {
            return new FullTestStepResult(
                "Atencao",
                "Nenhum endpoint Modbus candidato encontrado para descoberta ativa de mapa.",
                "Execute descoberta Modbus ou configure alvo/porta antes de habilitar descoberta de mapa.");
        }

        var maxAddress = Math.Clamp(MapDiscoveryMaxAddress, 0, ushort.MaxValue);
        var blockSize = Math.Clamp(MapDiscoveryBlockSize, 1, 120);
        var timeoutMs = Math.Clamp(ActiveScanTimeoutMs, 200, 3000);
        var unitIds = BuildMapDiscoveryUnitIds();
        var functions = BuildSelectedMapDiscoveryFunctions();
        if (functions.Count == 0)
        {
            return new FullTestStepResult(
                "Atencao",
                "Nenhum function code foi selecionado para descoberta ativa de mapa.",
                "Selecione ao menos um FC de leitura: FC01, FC02, FC03 ou FC04.");
        }

        var requests = 0;
        var successfulRanges = 0;
        var failures = new List<string>();

        foreach (var endpoint in endpoints)
        {
            foreach (var unitId in EnableMapDiscoveryUnitSweep ? unitIds
                : new[] { _discoveredUnitIds.GetValueOrDefault($"{endpoint.Ip}:{endpoint.Port}", ClientSessions.FirstOrDefault(x => x.Address == endpoint.Ip && x.Port == endpoint.Port)?.UnitId ?? (byte)1) })
            {
                foreach (var functionCode in functions)
                {
                    var discovered = new List<MapRangeProbe>();
                    var unsupportedFunction = false;
                    var transportFailures = 0;

                    for (var start = 0; start <= maxAddress && !unsupportedFunction; start += blockSize)
                    {
                        var quantity = (ushort)Math.Min(blockSize, maxAddress - start + 1);
                        var startAddress = (ushort)start;
                        requests++;
                        var blockProbe = await ProbeMapRangeAsync(endpoint.Ip, endpoint.Port, unitId, functionCode, startAddress, quantity, timeoutMs, cancellationToken);

                        if (blockProbe.Success)
                        {
                            transportFailures = 0;
                            discovered.Add(new MapRangeProbe(startAddress, (ushort)(startAddress + quantity - 1), blockProbe.Sample));
                            continue;
                        }

                        if (IsUnsupportedFunction(blockProbe.Error))
                        {
                            unsupportedFunction = true;
                            failures.Add($"{endpoint.Ip}:{endpoint.Port} UID {unitId} FC{functionCode}: function code nao suportado.");
                            break;
                        }

                        if (IsTransportFailure(blockProbe.Error))
                        {
                            transportFailures++;
                            failures.Add($"{endpoint.Ip}:{endpoint.Port} UID {unitId} FC{functionCode} addr={startAddress} qty={quantity}: {blockProbe.Error}");
                            if (transportFailures >= 2)
                            {
                                break;
                            }
                            continue;
                        }

                        if (!EnableMapDiscoveryPointFallback || blockSize <= 1)
                        {
                            continue;
                        }

                        for (var address = start; address < start + quantity; address++)
                        {
                            requests++;
                            var pointProbe = await ProbeMapRangeAsync(endpoint.Ip, endpoint.Port, unitId, functionCode, (ushort)address, 1, timeoutMs, cancellationToken);
                            if (pointProbe.Success)
                            {
                                discovered.Add(new MapRangeProbe((ushort)address, (ushort)address, pointProbe.Sample));
                            }
                            else if (IsUnsupportedFunction(pointProbe.Error))
                            {
                                unsupportedFunction = true;
                                break;
                            }
                        }
                    }

                    foreach (var range in MergeMapRanges(discovered))
                    {
                        successfulRanges++;
                        DiscoveredMapRows.Add(new MapDiscoveryRow
                        {
                            Endpoint = $"{endpoint.Ip}:{endpoint.Port}",
                            UnitId = unitId.ToString(),
                            Function = FormatFunctionCode(functionCode),
                            StartAddress = range.Start,
                            EndAddress = range.End,
                            Quantity = range.End - range.Start + 1,
                            DiscoveryMode = "Ativo/client",
                            Confidence = range.Start == range.End ? "Ponto validado" : "Range validado",
                            Notes = $"Leitura read-only OK. Amostra: {range.Sample}"
                        });
                    }
                }
            }
        }

        var status = DiscoveredMapRows.Count > 0 ? "OK" : "Atencao";
        var details = string.Join(Environment.NewLine, [
            $"Modo: Client ativo/read-only",
            $"Endpoints avaliados: {endpoints.Count}",
            $"Unit IDs avaliados: {FormatUnitIdList(unitIds)}",
            $"Function codes avaliados: {string.Join(", ", functions.Select(x => FormatFunctionCode(x)))}",
            $"Endereco inicial: 0",
            $"Endereco maximo: {maxAddress}",
            $"Bloco de leitura: {blockSize}",
            $"Fallback ponto a ponto: {(EnableMapDiscoveryPointFallback ? "habilitado" : "desabilitado")}",
            $"Timeout por request: {timeoutMs} ms",
            $"Requests executados: {requests}",
            $"Ranges descobertos: {successfulRanges}",
            "",
            "Interpretacao:",
            InterpretActiveMapDiscovery(endpoints.Count, requests, successfulRanges),
            "",
            DiscoveredMapRows.Count == 0
                ? "Nenhum range respondeu com leitura valida."
                : string.Join(Environment.NewLine, DiscoveredMapRows.Take(120).Select(x => $"{x.Endpoint} UID {x.UnitId} {x.Function} {x.StartAddress}-{x.EndAddress} ({x.Quantity}) | {x.Confidence} | {x.Notes}")),
            failures.Count == 0 ? "" : "",
            failures.Count == 0 ? "" : "Falhas/amostras tecnicas:",
            failures.Count == 0 ? "" : string.Join(Environment.NewLine, failures.Take(40))
        ]);
        var recommendation = status == "OK"
            ? "Comparar os ranges descobertos com o mapa de engenharia; repetir com maior endereco maximo se houver suspeita de mapa acima do limite configurado."
            : "Validar Unit ID, porta, endereco base zero/um, bloqueio de function code e se o dispositivo permite leitura dos ranges testados.";

        return new FullTestStepResult(status, details, recommendation);
    }

    private Task<FullTestStepResult> RunServerRequestedMapDiscoveryAsync(CancellationToken cancellationToken)
        => ObserveRequestedServerMapAsync(cancellationToken);


    private async Task<FullTestStepResult> RunTcpConnectivityAsync(CancellationToken cancellationToken, ClientConnectionSession? session)
    {
        if (FullTestIsServerMode)
        {
            return new FullTestStepResult(IsServerRunning ? "OK" : "Falha",
                $"Servidor local {ServerEndpoint}: {(IsServerRunning ? "escutando" : "parado")}. Esta verificacao nao comprova acesso a partir de outros hosts.",
                "A acessibilidade externa depende das requisicoes recebidas, firewall e caminho de rede.");
        }
        session ??= CurrentTestTargets().FirstOrDefault() ?? throw new InvalidOperationException("Nenhum alvo incluido no teste.");
        var TargetIp = session.Address;
        var Port = session.Port;
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
            string.Join(Environment.NewLine, [
                $"Socket TCP abriu em {started.ElapsedMilliseconds} ms para {TargetIp}:{Port}.",
                "",
                "Interpretacao:",
                "Abertura TCP confirmada. Esse tempo inclui estabelecimento do socket, nao resposta Modbus nem varredura do PLC. Comparar com referencia do mesmo caminho; nao ha limiar universal de latencia industrial aplicado aqui."
            ]),
            "Conectividade TCP basica OK. Se Modbus falhar, investigue Unit ID, mapa, function code, gateway ou resposta de aplicacao.");
    }

    private Task<FullTestStepResult> RunTrafficLoadAsync(CancellationToken cancellationToken)
        => ObserveTrafficWindowAsync(false, cancellationToken);

    private Task<FullTestStepResult> RunTopologyInferenceAsync(CancellationToken cancellationToken) => RunScopedTopologyAsync(cancellationToken);


    private Task<FullTestStepResult> RunClientMapValidationAsync(CancellationToken cancellationToken, ClientConnectionSession? session)
    {
        if (FullTestIsServerMode)
        {
            var points = _serverMap.ToPoints();
            return Task.FromResult(new FullTestStepResult(points.Count == 0 ? "Inconclusivo" : "OK",
                $"Mapa local: {points.Count} pontos, {points.Count(x => x.IsWritable)} gravaveis. Apenas configuracao local, nao valida requisicoes externas.",
                "A validacao externa depende de clientes externos e da etapa envio/recebimento."));
        }
        return ValidateClientRepeatedAsync(session ?? CurrentTestTargets().FirstOrDefault() ?? throw new InvalidOperationException("Nenhum alvo incluido no teste."), cancellationToken);
    }

    private async Task<FullTestStepResult> RunSendReceiveValidationAsync(CancellationToken cancellationToken, ClientConnectionSession? session)
    {
        if (FullTestIsServerMode)
        {
            return new FullTestStepResult(_serverWindowExceptions > 0 ? "Falha" : _serverWindowRequests == 0 ? "Inconclusivo"
                    : _serverWindowReplies != _serverWindowRequests || _serverWindowUnmatched > 0 ? "Inconclusivo" : "OK",
                $"Contadores da execucao (independentes da lista de 500 eventos): {_serverWindowRequests} requisicoes, {_serverWindowReplies} respostas correlacionadas, {_serverWindowExceptions} exceptions. Pendentes: {_serverPendingRequests.Count}; sem correlacao/limite: {_serverWindowUnmatched}. Sondagens proprias excluidas. Sem requisicoes externas, nao ha validacao de clientes.",
                "Correlacao por endpoint/TID/UID. Pendentes ao fim podem estar em transito, nao comprovam timeout por si mesmos. Consultar tempos e blocos no relatorio.");
        }
        session ??= CurrentTestTargets().FirstOrDefault() ?? throw new InvalidOperationException("Nenhum alvo incluido no teste.");
        var _client = session.Client;
        var TargetIp = session.Address;
        var Port = session.Port;
        var UnitId = session.UnitId;
        var row = session.Rows.FirstOrDefault(x => x.Enabled);
        if (row is null) return new FullTestStepResult("Nao aplicavel", "Nenhum bloco habilitado; nao foi sondado um endereco arbitrario.", "Configurar o mapa do alvo para testar request/response.");

        if (row.FunctionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs)
        {
            var values = await _client.ReadBitsAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
            return new FullTestStepResult(
                "OK",
                string.Join(Environment.NewLine, [
                    $"Request/response read-only OK em {TargetIp}:{Port} UID {UnitId}, FC{row.FunctionCode}, addr {row.StartAddress}, qty {row.Quantity}. Valores: {string.Join(", ", values.Select(x => x ? "1" : "0"))}",
                    "",
                    "Interpretacao:",
                    "Criterio: transacao Modbus read-only completa. Resultado: request enviado e response valida recebida. Implicacao: socket TCP, MBAP, Unit ID e function code da linha testada estao funcionais; falhas em outras linhas devem ser isoladas por range, offset, FC ou quantidade."
                ]),
                "Envio e recebimento Modbus confirmados sem escrita. Para teste de escrita, configure futuramente um ponto seguro de teste.");
        }

        var registers = await _client.ReadRegistersAsync(TargetIp, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
        return new FullTestStepResult(
            "OK",
            string.Join(Environment.NewLine, [
                $"Request/response read-only OK em {TargetIp}:{Port} UID {UnitId}, FC{row.FunctionCode}, addr {row.StartAddress}, qty {row.Quantity}. Valores: {string.Join(", ", registers)}",
                "",
                "Interpretacao:",
                "Criterio: transacao Modbus read-only completa. Resultado: request enviado e response valida recebida. Implicacao: socket TCP, MBAP, Unit ID e function code da linha testada estao funcionais; falhas em outras linhas devem ser isoladas por range, offset, FC ou quantidade."
            ]),
            "Envio e recebimento Modbus confirmados sem escrita. Para teste de escrita, configure futuramente um ponto seguro de teste.");
    }

    private Task<FullTestStepResult> RunObservedFailuresAsync(CancellationToken cancellationToken)
    {
        var warnings = _runWarnings.Values.ToList();
        var status = warnings.Any(x => x.Severity is "Falha" or "Erro")
            ? "Falha"
            : warnings.Count > 0 ? "Atencao" : "OK";

        var details = string.Join(Environment.NewLine, [
            $"Avisos importantes consolidados: {warnings.Count}",
            $"Escopo: eventos desde {_fullTestStartedAt:HH:mm:ss}, somente sessoes testadas. Historico e outros alvos nao reprovam esta execucao.",
            $"Sessoes testadas: {(_fullTestClients.Length == 0 ? "Servidor local " + ServerEndpoint : string.Join(", ", _fullTestClients.Select(x => $"{x.Name} {x.Endpoint} UID {x.UnitId}")))}",
            warnings.Count == 0 ? "Sem avisos importantes nesta execucao." : string.Join(Environment.NewLine, warnings.Select(x => $"{x.Severity} x{x.Count}: {x.Title} - {x.LatestDetail}")),
            "Checks globais do Registro de avisos nao sao usados para reprovar este teste. As validacoes por alvo constam nas etapas TCP, mapa e envio/recebimento.",
            "",
            "Interpretacao:",
            InterpretObservedFailures(warnings)
        ]);
        var recommendation = status == "OK"
            ? "Nenhum evento classificado como falha/atencao foi consolidado na janela analisada."
            : "Ordenar investigacao por severidade: erro/falha, exception Modbus, acesso fora de mapa, polling e carga.";

        return Task.FromResult(new FullTestStepResult(status, details, recommendation));
    }

    private Task<FullTestStepResult> RunFullTestConclusionAsync(CancellationToken cancellationToken)
    {
        var steps = FullTestSteps.Where(x => x.Name != "Conclusao").ToList();
        var faults = steps.Where(x => x.Status is "Falha" or "Erro").ToList();
        var warnings = steps.Where(x => x.Status == "Atencao").ToList();
        var unknown = steps.Where(x => x.Status == "Inconclusivo").ToList();
        var notApplicable = steps.Count(x => x.Status == "Nao aplicavel");
        var status = faults.Count > 0 ? "Falha" : warnings.Count > 0 ? "Atencao" : unknown.Count > 0 ? "Inconclusivo" : "OK";
        return Task.FromResult(new FullTestStepResult(status,
            $"Agregacao, nao nova transacao: falhas {faults.Count}, atencoes {warnings.Count}, inconclusivas {unknown.Count}, nao aplicaveis {notApplicable}."
            + Environment.NewLine + $"Falhas: {string.Join(", ", faults.Select(x => x.Name))}."
            + Environment.NewLine + $"Sem evidencia suficiente: {string.Join(", ", unknown.Select(x => x.Name))}."
            + Environment.NewLine + FullTestCoverage
            + Environment.NewLine + "Resultado restrito aos alvos, blocos e janelas executados. Falha em um alvo nao invalida os resultados independentes dos demais. Topologia fisica e saude de todos os enlaces nao foram validadas.",
            faults.Count > 0 ? "Investigar as evidencias por alvo/bloco; repetir com os mesmos parametros apos a correcao."
                : "Comparar com referencia e ampliar observacao se houver suspeita de intermitencia. Etapas inconclusivas requerem instrumentacao/dados adicionais."));
    }


    private string BuildTechnicalAppendix()
    {
        var builder = new StringBuilder();
        builder.AppendLine("### Parametros de coleta");
        builder.AppendLine($"Interface: {SelectedCaptureDevice?.Description ?? "indisponivel"}. BPF: `{GeneratedCaptureFilter}`.");
        builder.AppendLine($"Referencia: {PassiveObservationSeconds} s; monitor TCP: {TcpMonitoringSeconds} s; {ReadValidationAttempts} tentativas/bloco; intervalo adicional: {ReadValidationIntervalMs} ms.");
        builder.AppendLine($"Sondagem: {(EnableActiveSubnetScan ? ActiveScanCidr : "desabilitada")}; portas: {ModbusDiscoveryPorts}; limite: {ProbeRatePerSecond}/s; concorrencia: {ActiveScanConcurrency}.");
        builder.AppendLine();
        builder.AppendLine("### Checklist da execucao");
        builder.AppendLine("| Etapa | Resultado | Duracao s |");
        builder.AppendLine("|---|---|---:|");
        foreach (var step in FullTestSteps)
        {
            var duration = step.StartedAt is not null && step.FinishedAt is not null
                ? $"{(step.FinishedAt.Value - step.StartedAt.Value).TotalSeconds:0.0}" : "-";
            builder.AppendLine($"| {step.Order}. {EscapeMarkdownTable(step.Name)} | {step.Status} | {duration} |");
        }
        builder.AppendLine();
        var detailed = FullTestSteps.Where(x => x.Name != "Conclusao" && x.Status is not ("OK" or "Nao aplicavel")).ToList();
        if (detailed.Count > 0) builder.AppendLine("### Evidencias adicionais das etapas com ocorrencias");
        foreach (var step in detailed)
        {
            builder.AppendLine($"#### {step.Order}. {step.Name} - {step.Status}");
            builder.AppendLine("```text");
            builder.AppendLine(step.Result);
            builder.AppendLine("```");
        }
        foreach (var stat in _blockReadStatistics.Where(x => x.Errors.Count > 0))
            builder.AppendLine($"- {stat.Endpoint} / UID {stat.UnitId}: {stat.Describe()}");
        var relevant = NetworkDiscoveryRows.Where(x => !x.IsSpecialAddress
            && (x.IsModbusConfirmed || x.IsModbusObserved || !string.IsNullOrWhiteSpace(x.OpenTcpPorts))).OrderBy(x => IpSortKey(x.Ip)).ToList();
        if (relevant.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Dispositivos com evidencia de servico");
            builder.AppendLine($"{relevant.Count} entradas com Modbus ou porta TCP aberta. Inventario completo disponivel na aba Dispositivos do teste.");
            builder.AppendLine("| IP | Papel / evidencia | TCP aberto | Modbus confirmado | Modbus observado |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var row in relevant)
                builder.AppendLine($"| {row.Ip} | {EscapeMarkdownTable(row.RoleGuess)} | {row.OpenTcpPorts} | {row.ConfirmedModbusPorts} | {row.ObservedModbusPorts} |");
            builder.AppendLine("Porta aberta nao confirma protocolo. Modbus observado passivamente nao equivale a uma validacao ativa do mapa.");
        }
        if (DiscoveredMapRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Faixas de mapa descobertas");
            builder.AppendLine("| Endpoint / UID | Funcao | Inicio - Fim | Confianca |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var row in DiscoveredMapRows.OrderBy(x => x.Endpoint).ThenBy(x => x.UnitId).ThenBy(x => x.Function).ThenBy(x => x.StartAddress))
                builder.AppendLine($"| {row.Endpoint} / {row.UnitId} | {EscapeMarkdownTable(row.Function)} | {row.StartAddress} - {row.EndAddress} | {EscapeMarkdownTable(row.Confidence + "; " + row.Notes)} |");
        }
        return builder.ToString();
    }

    private static string EscapeMarkdownTable(string value)
    {
        return value.Replace("|", "\\|").Replace(Environment.NewLine, " ");
    }

    private void UpdateFullTestSummaryCards()
    {
        var steps = FullTestSteps.ToList();
        var completed = steps.Count(x => x.Status is "OK" or "Atencao" or "Falha" or "Erro" or "Inconclusivo" or "Nao aplicavel" or "Cancelado");
        FullTestTotalSteps = steps.Count;
        FullTestCompletedSteps = completed;
        FullTestProgressPercent = steps.Count == 0 ? 0 : (int)Math.Round(100d * completed / steps.Count);
        FullTestOkCount = steps.Count(x => x.Status == "OK");
        FullTestWarningCount = steps.Count(x => x.Status == "Atencao");
        FullTestFailureCount = steps.Count(x => x.Status is "Falha" or "Erro");
        FullTestInconclusiveCount = steps.Count(x => x.Status == "Inconclusivo");
        FullTestNotApplicableCount = steps.Count(x => x.Status == "Nao aplicavel");
        FullTestScore = $"{completed}/{steps.Count} etapas processadas";
        FullTestCoverage = $"Blocos exercitados: {_blockReadStatistics.Count(x => x.Attempted > 0)}; leituras validas/tentadas: {_blockReadStatistics.Sum(x => x.Success)}/{_blockReadStatistics.Sum(x => x.Attempted)}. Referencia: {_baselineTraffic?.Seconds ?? 0:0.0} s; monitor TCP: {_operationalTraffic?.Seconds ?? 0:0.0} s. Inconclusivas: {FullTestInconclusiveCount}; nao aplicaveis: {FullTestNotApplicableCount}.";
        FullTestOverallStatus = IsFullTestRunning && completed < steps.Count ? "Executando"
            : steps.Any(x => x.Status == "Cancelado") ? "Cancelado"
            : completed == 0 ? "Aguardando"
            : FullTestFailureCount > 0 ? "Falha" : FullTestWarningCount > 0 ? "Atencao"
            : FullTestInconclusiveCount > 0 || completed < steps.Count ? "Inconclusivo" : "OK";
        RefreshTestExperience();
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

    private static string InterpretRoutes(IReadOnlyCollection<NetworkInterfaceProfile> activeProfiles, bool defaultRouteFound)
    {
        if (activeProfiles.Count == 0)
        {
            return "Criterio: existencia de interface IPv4 operacional. Resultado: zero interfaces ativas. Implicacao: qualquer falha posterior de TCP/ARP/Modbus pode ser consequencia de conectividade local ausente.";
        }

        var industrialCandidates = activeProfiles.Where(x => IsPrivateIPv4(x.Address)).ToList();
        var gatewayText = activeProfiles.Any(x => IsIPv4(x.Gateway))
            ? "gateway IPv4 presente"
            : "gateway IPv4 ausente";
        var routeText = defaultRouteFound
            ? "rota default presente"
            : "rota default ausente";

        return $"Criterio: interfaces IPv4, gateway e rota default. Resultado: {activeProfiles.Count} interface(s) ativa(s), {industrialCandidates.Count} em RFC1918/APIPA, {gatewayText}, {routeText}. Implicacao: se o alvo estiver fora da sub-rede local e nao houver gateway/rota, a comunicacao depende de roteamento ausente ou configuracao manual.";
    }

    private static string InterpretPassiveInventory(int packetCount, int endpointCount, int modbusRows, int publicEndpointCount, string dominantProtocol, int dominantProtocolCount)
    {
        var modbusPct = FormatPercent(modbusRows, packetCount);
        var dominantPct = FormatPercent(dominantProtocolCount, packetCount);
        var publicText = publicEndpointCount > 0
            ? $"Endpoint(s) publico(s): {publicEndpointCount}; isso indica trafego roteado/externo presente na interface capturada."
            : "Endpoint(s) publico(s): 0; amostra sem trafego externo detectado.";
        var modbusText = modbusRows == 0
            ? "Modbus/TCP passivo: 0 pacote; a captura nao observou ciclo Modbus nesta janela."
            : $"Modbus/TCP passivo: {modbusPct} da amostra; o restante e trafego concorrente ou nao classificado como Modbus.";

        return $"Criterio: distribuicao de protocolos/endpoints na janela passiva. Resultado: {packetCount} pacote(s), {endpointCount} endpoint(s), protocolo dominante {dominantProtocol} ({dominantPct}). Implicacao: {modbusText} {publicText}";
    }

    private static string InterpretArp(int entries, bool targetSeen)
    {
        if (entries == 0)
        {
            return "Criterio: entradas ARP IPv4 locais. Resultado: zero entradas parseaveis. Implicacao: nao ha evidencia L2 suficiente para confirmar presenca do alvo ou dos ativos no mesmo segmento.";
        }

        return targetSeen
            ? $"Criterio: presenca do alvo na tabela ARP. Resultado: alvo presente em {entries} entrada(s). Implicacao: resolucao L2 do alvo confirmada; falhas restantes tendem a estar acima de ARP ou em conflito intermitente."
            : $"Criterio: presenca do alvo na tabela ARP. Resultado: {entries} entrada(s), alvo ausente. Implicacao: sem evidencia de resolucao L2 para o alvo; causas provaveis incluem alvo fora do segmento, IP incorreto, VLAN/mascara incorreta ou ausencia de trafego ARP recente.";
    }

    private static string InterpretHostDiscovery(int candidates, int active, int modbusOpen)
    {
        if (candidates == 0)
        {
            return "Criterio: lista de candidatos para probe. Resultado: zero candidatos. Implicacao: etapa inconclusiva por ausencia de universo de teste.";
        }

        var activePct = FormatPercent(active, candidates);
        if (active == 0)
        {
            return $"Criterio: resposta por ICMP ou abertura TCP. Resultado: 0/{candidates} candidatos ativos. Implicacao: probe bloqueado por firewall/politica, candidatos fora de segmento, interface incorreta ou ausencia real de hosts.";
        }

        var modbusText = modbusOpen == 0
            ? "Porta Modbus/configurada aberta: 0 nos hosts ativos."
            : $"Porta Modbus/configurada aberta: {modbusOpen} host(s).";
        return $"Criterio: resposta ICMP/TCP e abertura de porta. Resultado: {active}/{candidates} candidatos ativos ({activePct}). {modbusText} Implicacao: hosts ativos nao documentados indicam divergencia de inventario; porta Modbus aberta identifica superficie de comunicacao industrial.";
    }

    private static string InterpretModbusDiscovery(int hostCount, IReadOnlyCollection<(string Ip, int Port, bool Open)> openEndpoints, int passiveModbusHosts)
    {
        if (openEndpoints.Count == 0 && passiveModbusHosts == 0)
        {
            return "Criterio: porta TCP Modbus/configurada aberta ou Modbus passivo observado. Resultado: zero endpoints. Implicacao: sem evidencia de servico Modbus na amostra; causas incluem porta nao padrao, firewall, VLAN incorreta, filtro de captura ou servico inativo.";
        }

        var uniqueHosts = openEndpoints.Select(x => x.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return $"Criterio: abertura TCP em porta Modbus/configurada e assinatura passiva. Resultado: {uniqueHosts}/{hostCount} host(s) com porta aberta ({FormatPercent(uniqueHosts, Math.Max(1, hostCount))}); {passiveModbusHosts} host(s) com Modbus passivo. Implicacao: cada endpoint deve corresponder a PLC, gateway ou simulador conhecido; endpoint nao inventariado representa superficie Modbus nao documentada.";
    }

    private static string InterpretActiveMapDiscovery(int endpoints, int requests, int successfulRanges)
    {
        if (endpoints == 0)
        {
            return "Criterio: endpoints Modbus candidatos. Resultado: zero endpoints. Implicacao: descoberta ativa de mapa nao possui alvo tecnico.";
        }

        if (successfulRanges == 0)
        {
            return $"Criterio: leitura read-only FC01-FC04 por blocos e fallback por ponto. Resultado: {requests} request(s), zero ranges validos. Implicacao: Unit ID/porta/range podem estar incorretos, function codes podem estar bloqueados ou o equipamento pode responder apenas em enderecos acima do limite configurado.";
        }

        return $"Criterio: leitura read-only FC01-FC04 por blocos e fallback por ponto. Resultado: {successfulRanges} range(s) validado(s) em {endpoints} endpoint(s), {requests} request(s). Implicacao: ranges listados representam areas que aceitaram leitura no Unit ID configurado; nao provam inexistencia de ranges fora do limite maximo testado.";
    }

    private static string InterpretServerRequestedMapDiscovery(int requests, int distinctRanges)
    {
        if (requests == 0)
        {
            return "Criterio: requests Modbus recebidos pelo server simulado durante a janela. Resultado: zero requests. Implicacao: nenhum cliente consumiu este servidor durante a etapa, ou a janela foi menor que o ciclo real de polling.";
        }

        return $"Criterio: requests Modbus client->server recebidos. Resultado: {requests} request(s), {distinctRanges} range(s) distinto(s). Implicacao: tabela representa o mapa efetivamente consumido pelos clientes, inclusive ranges inexistentes no mapa simulado e escritas observadas.";
    }

    private static string InterpretTrafficLoad(double packetsPerSecond, double bytesPerSecond, int totalPackets, int modbusPackets, int publicPackets, int broadcastOrMulticastPackets, IReadOnlyList<string> topTalkers)
    {
        var loadLevel = packetsPerSecond switch
        {
            < 200 => "baixa",
            < 1000 => "moderada",
            < 5000 => "alta",
            _ => "muito alta"
        };

        var modbusPct = FormatPercent(modbusPackets, totalPackets);
        var publicPct = FormatPercent(publicPackets, totalPackets);
        var broadcastPct = FormatPercent(broadcastOrMulticastPackets, totalPackets);
        var attention = new List<string>();

        if (publicPackets > totalPackets * 0.25)
        {
            attention.Add($"trafego com IP publico={publicPct}, acima do limiar de 25%; indica mistura de trafego roteado/corporativo na interface capturada.");
        }
        if (broadcastOrMulticastPackets > totalPackets * 0.20)
        {
            attention.Add($"broadcast/multicast={broadcastPct}, acima do limiar de 20%; indica carga de descoberta, multicast ou broadcast storm parcial.");
        }
        if (modbusPackets == 0)
        {
            attention.Add("Modbus/TCP=0%; a carga medida nao caracteriza ciclo Modbus nesta janela.");
        }
        if (packetsPerSecond > 1000)
        {
            attention.Add("taxa >1000 pkt/s; correlacionar com abrangencia do SPAN, CPU local e eventos de broadcast/multicast.");
        }

        var topTalkerText = topTalkers.Count == 0
            ? "Sem conversa dominante."
            : $"Top conversa: {topTalkers[0]}.";
        var attentionText = attention.Count == 0
            ? "Nenhum limiar interno excedido nesta amostra."
            : string.Join(" ", attention);

        return $"Criterio: taxa de pacotes, volume, composicao e top talkers. Resultado: carga {loadLevel}, {packetsPerSecond:0.0} pkt/s, {bytesPerSecond / 1024:0.0} KB/s, Modbus={modbusPct}. Implicacao: {topTalkerText} {attentionText}";
    }

    private static string InterpretTopology(int gatewayCount, int knownHosts, int modbusHosts, int passiveOnly)
    {
        if (knownHosts == 0)
        {
            return "Nao ha dispositivos consolidados suficientes para inferir topologia. Capture trafego ou rode ARP/varredura com a interface correta.";
        }

        var gatewayText = gatewayCount == 0
            ? "gateway=0; rede pode ser ilha L2 ou host sem rota configurada."
            : $"gateway={gatewayCount}; ha ponto(s) de roteamento detectado(s).";
        return $"Criterio: gateway, hosts consolidados, endpoints Modbus e hosts passivos. Resultado: {gatewayText} hosts={knownHosts}, possiveis Modbus={modbusHosts}, somente passivos={passiveOnly}. Implicacao: topologia logica parcial disponivel; topologia fisica de switch L2 requer SNMP/LLDP/CDP ou tabela MAC do switch.";
    }

    private static string InterpretMapValidation(int ok, int failures, int total, IReadOnlyList<string> failureMessages)
    {
        if (total == 0)
        {
            return "Criterio: linhas habilitadas no mapa. Resultado: zero linhas. Implicacao: etapa sem validade diagnostica para ranges Modbus.";
        }

        if (failures == 0)
        {
            return $"Criterio: leitura read-only de todas as linhas habilitadas. Resultado: 100% sucesso. Implicacao: IP, porta, Unit ID, function code, endereco inicial e quantidade estao coerentes para as linhas testadas.";
        }

        var exception2 = failureMessages.Count(x => x.Contains("exception 2", StringComparison.OrdinalIgnoreCase));
        var timeout = failureMessages.Count(x => x.Contains("timeout", StringComparison.OrdinalIgnoreCase) || x.Contains("timed", StringComparison.OrdinalIgnoreCase));
        var causeHints = new List<string>();
        if (exception2 > 0)
        {
            causeHints.Add($"{exception2} falha(s) parecem Illegal Data Address/exception 02, normalmente range inexistente, offset base 0/1 incorreto ou function code errado.");
        }
        if (timeout > 0)
        {
            causeHints.Add($"{timeout} falha(s) parecem timeout, normalmente conectividade, firewall, equipamento ocupado ou porta errada.");
        }

        return $"Criterio: leitura read-only por linha habilitada. Resultado: {ok}/{total} linha(s) responderam ({FormatPercent(ok, total)}). Implicacao: {string.Join(" ", causeHints.DefaultIfEmpty("classificar falhas por exception Modbus, timeout ou erro de socket para separar erro de mapa de erro de transporte."))}";
    }

    private static string InterpretObservedFailures(IReadOnlyList<ImportantWarningSummary> warnings)
    {
        if (warnings.Count == 0)
        {
            return "Criterio: eventos consolidados das sessoes testadas nesta execucao. Resultado: zero eventos classificados. Implicacao: os sintomas monitorados nao foram registrados na janela analisada; isso nao comprova estabilidade fora dessa janela.";
        }

        var critical = warnings.Count(x => x.Severity is "Falha" or "Erro");
        var attention = warnings.Count - critical;
        return $"Criterio: severidade agregada. Resultado: criticos={critical}, atencao={attention}. Implicacao: falhas de transporte/mapa invalidam conclusao de estabilidade; itens de polling/carga devem ser analisados apos eliminar exceptions e timeouts.";
    }

    private static string InterpretConclusion(int total, int failures, int warnings)
    {
        if (total == 0)
        {
            return "Criterio: etapas concluidas. Resultado: zero etapas. Implicacao: relatorio sem validade diagnostica.";
        }

        if (failures > 0)
        {
            return $"Criterio: etapas com status Falha/Erro nesta execucao. Resultado: {failures}/{total}. Implicacao: houve falha no escopo testado; consultar as etapas e os alvos identificados. Falha em um servidor nao implica falha nos demais.";
        }

        if (warnings > 0)
        {
            return $"Criterio: etapas com status Atencao. Resultado: {warnings}/{total}. Implicacao: comunicacao parcialmente validada, com divergencias ou baixa confianca em inventario/captura/carga.";
        }

        return "Criterio: status final agregado. Resultado: zero falhas e zero atencoes. Implicacao: referencia tecnica valida apenas para as condicoes, filtros e janela de observacao executados.";
    }

    private List<string> BuildDiscoveryCandidates() => AuthorizedCandidates();

    private IReadOnlyList<MapDiscoveryEndpoint> BuildMapDiscoveryEndpoints()
    {
        var endpoints = new HashSet<(string Address, int Port)>();
        foreach (var confirmed in _confirmedModbusEndpoints)
            if (IPEndPoint.TryParse(confirmed, out var ep) && NetworkDiscoveryRows.Any(row => row.Ip == ep.Address.ToString()
                && row.IsModbusConfirmed && row.ConfirmedModbusPorts.Split(',').Any(port => port.Trim() == ep.Port.ToString())))
                endpoints.Add((ep.Address.ToString(), ep.Port));
        foreach (var session in FullTestIsClientMode ? CurrentTestTargets() : [])
            endpoints.Add((session.Address, session.Port));
        return endpoints.Where(x => IsIPv4(x.Address) && x.Port is > 0 and <= 65535)
            .OrderBy(x => IpSortKey(x.Address)).ThenBy(x => x.Port)
            .Select(x => new MapDiscoveryEndpoint(x.Address, x.Port)).ToList();
    }


    private IReadOnlyList<byte> BuildMapDiscoveryUnitIds()
    {
        if (!EnableMapDiscoveryUnitSweep)
        {
            return FullTestIsServerMode ? [ServerUnitId]
                : CurrentTestTargets().Select(x => x.UnitId).DefaultIfEmpty((byte)1).Distinct().ToArray();
        }

        var start = Math.Clamp(MapDiscoveryUnitIdStart, byte.MinValue, byte.MaxValue);
        var end = Math.Clamp(MapDiscoveryUnitIdEnd, byte.MinValue, byte.MaxValue);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return Enumerable.Range(start, end - start + 1)
            .Select(x => (byte)x)
            .ToList();
    }

    private IReadOnlyList<byte> BuildSelectedMapDiscoveryFunctions()
    {
        var functions = new List<byte>();
        if (MapDiscoveryFc01)
        {
            functions.Add(ModbusProtocol.ReadCoils);
        }
        if (MapDiscoveryFc02)
        {
            functions.Add(ModbusProtocol.ReadDiscreteInputs);
        }
        if (MapDiscoveryFc03)
        {
            functions.Add(ModbusProtocol.ReadHoldingRegisters);
        }
        if (MapDiscoveryFc04)
        {
            functions.Add(ModbusProtocol.ReadInputRegisters);
        }

        return functions;
    }

    private async Task<MapProbeResult> ProbeMapRangeAsync(
        string ip,
        int endpointPort,
        byte unitId,
        byte functionCode,
        ushort startAddress,
        ushort quantity,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        await PaceProbeAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            var probe = new ModbusTcpClientProbe();

            if (functionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs)
            {
                var bits = await probe.ReadBitsAsync(ip, endpointPort, unitId, functionCode, startAddress, quantity, timeout.Token);
                return new MapProbeResult(true, string.Join(",", bits.Take(8).Select(x => x ? "1" : "0")), "");
            }

            var registers = await probe.ReadRegistersAsync(ip, endpointPort, unitId, functionCode, startAddress, quantity, timeout.Token);
            return new MapProbeResult(true, string.Join(",", registers.Take(6)), "");
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return new MapProbeResult(false, "", "timeout");
        }
        catch (Exception ex)
        {
            return new MapProbeResult(false, "", ex.Message);
        }
    }

    private static IReadOnlyList<MapRangeProbe> MergeMapRanges(IEnumerable<MapRangeProbe> probes)
    {
        var ordered = probes
            .OrderBy(x => x.Start)
            .ThenBy(x => x.End)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var merged = new List<MapRangeProbe>();
        var current = ordered[0];
        foreach (var probe in ordered.Skip(1))
        {
            if (probe.Start <= current.End + 1)
            {
                current = current with { End = Math.Max(current.End, probe.End) };
                continue;
            }

            merged.Add(current);
            current = probe;
        }

        merged.Add(current);
        return merged;
    }

    private static IReadOnlyList<int> ExtractPorts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Regex.Matches(text, @"\b\d{1,5}\b")
            .Select(x => int.TryParse(x.Value, out var port) ? port : 0)
            .Where(x => x > 0 && x <= 65535)
            .Distinct()
            .ToList();
    }

    private static bool IsUnsupportedFunction(string error)
    {
        return error.Contains("exception 1", StringComparison.OrdinalIgnoreCase)
            || error.Contains("function code", StringComparison.OrdinalIgnoreCase) && error.Contains("nao suportado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransportFailure(string error)
    {
        return string.IsNullOrWhiteSpace(error)
            || error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || error.Contains("conexao", StringComparison.OrdinalIgnoreCase)
            || error.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || error.Contains("forc", StringComparison.OrdinalIgnoreCase);
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
                DeviceIdentity = DeviceIdentityFor(ip, _localIpv4Addresses),
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
        row.DeviceIdentity = DeviceIdentityFor(ip, _localIpv4Addresses);
        row.Source = MergeText(row.Source, source);
        if (!row.IsModbusConfirmed && !row.IsModbusObserved && !row.IsModbusClientObserved)
            row.RoleGuess = source.Contains("ARP") ? "Vizinho IPv4 (ARP)" : roleGuess;
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
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up
            && x.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
            && !Regex.IsMatch(x.Description, "WFP|Npcap|QoS|Filter|Virtual|Pseudo", RegexOptions.IgnoreCase)))
        {
            try
            {
                var stats = networkInterface.GetIPv4Statistics();
                snapshots[networkInterface.Id] = new InterfaceTrafficSnapshot(
                    networkInterface.Name,
                    stats.BytesReceived,
                    stats.BytesSent,
                    networkInterface.Speed,
                    stats.IncomingPacketsWithErrors + stats.OutgoingPacketsWithErrors,
                    stats.IncomingPacketsDiscarded + stats.OutgoingPacketsDiscarded);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
            _ => "Host observado (tipo desconhecido)"
        };
    }

    private static string FormatPercent(int part, int total)
    {
        return total <= 0 ? "0,0%" : $"{part * 100.0 / total:0.0}%";
    }

    private static string FormatFunctionCode(int functionCode) => functionCode switch
    {
        ModbusProtocol.ReadCoils => "FC01 Coils",
        ModbusProtocol.ReadDiscreteInputs => "FC02 Discrete Inputs",
        ModbusProtocol.ReadHoldingRegisters => "FC03 Holding Registers",
        ModbusProtocol.ReadInputRegisters => "FC04 Input Registers",
        ModbusProtocol.WriteSingleCoil => "FC05 Write Single Coil",
        ModbusProtocol.WriteSingleRegister => "FC06 Write Single Register",
        ModbusProtocol.WriteMultipleCoils => "FC15 Write Multiple Coils",
        ModbusProtocol.WriteMultipleRegisters => "FC16 Write Multiple Registers",
        _ => $"FC{functionCode:00}"
    };


    private static string FormatUnitIdList(IReadOnlyList<byte> unitIds)
    {
        if (unitIds.Count == 0)
        {
            return "nenhum";
        }
        if (unitIds.Count == 1)
        {
            return unitIds[0].ToString();
        }

        return unitIds.SequenceEqual(Enumerable.Range(unitIds[0], unitIds.Count).Select(x => (byte)x))
            ? $"{unitIds[0]}-{unitIds[^1]}"
            : string.Join(", ", unitIds);
    }

    private static bool IsIPv4(string value)
    {
        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static bool IsPrivateIPv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 127;
    }

    private static bool IsPublicIPv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        return !IsPrivateIPv4(value) && !IsBroadcastOrMulticast(value) && !value.StartsWith("0.", StringComparison.Ordinal);
    }

    private static bool IsBroadcastOrMulticast(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] >= 224 || value == "255.255.255.255";
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

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024.0:0.00} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.00} KB";
        }
        return $"{bytes} B";
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
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding,
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
            SetCheck("Escritas", "OK", $"Escrita observada: FC{trafficEvent.FunctionCode}, endereco {trafficEvent.StartAddress}; consulte a resposta para confirmar o resultado.");
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

        if (trafficEvent.Direction == TrafficDirection.ClientToServer && trafficEvent.StartAddress is not null
            && _capturePhase is not ("Sondagem" or "Validacao de mapa"))
        {
            var signature = $"{trafficEvent.SessionId}|{trafficEvent.Endpoint}|UID{trafficEvent.UnitId}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
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
                    var owner = ClientSessions.FirstOrDefault(x => x.Id == trafficEvent.SessionId);
                    var expectedMs = owner?.ScanRateMs;
                    var unexpectedRate = expectedMs is > 0 && intervals.Count >= 6 && medianMs < expectedMs.Value * 0.75;
                    var reference = expectedMs is > 0
                        ? $" Taxa configurada: {expectedMs.Value} ms; desvio: {(medianMs / expectedMs.Value - 1) * 100:0.0}%."
                        : " Periodo configurado do cliente externo desconhecido; frequencia isolada nao determina sobrecarga.";
                    SetCheck("Padrao de polling", unexpectedRate ? "Atencao" : "OK", $"FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress} qty {trafficEvent.Quantity}: mediana ~{medianMs:0} ms em {intervals.Count} amostras. Ultimo intervalo bruto: {intervalMs:0} ms.{reference}");

                    if (unexpectedRate)
                    {
                        UpsertImportantWarning(
                            $"{trafficEvent.SessionId}|polling-rapido|{signature}",
                            "Atencao",
                            "Periodo de polling inferior ao configurado",
                            $"{trafficEvent.Origin} {trafficEvent.Endpoint}: mediana {medianMs:0} ms, configurado {expectedMs} ms, para FC{trafficEvent.FunctionCode} addr {trafficEvent.StartAddress}, em {intervals.Count} amostras. Limite de sinalizacao: 75% do periodo configurado.",
                            "Verifique requisicoes concorrentes para o mesmo bloco e a origem das leituras adicionais. A divergencia de periodo nao comprova sobrecarga.",
                            trafficEvent.Timestamp);
                    }
                }
            }

            _lastRequestBySignature[signature] = trafficEvent.Timestamp;
        }

        if (trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"{trafficEvent.SessionId}|{trafficEvent.Endpoint}|UID{trafficEvent.UnitId}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
            _exceptionCounts[key] = _exceptionCounts.GetValueOrDefault(key) + 1;
            SetCheck("Exceptions repetidas", _exceptionCounts[key] >= 3 ? "Falha" : "Atencao", $"{_exceptionCounts[key]} exception(s) em {key}.");
        }

        if (trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase))
        {
            var key = $"{trafficEvent.SessionId}|{trafficEvent.Endpoint}|UID{trafficEvent.UnitId}|FC{trafficEvent.FunctionCode}|{trafficEvent.StartAddress}|{trafficEvent.Quantity}";
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
        var key = $"{trafficEvent.SessionId}|{BuildWarningKey(trafficEvent, finding)}";
        UpsertImportantWarning(key, finding.Severity, BuildWarningTitle(trafficEvent, finding), $"{trafficEvent.Origin} {trafficEvent.Endpoint}: {finding.Message}", finding.Recommendation, trafficEvent.Timestamp);
    }

    private void UpsertImportantWarning(string key, string severity, string title, string latestDetail, string recommendation, DateTimeOffset timestamp)
    {
        if (IsFullTestRunning && timestamp >= _fullTestStartedAt
            && !(_capturePhase == "Sondagem" && key.StartsWith("local-server|", StringComparison.Ordinal))
            && _fullTestSessionIds.Any(id => key.StartsWith(id + "|", StringComparison.Ordinal)))
        {
            if (_runWarnings.TryGetValue(key, out var runWarning))
            {
                runWarning.Count++;
                runWarning.Severity = MergeSeverity(runWarning.Severity, severity);
                runWarning.LatestDetail = latestDetail;
                runWarning.LastSeenAt = timestamp;
            }
            else _runWarnings[key] = new ImportantWarningSummary(key, severity, title, latestDetail, recommendation, timestamp);
        }
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

public sealed partial class ClientCommunicationPointRow : ObservableObject
{
    [ObservableProperty] private string sourceLine = "";
    [ObservableProperty] private string function = "";
    [ObservableProperty] private byte functionCode;
    [ObservableProperty] private string type = "";
    [ObservableProperty] private ushort address;
    [ObservableProperty] private ushort value;
    [ObservableProperty] private string quality = "";
    [ObservableProperty] private string lastUpdatedAt = "";
    [ObservableProperty] private bool writable;
}

public sealed partial class ServerPointRow : ObservableObject
{
    public ServerPointRow(ModbusPoint point)
    {
        Type = point.Type;
        Address = point.Address;
        Name = point.Name;
        Value = point.Value;
        IsWritable = point.IsWritable;
        LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
    }

    public ModbusPointType Type { get; }
    public ushort Address { get; }
    public string Name { get; }
    public bool IsWritable { get; }
    [ObservableProperty] private ushort value;
    [ObservableProperty] private string lastUpdatedAt = "";

    public string TypeLabel => Type switch
    {
        ModbusPointType.Coil => "Coil",
        ModbusPointType.DiscreteInput => "Discrete Input",
        ModbusPointType.HoldingRegister => "Holding Register",
        ModbusPointType.InputRegister => "Input Register",
        _ => Type.ToString()
    };

    public string Access => IsWritable ? "R/W" : "R";

    public ModbusPoint ToModbusPoint() => new(Type, Address, Name, Value, IsWritable);
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
            "Inconclusivo" => "#9B751C",
            "Nao aplicavel" => "#74828B",
            "Falha" or "Erro" => "#B3261E",
            "Executando" => "#1E6BD6",
            _ => "#9AA39C"
        };
    }
}

public sealed record FullTestStepResult(string Status, string Detail, string Recommendation);

public sealed partial class NetworkDiscoveryRow : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IpSortKey))] private string ip = "";
    public uint IpSortKey
    {
        get
        {
            if (!System.Net.IPAddress.TryParse(Ip, out var address)
                || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return uint.MaxValue;
            var bytes = address.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }
    }
    [ObservableProperty] private string mac = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    private string source = "";
    [ObservableProperty] private string roleGuess = "";
    [ObservableProperty] private string modbusStatus = "";
    [ObservableProperty] private string notes = "";
}

public sealed partial class MapDiscoveryRow : ObservableObject
{
    [ObservableProperty] private string endpoint = "";
    [ObservableProperty] private string unitId = "";
    [ObservableProperty] private string function = "";
    [ObservableProperty] private int startAddress;
    [ObservableProperty] private int endAddress;
    [ObservableProperty] private int quantity;
    [ObservableProperty] private string discoveryMode = "";
    [ObservableProperty] private string confidence = "";
    [ObservableProperty] private string notes = "";
}

internal sealed record MapDiscoveryEndpoint(string Ip, int Port);

internal sealed record MapProbeResult(bool Success, string Sample, string Error);

internal sealed record MapRangeProbe(ushort Start, ushort End, string Sample);

internal sealed record NetworkInterfaceProfile(
    string Name,
    string Description,
    string Address,
    int PrefixLength,
    string Gateway,
    string MacAddress,
    long SpeedBitsPerSecond,
    bool IsOperational);

internal sealed record InterfaceTrafficSnapshot(string Name, long BytesReceived, long BytesSent, long SpeedBitsPerSecond, long Errors = 0, long Discards = 0);

internal sealed record ArpEntry(string Ip, string Mac, string Type);

internal sealed record HostProbeResult(string Ip, bool PingOk, bool ConfiguredPortOpen, bool ModbusPortOpen);

public sealed class TcpTimelineRow
{
    public NeighborAdvertisement? Neighbor { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string SourceHost { get; init; } = "";
    public string DestinationHost { get; init; } = "";
    public string CapturePhase { get; set; } = "Operacional";
    public bool IsTcp { get; init; }
    public bool TcpReset { get; init; }
    public bool TcpSynchronize { get; init; }
    public bool TcpAcknowledgment { get; init; }
    public ushort TcpWindow { get; init; }
    public uint TcpSequence { get; init; }
    public int TcpPayloadLength { get; init; }
    public string ModbusKind { get; init; } = "";
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
        $"Info: {Info}",
        $"Fase: {CapturePhase}",
        $"Modbus: {(ModbusKind.Length == 0 ? "Nao confirmado" : ModbusKind)}"
    });
}
