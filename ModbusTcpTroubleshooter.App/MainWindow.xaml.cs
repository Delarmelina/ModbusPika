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

    private double _connectionsPaneWidth = 260;
    private readonly System.Windows.Threading.DispatcherTimer _experienceTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private object? _lastWorkspaceTab;
    private bool _restoringWorkspace;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        ((MainViewModel)DataContext).ConfirmPendingDeviceEdits = DeviceSettings.ConfirmLeave;
        MainTabs.Items.Remove(TimelineTab);
        MainTabs.Items.Insert(MainTabs.Items.IndexOf(CommunicationMapTab) + 1, TimelineTab);
        AddGlobalScopeBanner(TimelineTab, "Linha do tempo: tráfego de todas as conexões e da interface de captura selecionada.");
        AddGlobalScopeBanner(IssueLogsTab, "Avisos: ocorrências consolidadas de todas as conexões e testes desta sessão.");
        MainTabs.SelectedItem = FullTestTab;
        UiLocalization.Apply(this);
        SourceInitialized += (_, _) => ApplyCaptionColors();
        _lastWorkspaceTab = MainTabs.SelectedItem;
        MainTabs.SelectionChanged += WorkspaceSelectionChanged;
        _experienceTimer.Tick += (_, _) => { if (DataContext is MainViewModel vm) vm.RefreshTestExperience(); };
        _experienceTimer.Start();
        Closing += (_, e) => { if (!DeviceSettings.ConfirmLeave()) e.Cancel = true; };
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

    private void ManualMenuItem_Click(object sender, RoutedEventArgs e) =>
        ManualHelp.Open(this, ManualHelp.ContextFor(this, Keyboard.FocusedElement as DependencyObject));

    protected override void OnClosed(EventArgs e)
    {
        _experienceTimer.Stop();
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

    private void FocusTestExecution_Click(object sender, RoutedEventArgs e) =>
        FullTestSections.SelectedItem = FullTestExecutionTab;

    private async void ReviewAndStartTest_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.StartFullTestCommand.CanExecute(null)) return;
        string review;
        try { review = vm.BuildTestReview(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Revisar configuração", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var dialog = new FullTestReviewDialog(review) { Owner = this };
        if (dialog.ShowDialog() != true || !vm.StartFullTestCommand.CanExecute(null)) return;
        FullTestSections.SelectedItem = FullTestExecutionTab;
        await vm.StartFullTestCommand.ExecuteAsync(null);
        vm.RefreshTestExperience();
        if (!vm.IsFullTestRunning) FullTestSections.SelectedItem = FullTestSummaryTab;
    }

    private void OpenFinding_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is FrameworkElement { DataContext: FullTestStep step })
        {
            vm.SelectedFullTestStep = step;
            FullTestSections.SelectedItem = FullTestExecutionTab;
        }
    }

    private void WorkspaceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringWorkspace || e.Source != MainTabs) return;
        if (_lastWorkspaceTab == CommunicationMapTab && MainTabs.SelectedItem != CommunicationMapTab && !DeviceSettings.ConfirmLeave())
        {
            _restoringWorkspace = true;
            MainTabs.SelectedItem = _lastWorkspaceTab;
            _restoringWorkspace = false;
            return;
        }
        _lastWorkspaceTab = MainTabs.SelectedItem;
    }

    private void SelectTestClient_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsFullTestRunning) vm.TestMode = "Client";
    }

    private void SelectTestServer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsFullTestRunning) vm.TestMode = "Server";
    }

    private void SelectTestTargets_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsFullTestRunning) return;
        var dialog = new FullTestTargetsDialog(vm.ClientSessions) { Owner = this };
        if (dialog.ShowDialog() == true) vm.SetFullTestTargets(dialog.SelectedTargets);
    }

    private void VerifyDiscoveredDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsFullTestRunning || vm.IsDeviceProbeRunning) return;
        var address = (sender as FrameworkElement)?.DataContext is NetworkDiscoveryRow host ? host.Ip : "";
        new DeviceProbeDialog(vm, address) { Owner = this }.ShowDialog();
    }

    private void AddDiscoveredTarget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { DataContext: NetworkDiscoveryRow host }) return;
        if (!DeviceSettings.ConfirmLeave()) return;
        if (vm.IsFullTestRunning || vm.IsDeviceProbeRunning)
        {
            MessageBox.Show(this, "Aguarde a verificacao atual terminar antes de adicionar um alvo.",
                "Adicionar alvo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new DiscoveredTargetDialog(vm, host) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.AddedTarget is null) return;
        MainTabs.SelectedItem = CommunicationMapTab;
        OpenDeviceConfiguration();
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
        if (DataContext is MainViewModel viewModel && !viewModel.IsFullTestRunning)
        {
            viewModel.SelectedMode = "Client";
        }
    }

    private void AddGlobalScopeBanner(TabItem tab, string description)
    {
        if (tab.Content is not UIElement content) return;
        tab.Content = null;
        var panel = new DockPanel();
        var banner = new Border { Background = new SolidColorBrush(Color.FromRgb(242, 246, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(197, 208, 215)),
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 8, 12, 8) };
        banner.Child = new TextBlock { Text = "ESCOPO GLOBAL  ·  " + description, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(banner, Dock.Top);
        panel.Children.Add(banner);
        panel.Children.Add(content);
        tab.Content = panel;
        tab.ToolTip = description;
    }

    private void ShowClientStation_Click(object sender, RoutedEventArgs e)
    {
        ClientStationMenuItem.IsChecked = true;
        if (DataContext is MainViewModel vm) vm.SelectedMode = "Client";
        MainTabs.SelectedItem = ClientStationTab;
    }

    private void ShowClientStationView_Click(object sender, RoutedEventArgs e)
    {
        if (ClientStationMenuItem.IsChecked) ShowClientStation_Click(sender, e);
        else if (ReferenceEquals(MainTabs.SelectedItem, ClientStationTab)) MainTabs.SelectedItem = FullTestTab;
    }

    private void OpenSelectedTarget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.IsFullTestRunning || !vm.HasClientTargets) return;
        vm.SelectedMode = "Client";
        OpenDeviceConfiguration();
    }

    private async void RemoveClientTarget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.RemoveClientSessionCommand.CanExecute(null)) return;
        if (!DeviceSettings.ConfirmLeave()) return;
        await vm.RemoveClientSessionCommand.ExecuteAsync(null);
        if (vm.HasClientTargets || vm.SelectedMode != "Client") return;
        ClientStationMenuItem.IsChecked = true;
        MainTabs.SelectedItem = ClientStationTab;
    }

    private void StationTargetsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not null)
            OpenSelectedTarget_Click(sender, e);
    }

    private void OpenDeviceConfiguration()
    {
        if (CommunicationMapTab is null || DeviceConfigurationTab is null) return;
        CommunicationMapTab.Visibility = Visibility.Visible;
        CommunicationMapMenuItem.IsChecked = true;
        MainTabs.SelectedItem = CommunicationMapTab;
        MapPresentationTabs.SelectedItem = DeviceConfigurationTab;
        DeviceSettings.Reload();
    }

    private void ClientTargetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection can also be synchronized from the station table or an imported file.
        // Only direct sidebar interaction should navigate away from the current workspace.
        if (ClientTargetList.IsKeyboardFocusWithin && DataContext is MainViewModel { HasClientTargets: true }) OpenDeviceConfiguration();
        else DeviceSettings?.Reload();
    }

    private void ClientTargetList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.IsFullTestRunning
            && FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not null)
        {
            viewModel.SelectedMode = "Client";
            OpenDeviceConfiguration();
        }
    }

    private async void MapPoint_Activated(object? sender, MapPointEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (e.Point is ClientCommunicationPointRow client)
        {
            if (!client.Writable)
            {
                MessageBox.Show(this, "Esta área de dados é somente leitura no protocolo Modbus.", "Ponto somente leitura", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var result = ShowWriteDialog(viewModel, client.SourceLine, client.Address, client.Address, client.Value, client.FunctionCode);
            if (result is not null) await viewModel.WritePointFromCommunicationPointAsync(client, result.Value.Value);
        }
        else if (e.Point is ServerPointRow server)
        {
            var bit = server.Type is ModbusPointType.Coil or ModbusPointType.DiscreteInput;
            var dialog = new WriteRegisterDialog($"Memória local · {viewModel.ServerEndpoint} · {server.TypeLabel} {server.Address}", server.Address, server.Address, server.Value, "Alterar valor do servidor simulado", bit ? "Novo valor (0=OFF, 1=ON)" : "Novo valor") { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (dialog.Address != server.Address || dialog.EndAddress != server.Address)
            {
                MessageBox.Show(this, "A edição deste bloco visual altera apenas o endereço selecionado.", "Endereço inválido", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            server.Value = bit && dialog.Value != 0 ? (ushort)1 : dialog.Value;
        }
    }

    private void SelectServerMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.IsFullTestRunning)
        {
            viewModel.SelectedMode = "Server";
            OpenDeviceConfiguration();
        }
    }

    private void ConfigureActiveEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ShowConnectionSettings(viewModel.IsClientMode);
        }
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsFullTestRunning) return;
        if (!DeviceSettings.ConfirmLeave()) return;
        if (viewModel.IsServerRunning || viewModel.AnyClientRunning)
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
        ShowObservedDeviceDetails(host);
    }

    private void DiscoveredDevicesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: NetworkDiscoveryRow host }
            && FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not null)
            ShowObservedDeviceDetails(host);
    }

    private void DiscoveredDevicesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        grid.ContextMenu.IsEnabled = row is not null;
        if (row is not null) grid.SelectedItem = row.Item;
    }

    private void ShowObservedDeviceDetails(NetworkDiscoveryRow host)
    {
        var details = $"IP: {host.Ip}\nMAC: {host.Mac}\nFonte: {host.Source}\nPapel / evidencia: {host.RoleGuess}\n"
            + $"TCP aberto: {host.OpenTcpPorts}\nModbus confirmado: {host.ConfirmedModbusPorts}\n"
            + $"Modbus observado: {host.ObservedModbusPorts}\nPortas sondadas: {host.ProbedPorts}\n"
            + $"Ultima sondagem: {host.LastProbeAt}\n\n{host.ProbeSummary}\n\n{host.Notes}";
        var dialog = new Window { Owner = this, Title = $"Dispositivo observado - {host.Ip}",
            Width = 570, Height = 420, MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(14) };
        var close = new Button { Content = "Fechar", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 85 };
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Bottom);
        panel.Children.Add(close);
        panel.Children.Add(new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void ConfigureFullTestScope_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsFullTestRunning)
        {
            return;
        }

        var dialog = new FullTestScopeDialog(
            viewModel.EnableActiveSubnetScan,
            viewModel.ActiveScanTimeoutMs,
            viewModel.ActiveScanConcurrency,
            viewModel.PassiveObservationSeconds,
            viewModel.TcpMonitoringSeconds,
            viewModel.ReadValidationAttempts,
            viewModel.ReadValidationIntervalMs,
            viewModel.ActiveScanCidr,
            viewModel.ProbeRatePerSecond,
            viewModel.PacketRateWarningThreshold,
            viewModel.EnableRouteTracing, viewModel.RouteTraceMaxHops, viewModel.RouteTraceTimeoutMs, viewModel.RouteTraceMaxTargets,
            viewModel.ModbusDiscoveryPorts, viewModel.AutomaticNetworkScope)
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
        viewModel.TcpMonitoringSeconds = dialog.TcpMonitoringSeconds;
        viewModel.ReadValidationAttempts = dialog.ReadValidationAttempts;
        viewModel.ReadValidationIntervalMs = dialog.ReadValidationIntervalMs;
        viewModel.ActiveScanCidr = dialog.ActiveScanCidr;
        viewModel.ProbeRatePerSecond = dialog.ProbeRatePerSecond;
        viewModel.PacketRateWarningThreshold = dialog.PacketRateWarningThreshold;
        viewModel.EnableRouteTracing = dialog.EnableRouteTracing;
        viewModel.RouteTraceMaxHops = dialog.RouteTraceMaxHops;
        viewModel.RouteTraceTimeoutMs = dialog.RouteTraceTimeoutMs;
        viewModel.RouteTraceMaxTargets = dialog.RouteTraceMaxTargets;
        viewModel.ModbusDiscoveryPorts = dialog.ModbusDiscoveryPorts;
        viewModel.AutomaticNetworkScope = dialog.AutomaticNetworkScope;
        viewModel.SaveTestScopeSettings();
    }

    private void ConfigureMapDiscovery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsFullTestRunning)
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
            "O teste completo possui papel e alvos independentes da selecao lateral. Escolha Cliente / Mestre ou Servidor / Escravo na aba Execucao. Como cliente, selecione quais servidores cadastrados participarao. Como servidor, o teste utiliza o simulador local. IP, porta e mapas sao configurados na area Dispositivo.\n\nConfigure os procedimentos para ajustar as janelas TCP, tentativas por bloco, sondagem da sub-rede e rotas ICMP. A descoberta ativa exige autorizacao; nenhuma escrita Modbus e enviada. No modo cliente, a descoberta de mapa sonda faixas FC01-FC04; como servidor, observa os enderecos solicitados pelos clientes.\n\nExecucao, resumo, dispositivos, topologia, mapa descoberto e relatorio ficam na mesma aba Teste completo. A exportacao MD / PDF esta na subaba Relatorio. Alterar a configuracao da proxima execucao nao apaga os resultados anteriores.",
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

    private void AddClientTarget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.IsFullTestRunning) return;
        if (!DeviceSettings.ConfirmLeave()) return;
        var dialog = new ConnectionSettingsDialog(true, "127.0.0.1", 502, 1, 1000, true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        viewModel.AddClientSession(dialog.Address, dialog.Port, dialog.UnitId, dialog.ScanRateMs, dialog.KeepConnectionOpen);
        MainTabs.SelectedItem = CommunicationMapTab;
        OpenDeviceConfiguration();
    }

    private async void ShowConnectionSettings(bool isClient)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsDeviceProbeRunning || !(isClient ? viewModel.CanConfigureClient : viewModel.CanConfigureServer)) return;
        if (!DeviceSettings.ConfirmLeave()) return;
        var session = viewModel.SelectedClientSession;

        var dialog = new ConnectionSettingsDialog(
            isClient,
            isClient ? viewModel.TargetIp : viewModel.LocalIp,
            isClient ? viewModel.SelectedClientSession.Port : viewModel.ServerPort,
            isClient ? viewModel.SelectedClientSession.UnitId : viewModel.ServerUnitId,
            viewModel.ScanRateMs,
            viewModel.KeepClientConnectionOpen)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await viewModel.ConfigureDeviceAsync(isClient, session, isClient ? session.Name : viewModel.ServerName,
                dialog.Address, dialog.Port, dialog.UnitId, dialog.ScanRateMs, dialog.KeepConnectionOpen);
            viewModel.SelectedMode = isClient ? "Client" : "Server";
            DeviceSettings.Reload();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Configuração do dispositivo", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }


    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Modbus Diagnostic Tool - Autvix\nRevisão 1.1 | Versão 1.1.0\n\n"
                + "Diagnóstico de comunicação Modbus TCP, captura de rede e teste completo.\n\n"
                + "Felipe Ferreira Delarmelina\n"
                + "Contato para dúvidas: Felipe.Ferreira@autvix.com.br",
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
