using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class FullTestScopeDialog : Window
{
    public FullTestScopeDialog(bool enableActiveSubnetScan, int timeoutMs, int concurrency, int observationSeconds,
        int monitoringSeconds = 30, int attempts = 15, int intervalMs = 1000, string cidr = "", int probeRate = 5, int packetRate = 5000,
        bool traceRoutes = true, int traceHops = 12, int traceTimeout = 500, int traceTargets = 8, string discoveryPorts = "502,1501,1502", bool automaticScope = true)
    {
        InitializeComponent();
        EnableActiveSubnetScan = enableActiveSubnetScan;
        ActiveScanTimeoutMs = timeoutMs;
        ActiveScanConcurrency = concurrency;
        PassiveObservationSeconds = observationSeconds;

        ActiveSubnetScanCheckBox.IsChecked = enableActiveSubnetScan;
        TimeoutTextBox.Text = timeoutMs.ToString();
        ConcurrencyTextBox.Text = concurrency.ToString();
        ObservationTextBox.Text = observationSeconds.ToString();
        MonitoringTextBox.Text = monitoringSeconds.ToString();
        AttemptsTextBox.Text = attempts.ToString();
        ReadIntervalTextBox.Text = intervalMs.ToString();
        CidrTextBox.Text = cidr;
        ProbeRateTextBox.Text = probeRate.ToString();
        PacketRateTextBox.Text = packetRate.ToString();
        TraceRoutesCheckBox.IsChecked = traceRoutes;
        TraceHopsTextBox.Text = traceHops.ToString();
        TraceTimeoutTextBox.Text = traceTimeout.ToString();
        TraceTargetsTextBox.Text = traceTargets.ToString();
        DiscoveryPortsTextBox.Text = discoveryPorts;
        AutomaticScopeCheckBox.IsChecked = automaticScope;
        ActiveSubnetScanCheckBox.Content = "Descobrir equipamentos de toda a sub-rede durante o teste";
        UiLocalization.Apply(this);
    }

    public bool EnableActiveSubnetScan { get; private set; }
    public int ActiveScanTimeoutMs { get; private set; }
    public int ActiveScanConcurrency { get; private set; }
    public int PassiveObservationSeconds { get; private set; }
    public int TcpMonitoringSeconds { get; private set; }
    public int ReadValidationAttempts { get; private set; }
    public int ReadValidationIntervalMs { get; private set; }
    public string ActiveScanCidr { get; private set; } = "";
    public int ProbeRatePerSecond { get; private set; }
    public int PacketRateWarningThreshold { get; private set; }
    public bool EnableRouteTracing { get; private set; }
    public int RouteTraceMaxHops { get; private set; }
    public int RouteTraceTimeoutMs { get; private set; }
    public int RouteTraceMaxTargets { get; private set; }
    public string ModbusDiscoveryPorts { get; private set; } = "502,1501,1502";
    public bool AutomaticNetworkScope { get; private set; } = true;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutTextBox.Text, out var timeout) || timeout is < 200 or > 3000)
        {
            ValidationText.Text = "O tempo limite deve estar entre 200 e 3000 ms.";
            return;
        }

        if (!int.TryParse(ConcurrencyTextBox.Text, out var concurrency) || concurrency is < 1 or > 16)
        {
            ValidationText.Text = "A concorrencia deve estar entre 1 e 16.";
            return;
        }

        if (!int.TryParse(ObservationTextBox.Text, out var observation) || observation is < 1 or > 3600)
        {
            ValidationText.Text = "A referencia passiva deve estar entre 1 e 3600 segundos.";
            return;
        }

        if (!int.TryParse(MonitoringTextBox.Text, out var monitoring) || monitoring is < 1 or > 86400
            || !int.TryParse(AttemptsTextBox.Text, out var attempts) || attempts is < 1 or > 1000
            || !int.TryParse(ReadIntervalTextBox.Text, out var interval) || interval is < 100 or > 60000
            || !int.TryParse(ProbeRateTextBox.Text, out var rate) || rate is < 1 or > 100
            || !int.TryParse(PacketRateTextBox.Text, out var packetRate) || packetRate is < 1 or > 1000000)
        {
            ValidationText.Text = "Revise duracao TCP, tentativas, intervalo e limiares conforme os limites indicados.";
            return;
        }
        if (ActiveSubnetScanCheckBox.IsChecked == true && AutomaticScopeCheckBox.IsChecked != true && !MainViewModel.TryParseScanScope(CidrTextBox.Text, out _, out _))
        {
            ValidationText.Text = "Informe um CIDR IPv4 /16 a /32 ou use a deteccao automatica da sub-rede.";
            return;
        }
        if (!int.TryParse(TraceHopsTextBox.Text, out var hops) || hops is < 1 or > 30
            || !int.TryParse(TraceTimeoutTextBox.Text, out var traceTimeout) || traceTimeout is < 200 or > 3000
            || !int.TryParse(TraceTargetsTextBox.Text, out var traceTargets) || traceTargets is < 1 or > 32)
        {
            ValidationText.Text = "Rota ICMP: 1 a 30 saltos, timeout 200 a 3000 ms e 1 a 32 alvos.";
            return;
        }
        if (!MainViewModel.TryParseDiscoveryPorts(DiscoveryPortsTextBox.Text, out var discoveryPorts))
        {
            ValidationText.Text = "Informe 1 a 16 portas TCP validas, separadas por virgula (ex.: 502,1501,1502).";
            return;
        }
        ModbusDiscoveryPorts = string.Join(",", discoveryPorts);
        AutomaticNetworkScope = AutomaticScopeCheckBox.IsChecked == true;
        EnableRouteTracing = TraceRoutesCheckBox.IsChecked == true;
        RouteTraceMaxHops = hops;
        RouteTraceTimeoutMs = traceTimeout;
        RouteTraceMaxTargets = traceTargets;
        EnableActiveSubnetScan = ActiveSubnetScanCheckBox.IsChecked == true;
        ActiveScanTimeoutMs = timeout;
        ActiveScanConcurrency = concurrency;
        PassiveObservationSeconds = observation;
        TcpMonitoringSeconds = monitoring;
        ReadValidationAttempts = attempts;
        ReadValidationIntervalMs = interval;
        ActiveScanCidr = CidrTextBox.Text.Trim();
        ProbeRatePerSecond = rate;
        PacketRateWarningThreshold = packetRate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
