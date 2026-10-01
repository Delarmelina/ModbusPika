using System.Collections.ObjectModel;
using System.Text;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    public Func<bool>? ConfirmPendingDeviceEdits { get; set; }
    public ObservableCollection<FullTestStep> TestFindings { get; } = [];
    public ObservableCollection<FullTestStep> TestCoverageGaps { get; } = [];
    public string TestFindingsCaption => TestFindings.Count == 0
        ? IsFullTestRunning ? "Ainda não há achados concluídos; execução em andamento."
            : FullTestSteps.Any(x => x.FinishedAt is not null) ? "Nenhuma falha ou atenção registrada nas etapas concluídas. Verifique também as limitações de cobertura abaixo."
            : "Execute um teste para obter os achados."
        : $"{TestFindings.Count} etapa(s) com falha ou atenção. Selecione um achado para consultar a evidência completa.";
    public string TestCoverageCaption => TestCoverageGaps.Count == 0 ? "Nenhuma etapa concluída como inconclusiva."
        : $"{TestCoverageGaps.Count} verificação(ões) inconclusiva(s) ou interrompida(s). Não representam, por si só, falhas do equipamento.";
    public string FullTestElapsed => _fullTestStartedAt == default || FullTestStartedAtText == "-" ? "Tempo decorrido: —"
        : $"Tempo decorrido: {((IsFullTestRunning ? DateTimeOffset.Now : _fullTestFinishedAt ?? _fullTestStartedAt) - _fullTestStartedAt):hh\\:mm\\:ss}  ·  Etapa atual: {FullTestSteps.FirstOrDefault(x => x.Status == "Executando")?.Name ?? "nenhuma"}";

    public void RefreshTestExperience()
    {
        OnPropertyChanged(nameof(FullTestElapsed));
        SyncSteps(TestFindings, FullTestSteps.Where(x => x.Name != "Conclusao" && (x.Status is "Falha" or "Erro" or "Atencao"))
            .OrderBy(x => x.Status == "Atencao" ? 1 : 0));
        SyncSteps(TestCoverageGaps, FullTestSteps.Where(x => x.Status is "Inconclusivo" or "Cancelado"));
        OnPropertyChanged(nameof(TestFindingsCaption));
        OnPropertyChanged(nameof(TestCoverageCaption));
    }

    private static void SyncSteps(ObservableCollection<FullTestStep> destination, IEnumerable<FullTestStep> values)
    {
        var items = values.ToArray();
        if (destination.SequenceEqual(items)) return;
        destination.Clear();
        foreach (var item in items) destination.Add(item);
    }

    public string BuildTestReview()
    {
        var targets = TestMode == "Client" ? ConfiguredTestTargets() : [];
        var text = new StringBuilder();
        text.AppendLine($"PAPEL ANALISADO: {(TestMode == "Client" ? "Cliente / Mestre" : "Servidor / Escravo")}");
        text.AppendLine("A seleção lateral não altera este escopo. O teste não executa escritas Modbus.");
        text.AppendLine();
        text.AppendLine("ALVOS E MAPAS");
        if (TestMode != "Client") text.AppendLine($"Servidor local: {ServerName} · {ServerEndpoint}. Observa requisições de clientes externos.");
        else if (targets.Length == 0) text.AppendLine("Sem alvos cadastrados incluídos: valida somente servidores confirmados pela descoberta ativa autorizada.");
        else foreach (var target in targets) text.AppendLine($"• {target.Name} · {target.Endpoint} · UID {target.UnitId} · {target.Rows.Count(x => x.Enabled)} bloco(s) habilitado(s)");
        text.AppendLine();
        text.AppendLine("COLETA E SONDAGEM");
        text.AppendLine(IsNetworkCaptureRunning
            ? $"Captura já ativa: {SelectedCaptureDevice?.Description}. BPF em uso: {GeneratedCaptureFilter}."
            : $"Captura: interface selecionada na preparação conforme tráfego; seleção atual {SelectedCaptureDevice?.Description ?? "indisponível"}. BPF previsto: {BuildCaptureFilter()}.");
        text.AppendLine($"Varredura ativa: {(EnableActiveSubnetScan ? "habilitada" : "desabilitada")}. Rede: {(AutomaticNetworkScope ? "sub-rede automática da interface (definida na preparação)" : ActiveScanCidr)}. Portas: {ModbusDiscoveryPorts}.");
        text.AppendLine($"Rotas ICMP: {(EnableRouteTracing ? $"habilitadas; até {RouteTraceMaxTargets} destinos e {RouteTraceMaxHops} saltos, timeout {RouteTraceTimeoutMs} ms" : "desabilitadas")}.");
        text.AppendLine($"Limites: {ProbeRatePerSecond} sondagens/s; concorrência {ActiveScanConcurrency}; timeout TCP {ActiveScanTimeoutMs} ms.");
        text.AppendLine($"Referência passiva: {PassiveObservationSeconds} s. Monitoramento operacional: {TcpMonitoringSeconds} s.");
        text.AppendLine($"Validação: {ReadValidationAttempts} tentativas por bloco; pausa adicional de {ReadValidationIntervalMs} ms entre ciclos.");
        text.AppendLine($"Descoberta de mapa: {(EnableMapDiscovery ? TestMode == "Client" ? $"leitura ativa de 0 a {MapDiscoveryMaxAddress}, blocos de {MapDiscoveryBlockSize}; {(EnableMapDiscoveryUnitSweep ? $"UIDs {MapDiscoveryUnitIdStart} a {MapDiscoveryUnitIdEnd}" : "UID dos alvos")}" : "observação das requisições recebidas pelo servidor" : "desabilitada")}.");
        var waits = PassiveObservationSeconds + TcpMonitoringSeconds
            + targets.Count(x => x.Rows.Any(r => r.Enabled)) * Math.Max(0, ReadValidationAttempts - 1) * ReadValidationIntervalMs / 1000d;
        text.AppendLine();
        text.AppendLine($"TEMPO PROGRAMADO: aproximadamente {TimeSpan.FromSeconds(waits):hh\\:mm\\:ss} somente de janelas e pausas.");
        text.AppendLine("A duração total será maior: soma transações, descoberta, rotas e timeouts. Novos servidores e fallback ponto a ponto podem ampliar significativamente o tempo; não se trata de previsão de término.");
        text.AppendLine();
        text.AppendLine("A execução pode iniciar o cliente/servidor e a captura automaticamente. Cancelar interrompe o teste, mas não encerra necessariamente essas operações. Confirme a autorização de sondagem antes de continuar.");
        return text.ToString();
    }
}

public sealed partial class FullTestStep
{
    public string EvidencePreview => Result.Length <= 280 ? Result : Result[..280] + "…";
    partial void OnResultChanged(string value) => OnPropertyChanged(nameof(EvidencePreview));
}
