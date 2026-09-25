using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private double _connectionsPaneWidth = 220;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        MainTabs.Items.Remove(TimelineTab);
        MainTabs.Items.Insert(2, TimelineTab);
        MainTabs.SelectedItem = FullTestTab;
        UiLocalization.Apply(this);
        SourceInitialized += (_, _) => ApplyCaptionColors();
    }

    private void ApplyCaptionColors()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var captionColor = 0x0056381E; // DWM COLORREF: navy blue.
        var textColor = 0x00FFFFFF;
        _ = DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 36, ref textColor, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.StopAllOperations();
        base.OnClosed(e);
    }

    private void ShowCommunicationMap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button) CommunicationMapMenuItem.IsChecked = true;
        ToggleViewTab(CommunicationMapTab, CommunicationMapMenuItem);
    }

    private void ShowTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button) TimelineMenuItem.IsChecked = true;
        ToggleViewTab(TimelineTab, TimelineMenuItem);
    }

    private void ShowIssueLogs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button) IssueLogsMenuItem.IsChecked = true;
        ToggleViewTab(IssueLogsTab, IssueLogsMenuItem);
    }

    private void ShowFullTest_Click(object sender, RoutedEventArgs e)
    {
        ToggleViewTab(FullTestTab, FullTestMenuItem);
    }

    private void ToggleConnectionsPane_Click(object sender, RoutedEventArgs e)
    {
        SetConnectionsPaneVisible(ConnectionsPaneMenuItem.IsChecked);
    }

    private void HideConnectionsPane_Click(object sender, RoutedEventArgs e)
    {
        ConnectionsPaneMenuItem.IsChecked = false;
        SetConnectionsPaneVisible(false);
    }

    private void SetConnectionsPaneVisible(bool visible)
    {
        if (!visible && ConnectionsColumn.ActualWidth > 0)
        {
            _connectionsPaneWidth = ConnectionsColumn.ActualWidth;
        }

        ConnectionsPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsColumn.Width = new GridLength(visible ? _connectionsPaneWidth : 0);
        ConnectionsSplitterColumn.Width = new GridLength(visible ? 5 : 0);
    }

    private void SelectClientMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.IsServerRunning && !viewModel.IsClientScanning && !viewModel.IsFullTestRunning)
        {
            viewModel.SelectedMode = "Client";
        }
    }

    private void SelectServerMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.IsServerRunning && !viewModel.IsClientScanning && !viewModel.IsFullTestRunning)
        {
            viewModel.SelectedMode = "Server";
        }
    }

    private void ConfigureActiveEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ShowConnectionSettings(viewModel.IsClientMode);
        }
    }

    private void StartFullTest_Click(object sender, RoutedEventArgs e)
    {
        FullTestMenuItem.IsChecked = true;
        FullTestTab.Visibility = Visibility.Visible;
        MainTabs.SelectedItem = FullTestTab;

        if (DataContext is MainViewModel viewModel && viewModel.StartFullTestCommand.CanExecute(null))
        {
            viewModel.StartFullTestCommand.Execute(null);
        }
    }

    private void ShowFullTestReport_Click(object sender, RoutedEventArgs e)
    {
        LegacyFullTestTab.Visibility = Visibility.Visible;
        MainTabs.SelectedItem = LegacyFullTestTab;
        FullTestSections.SelectedItem = FullTestReportTab;
    }

    private void ShowTestDetails_Click(object sender, RoutedEventArgs e)
    {
        LegacyFullTestTab.Visibility = Visibility.Visible;
        MainTabs.SelectedItem = LegacyFullTestTab;
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsFullTestRunning) return;
        if (viewModel.IsServerRunning || viewModel.IsClientScanning)
        {
            MessageBox.Show(this, "Desconecte o cliente ou pare o servidor antes de iniciar uma nova sessão.", "Nova sessão", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, "Limpar dados da sessão atual e iniciar um novo diagnóstico?", "Nova sessão", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        viewModel.ResetDiagnosticSession();
        MainTabs.SelectedItem = FullTestTab;
    }

    private void OpenDiscoveredHost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkDiscoveryRow host }) return;

        FullTestMenuItem.IsChecked = true;
        LegacyFullTestTab.Visibility = Visibility.Visible;
        MainTabs.SelectedItem = LegacyFullTestTab;
        FullTestSections.SelectedItem = FullTestDevicesTab;
        DiscoveredDevicesGrid.SelectedItem = host;
        DiscoveredDevicesGrid.ScrollIntoView(host);
    }

    private void ConfigureFullTestScope_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new FullTestScopeDialog(
            viewModel.EnableActiveSubnetScan,
            viewModel.ActiveScanTimeoutMs,
            viewModel.ActiveScanConcurrency,
            viewModel.PassiveObservationSeconds)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        viewModel.EnableActiveSubnetScan = dialog.EnableActiveSubnetScan;
        viewModel.ActiveScanTimeoutMs = dialog.ActiveScanTimeoutMs;
        viewModel.ActiveScanConcurrency = dialog.ActiveScanConcurrency;
        viewModel.PassiveObservationSeconds = dialog.PassiveObservationSeconds;
    }

    private void ConfigureMapDiscovery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new MapDiscoverySettingsDialog(
            viewModel.EnableMapDiscovery,
            viewModel.EnableMapDiscoveryUnitSweep,
            viewModel.MapDiscoveryUnitIdStart,
            viewModel.MapDiscoveryUnitIdEnd,
            viewModel.MapDiscoveryMaxAddress,
            viewModel.MapDiscoveryBlockSize,
            viewModel.MapDiscoveryFc01,
            viewModel.MapDiscoveryFc02,
            viewModel.MapDiscoveryFc03,
            viewModel.MapDiscoveryFc04,
            viewModel.EnableMapDiscoveryPointFallback)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        viewModel.EnableMapDiscovery = dialog.EnableDiscovery;
        viewModel.EnableMapDiscoveryUnitSweep = dialog.EnableUnitSweep;
        viewModel.MapDiscoveryUnitIdStart = dialog.UnitIdStart;
        viewModel.MapDiscoveryUnitIdEnd = dialog.UnitIdEnd;
        viewModel.MapDiscoveryMaxAddress = dialog.MaxAddress;
        viewModel.MapDiscoveryBlockSize = dialog.BlockSize;
        viewModel.MapDiscoveryFc01 = dialog.Fc01;
        viewModel.MapDiscoveryFc02 = dialog.Fc02;
        viewModel.MapDiscoveryFc03 = dialog.Fc03;
        viewModel.MapDiscoveryFc04 = dialog.Fc04;
        viewModel.EnableMapDiscoveryPointFallback = dialog.PointFallback;
    }

    private void FullTestHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "O teste completo executa uma coleta tecnica guiada. Ele inicia o modo cliente ou servidor selecionado quando necessario, captura trafego, verifica rotas IP e ARP, mede a carga observada e valida a comunicacao Modbus configurada.\n\nEscopo do teste configura a sondagem TCP ativa e o tempo de observacao passiva. Habilite a varredura ativa de sub-rede somente quando houver autorizacao.\n\nA descoberta de mapa e somente para leitura. No modo cliente, sonda faixas FC01-FC04. No modo servidor, registra as faixas requisitadas pelos clientes conectados. Nenhuma funcao Modbus de escrita e enviada.",
            "Teste completo - Ajuda",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ToggleViewTab(TabItem tab, MenuItem menuItem)
    {
        if (menuItem.IsChecked)
        {
            tab.Visibility = Visibility.Visible;
            MainTabs.SelectedItem = tab;
            return;
        }

        tab.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(MainTabs.SelectedItem, tab))
        {
            MainTabs.SelectedItem = CommunicationMapTab;
        }
    }

    private void ConfigureClient_Click(object sender, RoutedEventArgs e)
    {
        ShowConnectionSettings(isClient: true);
    }

    private void ConfigureServer_Click(object sender, RoutedEventArgs e)
    {
        ShowConnectionSettings(isClient: false);
    }

    private async void ShowConnectionSettings(bool isClient)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new ConnectionSettingsDialog(
            isClient,
            isClient ? viewModel.TargetIp : viewModel.LocalIp,
            viewModel.Port,
            viewModel.UnitId,
            viewModel.ScanRateMs,
            viewModel.KeepClientConnectionOpen)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        viewModel.SelectedMode = isClient ? "Client" : "Server";
        if (isClient)
        {
            viewModel.TargetIp = dialog.Address;
            viewModel.ScanRateMs = dialog.ScanRateMs;
        }
        else
        {
            viewModel.LocalIp = dialog.Address;
        }

        viewModel.Port = dialog.Port;
        viewModel.UnitId = dialog.UnitId;
        if (isClient)
        {
            await viewModel.SetClientConnectionModeAsync(dialog.KeepConnectionOpen);
        }
    }


    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Modbus TCP Troubleshooter\nFerramenta de troubleshooting para Modbus TCP client/server, timeline de rede e teste completo.",
            "Sobre",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TcpTimelineGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: TcpTimelineRow row })
        {
            return;
        }

        MessageBox.Show(this, row.Details, $"Detalhes do pacote #{row.Number}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ClientMapGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ClientMapRow row })
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        if (row.FunctionCode is not (ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadCoils))
        {
            MessageBox.Show(
                this,
                "Escrita pela tabela esta disponivel apenas para FC03 Holding Registers e FC01 Coils.",
                "Escrita indisponivel para este FC",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var initialValue = TryGetFirstRegisterValue(row.LastValue, out var parsedValue) ? parsedValue : (ushort)0;
        var endAddress = (ushort)(row.StartAddress + Math.Max(1, (int)row.Quantity) - 1);
        var result = ShowWriteDialog(viewModel, row.Name, row.StartAddress, endAddress, initialValue, row.FunctionCode);
        if (result is null)
        {
            return;
        }

        await viewModel.WriteRangeFromMapAsync(row, result.Value.Address, result.Value.EndAddress, result.Value.Value);
    }

    private async void ClientCommunicationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: ClientCommunicationPointRow point })
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is null)
        {
            return;
        }

        if (!point.Writable || point.FunctionCode is not (ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadCoils))
        {
            MessageBox.Show(
                this,
                "Escrita pela tabela esta disponivel apenas para Holding Registers e Coils.",
                "Escrita indisponivel para este ponto",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var result = ShowWriteDialog(viewModel, point.SourceLine, point.Address, point.Address, point.Value, point.FunctionCode);
        if (result is null)
        {
            return;
        }

        await viewModel.WritePointFromCommunicationPointAsync(point, result.Value.Value);
    }

    private (ushort Address, ushort EndAddress, ushort Value)? ShowWriteDialog(MainViewModel viewModel, string sourceName, ushort address, ushort endAddress, ushort value, byte functionCode)
    {
        var pointLabel = functionCode == ModbusProtocol.ReadCoils ? "COIL" : "HR";
        var writeCode = functionCode == ModbusProtocol.ReadCoils ? "FC05" : "FC06";
        var context = $"{viewModel.TargetIp}:{viewModel.Port} | UID {viewModel.UnitId} | {sourceName} | {pointLabel} {address}-{endAddress}";
        var dialog = new WriteRegisterDialog(
            context,
            address,
            endAddress,
            functionCode == ModbusProtocol.ReadCoils && value != 0 ? (ushort)1 : value,
            $"Escrita {writeCode} pela tabela do mapa",
            functionCode == ModbusProtocol.ReadCoils ? "Novo valor (0=OFF, diferente de 0=ON)" : "Novo valor")
        {
            Owner = this
        };

        return dialog.ShowDialog() == true
            ? (dialog.Address, dialog.EndAddress, dialog.Value)
            : null;
    }

    private static bool TryGetFirstRegisterValue(string valueText, out ushort value)
    {
        var first = valueText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return ushort.TryParse(first, out value);
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
            {
                return typed;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

}
