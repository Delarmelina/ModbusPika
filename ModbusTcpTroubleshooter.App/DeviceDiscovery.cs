using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    private HashSet<string> _localIpv4Addresses = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshLocalIpv4Addresses()
    {
        _localIpv4Addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(x => x.GetIPProperties().UnicastAddresses)
            .Where(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(x => x.Address.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in NetworkDiscoveryRows) row.DeviceIdentity = DeviceIdentityFor(row.Ip, _localIpv4Addresses);
        NotifyDiscoveryGroups();
    }

    public static string DeviceIdentityFor(string ip, IReadOnlySet<string> localAddresses) =>
        IPAddress.TryParse(ip, out var address) && (IPAddress.IsLoopback(address) || localAddresses.Contains(ip))
            ? "Este computador" : "Host nao identificado";

    private int ConfirmedModbusDeviceCount => _confirmedModbusEndpoints.Select(x => IPEndPoint.TryParse(x, out var endpoint)
        ? DeviceIdentityFor(endpoint.Address.ToString(), _localIpv4Addresses) == "Este computador" ? "local" : endpoint.Address.ToString()
        : x).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    [ObservableProperty] private string modbusDiscoveryPorts = "502,1501,1502";
    [ObservableProperty] private bool isDeviceProbeRunning;
    [ObservableProperty] private bool automaticNetworkScope = true;
    private readonly Dictionary<string, byte> _discoveredUnitIds = [];
    public string DiscoveryScopeHint => EnableActiveSubnetScan
        ? $"Descoberta da rede: {(AutomaticNetworkScope ? "sub-rede automatica da interface" : ActiveScanCidr)}; portas {ModbusDiscoveryPorts}."
        : "Descoberta passiva + alvos cadastrados. Outros IPs ARP nao foram sondados. Use Verificar IP ou autorize um CIDR no teste.";

    partial void OnModbusDiscoveryPortsChanged(string value) => OnPropertyChanged(nameof(DiscoveryScopeHint));
    partial void OnEnableActiveSubnetScanChanged(bool value) => OnPropertyChanged(nameof(DiscoveryScopeHint));
    partial void OnActiveScanCidrChanged(string value) => OnPropertyChanged(nameof(DiscoveryScopeHint));
    partial void OnIsDeviceProbeRunningChanged(bool value) => StartFullTestCommand.NotifyCanExecuteChanged();
    partial void OnAutomaticNetworkScopeChanged(bool value) => OnPropertyChanged(nameof(DiscoveryScopeHint));

    private void ResolveAutomaticNetworkScope()
    {
        if (!EnableActiveSubnetScan || !AutomaticNetworkScope) return;
        var profiles = GetNetworkProfiles().Where(x => x.IsOperational && IsPrivateIPv4(x.Address)).ToList();
        var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        var nic = interfaces.FirstOrDefault(x => SelectedCaptureDevice?.Name.Contains(x.Id.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) == true);
        var chosen = nic is null ? null : profiles.FirstOrDefault(x => x.Name == nic.Name);
        chosen ??= profiles.FirstOrDefault(x => x.Address == LocalIp && FullTestIsServerMode);
        var physical = profiles.Where(x => interfaces.Any(n => n.Name == x.Name
            && n.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211
            && !System.Text.RegularExpressions.Regex.IsMatch(n.Description, "WFP|Npcap|Filter|Virtual|Pseudo", System.Text.RegularExpressions.RegexOptions.IgnoreCase))).ToList();
        if (chosen is null && physical.Count == 1) chosen = physical[0];
        if (chosen is null) throw new InvalidOperationException("Nao foi possivel identificar uma unica sub-rede local. Selecione a interface de rede na captura ou use CIDR manual no escopo; nao e necessario cadastrar IPs individualmente.");
        if (chosen.PrefixLength is < 16 or > 32) throw new InvalidOperationException($"Mascara /{chosen.PrefixLength} fora do limite /16 a /32. Nenhum IP adicional sondado.");
        var network = UInt32ToIp(IpToUInt32(chosen.Address) & (uint.MaxValue << (32 - chosen.PrefixLength)));
        ActiveScanCidr = $"{network}/{chosen.PrefixLength}";
        if (!TryParseScanScope(ActiveScanCidr, out _, out _)) throw new InvalidOperationException($"Sub-rede {ActiveScanCidr} fora do limite /16 a /32. Nenhum IP adicional sondado.");
        _fullTestStartupActions.Add($"Descoberta automatica: {chosen.Name}, {ActiveScanCidr}. Todos os hosts desse segmento; portas {ModbusDiscoveryPorts}. Servicos em outras portas ou fora do segmento nao sao enumerados.");
    }

    public async Task<string> VerifyDeviceAsync(string address, string portsText, byte unitId, CancellationToken token)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || ip.GetAddressBytes()[0] is 0 or >= 224 || !TryParseDiscoveryPorts(portsText, out var ports))
            throw new ArgumentException("Informe um IP unicast IPv4 e 1 a 16 portas TCP validas.");
        if (IsFullTestRunning || IsDeviceProbeRunning) throw new InvalidOperationException("Aguarde a verificacao atual terminar.");
        IsDeviceProbeRunning = true;
        RefreshLocalIpv4Addresses();
        try
        {
            var lines = new List<string>();
            foreach (var port in ports)
            {
                token.ThrowIfCancellationRequested();
                await PaceProbeAsync(token);
                lines.Add(await ConfirmEndpointAsync(ip.ToString(), port, [unitId], token));
            }
            RefreshTopologyEvidence();
            Status = $"Verificacao de {ip} concluida. Consulte a categoria e as notas do dispositivo.";
            return string.Join(Environment.NewLine, lines);
        }
        finally { IsDeviceProbeRunning = false; }
    }

    private async Task<string> ConfirmEndpointAsync(string address, int port, IReadOnlyList<byte> unitIds, CancellationToken token)
    {
        var owner = ClientSessions.FirstOrDefault(x => x.Address == address && x.Port == port);
        var block = owner?.Rows.FirstOrDefault(x => x.Enabled);
        var fc = block?.FunctionCode ?? (byte)3;
        var start = block?.StartAddress ?? (ushort)0;
        var lines = new List<string>();
        var open = false;
        var confirmed = false;
        foreach (var uid in unitIds)
        {
            token.ThrowIfCancellationRequested();
            if (lines.Count > 0) await PaceProbeAsync(token);
            var probe = new ModbusTcpTroubleshooter.Core.ModbusTcpClientProbe();
            var detail = "";
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(Math.Max(1000, ActiveScanTimeoutMs));
                if (fc is 1 or 2) await probe.ReadBitsAsync(address, port, uid, fc, start, 1, deadline.Token);
                else await probe.ReadRegistersAsync(address, port, uid, fc, start, 1, deadline.Token);
                confirmed = true;
                detail = "Resposta Modbus validada por TID/UID/FC/MBAP.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Modbus exception", StringComparison.Ordinal))
            { confirmed = true; detail = ex.Message + " Protocolo confirmado; endereco/FC nao validado."; }
            catch (OperationCanceledException) { detail = "Timeout; protocolo nao confirmado."; }
            catch (Exception ex) { detail = "Nao confirmado: " + ex.Message; }
            finally { open |= probe.ConnectionOpenCount > 0; await probe.DisconnectAsync(); }
            lines.Add($"{address}:{port} UID {uid} FC{fc}: {detail}");
            if (confirmed) break;
            if (!open) break;
        }
        var endpoint = $"{address}:{port}";
        if (confirmed)
        {
            _discoveredUnitIds[endpoint] = unitIds[Math.Min(lines.Count - 1, unitIds.Count - 1)];
            _confirmedModbusEndpoints.Add(endpoint);
            RegisterModbusDevice(endpoint, true, string.Join("; ", lines));
        }
        else UpsertDiscovery(address, "", "Sondagem explicita read-only", "Alvo sondado; tipo desconhecido", "", string.Join("; ", lines));
        var row = NetworkDiscoveryRows.First(x => x.Ip == address);
        if (open) row.OpenTcpPorts = MergePortList(row.OpenTcpPorts, port.ToString());
        row.ProbedPorts = MergePortList(row.ProbedPorts, port.ToString());
        row.ProbeSummary = confirmed ? "Resposta Modbus validada" : open ? "TCP aberto; Modbus sem confirmacao" : "Sem conexao TCP; fechado, filtrado ou indisponivel";
        row.LastProbeAt = DateTimeOffset.Now.ToString("HH:mm:ss");
        return string.Join(Environment.NewLine, lines);
    }
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredModbusDevices => GroupModbusDevices(
        NetworkDiscoveryRows.Where(x => x.IsModbusConfirmed || x.IsModbusObserved), _localIpv4Addresses);

    public static IReadOnlyList<NetworkDiscoveryRow> GroupModbusDevices(IEnumerable<NetworkDiscoveryRow> rows, IReadOnlySet<string> localAddresses)
    {
        var discovered = rows.ToArray();
        var local = discovered.Where(x => DeviceIdentityFor(x.Ip, localAddresses) == "Este computador").ToArray();
        if (local.Length == 0) return discovered;
        var primary = local.FirstOrDefault(x => !IPAddress.IsLoopback(IPAddress.Parse(x.Ip))) ?? local[0];
        var grouped = new NetworkDiscoveryRow
        {
            Ip = primary.Ip,
            DeviceIdentity = "Este computador",
            AliasCaption = "IPs locais: " + string.Join(", ", local.Select(x => x.Ip)),
            RoleGuess = "Servidor Modbus local",
            Source = string.Join(", ", local.Select(x => x.Source).Distinct()),
            IsModbusConfirmed = local.Any(x => x.IsModbusConfirmed),
            IsModbusObserved = local.Any(x => x.IsModbusObserved),
            ConfirmedModbusPorts = MergePortList("", string.Join(",", local.Select(x => x.ConfirmedModbusPorts))),
            ObservedModbusPorts = MergePortList("", string.Join(",", local.Select(x => x.ObservedModbusPorts))),
            ModbusPorts = MergePortList("", string.Join(",", local.Select(x => x.ModbusPorts))),
            OpenTcpPorts = MergePortList("", string.Join(",", local.Select(x => x.OpenTcpPorts))),
            ProbedPorts = MergePortList("", string.Join(",", local.Select(x => x.ProbedPorts))),
            ProbeSummary = string.Join("; ", local.Select(x => x.ProbeSummary).Where(x => x.Length > 0).Distinct()),
            Notes = "Enderecos deste computador (nao sao dispositivos distintos): "
                + string.Join("; ", local.Select(x => $"{x.Ip}: TCP {x.ModbusPorts}. {x.Notes}"))
        };
        return [grouped, .. discovered.Where(x => DeviceIdentityFor(x.Ip, localAddresses) != "Este computador")];
    }
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredModbusClients => NetworkDiscoveryRows.Where(x => x.IsModbusClientObserved).ToArray();
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredTcpServices => NetworkDiscoveryRows.Where(x => !x.IsModbusConfirmed && !x.IsModbusObserved && !x.IsModbusClientObserved && x.OpenTcpPorts.Length > 0).ToArray();
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredIpv4Neighbors => NetworkDiscoveryRows.Where(x => !x.IsModbusConfirmed && !x.IsModbusObserved && !x.IsModbusClientObserved && x.OpenTcpPorts.Length == 0 && x.Source.Contains("ARP") && !x.IsSpecialAddress).ToArray();
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredOtherHosts => NetworkDiscoveryRows.Where(x => !x.IsModbusConfirmed && !x.IsModbusObserved && !x.IsModbusClientObserved && x.OpenTcpPorts.Length == 0 && !x.Source.Contains("ARP") && !x.IsSpecialAddress).ToArray();
    public IReadOnlyList<NetworkDiscoveryRow> DiscoveredSpecialAddresses => NetworkDiscoveryRows.Where(x => !x.IsModbusConfirmed && !x.IsModbusObserved && x.OpenTcpPorts.Length == 0 && x.IsSpecialAddress).ToArray();

    private void InitializeDiscoveryGroups()
    {
        NetworkDiscoveryRows.CollectionChanged += (_, change) =>
        {
            if (change.OldItems is not null) foreach (NetworkDiscoveryRow row in change.OldItems) row.PropertyChanged -= OnDiscoveryRowChanged;
            if (change.NewItems is not null) foreach (NetworkDiscoveryRow row in change.NewItems) row.PropertyChanged += OnDiscoveryRowChanged;
            NotifyDiscoveryGroups();
        };
    }
    private void OnDiscoveryRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDiscoveryRow.IsModbusConfirmed) or nameof(NetworkDiscoveryRow.IsModbusObserved)
            or nameof(NetworkDiscoveryRow.IsModbusClientObserved)
            or nameof(NetworkDiscoveryRow.OpenTcpPorts) or nameof(NetworkDiscoveryRow.Source) or nameof(NetworkDiscoveryRow.Ip)) NotifyDiscoveryGroups();
    }
    private void NotifyDiscoveryGroups()
    {
        OnPropertyChanged(nameof(DiscoveredModbusDevices)); OnPropertyChanged(nameof(DiscoveredTcpServices));
        OnPropertyChanged(nameof(DiscoveredIpv4Neighbors)); OnPropertyChanged(nameof(DiscoveredOtherHosts));
        OnPropertyChanged(nameof(DiscoveredSpecialAddresses));
        OnPropertyChanged(nameof(DiscoveredModbusClients));
    }

    public static bool TryParseDiscoveryPorts(string text, out int[] ports)
    {
        ports = [];
        var pieces = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length == 0 || pieces.Length > 16) return false;
        var values = new List<int>();
        foreach (var piece in pieces)
        {
            if (!int.TryParse(piece, out var port) || port is < 1 or > 65535) return false;
            values.Add(port);
        }
        ports = values.Distinct().Order().ToArray();
        return true;
    }

    private void RegisterModbusDevice(string endpoint, bool activelyConfirmed, string detail)
    {
        if (!IPEndPoint.TryParse(endpoint, out var parsed) || !IsIPv4(parsed.Address.ToString())) return;
        var ip = parsed.Address.ToString();
        UpsertDiscovery(ip, "", activelyConfirmed ? "Confirmacao Modbus read-only" : "Resposta Modbus capturada",
            activelyConfirmed ? "Servidor Modbus confirmado" : "Resposta Modbus observada", parsed.Port.ToString(), detail);
        var row = NetworkDiscoveryRows.First(x => x.Ip == ip);
        row.ModbusPorts = MergePortList(row.ModbusPorts, parsed.Port.ToString());
        if (activelyConfirmed)
        {
            row.ConfirmedModbusPorts = MergePortList(row.ConfirmedModbusPorts, parsed.Port.ToString());
            row.OpenTcpPorts = MergePortList(row.OpenTcpPorts, parsed.Port.ToString());
        }
        else row.ObservedModbusPorts = MergePortList(row.ObservedModbusPorts, parsed.Port.ToString());
        row.IsModbusConfirmed |= activelyConfirmed;
        row.IsModbusObserved |= !activelyConfirmed;
        row.RoleGuess = row.IsModbusConfirmed ? "Servidor Modbus confirmado" : "Resposta Modbus observada";
    }
    private static string MergePortList(string current, string next) => string.Join(", ", (current + "," + next)
        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(x => int.TryParse(x, out _)).Distinct().OrderBy(int.Parse));

    private int[] DiscoveryPortsForTest() => TryParseDiscoveryPorts(ModbusDiscoveryPorts, out var configured)
        ? _fullTestClients.Select(x => x.Port).Append(ServerPort).Concat(configured).Where(x => x is > 0 and <= 65535).Distinct().Order().ToArray()
        : [];
}

