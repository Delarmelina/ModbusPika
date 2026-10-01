using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class ClientConnectionSession : ObservableObject
{
    private CancellationTokenSource? _scanCancellation;
    private TaskCompletionSource? _scanStopped;
    private readonly Dictionary<(string, byte, ushort), ClientCommunicationPointRow> _pointIndex = [];
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public ModbusTcpClientProbe Client { get; } = new();
    public event EventHandler<TrafficEvent>? FindingObserved;
    public ObservableCollection<ClientMapRow> Rows { get; } = [];
    public ObservableCollection<ClientCommunicationPointRow> Points { get; } = [];
    public ObservableCollection<ClientCommunicationPointRow> HoldingPoints { get; } = [];
    public ObservableCollection<ClientCommunicationPointRow> InputPoints { get; } = [];
    public ObservableCollection<ClientCommunicationPointRow> CoilPoints { get; } = [];
    public ObservableCollection<ClientCommunicationPointRow> DiscretePoints { get; } = [];

    [ObservableProperty] private string name = "Alvo 1";
    [ObservableProperty] private bool includeInFullTest = true;
    [ObservableProperty] private string address = "127.0.0.1";
    [ObservableProperty] private int port = 1502;
    [ObservableProperty] private byte unitId = 1;
    [ObservableProperty] private int scanRateMs = 1000;
    [ObservableProperty] private bool keepConnectionOpen = true;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private bool isReading;
    [ObservableProperty] private string connectionState = "Parado";
    [ObservableProperty] private string lastError = "";
    public string Endpoint => $"{Address}:{Port}";
    public string StateColor => ConnectionState switch
    {
        "Leitura ativa" or "Conectado" or "Leitura OK" => "#2D9B62",
        "Conectando" or "Lendo" => "#3289C1",
        "Falha de conexão" or "Falha de leitura" => "#CB5048",
        _ => "#8A99A5"
    };

    public ClientConnectionSession()
    {
        AddRow(new ClientMapRow { Name = "Bloco 1", Function = "FC03 Holding Registers", Quantity = 10, Enabled = true });
        AddRow(new ClientMapRow { Name = "Bloco 2", Function = "FC04 Input Registers", Quantity = 10, Enabled = false });
    }

    partial void OnAddressChanged(string value) => OnPropertyChanged(nameof(Endpoint));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(Endpoint));
    partial void OnConnectionStateChanged(string value) => OnPropertyChanged(nameof(StateColor));

    public void AddRow(ClientMapRow row)
    {
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
        RefreshMap();
    }

    public void RemoveRow(ClientMapRow row)
    {
        row.PropertyChanged -= OnRowChanged;
        Rows.Remove(row);
        RefreshMap();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ClientMapRow row || e.PropertyName is not (nameof(ClientMapRow.Enabled)
            or nameof(ClientMapRow.Name) or nameof(ClientMapRow.Function)
            or nameof(ClientMapRow.StartAddress) or nameof(ClientMapRow.Quantity))) return;
        row.LastValue = "";
        row.LastStatus = "Nao lido";
        row.LastReadAt = "";
        RefreshMap();
        foreach (var point in Points.Where(x => x.SourceLine == row.Name && x.FunctionCode == row.FunctionCode))
        {
            point.Value = 0;
            point.Quality = "Nao lido";
            point.LastUpdatedAt = "";
        }
    }

    public void RefreshMap()
    {
        var keys = new HashSet<(string, byte, ushort)>();
        var existing = Points.Select(x => (x.SourceLine, x.FunctionCode, x.Address)).ToHashSet();
        foreach (var row in Rows.Where(x => x.Enabled))
        {
            for (var i = 0; i < row.Quantity && row.StartAddress + i <= ushort.MaxValue; i++)
            {
                var address = (ushort)(row.StartAddress + i);
                keys.Add((row.Name, row.FunctionCode, address));
                if (!existing.Add((row.Name, row.FunctionCode, address))) continue;
                Points.Add(new ClientCommunicationPointRow
                {
                    SourceLine = row.Name, FunctionCode = row.FunctionCode, Function = row.Function,
                    Type = PointType(row.FunctionCode), Address = address, Quality = "Nao lido",
                    Writable = row.FunctionCode is 1 or 3
                });
            }
        }
        foreach (var point in Points.Where(x => !keys.Contains((x.SourceLine, x.FunctionCode, x.Address))).ToList()) Points.Remove(point);
        _pointIndex.Clear();
        foreach (var point in Points) _pointIndex[(point.SourceLine, point.FunctionCode, point.Address)] = point;
        SyncViews();
    }

    public void ResetReadings()
    {
        foreach (var row in Rows)
        {
            row.LastValue = "";
            row.LastStatus = "Nao lido";
            row.LastReadAt = "";
        }
        foreach (var point in Points)
        {
            point.Value = 0;
            point.Quality = "Nao lido";
            point.LastUpdatedAt = "";
        }
        LastError = "";
    }

    public void RecordValues(ClientMapRow row, IReadOnlyList<ushort> values, string quality)
    {
        for (var i = 0; i < values.Count; i++) RecordValue(row, (ushort)(row.StartAddress + i), values[i], quality);
    }

    public void RecordValue(ClientMapRow row, ushort address, ushort value, string quality)
    {
        if (!_pointIndex.TryGetValue((row.Name, row.FunctionCode, address), out var point)) return;
        point.Value = value;
        point.Quality = quality;
        point.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
    }

    public void RecordFailure(ClientMapRow row, string error)
    {
        foreach (var point in Points.Where(x => x.SourceLine == row.Name && x.FunctionCode == row.FunctionCode))
        {
            point.Quality = error;
            point.LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss.fff");
        }
    }

    public void SyncViews()
    {
        Sync(HoldingPoints, 3); Sync(InputPoints, 4); Sync(CoilPoints, 1); Sync(DiscretePoints, 2);
    }

    private void Sync(ObservableCollection<ClientCommunicationPointRow> target, byte function)
    {
        var expected = Points.Where(x => x.FunctionCode == function).OrderBy(x => x.Address).ThenBy(x => x.SourceLine).ToList();
        if (target.SequenceEqual(expected)) return;
        target.Clear();
        foreach (var point in expected) target.Add(point);
    }

    private static string PointType(byte function) => function switch { 1 => "Coil", 2 => "Discrete Input", 4 => "Input Register", _ => "Holding Register" };

    public async Task ReadCycleAsync(CancellationToken cancellationToken)
    {
        IsReading = true;
        var failures = 0;
        try
        {
            if (Client.KeepConnectionOpen != KeepConnectionOpen) await Client.ConfigureConnectionModeAsync(KeepConnectionOpen, cancellationToken);
            if (!IsScanning) ConnectionState = "Lendo";
            var rows = Rows.Where(x => x.Enabled).ToList();
            if (rows.Count == 0) { ConnectionState = "Sem blocos ativos"; return; }
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A scan belongs to this session, never to whichever target is selected in the UI.
                var snapshot = (row.Name, row.FunctionCode, row.StartAddress, row.Quantity);
                try
                {
                    IReadOnlyList<ushort> values = row.FunctionCode is 1 or 2
                        ? (await Client.ReadBitsAsync(Address, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken)).Select(x => x ? (ushort)1 : (ushort)0).ToList()
                        : await Client.ReadRegistersAsync(Address, Port, UnitId, row.FunctionCode, row.StartAddress, row.Quantity, cancellationToken);
                    if (snapshot != (row.Name, row.FunctionCode, row.StartAddress, row.Quantity) || !row.Enabled || !Rows.Contains(row)) continue;
                    row.LastValue = string.Join(", ", values);
                    row.LastStatus = "OK";
                    row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
                    RecordValues(row, values, "OK");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures++;
                    LastError = ex.Message;
                    ReportError($"Falha na leitura '{row.Name}': {ex.Message}");
                    row.LastStatus = ex.Message;
                    row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
                    RecordFailure(row, ex.Message);
                }
            }
            ConnectionState = failures == 0 ? (IsScanning ? "Leitura ativa" : "Leitura OK") : "Falha de leitura";
            if (failures == 0) LastError = "";
        }
        finally { IsReading = false; }
    }

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        _scanStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsScanning = true;
        ConnectionState = "Conectando";
        try
        {
            await Client.ConfigureConnectionModeAsync(KeepConnectionOpen, cancellation.Token);
            if (KeepConnectionOpen) await Client.ConnectAsync(Address, Port, cancellation.Token);
            while (!cancellation.IsCancellationRequested)
            {
                await ReadCycleAsync(cancellation.Token);
                await Task.Delay(Math.Max(100, ScanRateMs), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ConnectionState = "Parado"; }
        catch (Exception ex) { ConnectionState = "Falha de conexão"; LastError = ex.Message; ReportError($"Falha de conexão: {ex.Message}"); }
        finally
        {
            _scanCancellation = null;
            await Client.DisconnectAsync();
            IsScanning = false;
            if (ConnectionState == "Parado") MarkStopped();
            _scanStopped.TrySetResult();
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    private void ReportError(string message) => FindingObserved?.Invoke(this, new TrafficEvent(DateTimeOffset.Now, TrafficDirection.System, Endpoint, null, UnitId, null, null, null, message, ""));

    public async Task DisconnectAsync()
    {
        CancelScan();
        if (_scanStopped is not null) await _scanStopped.Task;
        await Client.DisconnectAsync();
        ConnectionState = "Parado";
        MarkStopped();
    }

    private void MarkStopped()
    {
        foreach (var point in Points.Where(x => x.Quality is "OK" or "Escrita OK")) point.Quality = "Leitura parada";
        foreach (var row in Rows.Where(x => x.LastStatus == "OK" || x.LastStatus.StartsWith("Escrita OK"))) row.LastStatus = "Leitura parada";
    }
}
