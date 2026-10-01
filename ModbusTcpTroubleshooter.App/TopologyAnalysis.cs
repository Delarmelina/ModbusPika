using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public sealed record TopologyLink(string Source, string Destination, string Evidence, long Count, string Details);
public sealed record RouteHop(string Target, int Hop, string Address, string Status, long Milliseconds)
{
    public string StatusLabel => Status switch
    {
        "Success" => "Destino alcancado",
        "TtlExpired" => "TTL expirado (roteador respondeu)",
        "TimedOut" => "Sem resposta ICMP",
        "DestinationHostUnreachable" or "DestinationNetworkUnreachable" => "Destino/rede inacessivel por ICMP",
        _ => Status
    };
}

public sealed partial class MainViewModel
{
    [ObservableProperty] private bool enableRouteTracing = true;
    [ObservableProperty] private int routeTraceMaxHops = 12;
    [ObservableProperty] private int routeTraceTimeoutMs = 500;
    [ObservableProperty] private int routeTraceMaxTargets = 8;
    [ObservableProperty] private string topologySummary = "Execute o teste completo para coletar evidencias.";
    [ObservableProperty] private string topologyCoverage = "Sem coleta. Switches transparentes nao aparecem no rastreamento ICMP.";
    public ObservableCollection<TopologyLink> TopologyLinks { get; } = [];
    public ObservableCollection<TopologyLink> RelevantTopologyLinks { get; } = [];
    public ObservableCollection<RouteHop> TopologyRouteHops { get; } = [];
    public ObservableCollection<NeighborAdvertisement> TopologyNeighbors { get; } = [];
    private readonly object _topologyGate = new();
    private readonly Dictionary<(string Source, string Destination, string Evidence), (long Count, string Details)> _topologyFlows = [];
    private readonly Dictionary<string, NeighborAdvertisement> _topologyNeighbors = [];
    private bool _topologyLimited;
    private TopologyLink[] _fullTestTopologySnapshot = [];

    private void ResetTopology()
    {
        lock (_topologyGate) { _topologyFlows.Clear(); _topologyNeighbors.Clear(); _topologyLimited = false; }
        TopologyLinks.Clear(); RelevantTopologyLinks.Clear(); TopologyRouteHops.Clear(); TopologyNeighbors.Clear();
        _fullTestTopologySnapshot = [];
        TopologySummary = "Coletando topologia de comunicacao...";
        TopologyCoverage = "Infraestrutura fisica nao consultada. Sem SNMP; anuncios nao comprovam integridade do switch.";
    }

    private void ObserveTopologyPacket(TcpTimelineRow row)
    {
        if (!IsFullTestRunning || row.Timestamp < _fullTestStartedAt) return;
        lock (_topologyGate)
        {
            if (row.Neighbor is { } neighbor)
            {
                var key = neighbor.Protocol + "|" + neighbor.SourceMac + "|" + neighbor.Port;
                if (neighbor.Ttl == 0) _topologyNeighbors.Remove(key);
                else if (_topologyNeighbors.Count < 1024 || _topologyNeighbors.ContainsKey(key)) _topologyNeighbors[key] = neighbor;
                else _topologyLimited = true;
            }
            if (row.CapturePhase == "Sondagem" || row.SourceHost.Length == 0 || row.DestinationHost.Length == 0) return;
            var flow = (row.SourceHost, row.DestinationHost,
                row.ModbusKind.Length > 0 ? "Captura Modbus TCP" : "Captura " + row.Protocol);
            if (_topologyFlows.Count < 4096 || _topologyFlows.ContainsKey(flow))
                _topologyFlows[flow] = (_topologyFlows.GetValueOrDefault(flow).Count + 1, row.Source + " -> " + row.Destination);
            else _topologyLimited = true;
        }
    }

