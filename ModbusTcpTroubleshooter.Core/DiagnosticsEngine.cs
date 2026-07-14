namespace ModbusTcpTroubleshooter.Core;

public sealed class DiagnosticsEngine
{
    public DiagnosticFinding Analyze(TrafficEvent trafficEvent)
    {
        var severity = "Info";
        var message = trafficEvent.Summary;
        var recommendation = "Continue observando o ciclo de polling.";

        if (trafficEvent.Summary.Contains("fora do mapa", StringComparison.OrdinalIgnoreCase))
        {
            severity = "Alerta";
            recommendation = "Compare offset 0-based/1-based e confirme se o mapa carregado cobre o range solicitado.";
        }
        else if (trafficEvent.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase))
        {
            severity = "Erro";
            recommendation = "Verifique function code, permissao de escrita e range do endereco.";
        }
        else if (trafficEvent.Summary.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            severity = "Erro";
            recommendation = "Confirme IP, porta, firewall, cabo/rede e se outro software ja esta usando a porta.";
        }
        else if (trafficEvent.FunctionCode is 5 or 6 or 15 or 16)
        {
            severity = "Atencao";
            recommendation = "Escrita detectada. Confirme se esse ponto pode ser alterado em ambiente real.";
        }

        return new DiagnosticFinding(DateTimeOffset.Now, severity, message, recommendation);
    }
}