public sealed partial class NetworkDiscoveryRow
{
    [ObservableProperty] private string deviceIdentity = "Host nao identificado";
    [ObservableProperty] private string aliasCaption = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SidebarCaption)), NotifyPropertyChangedFor(nameof(EvidenceColor))] private bool isModbusClientObserved;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SidebarCaption))] private string probedPorts = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SidebarCaption))] private string probeSummary = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SidebarCaption))] private string lastProbeAt = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    [NotifyPropertyChangedFor(nameof(EvidenceColor))]
    private bool isModbusConfirmed;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    [NotifyPropertyChangedFor(nameof(EvidenceColor))]
    private bool isModbusObserved;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    private string modbusPorts = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    private string confirmedModbusPorts = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    private string observedModbusPorts = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarCaption))]
    [NotifyPropertyChangedFor(nameof(EvidenceColor))]
    private string openTcpPorts = "";
    public bool IsSpecialAddress => IPAddress.TryParse(Ip, out var address)
        && (IPAddress.IsLoopback(address) || address.GetAddressBytes()[0] >= 224 || address.Equals(IPAddress.Any));
    public string SidebarCaption => IsModbusConfirmed ? "Modbus confirmado | TCP " + ConfirmedModbusPorts
            + (ObservedModbusPorts.Length > 0 ? "\nObservado na captura: " + ObservedModbusPorts : "")
        : IsModbusObserved ? "Modbus observado | TCP " + ObservedModbusPorts
        : OpenTcpPorts.Length > 0 ? "TCP aberto: " + OpenTcpPorts + " | Modbus nao confirmado"
        : IsModbusClientObserved ? "Cliente Modbus | requisicoes observadas"
        : ProbedPorts.Length > 0 ? $"{ProbeSummary}\nPortas sondadas: {ProbedPorts} | {LastProbeAt}"
        : Source.Contains("ARP") ? "Entrada ARP | Modbus nao sondado" : "Host observado | Modbus nao sondado";
    public string EvidenceColor => IsModbusConfirmed ? "#2D9B62" : IsModbusObserved ? "#3289C1" : OpenTcpPorts.Length > 0 ? "#B08032" : "#718595";
}