    private void RefreshTopologyEvidence()
    {
        TopologyLinks.Clear(); TopologyNeighbors.Clear();
        lock (_topologyGate)
        {
            foreach (var flow in _topologyFlows.OrderByDescending(x => x.Value.Count).Take(200))
                TopologyLinks.Add(new(flow.Key.Source, flow.Key.Destination, flow.Key.Evidence, flow.Value.Count, flow.Value.Details));
            foreach (var neighbor in _topologyNeighbors.Values.OrderBy(x => x.Name))
                if (neighbor.SeenAt.AddSeconds(neighbor.Ttl) > DateTimeOffset.Now)
                {
                    TopologyNeighbors.Add(neighbor);
                    TopologyLinks.Add(new("Interface de captura", neighbor.Name.Length > 0 ? neighbor.Name : neighbor.Chassis,
                        neighbor.Protocol + " anunciado", 1, $"Porta {neighbor.Port}; MAC {neighbor.SourceMac}. Nao confirma caminho ate o PLC."));
                }
        }
        var localEndpoints = _confirmedModbusEndpoints.Where(x => IPEndPoint.TryParse(x, out var parsed)
            && DeviceIdentityFor(parsed.Address.ToString(), _localIpv4Addresses) == "Este computador").Order().ToArray();
        if (localEndpoints.Length > 0)
            TopologyLinks.Add(new("Aplicacao / Cliente", "Este computador", "Modbus confirmado", localEndpoints.Length,
                "Endpoints locais: " + string.Join(", ", localEndpoints) + ". Respostas Modbus validadas; nao sao dispositivos distintos."));
        foreach (var endpoint in _confirmedModbusEndpoints.Except(localEndpoints).Order())
            TopologyLinks.Add(new("Aplicacao / Cliente", endpoint, "Modbus confirmado", 1, "Resposta Modbus validada; enlace logico, nao cabo fisico."));
        foreach (var profile in GetNetworkProfiles().Where(x => x.IsOperational && IPAddress.TryParse(x.Gateway, out _)))
            TopologyLinks.Add(new(profile.Address, profile.Gateway, "Gateway configurado", 1,
                $"Interface {profile.Name}; configuracao local, nao comprova uso deste gateway pelo alvo."));
        foreach (var hop in TopologyRouteHops.Where(x => x.Address.Length > 0))
        {
            var previous = TopologyRouteHops.LastOrDefault(x => x.Target == hop.Target && x.Hop == hop.Hop - 1);
            var source = hop.Hop == 1 ? "Computador local" : previous?.Address.Length > 0 ? previous.Address : $"Salto {hop.Hop - 1} sem resposta ({hop.Target})";
            TopologyLinks.Add(new(source, hop.Address, "Rota ICMP", 1, $"Alvo {hop.Target}; TTL {hop.Hop}; {hop.Status}; {hop.Milliseconds} ms. Caminho de camada 3 amostrado."));
        }
        RelevantTopologyLinks.Clear();
        foreach (var link in TopologyLinks.Where(x => x.Evidence is "Captura Modbus TCP" or "Modbus confirmado"
            || x.Evidence.EndsWith(" anunciado", StringComparison.Ordinal))) RelevantTopologyLinks.Add(link);
        var overview = ModbusTopologyOverview.Build(NetworkDiscoveryRows, TopologyLinks, TopologyNeighbors, FullTestIsServerMode);
        TopologySummary = $"{overview.Peers.Count + overview.HiddenPeers} participantes Modbus remotos; {overview.AnnouncedNeighbors} anuncios LLDP/CDP; {overview.OtherHosts} outros hosts fora do foco.";
        TopologyCoverage = "Diagrama logico de evidencias Modbus. O vizinho LLDP/CDP pertence a interface de captura, mas nao comprova que esteja no caminho de cada dispositivo. "
            + "Switches L2 nao aparecem no tracert; portas, cabos e VLANs exigem inventario ou consulta autorizada."
            + (_topologyLimited ? " Coleta limitada pelo volume de trafego." : "");
    }

