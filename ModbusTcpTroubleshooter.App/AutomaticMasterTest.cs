using System.Net;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    private async Task<FullTestStepResult> RunDiscoveredServersValidationAsync(CancellationToken token)
    {
        var endpoints = _confirmedModbusEndpoints
            .Select(x => IPEndPoint.TryParse(x, out var endpoint) ? endpoint : null)
            .Where(x => x is not null && DeviceIdentityFor(x.Address.ToString(), _localIpv4Addresses) != "Este computador"
                && NetworkDiscoveryRows.Any(row => row.Ip == x.Address.ToString() && row.IsModbusConfirmed
                    && row.ConfirmedModbusPorts.Split(',').Any(port => port.Trim() == x.Port.ToString())))
            .Cast<IPEndPoint>().OrderBy(x => IpSortKey(x.Address.ToString())).ThenBy(x => x.Port).ToArray();
        if (endpoints.Length == 0)
            return new("Inconclusivo", "Nenhum servidor Modbus remoto foi confirmado por sondagem ativa no escopo autorizado. Respostas apenas capturadas permanecem no inventario passivo; nenhuma leitura automatica foi enviada a esses IPs.",
                "Confira a interface, o CIDR autorizado, as portas de descoberta e o filtro de captura. Sem alvo cadastrado, a ausencia de resposta nao comprova ausencia de PLCs.");

        const int maxEndpoints = 64;
        var lines = new List<string>();
        var failures = 0;
        var withoutMap = 0;
        foreach (var endpoint in endpoints.Take(maxEndpoints))
        {
            token.ThrowIfCancellationRequested();
            var address = endpoint.Address.ToString();
            var key = $"{address}:{endpoint.Port}";
            var unitId = _discoveredUnitIds.GetValueOrDefault(key, (byte)1);
            var rows = BuildClientRowsFromDiscovery(DiscoveredMapRows, key, unitId)
                .GroupBy(x => x.Function).Select(x => x.First()).ToArray();
            FullTestProgressLabel = $"Validando servidor descoberto {key} UID {unitId}";
            if (rows.Length == 0)
            {
                withoutMap++;
                var attempts = Math.Clamp(ReadValidationAttempts, 1, 1000);
                var replies = 0;
                var errors = new List<string>();
                for (var attempt = 0; attempt < attempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    var probe = await ProbeMapRangeAsync(address, endpoint.Port, unitId,
                        ModbusProtocol.ReadHoldingRegisters, 0, 1, Math.Clamp(ActiveScanTimeoutMs, 1000, 3000), token);
                    if (probe.Success || probe.Error.StartsWith("Modbus exception", StringComparison.OrdinalIgnoreCase)) replies++;
                    else if (errors.Count < 3) errors.Add(probe.Error);
                    if (attempt + 1 < attempts) await Task.Delay(Math.Clamp(ReadValidationIntervalMs, 0, 60000), token);
                }
                if (replies < attempts) failures++;
                lines.Add($"{key} UID {unitId}: {replies}/{attempts} respostas de protocolo; mapa nao validado"
                    + (errors.Count == 0 ? "." : $". Falhas: {string.Join("; ", errors)}."));
                continue;
            }

            var session = new ClientConnectionSession
            {
                Name = $"Descoberto {address}", Address = address, Port = endpoint.Port,
                UnitId = unitId, ScanRateMs = Math.Max(100, ReadValidationIntervalMs)
            };
            foreach (var row in session.Rows.ToArray()) session.RemoveRow(row);
            foreach (var row in rows) session.AddRow(row);
            try
            {
                var result = await ValidateClientRepeatedAsync(session, token);
                if (result.Status == "Falha") failures++;
                lines.Add($"{key} UID {unitId}: {result.Status}; {rows.Length} bloco(s) amostrado(s). {result.Detail}");
            }
            finally { await session.DisconnectAsync(); }
        }
        var omitted = Math.Max(0, endpoints.Length - maxEndpoints);
        return new(failures > 0 ? "Falha" : withoutMap > 0 || omitted > 0 ? "Atencao" : "OK",
            $"Servidores remotos confirmados: {endpoints.Length}; validados: {Math.Min(endpoints.Length, maxEndpoints)}; sem mapa: {withoutMap}; omitidos pelo limite: {omitted}. Nenhum alvo permanente foi criado e nenhuma escrita foi enviada."
                + Environment.NewLine + string.Join(Environment.NewLine, lines),
            withoutMap > 0 ? "Ative Descoberta de mapa para validar faixas antes das leituras repetidas; resposta de protocolo, inclusive exception Modbus, nao prova endereco legivel."
                : "Compare as taxas de resposta e latencias por bloco com o intervalo de leitura configurado.");
    }
}
