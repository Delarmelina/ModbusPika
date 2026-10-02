using System.Text;

namespace ModbusTcpTroubleshooter.App;

public partial class MainViewModel
{
    private TcpTimelineRow[] _reportTrafficRows = [];

    private string BuildTimelineTrafficReport(int limit)
    {
        var text = new StringBuilder();
        text.AppendLine("## Dados da linha do tempo TCP");
        text.AppendLine();
        var rows = _reportTrafficRows;
        text.AppendLine("Amostra congelada ao gerar o relatorio: apenas quadros desta execucao ainda retidos na linha do tempo (limite de 2.000). Inclui sondagens e trafego externo; nao equivale somente as janelas passivas de referencia e monitoramento.");
        if (rows.Length == 0)
        {
            text.AppendLine("Nenhum quadro retido nesta execucao. Nao ha amostra para calcular taxas ou rankings. Verificar interface/BPF e se o historico foi limpo; ausencia na captura nao comprova ausencia de comunicacao.");
            return text.ToString();
        }
        var seconds = (rows[^1].Timestamp - rows[0].Timestamp).TotalSeconds;
        var bytes = rows.Sum(x => (long)x.Length);
        var buckets = rows.GroupBy(x => x.Timestamp.ToUnixTimeSeconds()).Where(g =>
            g.Key * 1000 >= rows[0].Timestamp.ToUnixTimeMilliseconds()
            && (g.Key + 1) * 1000 <= rows[^1].Timestamp.ToUnixTimeMilliseconds()).ToArray();
        text.AppendLine("### Resumo da amostra");
        text.AppendLine($"Periodo observado: {rows[0].Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} a {rows[^1].Timestamp:HH:mm:ss.fff zzz}; {seconds:0.0} s entre primeiro e ultimo quadro.");
        text.AppendLine($"Quadros: {rows.Length:N0}. Volume: {bytes / 1024d:0.0} KiB.");
        text.AppendLine(seconds > 0 ? $"Media: {rows.Length / seconds:0.0} pkt/s; {bytes / seconds / 1024d:0.0} KiB/s." : "Taxas indisponiveis: marcas de tempo insuficientes.");
        text.AppendLine(buckets.Length > 0 ? $"Picos em segundos completos: {buckets.Max(g => g.Count()):N0} pkt/s; {buckets.Max(g => g.Sum(x => (long)x.Length)) / 1024d:0.0} KiB/s." : "Picos indisponiveis: nenhum segundo completo na amostra.");
        text.AppendLine("Bytes/s nao representam utilizacao percentual do enlace nem comprovam sobrecarga. Correlacionar com latencias, falhas Modbus e contadores de NIC/switch. Retencao, BPF e descartes podem limitar a representatividade.");
        text.AppendLine();
        text.AppendLine("### Distribuicao por protocolo");
        text.AppendLine("| Protocolo | Quadros | Participacao | KiB |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var g in rows.GroupBy(x => x.Protocol).OrderByDescending(g => g.Count()))
            text.AppendLine($"| {EscapeMarkdownTable(g.Key)} | {g.Count()} | {100d * g.Count() / rows.Length:0.0}% | {g.Sum(x => (long)x.Length) / 1024d:0.0} |");
        var hosts = rows.SelectMany(x => new[] {
            (Host: Host(x.SourceHost, x.Source), Sent: true, Row: x),
            (Host: Host(x.DestinationHost, x.Destination), Sent: false, Row: x) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Host)).GroupBy(x => x.Host, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(x => (long)x.Row.Length)).Take(limit);
        text.AppendLine();
        text.AppendLine($"### Top {limit} dispositivos por volume observado");
        text.AppendLine("| IP / MAC | Quadros enviados | Quadros recebidos | KiB enviados | KiB recebidos | Participacao em quadros |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var g in hosts)
            text.AppendLine($"| {EscapeMarkdownTable(g.Key)} | {g.Count(x => x.Sent)} | {g.Count(x => !x.Sent)} | {g.Where(x => x.Sent).Sum(x => (long)x.Row.Length) / 1024d:0.0} | {g.Where(x => !x.Sent).Sum(x => (long)x.Row.Length) / 1024d:0.0} | {100d * g.Select(x => x.Row).Distinct().Count() / rows.Length:0.0}% |");
        text.AppendLine("Ordenacao por bytes enviados + recebidos. Cada quadro envolve origem e destino: percentuais de dispositivos se sobrepoem e nao devem ser somados. IPs nao equivalem necessariamente a equipamentos fisicos distintos.");
        text.AppendLine();
        text.AppendLine($"### Top {limit} conversas por volume observado");
        text.AppendLine("| Endpoint A | Endpoint B | Protocolos | Quadros | KiB | Participacao em quadros |");
        text.AppendLine("|---|---|---|---:|---:|---:|");
        foreach (var g in rows.GroupBy(x => string.Compare(x.Source, x.Destination, StringComparison.OrdinalIgnoreCase) <= 0
                     ? (A: x.Source, B: x.Destination) : (A: x.Destination, B: x.Source))
                 .OrderByDescending(g => g.Sum(x => (long)x.Length)).Take(limit))
            text.AppendLine($"| {EscapeMarkdownTable(g.Key.A)} | {EscapeMarkdownTable(g.Key.B)} | {EscapeMarkdownTable(string.Join(", ", g.Select(x => x.Protocol).Distinct().Order()))} | {g.Count()} | {g.Sum(x => (long)x.Length) / 1024d:0.0} | {100d * g.Count() / rows.Length:0.0}% |");
        text.AppendLine("Conversas bidirecionais por par de endpoints (incluindo portas quando disponiveis). Alto volume identifica concentracao de trafego, nao causa raiz nem sobrecarga por si so.");
        return text.ToString();
    }

    private static string Host(string host, string endpoint) => !string.IsNullOrWhiteSpace(host) ? host
        : endpoint.Contains(':') ? endpoint[..endpoint.LastIndexOf(':')] : endpoint;
}