    private async Task<FullTestStepResult> DiscoverTopologyAsync(CancellationToken token)
    {
        var targets = _fullTestClients.Select(x => x.Address).ToList();
        targets.AddRange(_confirmedModbusEndpoints.Select(x => IPEndPoint.TryParse(x, out var ep) ? ep : null)
            .Where(ep => ep is not null && (FullTestIsServerMode || NetworkDiscoveryRows.Any(row => row.Ip == ep.Address.ToString()
                && row.IsModbusConfirmed && row.ConfirmedModbusPorts.Split(',').Any(port => port.Trim() == ep.Port.ToString()))))
            .Select(ep => ep!.Address.ToString()));
        if (FullTestIsServerMode)
        {
            targets.AddRange(_serverBlocks.Values.Select(x => IPEndPoint.TryParse(x.Endpoint, out var peer) ? peer.Address.ToString() : ""));
        }
        var validTargets = targets.Distinct().Where(x => IPAddress.TryParse(x, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.GetAddressBytes()[0] is > 0 and < 224)
            .ToList();
        var selected = validTargets.Take(Math.Clamp(RouteTraceMaxTargets, 1, 32)).ToList();
        var phase = _capturePhase;
        try
        {
            _capturePhase = "Sondagem";
            if (EnableRouteTracing)
                foreach (var target in selected)
                {
                    using var ping = new Ping();
                    for (var ttl = 1; ttl <= Math.Clamp(RouteTraceMaxHops, 1, 30); ttl++)
                    {
                        token.ThrowIfCancellationRequested();
                        FullTestProgressLabel = $"Topologia: rota {target}, salto {ttl}/{RouteTraceMaxHops}";
                        await PaceProbeAsync(token);
                        try
                        {
                            var clock = System.Diagnostics.Stopwatch.StartNew();
                            var reply = await ping.SendPingAsync(IPAddress.Parse(target),
                                TimeSpan.FromMilliseconds(Math.Clamp(RouteTraceTimeoutMs, 200, 3000)),
                                new byte[24], new PingOptions(ttl, false), token);
                            var address = reply.Status is IPStatus.Success or IPStatus.TtlExpired ? reply.Address?.ToString() ?? "" : "";
                            TopologyRouteHops.Add(new(target, ttl, address, reply.Status.ToString(), clock.ElapsedMilliseconds));
                            if (reply.Status == IPStatus.Success || reply.Status is IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable) break;
                        }
                        catch (PingException ex) { TopologyRouteHops.Add(new(target, ttl, "", "Erro ICMP: " + ex.GetBaseException().Message, 0)); break; }
                    }
                }
        }
        finally { _capturePhase = phase; RefreshTopologyEvidence(); }
        var reached = TopologyRouteHops.Where(x => x.Status == "Success").Select(x => x.Target).Distinct().Count();
        var hasEvidence = RelevantTopologyLinks.Count > 0 || NetworkDiscoveryRows.Any(x => x.IsModbusConfirmed || x.IsModbusObserved || x.IsModbusClientObserved);
        var partialRoutes = EnableRouteTracing && (reached < validTargets.Count || selected.Count == 0);
        return new(!hasEvidence ? "Inconclusivo" : partialRoutes || _topologyLimited ? "Atencao" : "OK",
            TopologySummary + Environment.NewLine + $"Rastreamento ICMP: {(EnableRouteTracing ? $"{reached}/{selected.Count} destinos responderam; {validTargets.Count - selected.Count} omitidos pelo limite." : "desabilitado")}" + Environment.NewLine
                + "Enlaces inferidos (resumo):" + Environment.NewLine
                + string.Join(Environment.NewLine, RelevantTopologyLinks.OrderBy(x => x.Evidence.StartsWith("Captura") ? 1 : 0).Take(12).Select(x => $"{x.Source} -> {x.Destination} | {x.Evidence} | {x.Count}"))
                + Environment.NewLine + TopologyCoverage,
            "Consultar a aba Topologia para separar conversas, saltos de roteadores e vizinhos anunciados. Timeout ICMP nao comprova falha TCP/Modbus; infraestrutura fisica requer inventario/consulta autorizada de switches.");
    }

    private string BuildTopologyReport()
    {
        RefreshTopologyEvidence();
        _fullTestTopologySnapshot = RelevantTopologyLinks.ToArray();
        var overview = ModbusTopologyOverview.Build(NetworkDiscoveryRows, TopologyLinks, TopologyNeighbors, FullTestIsServerMode);
        var text = new StringBuilder("## Topologia observada e rotas\n\n");
        text.AppendLine(TopologySummary);
        text.AppendLine($"Estacao local ({overview.LocalRole}) -> {overview.InfrastructureTitle} -> participantes Modbus. {overview.InfrastructureDetail}");
        text.AppendLine($"Outros hosts detectados, nao desenhados: {overview.OtherHosts}. Infraestrutura anunciada nao comprova o caminho fisico ate cada equipamento.");
        text.AppendLine("Switches L2 sao transparentes ao rastreamento ICMP. Sem consulta de infraestrutura, cabeamento, VLANs e saude dos switches permanecem nao verificados.");
        foreach (var peer in overview.Peers) text.AppendLine($"- {peer.Ip}: {peer.Role}; {peer.Ports}; {peer.Evidence}.");
        if (_topologyLimited) text.AppendLine("Coleta atingiu o limite de detalhes; cobertura parcial.");
        text.AppendLine("\n### Diagrama logico (enlaces de evidencia)\n```text");
        foreach (var link in _fullTestTopologySnapshot.Take(30))
            text.AppendLine($"[{link.Source}] -- {link.Evidence} ({link.Count}) --> [{link.Destination}]");
        if (_fullTestTopologySnapshot.Length == 0) text.AppendLine("Sem enlaces relevantes comprovados nesta janela.");
        if (_fullTestTopologySnapshot.Length > 30) text.AppendLine($"{_fullTestTopologySnapshot.Length - 30} enlaces adicionais disponiveis na aba Topologia.");
        text.AppendLine("```");
        if (TopologyRouteHops.Count > 0)
        {
            text.AppendLine("\n### Rastreamento ICMP\n| Alvo | TTL | Resposta | Status | Consulta ms |\n|---|---:|---|---|---:|");
            foreach (var hop in TopologyRouteHops) text.AppendLine($"| {hop.Target} | {hop.Hop} | {hop.Address} | {EscapeMarkdownTable(hop.StatusLabel)} | {hop.Milliseconds} |");
            text.AppendLine("Consulta ms e tempo da consulta ICMP, nao latencia Modbus. Timeout ICMP nao comprova indisponibilidade TCP.");
        }
        if (TopologyNeighbors.Count > 0)
        {
            text.AppendLine("\n### Vizinhos anunciados\n| Protocolo | Nome / Chassi | Porta | MAC | IP gestao | Ultimo anuncio / TTL |\n|---|---|---|---|---|---|");
            foreach (var neighbor in TopologyNeighbors) text.AppendLine($"| {neighbor.Protocol} | {EscapeMarkdownTable(neighbor.Name + " / " + neighbor.Chassis)} | {EscapeMarkdownTable(neighbor.Port)} | {neighbor.SourceMac} | {neighbor.ManagementIp} | {neighbor.SeenAt:HH:mm:ss} / {neighbor.Ttl} s |");
        }
        else text.AppendLine("Nenhum anuncio LLDP/CDP recebido; isso nao demonstra ausencia de switches.");
        return text.ToString();
    }
}
