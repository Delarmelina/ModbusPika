using System.Net;
using System.Net.Sockets;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    public (string Address, int Port, byte UnitId, int MapCount) SuggestDiscoveredTarget(NetworkDiscoveryRow host)
    {
        var addresses = host.DeviceIdentity == "Este computador"
            ? NetworkDiscoveryRows.Where(x => x.DeviceIdentity == "Este computador").Select(x => x.Ip).Append(host.Ip).Distinct().ToArray()
            : [host.Ip];
        var candidates = addresses.SelectMany(address => NetworkDiscoveryRows.Where(x => x.Ip == address).DefaultIfEmpty(host)
            .SelectMany(row => ExtractPorts(row.ConfirmedModbusPorts).Concat(ExtractPorts(row.ObservedModbusPorts))
                .Concat(ExtractPorts(row.OpenTcpPorts)).Distinct().Select(port => (Address: address, Port: port))))
            .Distinct().ToList();
        var best = candidates.OrderByDescending(x => DiscoveredMapRows.Count(row => row.Endpoint == $"{x.Address}:{x.Port}" && IsActiveDiscoveredMap(row)))
            .ThenByDescending(x => NetworkDiscoveryRows.Any(row => row.Ip == x.Address && row.ConfirmedModbusPorts.Split(',').Any(port => port.Trim() == x.Port.ToString())))
            .FirstOrDefault();
        var address = best.Address ?? host.Ip;
        var port = best.Port == 0 ? 502 : best.Port;
        var endpoint = $"{address}:{port}";
        var uid = DiscoveredMapRows.Where(row => row.Endpoint == endpoint && IsActiveDiscoveredMap(row))
            .Select(row => byte.TryParse(row.UnitId, out var value) ? (byte?)value : null).FirstOrDefault(x => x.HasValue)
            ?? _discoveredUnitIds.GetValueOrDefault(endpoint, (byte)1);
        return (address, port, uid, GetReusableDiscoveredMap(endpoint, uid).Count);
    }

    private static bool IsActiveDiscoveredMap(MapDiscoveryRow row) =>
        row.DiscoveryMode.StartsWith("Ativo", StringComparison.OrdinalIgnoreCase)
        && ClientMapRow.AvailableFunctions.Contains(row.Function);

    public IReadOnlyList<MapDiscoveryRow> GetReusableDiscoveredMap(string endpoint, byte unitId) =>
        DiscoveredMapRows.Where(row => row.Endpoint == endpoint && byte.TryParse(row.UnitId, out var value)
            && value == unitId && IsActiveDiscoveredMap(row)).ToArray();

    public static IReadOnlyList<ClientMapRow> BuildClientRowsFromDiscovery(IEnumerable<MapDiscoveryRow> discovered, string endpoint, byte unitId)
    {
        var rows = new List<ClientMapRow>();
        foreach (var function in ClientMapRow.AvailableFunctions)
        {
            var ranges = discovered.Where(row => row.Endpoint == endpoint && byte.TryParse(row.UnitId, out var value)
                && value == unitId && IsActiveDiscoveredMap(row) && row.Function == function
                && row.StartAddress is >= 0 and <= ushort.MaxValue && row.EndAddress >= row.StartAddress && row.EndAddress <= ushort.MaxValue)
                .OrderBy(row => row.StartAddress).ThenBy(row => row.EndAddress).ToArray();
            var merged = new List<(int Start, int End)>();
            foreach (var range in ranges)
            {
                if (merged.Count > 0 && range.StartAddress <= merged[^1].End + 1)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.EndAddress));
                else merged.Add((range.StartAddress, range.EndAddress));
            }
            foreach (var (start, end) in merged)
                for (var address = start; address <= end; address += 120)
                {
                    rows.Add(new ClientMapRow
                    {
                        Name = $"Bloco {rows.Count + 1}", Function = function, StartAddress = (ushort)address,
                        Quantity = (ushort)Math.Min(120, end - address + 1), Enabled = true
                    });
                    if (rows.Count > 1024) throw new InvalidOperationException("Mapa com mais de 1024 blocos; reduza o endereco maximo antes de importar.");
                }
        }
        return rows;
    }

    public ClientConnectionSession AddDiscoveredTarget(string address, int port, byte unitId, int scanMs, IReadOnlyList<MapDiscoveryRow> discovered)
    {
        if (IsFullTestRunning || IsDeviceProbeRunning) throw new InvalidOperationException("Aguarde a verificacao em andamento.");
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || ip.GetAddressBytes()[0] is 0 or >= 224 || port is < 1 or > 65535 || scanMs < 100)
            throw new ArgumentException("Informe IPv4 unicast, porta valida e scan de pelo menos 100 ms.");
        var endpoint = $"{ip}:{port}";
        var map = BuildClientRowsFromDiscovery(discovered, endpoint, unitId);
        var existing = ClientSessions.FirstOrDefault(x => x.Address == ip.ToString() && x.Port == port && x.UnitId == unitId);
        if (existing is not null)
        {
            SelectedClientSession = existing;
            if (existing.Rows.Count == 0 && map.Count > 0)
            {
                foreach (var row in map) existing.AddRow(row);
                SelectedClientMapRow = existing.Rows.FirstOrDefault();
                Status = $"Alvo {endpoint} UID {unitId} atualizado com {map.Count} blocos descobertos. Leitura ainda nao iniciada.";
            }
            else Status = $"Alvo {endpoint} UID {unitId} ja cadastrado; mapa existente preservado.";
            return existing;
        }
        var session = AddClientSession(ip.ToString(), port, unitId, scanMs, true);
        session.Name = $"Alvo {ip}";
        foreach (var row in session.Rows.ToArray()) session.RemoveRow(row);
        foreach (var row in map) session.AddRow(row);
        SelectedClientMapRow = session.Rows.FirstOrDefault();
        Status = map.Count == 0
            ? $"Alvo {endpoint} cadastrado sem mapa; adicione blocos antes de iniciar leitura."
            : $"Alvo {endpoint} cadastrado com {map.Count} blocos descobertos. Leitura ainda nao iniciada.";
        return session;
    }

    public async Task<IReadOnlyList<MapDiscoveryRow>> DiscoverMapForTargetAsync(string address, int port, byte unitId, CancellationToken token)
    {
        if (IsFullTestRunning || IsDeviceProbeRunning) throw new InvalidOperationException("Aguarde a verificacao em andamento.");
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || ip.GetAddressBytes()[0] is 0 or >= 224 || port is < 1 or > 65535)
            throw new ArgumentException("Informe um IPv4 unicast e uma porta TCP valida.");
        var functions = BuildSelectedMapDiscoveryFunctions();
        if (functions.Count == 0) throw new InvalidOperationException("Habilite ao menos uma funcao de leitura FC01 a FC04 nas configuracoes de descoberta.");
        var maxAddress = Math.Clamp(MapDiscoveryMaxAddress, 0, ushort.MaxValue);
        var blockSize = Math.Clamp(MapDiscoveryBlockSize, 1, 120);
        var timeoutMs = Math.Clamp(ActiveScanTimeoutMs, 1000, 3000);
        var results = new List<MapDiscoveryRow>();
        IsDeviceProbeRunning = true;
        try
        {
            foreach (var functionCode in functions)
            {
                var discovered = new List<MapRangeProbe>();
                var transportFailures = 0;
                for (var start = 0; start <= maxAddress; start += blockSize)
                {
                    token.ThrowIfCancellationRequested();
                    var quantity = (ushort)Math.Min(blockSize, maxAddress - start + 1);
                    var probe = await ProbeMapRangeAsync(ip.ToString(), port, unitId, functionCode, (ushort)start, quantity, timeoutMs, token);
                    if (!probe.Success && IsTransportFailure(probe.Error))
                    {
                        await Task.Delay(100, token);
                        probe = await ProbeMapRangeAsync(ip.ToString(), port, unitId, functionCode, (ushort)start, quantity, timeoutMs, token);
                    }
                    if (probe.Success)
                    {
                        transportFailures = 0;
                        discovered.Add(new MapRangeProbe((ushort)start, (ushort)(start + quantity - 1), probe.Sample));
                        continue;
                    }
                    if (IsUnsupportedFunction(probe.Error)) break;
                    if (IsTransportFailure(probe.Error))
                    {
                        if (++transportFailures >= 2) break;
                        continue;
                    }
                    if (!EnableMapDiscoveryPointFallback || blockSize == 1) continue;
                    for (var addressInBlock = start; addressInBlock < start + quantity; addressInBlock++)
                    {
                        var point = await ProbeMapRangeAsync(ip.ToString(), port, unitId, functionCode, (ushort)addressInBlock, 1, timeoutMs, token);
                        if (point.Success) discovered.Add(new MapRangeProbe((ushort)addressInBlock, (ushort)addressInBlock, point.Sample));
                        else if (IsUnsupportedFunction(point.Error)) break;
                    }
                }
                foreach (var range in MergeMapRanges(discovered))
                    results.Add(new MapDiscoveryRow
                    {
                        Endpoint = $"{ip}:{port}", UnitId = unitId.ToString(), Function = FormatFunctionCode(functionCode),
                        StartAddress = range.Start, EndAddress = range.End, Quantity = range.End - range.Start + 1,
                        DiscoveryMode = "Ativo/manual", Confidence = "Faixa validada por leitura",
                        Notes = $"Leitura somente leitura OK. Amostra: {range.Sample}"
                    });
            }
            return results;
        }
        finally { IsDeviceProbeRunning = false; }
    }
}
