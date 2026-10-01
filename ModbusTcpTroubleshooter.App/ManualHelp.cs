using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ModbusTcpTroubleshooter.App;

public static class ManualHelp
{
    private static ManualWindow? _window;

    public static void Open(Window source, string? topicId = null)
    {
        if (_window is null || !_window.IsLoaded)
        {
            _window = new ManualWindow(topicId) { Owner = source, ShowInTaskbar = false };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            _window.ShowTopic(topicId);
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
    }

    public static string ContextFor(Window window, DependencyObject? focused)
    {
        if (window is ConnectionSettingsDialog dialog)
            return dialog.IsClientConfiguration ? "conexao-cliente" : "conexao-servidor";
        if (window is FullTestScopeDialog)
        {
            var field = FindFieldName(focused);
            return field switch
            {
                "ObservationTextBox" or "MonitoringTextBox" or "PacketRateTextBox" or "AttemptsTextBox" or "ReadIntervalTextBox" => "escopo-tempos",
                "TraceRoutesCheckBox" or "TraceHopsTextBox" or "TraceTimeoutTextBox" or "TraceTargetsTextBox" => "escopo-rotas",
                "ActiveSubnetScanCheckBox" or "AutomaticScopeCheckBox" or "CidrTextBox" or "TimeoutTextBox" or "ConcurrencyTextBox" or "ProbeRateTextBox" or "DiscoveryPortsTextBox" => "escopo-rede",
                _ => "escopo"
            };
        }
        if (window is MapDiscoverySettingsDialog) return "descoberta-mapa";
        if (window is FullTestTargetsDialog) return "alvos-teste";
        if (window is FullTestReviewDialog) return "teste-completo";
        if (window is DeviceProbeDialog) return "verificar-ip";
        if (window is DiscoveredTargetDialog) return "adicionar-descoberto";
        if (window is WriteRegisterDialog) return "escrita";
        if (window is not MainWindow main) return "inicio";
        if (main.MainTabs.SelectedItem == main.FullTestTab)
        {
            return main.FullTestSections.SelectedItem switch
            {
                var tab when tab == main.FullTestTopologyTab => "topologia",
                var tab when tab == main.FullTestReportTab => "relatorio-completo",
                var tab when tab == main.FullTestMapResultsTab => "mapa-descoberto",
                var tab when tab == main.FullTestDevicesTab => "inventario",
                var tab when tab == main.FullTestSummaryTab => "resultado-status",
                _ => "teste-completo"
            };
        }
        if (main.MainTabs.SelectedItem == main.CommunicationMapTab)
            return main.DeviceConfigurationTab.IsSelected ? "config-dispositivo" :
                main.AddressMapTab.IsSelected ? "mapa-visual" : "mapa-comunicacao";
        if (main.MainTabs.SelectedItem == main.ClientStationTab) return "estacao-cliente";
        if (main.MainTabs.SelectedItem == main.TimelineTab)
            return main.NetworkTimelineTabs.SelectedItem == main.NetworkDataTab ? "rede-dados" : "timeline";
        if (main.MainTabs.SelectedItem == main.IssueLogsTab) return "avisos";
        return "inicio";
    }

    private static string? FindFieldName(DependencyObject? focused)
    {
        for (var current = focused; current is not null;)
        {
            if (current is FrameworkElement element && !string.IsNullOrEmpty(element.Name) && !element.Name.StartsWith("PART_", StringComparison.Ordinal)) return element.Name;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : (current as FrameworkContentElement)?.Parent;
        }
        return null;
    }

    public static void HandleF1(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1 || e.Handled || e.OriginalSource is not DependencyObject origin) return;
        var window = Window.GetWindow(origin);
        if (window is null || window is ManualWindow) return;
        e.Handled = true;
        Open(window, ContextFor(window, origin));
    }
}
