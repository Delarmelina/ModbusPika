using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    [ObservableProperty] private string testMode = "Client";
    [ObservableProperty] private string fullTestRunScope = "Nenhuma execucao realizada.";
    private string _fullTestMode = "Client";

    public bool FullTestIsClientMode => (IsFullTestRunning ? _fullTestMode : TestMode) == "Client";
    public bool FullTestIsServerMode => !FullTestIsClientMode;
    public string FullTestModeLabel => FullTestIsClientMode ? "Cliente / Mestre" : "Servidor / Escravo";
    public string FullTestTargetSummary => ClientSessions.Count(x => x.IncludeInFullTest) == 0
        ? "Nenhum alvo selecionado: o teste buscara servidores Modbus no escopo autorizado; trafego apenas capturado nao sera sondado."
        : $"{ClientSessions.Count(x => x.IncludeInFullTest)} de {ClientSessions.Count} servidores-alvo incluidos.";
    private ClientConnectionSession[] ConfiguredTestTargets() =>
        ClientSessions.Where(x => x.IncludeInFullTest).ToArray();
    private ClientConnectionSession[] CurrentTestTargets() =>
        IsFullTestRunning ? _fullTestClients : ConfiguredTestTargets();

    public void SetFullTestTargets(IEnumerable<ClientConnectionSession> targets)
    {
        if (IsFullTestRunning) throw new InvalidOperationException("Aguarde o termino do teste.");
        var selected = targets.ToHashSet();
        foreach (var session in ClientSessions) session.IncludeInFullTest = selected.Contains(session);
        RefreshFullTestScope();
    }

    private void RefreshFullTestScope()
    {
        OnPropertyChanged(nameof(FullTestIsClientMode));
        OnPropertyChanged(nameof(FullTestIsServerMode));
        OnPropertyChanged(nameof(FullTestModeLabel));
        OnPropertyChanged(nameof(FullTestTargetSummary));
        StartFullTestCommand.NotifyCanExecuteChanged();
        // Changing the next run's configuration must not discard the previous results.
        if (!IsFullTestRunning && string.IsNullOrWhiteSpace(FullTestReport))
        {
            CreateFullTestPlan();
            UpdateFullTestSummaryCards();
        }
    }

    partial void OnTestModeChanged(string value) => RefreshFullTestScope();
}
