using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    [ObservableProperty] private int tcpMonitoringSeconds = 30;
    [ObservableProperty] private int readValidationAttempts = 15;
    [ObservableProperty] private int readValidationIntervalMs = 1000;
    [ObservableProperty] private string activeScanCidr = "";
    [ObservableProperty] private int probeRatePerSecond = 5;
    [ObservableProperty] private int packetRateWarningThreshold = 5000;
    [ObservableProperty] private string fullTestCoverage = "Sem coleta";
    private readonly TrafficWindowAnalysis _trafficWindow = new();
    private TrafficWindowSummary? _baselineTraffic, _operationalTraffic;
    private readonly List<BlockReadStatistics> _blockReadStatistics = [];
    private readonly HashSet<string> _confirmedModbusEndpoints = [];
    private readonly SemaphoreSlim _probeRateGate = new(1, 1);
    private DateTimeOffset _nextProbeAt;
    private volatile string _capturePhase = "Operacional";
    private string _baselineCounters = "", _operationalCounters = "";
    private long _windowUiDrops;
    private DateTimeOffset? _fullTestFinishedAt;
    private readonly HashSet<(string Address, int Port)> _openDiscoveryEndpoints = [];
    private readonly Dictionary<string, (long Opens, long Closes)> _connectionBaselines = [];
    private readonly Dictionary<(string Endpoint, ushort? Tid, byte? Uid), (DateTimeOffset At, string Block)> _serverPendingRequests = [];
    private readonly Dictionary<string, ServerBlockStatistics> _serverBlocks = [];
    private long _serverWindowRequests, _serverWindowReplies, _serverWindowExceptions, _serverWindowUnmatched;
    [ObservableProperty] private int fullTestInconclusiveCount;
    [ObservableProperty] private int fullTestNotApplicableCount;

    private static string ScopeSettingsPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModbusTcpTroubleshooter", "test-scope.json");
    private sealed record ScopeSettings(int ReferenceSeconds, int MonitoringSeconds, int Attempts, int IntervalMs, string Cidr, int ProbeRate, int PacketRate, int TimeoutMs, int Concurrency,
        bool? TraceRoutes = null, int TraceHops = 12, int TraceTimeout = 500, int TraceTargets = 8, string? DiscoveryPorts = null,
        bool? AutomaticScope = null, bool? DiscoverNetwork = null);
    private void LoadTestScopeSettings()
    {
        try
        {
            if (!System.IO.File.Exists(ScopeSettingsPath)) return;
            var settings = System.Text.Json.JsonSerializer.Deserialize<ScopeSettings>(System.IO.File.ReadAllText(ScopeSettingsPath));
            if (settings is null) return;
            PassiveObservationSeconds = Math.Clamp(settings.ReferenceSeconds, 1, 3600);
            TcpMonitoringSeconds = Math.Clamp(settings.MonitoringSeconds, 1, 86400);
            ReadValidationAttempts = Math.Clamp(settings.Attempts, 1, 1000);
            ReadValidationIntervalMs = Math.Clamp(settings.IntervalMs, 100, 60000);
            ActiveScanCidr = settings.Cidr ?? "";
            ProbeRatePerSecond = Math.Clamp(settings.ProbeRate, 1, 100);
            PacketRateWarningThreshold = Math.Clamp(settings.PacketRate, 1, 1000000);
            ActiveScanTimeoutMs = Math.Clamp(settings.TimeoutMs, 200, 3000);
            ActiveScanConcurrency = Math.Clamp(settings.Concurrency, 1, 16);
            EnableRouteTracing = settings.TraceRoutes ?? true;
            RouteTraceMaxHops = Math.Clamp(settings.TraceHops, 1, 30);
            RouteTraceTimeoutMs = Math.Clamp(settings.TraceTimeout, 200, 3000);
            RouteTraceMaxTargets = Math.Clamp(settings.TraceTargets, 1, 32);
            if (settings.DiscoveryPorts is not null && TryParseDiscoveryPorts(settings.DiscoveryPorts, out _))
                ModbusDiscoveryPorts = settings.DiscoveryPorts;
            AutomaticNetworkScope = settings.AutomaticScope ?? true;
            EnableActiveSubnetScan = settings.DiscoverNetwork ?? true;
            // Full Test performs discovery by default; opening the app itself never starts probes.
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Falha ao carregar parametros do teste"); }
    }

    public void SaveTestScopeSettings()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScopeSettingsPath)!);
            var settings = new ScopeSettings(PassiveObservationSeconds, TcpMonitoringSeconds, ReadValidationAttempts,
                ReadValidationIntervalMs, ActiveScanCidr, ProbeRatePerSecond, PacketRateWarningThreshold, ActiveScanTimeoutMs, ActiveScanConcurrency,
                EnableRouteTracing, RouteTraceMaxHops, RouteTraceTimeoutMs, RouteTraceMaxTargets, ModbusDiscoveryPorts, AutomaticNetworkScope, EnableActiveSubnetScan);
            System.IO.File.WriteAllText(ScopeSettingsPath, System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Status = "Parametros aplicados, mas nao foi possivel salva-los: " + ex.Message; }
    }

    private void RecordServerWindowEvent(TrafficEvent e)
    {
        if (!IsFullTestRunning || !FullTestIsServerMode || e.SessionId != "local-server" || e.Timestamp < _fullTestStartedAt || _capturePhase == "Sondagem") return;
        var key = (e.Endpoint, e.TransactionId, e.UnitId);
        if (e.Direction == TrafficDirection.ClientToServer)
        {
            if (IPEndPoint.TryParse(e.Endpoint, out var remote))
            {
                var address = remote.Address.ToString();
                if (!NetworkDiscoveryRows.Any(x => x.Ip == address && x.IsModbusClientObserved))
                {
                    UpsertDiscovery(address, "", "Requisicao ao servidor local", "Cliente Modbus observado", "", "Cliente identificado por requisicao recebida pelo servidor simulado.");
                    var client = NetworkDiscoveryRows.FirstOrDefault(x => x.Ip == address);
                    if (client is not null) client.IsModbusClientObserved = true;
                }
            }
            _serverWindowRequests++;
            var signature = $"{e.Endpoint}|{e.UnitId}|{e.FunctionCode}|{e.StartAddress}|{e.Quantity}";
            if (!_serverBlocks.TryGetValue(signature, out var stat) && _serverBlocks.Count < 4096)
                _serverBlocks[signature] = stat = new(e.Endpoint, e.UnitId, e.FunctionCode, e.StartAddress, e.Quantity);
            if (stat is not null) stat.Requests++;
            if (_serverPendingRequests.Count < 4096) _serverPendingRequests[key] = (e.Timestamp, signature);
            else _serverWindowUnmatched++;
        }
        else if (e.Direction == TrafficDirection.ServerToClient)
        {
            var exception = e.FunctionCode >= 128 || e.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase);
            if (_serverPendingRequests.Remove(key, out var request))
            {
                _serverWindowReplies++;
                if (_serverBlocks.TryGetValue(request.Block, out var stat))
                {
                    stat.Replies++;
                    if (exception) stat.Exceptions++;
                    if (stat.Latencies.Count < 1000) stat.Latencies.Add(Math.Max(0, (e.Timestamp - request.At).TotalMilliseconds));
                }
            }
            else _serverWindowUnmatched++;
            if (exception) _serverWindowExceptions++;
        }
    }

    private string BuildFullTestReport()
    {
        var text = new StringBuilder();
        var end = _fullTestFinishedAt ?? DateTimeOffset.Now;
        text.AppendLine("# Diagnostico Modbus TCP");
        text.AppendLine();
        text.AppendLine($"Caso: {CaseName}. Resultado: **{FullTestOverallStatus}**.");
        text.AppendLine($"Execucao: {_fullTestStartedAt:yyyy-MM-dd HH:mm:ss zzz} ate {end:HH:mm:ss zzz}; duracao {(end - _fullTestStartedAt).TotalSeconds:0.0} s. Papel: {FullTestModeLabel}.");
        text.AppendLine("O resultado se refere aos alvos e blocos exercitados nesta execucao, nao a certificacao da rede inteira. Nenhuma escrita automatica foi realizada.");
        text.AppendLine();
        text.AppendLine(FullTestIsServerMode ? "## Servidor local e clientes observados" : "## Resumo por servidor");
        text.AppendLine();
        if (_fullTestClients.Length > 0)
        {
            text.AppendLine("| Alvo / Endpoint / UID | Blocos | Leituras validas / tentadas / planejadas | Sucesso | TCP | Resultado |");
            text.AppendLine("|---|---:|---|---:|---|---|");
        }
        foreach (var session in _fullTestClients)
        {
            var stats = _blockReadStatistics.Where(x => x.SessionId == session.Id).ToList();
            var stages = FullTestSteps.Where(x => x.Name.Contains($"{session.Name} · {session.Endpoint} · UID {session.UnitId}")).ToList();
            var warnings = _runWarnings.Where(x => x.Key.StartsWith(session.Id + "|", StringComparison.Ordinal)).Select(x => x.Value).ToList();
            var tcp = stages.FirstOrDefault(x => x.Name.StartsWith("Conectividade TCP"))?.Status ?? "Nao executado";
            var result = stats.Any(x => x.Success < x.Attempted) || stages.Any(x => x.Status is "Falha" or "Erro") || warnings.Any(x => x.Severity is "Erro" or "Falha") ? "Falha"
                : stats.Count == 0 || stages.Any(x => x.Status is "Pendente" or "Cancelado" or "Executando" or "Inconclusivo") || stats.Any(x => x.Attempted < x.Planned) ? "Inconclusivo"
                : warnings.Count > 0 || stages.Any(x => x.Status == "Atencao") ? "Atencao" : "OK";
            var attempted = stats.Sum(x => x.Attempted);
            var success = attempted == 0 ? "Nao medido" : $"{100d * stats.Sum(x => x.Success) / attempted:0.0}%";
            text.AppendLine($"| {EscapeMarkdownTable(session.Name)} / {session.Endpoint} / {session.UnitId} | {stats.Count(x => x.Attempted > 0)}/{session.Rows.Count(x => x.Enabled)} | {stats.Sum(x => x.Success)} / {attempted} / {stats.Sum(x => x.Planned)} | {success} | {tcp} | {result} |");
        }
        if (FullTestIsClientMode && _fullTestClients.Length == 0)
        {
            var automatic = FullTestSteps.FirstOrDefault(x => x.Name == "Validacao dos servidores descobertos");
            text.AppendLine($"Descoberta automatica sem alvos cadastrados: {_confirmedModbusEndpoints.Count} endpoints Modbus confirmados; validacao dos servidores remotos: {automatic?.Status ?? "Nao executada"}. A etapa correspondente detalha respostas, blocos e limites de cobertura.");
        }
        if (FullTestIsServerMode) text.AppendLine($"Servidor local {ServerEndpoint}: {_serverWindowRequests} requests, {_serverWindowReplies} respostas correlacionadas, {_serverWindowExceptions} exceptions, {_serverPendingRequests.Count} pendentes. Nao valida clientes que nao enviaram requisicoes.");
        text.AppendLine();
        text.AppendLine("## Evidencias e achados prioritarios");
        text.AppendLine();
        var findings = FullTestSteps.Where(x => x.Name != "Conclusao" && x.Status is "Falha" or "Erro" or "Atencao" or "Inconclusivo" or "Cancelado")
            .OrderBy(x => x.Status is "Falha" or "Erro" ? 0 : x.Status == "Atencao" ? 1 : 2).ToList();
        if (findings.Count == 0) text.AppendLine("Nenhuma falha/atencao registrada nas verificacoes executadas. Resultado limitado a esta janela e aos blocos configurados.");
        foreach (var step in findings)
        {
            text.AppendLine($"### {step.Status}: {step.Name}");
            text.AppendLine($"Horario: {step.StartedAt:HH:mm:ss} a {step.FinishedAt:HH:mm:ss}.");
            var evidence = step.Result.Split(Environment.NewLine).Where(x => x.Length > 0).Take(4);
            text.AppendLine($"Evidencia: {string.Join(" ", evidence)}");
            text.AppendLine($"Proxima verificacao tecnica: {step.Recommendation}");
            text.AppendLine("Causa raiz nao confirmada automaticamente; sinais de captura e hipoteses exigem correlacao.");
            text.AppendLine();
        }
        foreach (var warning in _runWarnings.Values.OrderBy(x => x.Severity is "Erro" or "Falha" ? 0 : 1))
            text.AppendLine($"- {warning.Severity}: {warning.Title}; {warning.Count} ocorrencias, {warning.FirstSeenAt:HH:mm:ss.fff} a {warning.LastSeenAt:HH:mm:ss.fff}. {warning.LatestDetail}");
        text.AppendLine();
        text.AppendLine("## Desempenho das leituras");
        if (_blockReadStatistics.Count > 0)
        {
            text.AppendLine("| Endpoint / UID / Bloco | FC / Endereco / Qtd | Validas / Tentadas | Mediana ms | p95 ms | Scan ms | Interpretacao |");
            text.AppendLine("|---|---|---|---:|---:|---:|---|");
            foreach (var stat in _blockReadStatistics)
            {
                var interpretation = stat.Attempted == 0 ? "Nao exercitado" : stat.Success < stat.Attempted ? "Falhas de leitura" : stat.Attempted < stat.Planned ? "Amostra incompleta"
                    : stat.Latencies.Count < 5 ? "Amostra insuficiente para comparar p95" : stat.SlowResponse ? "p95 >= periodo de scan" : "p95 abaixo do periodo de scan";
                var median = stat.Latencies.Count == 0 ? "-" : $"{Percentile(stat.Latencies, .5):0.0}";
                var p95 = stat.Latencies.Count == 0 ? "-" : $"{Percentile(stat.Latencies, .95):0.0}";
                text.AppendLine($"| {stat.Endpoint} / {stat.UnitId} / {EscapeMarkdownTable(stat.Row.Name)} | FC{stat.Row.FunctionCode:00} / {stat.Row.StartAddress} / {stat.Row.Quantity} | {stat.Success}/{stat.Attempted} | {median} | {p95} | {stat.ScanRateMs} | {interpretation} |");
            }
        }
        foreach (var stat in _serverBlocks.Values) text.AppendLine($"- Cliente externo {stat.Endpoint} UID {stat.UnitId}, FC{stat.Fc}, addr {stat.Address}, qty {stat.Quantity}: {stat.Requests} requisicoes, {stat.Replies} respostas, {stat.Exceptions} exceptions; p95 de resposta local {Percentile(stat.Latencies, .95):0.0} ms (primeiras {stat.Latencies.Count} amostras, limite 1000).");
        text.AppendLine("p95: 95% das latencias amostradas ficam ate esse valor. A comparacao com o scan e uma referencia por transacao, nao valida o ciclo inteiro. Latencia inclui espera pelo socket; nao isola processamento do PLC.");
        if (_blockReadStatistics.Any(x => x.Latencies.Count is > 0 and < 20))
            text.AppendLine("Amostras abaixo de 20 respostas nao caracterizam a cauda de latencia nem falhas intermitentes raras.");
        text.AppendLine();
        text.AppendLine("## Cobertura e limitacoes");
        text.AppendLine(FullTestCoverage);
        var discoveredOnly = _confirmedModbusEndpoints.Except(_fullTestClients.Select(x => x.Endpoint)).Order()
            .Select(x => x + (IPEndPoint.TryParse(x, out var endpoint)
                && DeviceIdentityFor(endpoint.Address.ToString(), _localIpv4Addresses) == "Este computador" ? " (este computador)" : "")).ToArray();
        if (discoveredOnly.Length > 0)
            text.AppendLine($"**Endpoints Modbus confirmados pela descoberta:** {string.Join(", ", discoveredOnly)}. "
                + (FullTestIsClientMode && _fullTestClients.Length == 0
                    ? "Consulte a etapa de validacao dos descobertos para saber quais mapas receberam leituras repetidas. "
                    : "Endpoints fora dos alvos selecionados nao receberam a validacao repetida do mapa. ")
                + "Resposta de protocolo isolada nao valida o mapa. IPs locais diferentes nao representam dispositivos adicionais.");
        text.AppendLine("Rede inteira, saturacao fisica, perda de enlace, saude dos switches, portas/VLANs, SNMP, mapa nao configurado e escrita segura: nao validados automaticamente. LLDP/CDP identificam apenas anuncios recebidos.");
        text.AppendLine("Captura de uma interface/BPF nao observa todas as conversas; loopback requer interface propria. ADUs passivos segmentados ou FCs nao suportados podem nao ser classificados. Repeticoes TCP sao indicios, nao prova de retransmissao.");
        text.AppendLine("Sondagens deste teste sao separadas das janelas passivas. Polling existente e trafego externo continuam; leituras de validacao adicionam carga. Aberturas/fechamentos sao contadores do cliente, nao inferencias de perda fisica.");
        text.AppendLine();
        text.AppendLine("## Trafego observado nas janelas");
        text.AppendLine("| Janela | Duracao | Pacotes | Media pkt/s | Pico pkt/s | RST | Zero-window | Segmentos repetidos |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, window) in new[] { ("Referencia sem sondagens adicionais", _baselineTraffic), ("Operacional apos sondagens", _operationalTraffic) })
        {
            if (window is null) { text.AppendLine($"| {name} | Nao coletada | | | | | | |"); continue; }
            text.AppendLine($"| {name} | {window.Seconds:0.0} s | {window.Packets} | {window.Packets / Math.Max(.001, window.Seconds):0.0} | {window.PeakPacketsPerSecond} | {window.Resets} | {window.ZeroWindows} | {window.RepeatedSegments} |");
        }
        text.AppendLine("Variacao entre janelas pode decorrer do polling iniciado pelo teste e de trafego externo. Nao caracteriza sobrecarga sem correlacao com erros/latencia/contadores.");
        if (_baselineTraffic is { Packets: > 0 } baselineTraffic && _operationalTraffic is { } operationalTraffic)
        {
            var baselineRate = baselineTraffic.Packets / Math.Max(.001, baselineTraffic.Seconds);
            var operationalRate = operationalTraffic.Packets / Math.Max(.001, operationalTraffic.Seconds);
            text.AppendLine($"Variacao da taxa media: {100 * (operationalRate / baselineRate - 1):+0.0;-0.0;0.0}%. Nao ha limite universal de pkt/s para PLCs; avaliar junto das latencias por bloco e da capacidade do enlace.");
        }
        var capturedWindows = new[] { _baselineTraffic, _operationalTraffic }.Where(x => x is { Packets: > 0 }).ToArray();
        if (capturedWindows.Length > 0)
            text.AppendLine(capturedWindows.All(x => x!.Resets == 0 && x.ZeroWindows == 0 && x.RepeatedSegments == 0)
                ? "Nas janelas com pacotes, nao foram detectados RST, zero-window ou segmentos repetidos. Isso nao comprova ausencia de falhas fora da captura."
                : "Ha indicadores TCP nas janelas capturadas; correlacionar endereco, horario e falhas Modbus. RST pode representar encerramento intencional; segmentos repetidos nao comprovam perda fisica.");
        text.AppendLine();
        text.AppendLine(BuildTopologyReport());
        text.AppendLine("## Apendice tecnico");
        text.AppendLine(BuildTechnicalAppendix());
        return text.ToString();
    }

    private string BuildDetailedFullTestReport(string summary)
    {
        var text = new StringBuilder(summary);
        text.AppendLine();
        text.AppendLine("## Evidencias completas da execucao");
        text.AppendLine("Esta secao preserva dados brutos para auditoria. Linhas ARP, conversas e enderecos IP nao demonstram, isoladamente, dispositivos Modbus distintos nem falha fisica.");
        text.AppendLine();
        foreach (var step in FullTestSteps)
        {
            text.AppendLine($"### Etapa {step.Order}: {step.Name} - {step.Status}");
            text.AppendLine($"Objetivo: {step.Objective}");
            text.AppendLine($"Inicio: {step.StartedAt:yyyy-MM-dd HH:mm:ss.fff zzz}; termino: {step.FinishedAt:yyyy-MM-dd HH:mm:ss.fff zzz}.");
            text.AppendLine("```text");
            text.AppendLine(step.Result);
            text.AppendLine("```");
            text.AppendLine($"Recomendacao: {step.Recommendation}");
            text.AppendLine();
        }
        text.AppendLine("### Inventario de IPs observado");
        text.AppendLine("| IP | Identidade | MAC | Fonte | Papel | TCP aberto | Modbus confirmado | Modbus observado | Notas |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var row in NetworkDiscoveryRows.OrderBy(x => IpSortKey(x.Ip)))
            text.AppendLine($"| {row.Ip} | {row.DeviceIdentity} | {row.Mac} | {EscapeMarkdownTable(row.Source)} | {EscapeMarkdownTable(row.RoleGuess)} | {row.OpenTcpPorts} | {row.ConfirmedModbusPorts} | {row.ObservedModbusPorts} | {EscapeMarkdownTable(row.Notes)} |");
        text.AppendLine();
        text.AppendLine("### Conversas capturadas (ate 200)");
        text.AppendLine("| Origem | Destino | Evidencia | Pacotes | Detalhe |");
        text.AppendLine("|---|---|---|---:|---|");
        foreach (var link in TopologyLinks.Where(x => x.Evidence.StartsWith("Captura", StringComparison.Ordinal)))
            text.AppendLine($"| {link.Source} | {link.Destination} | {link.Evidence} | {link.Count} | {EscapeMarkdownTable(link.Details)} |");
        if (DiscoveredMapRows.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("### Faixas de mapa e notas completas");
            text.AppendLine("| Endpoint | UID | FC | Inicio | Fim | Modo | Confianca | Notas |");
            text.AppendLine("|---|---|---|---:|---:|---|---|---|");
            foreach (var row in DiscoveredMapRows.OrderBy(x => x.Endpoint).ThenBy(x => x.StartAddress))
                text.AppendLine($"| {row.Endpoint} | {row.UnitId} | {EscapeMarkdownTable(row.Function)} | {row.StartAddress} | {row.EndAddress} | {EscapeMarkdownTable(row.DiscoveryMode)} | {EscapeMarkdownTable(row.Confidence)} | {EscapeMarkdownTable(row.Notes)} |");
        }
        return text.ToString();
    }

    private async Task<FullTestStepResult> RunScopedHostDiscoveryAsync(CancellationToken token)
    {
        if (!EnableActiveSubnetScan)
        {
            FullTestNetworkSummary = "Varredura de outros hosts desabilitada; alvos cadastrados validados nas etapas individuais.";
            return new("Nao aplicavel", FullTestNetworkSummary, "Autorizar um CIDR explicito no escopo somente se a varredura for necessaria.");
        }
        try { ResolveAutomaticNetworkScope(); }
        catch (InvalidOperationException ex) { return new("Inconclusivo", ex.Message, "Selecionar a interface local ou configurar o CIDR; nenhum host extra sondado."); }
        if (!TryParseScanScope(ActiveScanCidr, out _, out _))
            return new("Inconclusivo", "CIDR autorizado ausente/invalido. Nenhum host adicional sondado.", "Configurar escopo IPv4 /22 a /32 e autorizacao de varredura.");
        var candidates = AuthorizedCandidates();
        if (!TryParseDiscoveryPorts(ModbusDiscoveryPorts, out _))
            return new("Inconclusivo", "Lista de portas de descoberta invalida; sondagem nao executada.", "Informar ate 16 portas TCP entre 1 e 65535.");
        var ports = DiscoveryPortsForTest();
        var timeout = Math.Clamp(ActiveScanTimeoutMs, 200, 3000);
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Ip, bool Ping, int[] Ports)>();
        var completed = 0;
        FullTestProgressLabel = $"Descoberta de rede {ActiveScanCidr}: 0/{candidates.Count} IPs; portas {string.Join(",", ports)}";
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(ActiveScanConcurrency, 1, 16), CancellationToken = token }, async (ip, cancellation) =>
        {
            await PaceProbeAsync(cancellation);
            var ping = await TryPingAsync(ip, timeout);
            var open = new List<int>();
            foreach (var port in ports)
            {
                await PaceProbeAsync(cancellation);
                if (await TryTcpConnectAsync(ip, port, timeout, cancellation)) open.Add(port);
            }
            results.Add((ip, ping, open.ToArray()));
            var count = Interlocked.Increment(ref completed);
            if (count % 5 == 0 || count == candidates.Count)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => FullTestProgressLabel = $"Descoberta {ActiveScanCidr}: {count}/{candidates.Count} IPs verificados");
        });
        foreach (var result in results.OrderBy(x => IpSortKey(x.Ip)))
        {
            foreach (var port in result.Ports) _openDiscoveryEndpoints.Add((result.Ip, port));
            if (result.Ping || result.Ports.Length > 0 || NetworkDiscoveryRows.Any(x => x.Ip == result.Ip))
            {
                UpsertDiscovery(result.Ip, "", "Sondagem autorizada", "Host observado",
                    string.Join(", ", result.Ports), $"ICMP: {(result.Ping ? "respondeu" : "sem resposta")}; TCP aberto: {string.Join(", ", result.Ports)}. Porta aberta nao confirma Modbus.");
                var row = NetworkDiscoveryRows.First(x => x.Ip == result.Ip);
                row.OpenTcpPorts = MergePortList(row.OpenTcpPorts, string.Join(",", result.Ports));
                row.ProbedPorts = MergePortList(row.ProbedPorts, string.Join(",", ports));
                row.ProbeSummary = result.Ports.Length > 0 ? "TCP aberto; confirmacao Modbus pendente" : "Sem conexao TCP nas portas sondadas";
                row.LastProbeAt = DateTimeOffset.Now.ToString("HH:mm:ss");
            }
        }
        var active = results.Count(x => x.Ping || x.Ports.Length > 0);
        FullTestNetworkSummary = $"CIDR {ActiveScanCidr}: {active}/{candidates.Count} candidatos responderam; {_openDiscoveryEndpoints.Count} endpoints TCP abertos.";
        return new(active == 0 ? "Inconclusivo" : "OK", FullTestNetworkSummary
            + $" Portas TCP verificadas: {string.Join(", ", ports)}. Limites: {ProbeRatePerSecond} sondagens/s, concorrencia {ActiveScanConcurrency}, timeout {timeout} ms. Escopo inclui alvos explicitamente cadastrados. Sem resposta nao distingue host ausente de bloqueio ICMP/firewall.",
            "Confrontar candidatos e portas abertas com inventario autorizado; confirmar protocolo em etapa propria.");
    }

    private async Task<FullTestStepResult> RunConfirmedModbusDiscoveryAsync(CancellationToken token)
    {
        if (!TryParseDiscoveryPorts(ModbusDiscoveryPorts, out _))
            return new("Inconclusivo", "Portas de descoberta invalidas.", "Configurar 1 a 16 portas TCP validas.");
        var endpoints = _openDiscoveryEndpoints.ToHashSet();
        foreach (var session in _fullTestClients) endpoints.Add((session.Address, session.Port));
        if (FullTestIsServerMode && IsServerRunning)
            endpoints.Add((LocalIp == "0.0.0.0" ? "127.0.0.1" : LocalIp, ServerPort));
        var lines = new List<string>();
        foreach (var endpoint in endpoints.OrderBy(x => x.Address).ThenBy(x => x.Port))
        {
            token.ThrowIfCancellationRequested();
            await PaceProbeAsync(token);
            var owner = ClientSessions.FirstOrDefault(x => x.Address == endpoint.Address && x.Port == endpoint.Port);
            // A remote server need not use the local simulator's Unit ID.
            var units = owner is not null ? new[] { owner.UnitId }
                : new[] { (byte)1, FullTestIsClientMode ? (byte)1 : ServerUnitId }
                    .Concat(EnableMapDiscoveryUnitSweep ? BuildMapDiscoveryUnitIds() : []).Distinct().ToArray();
            lines.Add(await ConfirmEndpointAsync(endpoint.Address, endpoint.Port, units, token));
        }
        var passive = _baselineTraffic?.ConfirmedServers.Count ?? 0;
        FullTestModbusSummary = $"{ConfirmedModbusDeviceCount} {(ConfirmedModbusDeviceCount == 1 ? "grupo" : "grupos")} por IP, com enderecos locais consolidados ({_confirmedModbusEndpoints.Count} endpoints Modbus); {endpoints.Count} candidatos ativos; {passive} endpoints de resposta na referencia passiva. IPs remotos distintos nao comprovam equipamentos fisicos distintos.";
        return new(_confirmedModbusEndpoints.Count > 0 ? "OK" : "Inconclusivo",
            FullTestModbusSummary + Environment.NewLine + string.Join(Environment.NewLine, lines)
                + Environment.NewLine + DiscoveryScopeHint
                + Environment.NewLine + $"Entradas ARP/hosts sem sondagem: {NetworkDiscoveryRows.Count(x => !x.IsSpecialAddress && !x.IsModbusConfirmed && !x.IsModbusObserved && x.ProbedPorts.Length == 0)}. Ausencia na categoria Modbus nao demonstra ausencia de servidor."
                + Environment.NewLine + "Enderecos local/loopback e LAN podem representar o mesmo servidor, nao dispositivos fisicos diferentes. Frames passivos exigem ADU completo suportado; nao ha remontagem de streams.",
            "Confirmacao de protocolo nao valida todo o mapa; consultar as leituras repetidas por bloco.");
    }

    private Task<FullTestStepResult> RunScopedTopologyAsync(CancellationToken token) => DiscoverTopologyAsync(token);

    private async Task<FullTestStepResult> ObserveRequestedServerMapAsync(CancellationToken token)
    {
        var before = _serverBlocks.ToDictionary(x => x.Key, x => x.Value.Requests);
        var seconds = Math.Clamp(PassiveObservationSeconds, 1, 3600);
        _capturePhase = "Mapa solicitado (observacao)";
        FullTestProgressLabel = $"Observar mapas requisitados por clientes externos: {seconds} s";
        await Task.Delay(TimeSpan.FromSeconds(seconds), token);
        DiscoveredMapRows.Clear();
        foreach (var item in _serverBlocks)
        {
            var stat = item.Value;
            var count = stat.Requests - before.GetValueOrDefault(item.Key);
            if (count <= 0 || stat.Address is null || stat.Quantity is null || stat.Fc is null) continue;
            DiscoveredMapRows.Add(new MapDiscoveryRow
            {
                Endpoint = stat.Endpoint, UnitId = stat.UnitId.ToString() ?? "",
                Function = FormatFunctionCode(stat.Fc.Value), StartAddress = stat.Address.Value,
                EndAddress = Math.Min(65535, stat.Address.Value + Math.Max(1, (int)stat.Quantity.Value) - 1),
                Quantity = stat.Quantity.Value, DiscoveryMode = "Observado no servidor",
                Confidence = "Range requisitado, nao mapa completo", Notes = $"{count} requisicoes em {seconds} s. Contadores independentes da retencao da timeline."
            });
        }
        return new(DiscoveredMapRows.Count == 0 ? "Inconclusivo" : "OK",
            $"Janela {seconds} s; {DiscoveredMapRows.Count} ranges requisitados observados. Limite: 4096 assinaturas; somente ranges efetivamente requisitados, nao cadastro completo do cliente.",
            "Comparar ranges/FC/UID requisitados com o mapa local e as exceptions. Ausencia de requisicoes nao demonstra ausencia de clientes na rede.");
    }

    private async Task<FullTestStepResult> RunScopedArpAsync(CancellationToken token)
    {
        var output = await RunProcessAsync("arp", "-a", token);
        var entries = ParseArpEntries(output);
        foreach (var entry in entries) UpsertDiscovery(entry.Ip, entry.Mac, "ARP", "Vizinho IPv4", "", $"Entrada ARP {entry.Type}; nao confirma host industrial.");
        var profiles = GetNetworkProfiles().Where(x => x.IsOperational).ToList();
        var targets = FullTestIsClientMode ? _fullTestClients.Select(x => x.Address).Distinct().ToArray() : [LocalIp];
        var lines = new List<string>();
        var missing = false;
        foreach (var address in targets)
        {
            if (address == "localhost" || IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip))
            { lines.Add($"{address}: nao aplicavel (loopback nao usa ARP)."); continue; }
            if (!IsIPv4(address) || address == "0.0.0.0")
            { lines.Add($"{address}: nao aplicavel ao endereco de escuta/nome DNS."); continue; }
            var profile = profiles.FirstOrDefault(x => x.PrefixLength is > 0 and <= 32 &&
                (IpToUInt32(x.Address) & (uint.MaxValue << (32 - x.PrefixLength))) == (IpToUInt32(address) & (uint.MaxValue << (32 - x.PrefixLength))));
            if (profile is null) { lines.Add($"{address}: roteado; ARP do alvo nao e esperado. Consultar rota/gateway."); continue; }
            if (profile.Address == address) { lines.Add($"{address}: endereco local; nao exige entrada ARP propria."); continue; }
            var match = entries.FirstOrDefault(x => x.Ip == address);
            if (match is null) { missing = true; lines.Add($"{address}: vizinho direto sem entrada ARP nesta amostra; nao demonstra VLAN/IP incorreto por si so."); }
            else lines.Add($"{address}: vizinho direto com MAC {match.Mac}.");
        }
        return new(missing ? "Inconclusivo" : targets.All(x => x == "0.0.0.0" || IPAddress.TryParse(x, out var ip) && IPAddress.IsLoopback(ip)) ? "Nao aplicavel" : "OK",
            string.Join(Environment.NewLine, lines) + Environment.NewLine + "Entradas brutas (apendice):" + Environment.NewLine + output,
            "Correlacionar apenas vizinhos diretos com ARP; ausencia nao substitui teste TCP nem indica causa isoladamente.");
    }

    private async Task PaceProbeAsync(CancellationToken token)
    {
        await _probeRateGate.WaitAsync(token);
        try
        {
            var remaining = _nextProbeAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
            _nextProbeAt = DateTimeOffset.UtcNow.AddSeconds(1d / Math.Clamp(ProbeRatePerSecond, 1, 100));
        }
        finally { _probeRateGate.Release(); }
    }

    public static bool TryParseScanScope(string cidr, out string address, out int prefix)
    {
        address = ""; prefix = 0;
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out prefix) || prefix is < 16 or > 32) return false;
        var bytes = ip.GetAddressBytes();
        if (bytes[0] is 0 or >= 224 || IPAddress.IsLoopback(ip)) return false;
        address = ip.ToString();
        return true;
    }

    private List<string> AuthorizedCandidates()
    {
        var candidates = new HashSet<string>();
        foreach (var session in CurrentTestTargets())
            if (IsIPv4(session.Address)) candidates.Add(session.Address);
        if (FullTestIsServerMode && IsIPv4(LocalIp) && LocalIp != "0.0.0.0") candidates.Add(LocalIp);
        if (EnableActiveSubnetScan && TryParseScanScope(ActiveScanCidr, out var address, out var prefix))
        {
            if (prefix == 32) candidates.Add(address);
            else if (prefix == 31)
            {
                var start = IpToUInt32(address) & 0xfffffffe;
                candidates.Add(UInt32ToIp(start)); candidates.Add(UInt32ToIp(start + 1));
            }
            else foreach (var ip in EnumerateSubnetHosts(address, prefix, 65536)) candidates.Add(ip);
        }
        return candidates.OrderBy(IpSortKey).ToList();
    }

    private async Task<FullTestStepResult> ObserveTrafficWindowAsync(bool baseline, CancellationToken token)
    {
        var seconds = Math.Clamp(baseline ? PassiveObservationSeconds : TcpMonitoringSeconds, 1, baseline ? 3600 : 86400);
        if (!IsNetworkCaptureRunning)
            return new("Inconclusivo", "Captura indisponivel: esta etapa nao mede trafego nem descarta erros TCP.", "Verificar Npcap, permissao, interface e filtro BPF.");
        var before = TakeInterfaceSnapshots();
        var driverBefore = _networkCapture.ReadStatistics();
        var driverAfter = driverBefore;
        var after = before;
        var drops = Interlocked.Read(ref _droppedPassivePackets);
        _capturePhase = baseline ? "Referencia passiva (sem sondagens)" : "Monitoramento operacional";
        _trafficWindow.Begin(TimeSpan.FromSeconds(seconds));
        var clock = Stopwatch.StartNew();
        TrafficWindowSummary summary;
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                token.ThrowIfCancellationRequested();
                var remaining = Math.Max(0, seconds - clock.Elapsed.TotalSeconds);
                FullTestProgressLabel = $"{_capturePhase}: {clock.Elapsed.TotalSeconds:0}/{seconds} s";
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(1, remaining)), token);
            }
            after = TakeInterfaceSnapshots();
            driverAfter = _networkCapture.ReadStatistics();
            // Let the 100 ms pcap buffer deliver the window's final frames; timestamps exclude later traffic.
            await Task.Delay(250, token);
        }
        finally
        {
            summary = _trafficWindow.End();
            if (baseline) _baselineTraffic = summary; else _operationalTraffic = summary;
            _windowUiDrops = Math.Max(0, Interlocked.Read(ref _droppedPassivePackets) - drops);
            _capturePhase = "Operacional";
        }
        var counterLines = new List<string>();
        long nicErrors = 0, nicDiscards = 0;
        foreach (var end in after)
        {
            if (!before.TryGetValue(end.Key, out var start)) continue;
            var rx = Math.Max(0, end.Value.BytesReceived - start.BytesReceived) / Math.Max(summary.Seconds, 0.001);
            var tx = Math.Max(0, end.Value.BytesSent - start.BytesSent) / Math.Max(summary.Seconds, 0.001);
            var errors = Math.Max(0, end.Value.Errors - start.Errors);
            var discards = Math.Max(0, end.Value.Discards - start.Discards);
            nicErrors += errors; nicDiscards += discards;
            counterLines.Add($"{end.Value.Name}: RX {FormatBytesPerSecond(rx)}, TX {FormatBytesPerSecond(tx)}; erros SO {errors}, descartes SO {discards}; nominal {FormatBitsPerSecond(end.Value.SpeedBitsPerSecond)} (nao equivale a capacidade util medida).");
        }
        var counters = string.Join(Environment.NewLine, counterLines.Distinct());
        var uiDrops = Math.Max(0, Interlocked.Read(ref _droppedPassivePackets) - drops);
        var driverDrops = driverBefore is null || driverAfter is null ? (long?)null
            : Math.Max(0, driverAfter.Value.Dropped - driverBefore.Value.Dropped)
                + Math.Max(0, driverAfter.Value.InterfaceDropped - driverBefore.Value.InterfaceDropped);
        if (baseline) { _baselineTraffic = summary; _baselineCounters = counters; }
        else { _operationalTraffic = summary; _operationalCounters = counters; _windowUiDrops = uiDrops; }
        foreach (var endpoint in summary.ConfirmedServers)
        {
            _confirmedModbusEndpoints.Add(endpoint);
            RegisterModbusDevice(endpoint, false, "Resposta/exception Modbus reconhecida na janela passiva; sem sondagem ativa desse endpoint.");
        }
        foreach (var host in summary.ObservedHosts)
            UpsertDiscovery(host, "", baseline ? "Referencia passiva" : "Monitoramento", GuessRole(host), "", "Host observado; nao implica dispositivo industrial.");
        foreach (var host in summary.ObservedClients)
        {
            UpsertDiscovery(host, "", "Requisicao Modbus capturada", "Cliente Modbus observado", "", "ADU de requisicao reconhecido; nao indica porta de servidor nem mapa completo do cliente.");
            var client = NetworkDiscoveryRows.FirstOrDefault(x => x.Ip == host);
            if (client is not null) client.IsModbusClientObserved = true;
        }
        var averagePps = summary.Packets / Math.Max(summary.Seconds, 0.001);
        var averageBps = summary.Bytes / Math.Max(summary.Seconds, 0.001);
        var highRate = summary.PeakPacketsPerSecond > Math.Clamp(PacketRateWarningThreshold, 1, 1000000);
        var attention = summary.Resets > 0 || summary.ZeroWindows > 0 || summary.RepeatedSegments > 0
            || summary.ModbusExceptions > 0 || summary.DetailLimitReached || highRate || uiDrops > 0 || nicErrors > 0 || nicDiscards > 0;
        var status = summary.Packets == 0 || driverDrops > 0 ? "Inconclusivo" : attention ? "Atencao" : "OK";
        if (!baseline) FullTestBandwidthSummary = $"{summary.Seconds:0.0} s; {averagePps:0.0} pkt/s; {FormatBytesPerSecond(averageBps)} observados; pico {summary.PeakPacketsPerSecond} pkt/s";
        var lines = new List<string>
        {
            $"Janela efetivamente medida: {summary.Seconds:0.0} s. Pacotes: {summary.Packets}; bytes: {summary.Bytes}.",
            $"Media: {averagePps:0.0} pkt/s, {FormatBytesPerSecond(averageBps)}. Pico por intervalo de 1 s: {summary.PeakPacketsPerSecond} pkt/s, {FormatBytesPerSecond(summary.PeakBytesPerSecond)}.",
            $"TCP: {summary.TcpPackets}; RST: {summary.Resets}; anuncios zero-window: {summary.ZeroWindows}; segmentos repetidos: {summary.RepeatedSegments}.",
            $"Frames Modbus estruturais: {summary.ModbusFrames}; exceptions: {summary.ModbusExceptions}; endpoints de resposta: {summary.ConfirmedServers.Count}.",
            $"Descartes apenas na lista da UI: {uiDrops}. Contadores da janela sao anteriores a essa fila e nao dependem do limite de 2000 linhas.",
            $"Descartes informados pelo driver de captura na janela: {(driverDrops is null ? "indisponivel" : driverDrops.ToString())}. Quando ha descarte, a carga capturada e um limite inferior, nao trafego integral.",
            $"Deltas de interfaces fisicas: erros SO {nicErrors}, descartes SO {nicDiscards}. Esses contadores nao determinam perda fisica nem representam as portas dos switches.",
            $"Limiar operacional configurado: {PacketRateWarningThreshold} pkt/s. Pico acima do limiar: {(highRate ? "sim" : "nao")}; esse limiar nao caracteriza saturacao do enlace.",
            $"Protocolos: {string.Join("; ", summary.Protocols.Select(x => $"{x.Key}={x.Value}"))}",
            $"Principais conversas: {string.Join("; ", summary.Conversations.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key}: {x.Value} ({100d * x.Value / Math.Max(1, summary.Packets):0.0}%)"))}",
            $"Conversas com sinais TCP: {(summary.Signals.Count == 0 ? "nenhuma" : string.Join("; ", summary.Signals.Take(20).Select(x => $"{x.Key}: RST {x.Value.Resets}, zero-window {x.Value.ZeroWindows}, repetidos {x.Value.RepeatedSegments}")))}",
            baseline ? "Coleta anterior as sondagens deste teste. Comunicacoes e leituras previamente ativas continuam; nao e uma rede silenciosa." : "Coleta apos as sondagens; contem polling operacional e comunicacoes existentes, sem nova varredura de hosts nesta janela.",
            "Escopo: somente trafego visivel nesta interface/BPF. Sem espelhamento, nao representa toda a rede.",
            "RST pode representar rejeicao/encerramento esperado. Repeticao de sequencia+comprimento e indicio, nao prova de retransmissao: pode haver captura duplicada/reuso de conexao. Nao ha remontagem TCP completa nem calculo de perda fisica.",
            summary.DetailLimitReached ? "Detalhes de conversas/sequencias atingiram limite; contadores totais continuam, analise de repeticao parcial." : "Limites de detalhes nao atingidos.",
            "Contadores de interfaces fisicas (apendice):", counters
        };
        return new(status, string.Join(Environment.NewLine, lines), summary.Packets == 0
            ? "Revisar interface/BPF; loopback exige interface de captura propria."
            : attention ? "Correlacionar endpoints, RST/zero-window/segmentos repetidos com falhas e latencias Modbus; os sinais nao identificam causa por si mesmos."
            : "Nenhum sinal monitorado excedeu os criterios nesta janela; nao comprova estabilidade fora dela.");
    }

    private async Task<FullTestStepResult> ValidateClientRepeatedAsync(ClientConnectionSession session, CancellationToken token)
    {
        var rows = session.Rows.Where(x => x.Enabled).ToList();
        if (rows.Count == 0) return new("Nao aplicavel", "Nenhum bloco habilitado. Cobertura do mapa: zero.", "Habilitar os blocos do equipamento para validar leituras.");
        var attempts = Math.Clamp(ReadValidationAttempts, 1, 1000);
        var interval = Math.Clamp(ReadValidationIntervalMs, 0, 60000);
        var stats = rows.Select(row => new BlockReadStatistics(session, row, attempts)).ToList();
        _blockReadStatistics.AddRange(stats);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            FullTestProgressLabel = $"{session.Name}: ciclo {attempt + 1}/{attempts}, {rows.Count} bloco(s)";
            var blockNumber = 0;
            foreach (var stat in stats)
            {
                FullTestProgressLabel = $"{session.Name}: tentativa {attempt + 1}/{attempts}; bloco {++blockNumber}/{stats.Count} ({stat.Row.Name}); leituras válidas {stats.Sum(x => x.Success)}/{stats.Sum(x => x.Attempted)}";
                var row = stat.Row;
                var clock = Stopwatch.StartNew();
                stat.Attempted++;
                try
                {
                    IReadOnlyList<ushort> values;
                    if (row.FunctionCode is 1 or 2)
                        values = (await session.Client.ReadBitsAsync(session.Address, session.Port, session.UnitId, row.FunctionCode, row.StartAddress, row.Quantity, token)).Select(x => x ? (ushort)1 : (ushort)0).ToList();
                    else values = await session.Client.ReadRegistersAsync(session.Address, session.Port, session.UnitId, row.FunctionCode, row.StartAddress, row.Quantity, token);
                    stat.Success++;
                    stat.Latencies.Add(clock.Elapsed.TotalMilliseconds);
                    row.LastValue = string.Join(", ", values); row.LastStatus = "OK";
                    session.RecordValues(row, values, "OK");
                    _confirmedModbusEndpoints.Add(session.Endpoint);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    stat.RecordError(ex);
                    row.LastStatus = ex.Message; session.RecordFailure(row, ex.Message);
                }
                row.LastReadAt = DateTime.Now.ToString("HH:mm:ss.fff");
            }
            if (attempt + 1 < attempts) await Task.Delay(interval, token);
        }
        return new(stats.Any(x => x.Success != x.Attempted) ? "Falha" : stats.Any(x => x.SlowResponse) ? "Atencao" : "OK",
            string.Join(Environment.NewLine, stats.Select(x => x.Describe()))
                + Environment.NewLine + "Tempos incluem espera pelo socket compartilhado com polling previamente ativo; nao sao exclusivamente tempo de processamento do PLC. Somente blocos habilitados foram validados."
                + Environment.NewLine + $"Intervalo entre ciclos: {interval} ms, adicional ao tempo das transacoes. Sem escrita.",
            stats.Any(x => x.Success != x.Attempted) ? "Classificar a falha por bloco e tipo (socket, timeout, exception, frame divergente)." : "Leituras repetidas validadas para esta configuracao; executar janela maior para investigar falhas intermitentes.");
    }

    private sealed class BlockReadStatistics(ClientConnectionSession session, ClientMapRow row, int planned)
    {
        public string SessionId { get; } = session.Id;
        public string Endpoint { get; } = session.Endpoint;
        public byte UnitId { get; } = session.UnitId;
        public int ScanRateMs { get; } = session.ScanRateMs;
        public ClientMapRow Row { get; } = row;
        public int Planned { get; } = planned;
        public int Attempted, Success;
        public List<double> Latencies { get; } = [];
        public Dictionary<string, int> Errors { get; } = [];
        public DateTimeOffset? FirstErrorAt, LastErrorAt;
        public bool SlowResponse => Latencies.Count >= 5 && Percentile(Latencies, .95) >= Math.Max(1, ScanRateMs);
        public void RecordError(Exception ex)
        {
            var category = ex is OperationCanceledException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ? "Timeout"
                : ex is System.Net.Sockets.SocketException se ? "Socket " + se.SocketErrorCode
                : ex.Message.StartsWith("Modbus exception") ? ex.Message : ex.GetType().Name + ": " + ex.Message;
            Errors[category] = Errors.GetValueOrDefault(category) + 1;
            FirstErrorAt ??= DateTimeOffset.Now; LastErrorAt = DateTimeOffset.Now;
        }
        public string Timing => Latencies.Count == 0 ? "sem respostas validas" : $"min {Latencies.Min():0.0}, mediana {Percentile(Latencies, .5):0.0}, p95 {Percentile(Latencies, .95):0.0}, max {Latencies.Max():0.0} ms";
        public string Describe() => $"{Row.Name}: FC{Row.FunctionCode}, addr {Row.StartAddress}, qty {Row.Quantity}; {Success}/{Attempted} leituras validas ({100d * Success / Math.Max(1, Attempted):0.0}%), planejadas {Planned}; {Timing}. Erros: {(Errors.Count == 0 ? "nenhum" : string.Join("; ", Errors.Select(x => $"{x.Key} x{x.Value}")))}."
            + (FirstErrorAt is null ? "" : $" Primeira/ultima falha: {FirstErrorAt:HH:mm:ss.fff}/{LastErrorAt:HH:mm:ss.fff}.")
            + $" p95 >= scan configurado ({ScanRateMs} ms): {(SlowResponse ? "sim" : "nao demonstrado")}."
            + (Latencies.Count < 20 ? " Amostra de cauda pequena; p95 nao caracteriza eventos raros." : "");
    }

    private sealed class ServerBlockStatistics(string endpoint, byte? uid, byte? fc, ushort? address, ushort? quantity)
    {
        public string Endpoint { get; } = endpoint;
        public byte? UnitId { get; } = uid;
        public byte? Fc { get; } = fc;
        public ushort? Address { get; } = address;
        public ushort? Quantity { get; } = quantity;
        public long Requests, Replies, Exceptions;
        public List<double> Latencies { get; } = [];
    }

    public static double Percentile(IEnumerable<double> samples, double fraction)
    {
        var sorted = samples.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
    }
}
