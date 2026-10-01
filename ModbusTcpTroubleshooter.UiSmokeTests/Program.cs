using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Threading;
using ModbusTcpTroubleshooter.Core;
using ModbusTcpTroubleshooter.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        if (args.Contains("--manual-images"))
        {
            CaptureManualDialog(new ConnectionSettingsDialog(true, "172.27.30.84", 1501, 1, 1000), "ManualClient");
            CaptureManualDialog(new MapDiscoverySettingsDialog(true, false, 1, 10, 100, 10, true, true, true, true, true), "ManualMapDiscovery");
            app.Shutdown();
            return;
        }
        var startupReport = App.BuildStartupErrorReport(new InvalidOperationException("Erro externo", new IOException("Causa interna")));
        Require(startupReport.Contains("Causa interna") && startupReport.Contains("Arquitetura:") && startupReport.Contains(".NET:"), "Startup diagnosis includes root cause and runtime environment");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var originalLanguage = UiLocalization.CurrentLanguage;
        try
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();
            CheckManual(window);
            var vm = (MainViewModel)window.DataContext;
            CheckLocalDeviceIdentity();
            CheckFocusedTopologyAndNumericIpSort(window);
            CheckContextualStationTab(window, vm);
            CheckConfigurationRoundTrip();
            CheckDeviceConfiguration(window, vm);
            CheckDiagnosticExperience(window, vm);
            CheckIndependentTestWorkspace(window, vm);
            CheckServerTestRuntimeIndependence();
            CheckEmptyClientStation();
            CheckDiscoveredTargetWorkflow();
            var devicesGroup = (Expander)window.FindName("DiscoveredDevicesGroup");
            devicesGroup.IsExpanded = false;
            window.UpdateLayout();
            Require(!((Expander)window.FindName("DiscoveredModbusGroup")).IsVisible, "Collapsing discovered devices hides all subgroups");
            devicesGroup.IsExpanded = true;
            var devicesMenu = (MenuItem)window.FindName("DiscoveredDevicesMenuItem");
            devicesMenu.IsChecked = false;
            window.UpdateLayout();
            Require(devicesGroup.Visibility == Visibility.Collapsed, "View menu hides the whole discovered devices section");
            devicesMenu.IsChecked = true;
            CheckAnalysisWindows(vm);
            CheckFullTestWarningScope(vm);
            var toolbar = (StackPanel)window.FindName("MainToolbar");
            var shortcuts = Descendants(toolbar).OfType<Button>().ToArray();
            Require(shortcuts.Length == 8, "Toolbar has only New, Save and the six client/server actions");
            Require(ReferenceEquals(shortcuts[1].Command, vm.SaveConfigurationCommand), "Save toolbar persists the reusable configuration");
            Require(ReferenceEquals(shortcuts[4].Command, vm.DisconnectClientCommand), "Client disconnect uses its own command");
            Require(ReferenceEquals(shortcuts[7].Command, vm.StopServerCommand), "Server disconnect stops only the server");
            var plannedStepCount = vm.FullTestSteps.Count;
            Require(plannedStepCount >= 13 && vm.FullTestTotalSteps == plannedStepCount, "Full Test preview is populated before execution");
            vm.EnableMapDiscovery = true;
            Require(vm.FullTestSteps.Count == plannedStepCount + 1, "Map discovery appears in the preview");
            vm.EnableMapDiscovery = false;
            Require(vm.FullTestSteps.Count == plannedStepCount, "Map discovery can be removed from the preview");
            vm.SelectedMode = "Server";
            vm.LocalIp = "127.0.0.2";
            Require(vm.ActiveEndpoint == "127.0.0.2:1502", "Server endpoint");
            vm.IsServerRunning = true;
            Require(vm.StartClientScanCommand.CanExecute(null), "Client is available while server is running");
            Require(!vm.CanConfigureServer && vm.CanConfigureClient, "Client/server configuration locks are independent");
            vm.IsServerRunning = false;
            vm.IsFullTestRunning = true;
            Require(!vm.CanConfigureFullTest && vm.CancelFullTestCommand.CanExecute(null), "Full Test controls");
            vm.IsFullTestRunning = false;
            var step = new FullTestStep(1, "Cancellation", "Test operator cancellation");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Func<CancellationToken, Task<FullTestStepResult>> action = token => Task.FromCanceled<FullTestStepResult>(token);
            var execute = typeof(MainViewModel).GetMethod("ExecuteFullTestStepAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var task = (Task)execute.Invoke(vm, new object[] { step, action, cancelled.Token })!;
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            Require(step.Status == "Cancelado", "Cancellation must not become a communication failure");

            var label = new TextBlock();
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.Status)) { Source = vm });
            UiLocalization.Apply(label);
            Require(BindingOperations.IsDataBound(label, TextBlock.TextProperty), "Translation preserves binding");
            var menu = new MenuItem { Header = "_Help" };
            UiLocalization.CurrentLanguage = UiLanguage.Portuguese;
            UiLocalization.Apply(menu);
            Require((string)menu.Header == "_Ajuda", "Portuguese menu");
            UiLocalization.CurrentLanguage = UiLanguage.English;
            UiLocalization.Apply(menu);
            Require((string)menu.Header == "_Ajuda", "Portuguese-only UI remains Portuguese");
            UiLocalization.Apply(window);
            var fullTest = (TabItem)window.FindName("FullTestTab");
            fullTest.Visibility = Visibility.Visible;
            ((TabControl)window.FindName("MainTabs")).SelectedItem = fullTest;
            var testSections = (TabControl)window.FindName("FullTestSections");
            Require(ReferenceEquals(((TabControl)window.FindName("MainTabs")).SelectedItem, fullTest), "Full Test opens in the main tab strip");
            vm.SelectedFullTestStep = vm.FullTestSteps[0];
            vm.FullTestProgressLabel = "Aguardando início";
            window.UpdateLayout();
            SaveFrame(window, "FullTest");
            vm.FullTestReport = "# Relatório de teste";
            testSections.SelectedItem = window.FindName("FullTestReportTab");
            Require(ReferenceEquals(((TabControl)window.FindName("MainTabs")).SelectedItem, fullTest), "Report remains inside Full Test");
            vm.TopologyLinks.Add(new("192.168.1.10", "192.168.1.50", "Captura Modbus/TCP", 120, "Consulta FC03 observada na captura"));
            vm.TopologyLinks.Add(new("Computador local", "192.168.1.1", "Rota ICMP", 1, "Alvo 192.168.2.50; TTL 1; resposta do roteador"));
            vm.TopologyLinks.Add(new("Interface de captura", "SW01", "LLDP anunciado", 1, "Porta Gi1/1; identidade anunciada, nao saude do switch"));
            var demoDevice = new NetworkDiscoveryRow { Ip = "10.0.0.3", IsModbusConfirmed = true, ConfirmedModbusPorts = "1501", RoleGuess = "Servidor Modbus confirmado" };
            vm.NetworkDiscoveryRows.Add(demoDevice);
            testSections.SelectedItem = window.FindName("FullTestTopologyTab");
            Require(testSections.Items.Contains(window.FindName("FullTestTopologyTab"))
                && !((TabControl)window.FindName("MainTabs")).Items.Contains(window.FindName("FullTestTopologyTab")), "Topology is a Full Test result tab, not a separate workspace");
            window.UpdateLayout();
            SaveFrame(window, "Topology");
            vm.NetworkDiscoveryRows.Remove(demoDevice);
            vm.TopologyLinks.Clear();
            testSections.SelectedIndex = 0;
            var paneMenu = (MenuItem)window.FindName("ConnectionsPaneMenuItem");
            paneMenu.IsChecked = false;
            paneMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, paneMenu));
            Require(((FrameworkElement)window.FindName("ConnectionsPane")).Visibility == Visibility.Collapsed, "Connections pane can be hidden");
            paneMenu.IsChecked = true;
            paneMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, paneMenu));
            Require(((FrameworkElement)window.FindName("ConnectionsPane")).Visibility == Visibility.Visible, "Connections pane can be restored");
            vm.IsFullTestRunning = true;
            vm.FullTestSteps[0].Status = "OK";
            var updateSummary = typeof(MainViewModel).GetMethod("UpdateFullTestSummaryCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            updateSummary.Invoke(vm, null);
            Require(vm.FullTestCompletedSteps == 1 && vm.FullTestProgressPercent > 0 && vm.FullTestOverallStatus == "Executando", "Progress reflects completed steps while running");
            vm.IsFullTestRunning = false;
            vm.FullTestSteps[1].Status = "Atencao";
            updateSummary.Invoke(vm, null);
            Require(vm.FullTestWarningCount == 1 && vm.FullTestOverallStatus == "Atencao", "Final summary reflects warnings");
            vm.ResetDiagnosticSession();
            Require(vm.FullTestOverallStatus == "Aguardando" && vm.FullTestCompletedSteps == 0 && vm.FullTestSteps.Count >= 13, "New diagnostic session resets the Full Test view");
            CheckConcurrentSessions(vm);
            vm.SelectedMode = "Client";
            ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("CommunicationMapTab");
            ((TabControl)window.FindName("MapPresentationTabs")).SelectedItem = window.FindName("AddressMapTab");
            window.UpdateLayout();
            SaveFrame(window, "AddressMap");
            var cells = Descendants(window).OfType<Button>().Where(x => x.DataContext is MapCellViewModel && x.IsVisible).ToList();
            Require(cells.Count > 0 && cells.Count <= 96, "Visual address map is populated and paginated");
            vm.SelectedClientSession.Rows[0].Quantity = 200;
            WaitFor(() => Descendants(window).OfType<Button>().Count(x => x.DataContext is MapCellViewModel && x.IsVisible) == 96, "Large address maps render only one page");
            Require(((Button)((MapAddressView)window.FindName("ClientAddressMap")).FindName("NextPage")).IsEnabled, "Large map has a next page");
            Require(vm.SelectedClientSession.Points.All(x => x.Quality == "Nao lido"), "Changing block configuration invalidates its old samples");
            vm.SelectedClientSession.Rows[0].Quantity = 10;
            using (var cell = new MapCellViewModel(new ClientCommunicationPointRow { Quality = "Nao lido", Type = "Holding Register" })) Require(cell.ValueText == "-", "Unread map cells do not display a fake valid zero");
            window.Width = 980;
            window.Height = 720;
            ((TabControl)window.FindName("MainTabs")).SelectedItem = fullTest;
            window.UpdateLayout();
            SaveFrame(window, "CompactFullTest");
            window.Close();

            CheckDialog(new ConnectionSettingsDialog(true, "127.0.0.1", 1502, 1, 1000));
            CheckDialog(new ConnectionSettingsDialog(false, "0.0.0.0", 1502, 1, 1000));
            CheckDialog(new FullTestScopeDialog(false, 250, 2, 12));
            CheckDialog(new MapDiscoverySettingsDialog(false, false, 1, 10, 120, 10, true, true, true, true, true));
            CheckDialog(new WriteRegisterDialog("127.0.0.1:1502 | HR 0-10", 0, 10, 1, "Write", "Value"));
            CheckDialog(new FullTestTargetsDialog([new ClientConnectionSession()]));
            CheckDialog(new DiscoveredTargetDialog(vm, new NetworkDiscoveryRow { Ip = "127.0.0.1", IsModbusClientObserved = true }));
            CheckDialog(new ManualWindow("topologia"));
            Console.WriteLine("UI smoke tests OK: independent test role/targets, nested results/topology/report, server auto-start without selection changes, two concurrent clients + local server, FFD persistence and six dialogs.");
        }
        finally
        {
            UiLocalization.CurrentLanguage = originalLanguage;
            app.Shutdown();
        }
    }

    private static void CheckManual(MainWindow window)
    {
        var catalog = ManualCatalog.Load();
        Require(catalog.Sections.Count >= 7 && catalog.Topics.Count >= 35, "Manual includes structured technical sections and subtopics");
        Require(catalog.Search("conexao").Any(x => x.Id == "conexao-cliente"), "Manual search ignores accents");
        Require(catalog.Search("exceção 02").Any(x => x.Id == "erro-modbus"), "Manual search includes body and error codes");
        Require(catalog.Get("missing").Id == "inicio", "Unknown help topics fall back to the manual introduction");
        var helpMenu = window.FindName("ManualHelpMenuItem") as MenuItem;
        Require(helpMenu is not null && helpMenu.InputGestureText == "F1", "Help menu exposes the manual and F1 shortcut");
        Require(ManualHelp.ContextFor(window, null) == "teste-completo", "F1 opens the selected Full Test section");
        var settings = new ConnectionSettingsDialog(true, "127.0.0.1", 1502, 1, 1000);
        Require(ManualHelp.ContextFor(settings, null) == "conexao-cliente", "F1 identifies the client settings dialog");
        var viewer = new ManualWindow("topologia");
        Require(viewer.CurrentTopic.Id == "topologia", "Manual opens at the requested contextual topic");
        ((TextBox)viewer.FindName("SearchBox")).Text = "excecao 02";
        Require(((ListBox)viewer.FindName("SearchResults")).Items.Count > 0, "Manual search updates visible results");
        ((TextBox)viewer.FindName("SearchBox")).Clear();
        viewer.Show();
        foreach (var topic in catalog.Topics) viewer.ShowTopic(topic.Id);
        viewer.ShowTopic("conexao-cliente");
        viewer.UpdateLayout();
        SaveFrame(viewer, "ManualReference");
        viewer.Width = 780;
        viewer.Height = 560;
        viewer.UpdateLayout();
        SaveFrame(viewer, "ManualCompact");
        viewer.Close();
    }

    private static void CaptureManualDialog(Window dialog, string name)
    {
        if (dialog is MapDiscoverySettingsDialog) dialog.Height = 650;
        dialog.Show();
        dialog.UpdateLayout();
        SaveFrame(dialog, name);
        dialog.Close();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void CheckLocalDeviceIdentity()
    {
        var localIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "172.27.30.73", "172.27.30.186" };
        var rows = new[]
        {
            new NetworkDiscoveryRow { Ip = "127.0.0.1", ModbusPorts = "1502", ConfirmedModbusPorts = "1502", IsModbusConfirmed = true },
            new NetworkDiscoveryRow { Ip = "172.27.30.73", ModbusPorts = "1502", ConfirmedModbusPorts = "1502", IsModbusConfirmed = true },
            new NetworkDiscoveryRow { Ip = "172.27.30.186", ModbusPorts = "1501", ConfirmedModbusPorts = "1501", IsModbusConfirmed = true },
            new NetworkDiscoveryRow { Ip = "172.27.30.84", ModbusPorts = "502", ConfirmedModbusPorts = "502", IsModbusConfirmed = true }
        };
        var groups = MainViewModel.GroupModbusDevices(rows, localIps);
        Require(groups.Count == 2 && groups[0].DeviceIdentity == "Este computador"
            && groups[0].AliasCaption.Contains("172.27.30.186")
            && groups[0].Notes.Contains("127.0.0.1") && groups[0].Notes.Contains("172.27.30.73")
            && groups[0].Notes.Contains("172.27.30.186") && groups[0].ConfirmedModbusPorts == "1501, 1502"
            && groups[1].Ip == "172.27.30.84", "Local loopback and NIC addresses share one device identity without merging remote hosts");
        Require(MainViewModel.DeviceIdentityFor("127.99.1.1", localIps) == "Este computador"
            && MainViewModel.DeviceIdentityFor("172.27.30.84", localIps) != "Este computador",
            "Only loopback and exact local NIC addresses are consolidated, never a whole subnet");
    }

    private static void CheckFocusedTopologyAndNumericIpSort(MainWindow window)
    {
        var grid = (DataGrid)window.FindName("DiscoveredDevicesGrid");
        Require(grid.Columns[0].SortMemberPath == nameof(NetworkDiscoveryRow.IpSortKey),
            "Discovered-device IP column sorts by numeric address");
        var server = new NetworkDiscoveryRow { Ip = "14.5.1.2", IsModbusConfirmed = true, ConfirmedModbusPorts = "502" };
        var client = new NetworkDiscoveryRow { Ip = "104.10.1.2", IsModbusClientObserved = true };
        var other = new NetworkDiscoveryRow { Ip = "8.8.8.8" };
        var local = new NetworkDiscoveryRow { Ip = "127.0.0.1", DeviceIdentity = "Este computador", IsModbusConfirmed = true };
        Require(server.IpSortKey < client.IpSortKey, "IPv4 sorting compares octets, not strings");
        var rows = new System.Collections.ObjectModel.ObservableCollection<NetworkDiscoveryRow> { client, server };
        var view = CollectionViewSource.GetDefaultView(rows);
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(NetworkDiscoveryRow.IpSortKey),
            System.ComponentModel.ListSortDirection.Ascending));
        Require(ReferenceEquals(view.Cast<NetworkDiscoveryRow>().First(), server), "Ascending IP view places 14 before 104");
        var links = new[] { new TopologyLink("127.0.0.1", "14.5.1.2", "Captura Modbus TCP", 32, "Read FC03") };
        var overview = ModbusTopologyOverview.Build([local, client, server, other], links, [], false);
        Require(overview.Peers.Count == 2 && overview.Peers[0].Ip == server.Ip && overview.Peers[1].Ip == client.Ip
            && overview.OtherHosts == 1 && !overview.InfrastructureAnnounced
            && overview.InfrastructureTitle.Contains("não identificado") && overview.Peers[0].PacketCount == 32,
            "Focused topology shows Modbus peers, hides generic hosts and does not invent a switch");
        var neighbor = new NeighborAdvertisement("LLDP", "001122334455", "SW-01", "Gi1/1", "SW-01",
            "Industrial Ethernet Switch", "14.5.1.1", 120, DateTimeOffset.Now);
        var announced = ModbusTopologyOverview.Build([server, other], links, [neighbor], true);
        Require(announced.InfrastructureAnnounced && announced.InfrastructureTitle == "Switch anunciado"
            && announced.OtherHosts == 1 && announced.LocalRole == "Servidor / Escravo",
            "Switch appears only with announcement evidence and unrelated hosts remain summarized");
    }

    private static void CheckContextualStationTab(MainWindow window, MainViewModel vm)
    {
        var station = (TabItem)window.FindName("ClientStationTab");
        var tabs = (TabControl)window.FindName("MainTabs");
        vm.SelectedMode = "Server";
        window.UpdateLayout();
        Require(station.Visibility == Visibility.Collapsed, "Station tab is hidden for the local server");
        vm.SelectedMode = "Client";
        window.UpdateLayout();
        Require(station.Visibility == Visibility.Visible, "Selecting a client target shows the station tab");
        Require(((StackPanel)station.Header).Children.OfType<Image>().Any(), "Station tab has a client icon");
        var device = (TabItem)window.FindName("CommunicationMapTab");
        Require(((StackPanel)device.Header).Children.OfType<TextBlock>().Any(x => x.Text == "Dispositivo"),
            "Device tab uses the short requested label");
        var menu = (MenuItem)window.FindName("ClientStationMenuItem");
        menu.IsChecked = false;
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menu));
        window.UpdateLayout();
        Require(station.Visibility == Visibility.Collapsed, "View menu can still hide the contextual tab");
        vm.SelectedMode = "Server";
        typeof(MainWindow).GetMethod("ShowClientStation_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(window, new object[] { window, new RoutedEventArgs() });
        window.UpdateLayout();
        Require(station.Visibility == Visibility.Visible && ReferenceEquals(tabs.SelectedItem, station) && menu.IsChecked,
            "Explicitly opening the station restores it from server context and manual hiding");
        vm.SelectedMode = "Server";
        window.UpdateLayout();
        Require(station.Visibility == Visibility.Collapsed, "Opening the station does not permanently override contextual visibility");
        vm.SelectedMode = "Client";
        tabs.SelectedItem = window.FindName("FullTestTab");
    }

    private static void CheckConfigurationRoundTrip()
    {
        var source = new MainViewModel();
        var restored = new MainViewModel();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ffd");
        try
        {
            for (var i = 1; i < 4; i++) source.AddClientSession($"127.0.0.{i + 1}", 1501 + i, (byte)(i + 1), 750 + i * 100, i % 2 == 0);
            foreach (var session in source.ClientSessions)
            {
                session.Name = "Cliente " + session.UnitId;
                session.Rows[0].StartAddress = 100;
                session.Rows[0].Quantity = 12;
                session.Rows[1].Enabled = false;
            }
            source.SelectedMode = "Server";
            source.LocalIp = "127.0.0.10";
            source.ServerPort = 1501;
            source.ServerUnitId = 7;
            source.ClientStationName = "Estacao de diagnostico";
            source.ServerName = "Simulador PLC";
            source.TestMode = "Server";
            source.SetFullTestTargets(source.ClientSessions.Take(2));
            source.ServerMapRanges.Add(new ServerMapRange { NamePrefix = "Coils remotos", Type = ModbusPointType.Coil, StartAddress = 1000, Quantity = 3, InitialValue = 1, IncrementValue = false });
            source.ServerMapRanges.Add(new ServerMapRange { Enabled = false, NamePrefix = "Reserva", StartAddress = 1000, Quantity = 5 });
            source.ApplyServerMapCommand.Execute(null);
            source.ServerPoints.First(x => x.Type == ModbusPointType.HoldingRegister).Value = 43210;
            source.ServerPoints.First(x => x.Type == ModbusPointType.InputRegister).Value = 54321;
            source.ServerPoints.First(x => x.Type == ModbusPointType.Coil && x.Address == 1001).Value = 0;
            var expected = source.CaptureConfiguration();
            var save = ConfigurationFile.SaveAsync(path, expected);
            WaitFor(() => save.IsCompleted, "FFD save completes");
            save.GetAwaiter().GetResult();
            var load = ConfigurationFile.LoadAsync(path);
            WaitFor(() => load.IsCompleted, "FFD load completes");
            var configuration = load.GetAwaiter().GetResult();
            var restore = restored.RestoreConfigurationAsync(configuration);
            WaitFor(() => restore.IsCompleted, "FFD restore completes");
            restore.GetAwaiter().GetResult();
            var actual = restored.CaptureConfiguration();
            Require(System.Text.Json.JsonSerializer.Serialize(expected) == System.Text.Json.JsonSerializer.Serialize(actual), "FFD roundtrip preserves all clients, selection, ranges and edited values");
            Require(restored.ClientSessions.Count == 4 && !restored.IsServerRunning && !restored.IsNetworkCaptureRunning
                && restored.ClientSessions.All(x => !x.IsScanning && x.Client.ConnectionOpenCount == 0), "Opening FFD never starts communication");
            Require(restored.ClientSessions.All(x => x.Client.KeepConnectionOpen == x.KeepConnectionOpen), "Restored socket modes match client settings");
            Require(restored.ClientSessions.All(x => x.Rows.All(r => r.LastStatus == "Nao lido")), "Saved configuration does not restore stale OK readings");
            configuration.Version = 99;
            var rejected = restored.RestoreConfigurationAsync(configuration);
            WaitFor(() => rejected.IsCompleted, "Invalid FFD rejected");
            Require(rejected.IsFaulted && restored.ClientSessions.Count == 4 && restored.ServerPort == 1501, "Invalid version leaves configuration intact");
            configuration.Version = 1;
            configuration.Clients[0].Blocks[0] = configuration.Clients[0].Blocks[0] with { StartAddress = 65535, Quantity = 2 };
            try { ConfigurationFile.Validate(configuration); throw new Exception("Invalid address range accepted"); }
            catch (InvalidDataException) { }
            configuration = source.CaptureConfiguration();
            restored.IsServerRunning = true;
            var busy = restored.RestoreConfigurationAsync(configuration);
            WaitFor(() => busy.IsCompleted, "Running server blocks configuration replacement");
            Require(busy.IsFaulted, "Cannot replace configuration while server is running");
            restored.IsServerRunning = false;
            var before = File.ReadAllBytes(path);
            configuration.Version = 2;
            var invalidSave = ConfigurationFile.SaveAsync(path, configuration);
            WaitFor(() => invalidSave.IsCompleted, "Invalid save rejected");
            Require(invalidSave.IsFaulted && File.ReadAllBytes(path).SequenceEqual(before), "Invalid save preserves the existing FFD file");
            try { System.Text.Json.JsonSerializer.Deserialize<ApplicationConfiguration>("{}"); throw new Exception("Missing FFD signature accepted"); }
            catch (System.Text.Json.JsonException) { }
        }
        finally
        {
            source.StopAllOperations();
            restored.StopAllOperations();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CheckServerTestRuntimeIndependence()
    {
        var vm = new MainViewModel { TestMode = "Server", SelectedMode = "Client",
            LocalIp = "127.0.0.1", ServerPort = FreePort() };
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        try
        {
            typeof(MainViewModel).GetMethod("PrepareFullTestScope", flags)!.Invoke(vm, null);
            vm.IsFullTestRunning = true;
            typeof(MainViewModel).GetMethod("StartModbusRuntimeAfterBaseline", flags)!.Invoke(vm, null);
            WaitFor(() => vm.IsServerRunning, "Server runtime starts for independently selected test role");
            Require(vm.SelectedMode == "Client" && vm.FullTestIsServerMode, "Automatic server start does not change selected workspace role");
            var tcp = (Task<FullTestStepResult>)typeof(MainViewModel).GetMethod("RunTcpConnectivityAsync", flags)!.Invoke(vm, new object?[] { CancellationToken.None, null })!;
            Require(tcp.Result.Status == "OK" && tcp.Result.Detail.Contains(vm.ServerEndpoint),
                "Server test validates the local server even when a client target is selected");
            var map = (Task<FullTestStepResult>)typeof(MainViewModel).GetMethod("RunClientMapValidationAsync", flags)!.Invoke(vm, new object?[] { CancellationToken.None, null })!;
            Require(map.Result.Status == "OK" && map.Result.Detail.Contains("Mapa local"), "Server map test is independent of selected client map");
            var report = (string)typeof(MainViewModel).GetMethod("BuildFullTestReport", flags)!.Invoke(vm, null)!;
            Require(report.Contains("Servidor / Escravo") && !report.Contains("Sessao de cliente selecionada"),
                "Report identifies the captured test role, not the sidebar role");
            Require(!vm.ApplyServerMapCommand.CanExecute(null), "Test locks simulated map reconfiguration");
        }
        finally { vm.IsFullTestRunning = false; vm.StopAllOperations(); }
    }

    private static void CheckEmptyClientStation()
    {
        var vm = new MainViewModel { SelectedMode = "Server", TestMode = "Server" };
        try
        {
            Require(vm.RemoveClientSessionCommand.CanExecute(null), "Default target can be removed");
            var removal = vm.RemoveClientSessionCommand.ExecuteAsync(null);
            WaitFor(() => removal.IsCompleted, "Default target removal completes");
            removal.GetAwaiter().GetResult();
            Require(vm.ClientSessions.Count == 0 && !vm.HasClientTargets && vm.SelectedMode == "Server",
                "Removing the last target leaves the station empty without changing server mode");
            Require(!vm.StartClientScanCommand.CanExecute(null) && !vm.ReadOnceCommand.CanExecute(null)
                && !vm.AddClientMapRowCommand.CanExecute(null) && !vm.RemoveClientSessionCommand.CanExecute(null),
                "Client operations are unavailable without a target");
            Require(vm.StartServerCommand.CanExecute(null) && vm.StartFullTestCommand.CanExecute(null),
                "Server and server-role Full Test remain available without client targets");
            var configuration = vm.CaptureConfiguration();
            Require(configuration.Clients.Count == 0 && configuration.SelectedClient == -1,
                "Empty station is captured without a phantom target");
            ConfigurationFile.Validate(configuration);
            var restore = vm.RestoreConfigurationAsync(configuration);
            WaitFor(() => restore.IsCompleted, "Empty station configuration restores");
            restore.GetAwaiter().GetResult();
            Require(vm.ClientSessions.Count == 0 && vm.SelectedMode == "Server", "FFD restore preserves zero targets and server role");
            vm.TestMode = "Client";
            vm.EnableActiveSubnetScan = false;
            Require(vm.StartFullTestCommand.CanExecute(null)
                && vm.FullTestSteps.Any(x => x.Name == "Validacao dos servidores descobertos"),
                "Master Full Test is available without a configured target even for passive-only discovery");
            var validation = (Task<FullTestStepResult>)typeof(MainViewModel)
                .GetMethod("RunDiscoveredServersValidationAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(vm, [CancellationToken.None])!;
            Require(validation.GetAwaiter().GetResult().Status == "Inconclusivo" && vm.ClientSessions.Count == 0,
                "No confirmed remote server yields inconclusive evidence without creating a phantom target");
            var observed = new NetworkDiscoveryRow { Ip = "203.0.113.50", IsModbusObserved = true,
                ObservedModbusPorts = "1502" };
            vm.NetworkDiscoveryRows.Add(observed);
            var confirmed = (HashSet<string>)typeof(MainViewModel)
                .GetField("_confirmedModbusEndpoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(vm)!;
            confirmed.Add("203.0.113.50:1502");
            var passiveOnly = (Task<FullTestStepResult>)typeof(MainViewModel)
                .GetMethod("RunDiscoveredServersValidationAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(vm, [CancellationToken.None])!;
            Require(passiveOnly.GetAwaiter().GetResult().Status == "Inconclusivo" && vm.ClientSessions.Count == 0,
                "Passive-only Modbus evidence outside active scope is not probed automatically");
            confirmed.Clear();
            vm.NetworkDiscoveryRows.Remove(observed);
            vm.AddClientSession("127.0.0.2", 1502, 1, 1000, true);
            Require(vm.ClientSessions.Count == 1 && vm.StartClientScanCommand.CanExecute(null),
                "A real target can be added after the station was empty");
        }
        finally { vm.StopAllOperations(); }
    }

    private static void CheckDiscoveredTargetWorkflow()
    {
        var passive = new MapDiscoveryRow { Endpoint = "127.0.0.1:1501", UnitId = "1", Function = "FC03 Holding Registers",
            StartAddress = 0, EndAddress = 9, DiscoveryMode = "Observado no servidor" };
        var active = new MapDiscoveryRow { Endpoint = "127.0.0.1:1501", UnitId = "1", Function = "FC03 Holding Registers",
            StartAddress = 0, EndAddress = 129, DiscoveryMode = "Ativo/client" };
        var other = new MapDiscoveryRow { Endpoint = "127.0.0.2:1501", UnitId = "1", Function = "FC03 Holding Registers",
            StartAddress = 200, EndAddress = 209, DiscoveryMode = "Ativo/client" };
        var imported = MainViewModel.BuildClientRowsFromDiscovery([passive, active, other], "127.0.0.1:1501", 1);
        Require(imported.Count == 2 && imported[0].Quantity == 120 && imported[1].StartAddress == 120
            && imported[1].Quantity == 10, "Only active map for exact endpoint and UID is imported in safe blocks");
        Require(MainViewModel.BuildClientRowsFromDiscovery([passive], "127.0.0.1:1501", 1).Count == 0,
            "Observed client requests are not treated as confirmed server registers");

        var vm = new MainViewModel { MapDiscoveryMaxAddress = 9, MapDiscoveryBlockSize = 10,
            MapDiscoveryFc01 = false, MapDiscoveryFc02 = false, MapDiscoveryFc03 = true, MapDiscoveryFc04 = false };
        var serverMap = new ModbusDataMap();
        for (ushort i = 0; i < 10; i++) serverMap.AddPoint(ModbusPointType.HoldingRegister, i, (ushort)(1000 + i));
        var server = new ModbusTcpServer(serverMap);
        var port = FreePort();
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.StartAsync(IPAddress.Loopback, port, cancellation.Token);
        try
        {
            var discoveryTask = vm.DiscoverMapForTargetAsync("127.0.0.1", port, 1, CancellationToken.None);
            WaitFor(() => discoveryTask.IsCompleted, "Selected endpoint map discovery completes");
            var discovered = discoveryTask.GetAwaiter().GetResult();
            Require(discovered.Any(x => x.Endpoint == $"127.0.0.1:{port}" && x.StartAddress == 0 && x.EndAddress == 9),
                $"Targeted read-only discovery returns the selected server map; rows={discovered.Count}; server={serverTask.Status}; "
                + string.Join("; ", discovered.Select(x => $"{x.Endpoint} {x.Function} {x.StartAddress}-{x.EndAddress}")));
            var target = vm.AddDiscoveredTarget("127.0.0.1", port, 1, 1000, discovered);
            Require(target.Rows.Count == 1 && target.Rows[0].Quantity == 10 && target.Points.All(x => x.Quality == "Nao lido")
                && !target.IsScanning && vm.ClientSessions.Contains(target), "Discovered target is added with unread map and no automatic scan");
            Require(ReferenceEquals(target, vm.AddDiscoveredTarget("127.0.0.1", port, 1, 1000, []))
                && target.Rows.Count == 1, "Adding an existing target preserves its configured map");
        }
        finally { cancellation.Cancel(); vm.StopAllOperations(); }
    }

    private static void CheckIndependentTestWorkspace(MainWindow window, MainViewModel vm)
    {
        var first = vm.SelectedClientSession;
        var second = vm.AddClientSession("127.0.0.5", 1505, 5, 1000, true);
        vm.SetFullTestTargets([second]);
        vm.TestMode = "Client";
        vm.SelectedClientSession = first;
        var steps = vm.FullTestSteps.ToArray();
        Require(steps.Any(x => x.Name.Contains(second.Endpoint)) && !steps.Any(x => x.Name.Contains(first.Endpoint)),
            "Preview uses explicitly included targets rather than the sidebar selection");
        vm.SelectedMode = "Server";
        vm.SelectedClientSession = second;
        Require(vm.TestMode == "Client" && vm.FullTestSteps.SequenceEqual(steps),
            "Sidebar role and target changes do not rebuild or change the test");
        var targetsDialog = new FullTestTargetsDialog(vm.ClientSessions);
        targetsDialog.Show();
        targetsDialog.UpdateLayout();
        Require(targetsDialog.SelectedTargets.SequenceEqual(new[] { second }), "Target dialog reflects the independent scope");
        ((Button)Descendants(targetsDialog).First(x => x is Button { Content: "Selecionar todos" }))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(targetsDialog.SelectedTargets.Count == 2 && !first.IncludeInFullTest,
            "Target dialog edits a draft without changing the live scope");
        targetsDialog.Close();
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedItem = window.FindName("FullTestTab");
        var sections = (TabControl)window.FindName("FullTestSections");
        sections.SelectedItem = window.FindName("FullTestExecutionTab");
        window.UpdateLayout();
        Require(sections.Items.Count == 6 && window.FindName("LegacyFullTestTab") is null,
            "Full Test contains execution and every result without a legacy duplicate view");
        Require(!Descendants((DependencyObject)window.FindName("FullTestExecutionTab")).OfType<TextBox>().Any(),
            "Execution workspace has no endpoint/IP configuration fields");
        var serverRadio = Descendants(window).OfType<RadioButton>().Single(x => (x.Content as string) == "Servidor / Escravo" && x.IsVisible);
        var selectedMode = vm.SelectedMode;
        serverRadio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(vm.TestMode == "Server" && vm.SelectedMode == selectedMode && tabs.SelectedItem == window.FindName("FullTestTab"),
            "Choosing the server test role does not navigate to or select a device");
        typeof(MainViewModel).GetMethod("PrepareFullTestScope", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
        var runScope = vm.FullTestRunScope;
        vm.IsFullTestRunning = true;
        vm.SelectedMode = "Client";
        vm.TestMode = "Client";
        Require(vm.FullTestIsServerMode && vm.FullTestRunScope == runScope, "Running test retains its captured role and scope");
        vm.IsFullTestRunning = false;
        vm.FullTestReport = "# Resultado anterior";
        var completedSteps = vm.FullTestSteps.ToArray();
        vm.TestMode = "Server";
        vm.SetFullTestTargets([first]);
        Require(vm.FullTestReport == "# Resultado anterior" && vm.FullTestSteps.SequenceEqual(completedSteps),
            "Editing the next test leaves previous results intact");
        vm.FullTestReport = "";
        vm.TestMode = "Client";
        vm.SetFullTestTargets([]);
        Require(vm.StartFullTestCommand.CanExecute(null)
            && vm.FullTestSteps.Any(x => x.Name == "Validacao dos servidores descobertos"),
            "Client test without selected targets runs discovery and validates only confirmed servers");
        vm.SetFullTestTargets([first]);
        vm.SelectedClientSession = second;
        vm.RemoveClientSessionCommand.Execute(null);
        WaitFor(() => vm.ClientSessions.Count == 1, "Independent-scope test target removed");
        vm.SelectedClientSession = first;
        vm.ResetDiagnosticSession();
    }

    private static void CheckDiagnosticExperience(MainWindow window, MainViewModel vm)
    {
        var settings = (DeviceSettingsView)window.FindName("DeviceSettings");
        settings.Reload();
        var port = (TextBox)settings.FindName("PortInput");
        var originalPort = port.Text;
        port.Text = "65536";
        Require(((TextBlock)settings.FindName("PortError")).Text.Length > 0
            && !((Button)settings.FindName("ApplyButton")).IsEnabled, "Invalid device field has inline feedback and blocks Apply");
        port.Text = originalPort;
        settings.Reload();
        var review = vm.BuildTestReview();
        Require(review.Contains(vm.SelectedClientSession.Endpoint) && review.Contains("Portas:")
            && review.Contains("TEMPO PROGRAMADO") && review.Contains("não executa escritas"), "Test review describes scope, ports, duration limitations and read-only policy");
        CheckDialog(new FullTestReviewDialog(review));

        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedItem = window.FindName("TimelineTab");
        window.UpdateLayout();
        var timeline = (ModbusTimelineView)window.FindName("ModbusTimeline");
        var now = DateTimeOffset.Now;
        var request = new TrafficEvent(now, TrafficDirection.ClientToServer, "172.27.30.84:1501", 42, 1, 3, 0, 10, "Leitura HR 0-9", "00 2A") { SessionId = "test-ux", Origin = "CLP bancada" };
        var reply = request with { Timestamp = now.AddMilliseconds(25), Direction = TrafficDirection.ServerToClient, FunctionCode = 131, Summary = "Modbus exception 02: endereço inválido", Hex = "00 2A 00 00 00 03 01 83 02" };
        vm.Traffic.Add(request); vm.Traffic.Add(reply);
        ((TextBox)timeline.FindName("SessionFilter")).Text = "172.27.30.84";
        ((TextBox)timeline.FindName("UnitFilter")).Text = "1";
        ((TextBox)timeline.FindName("FunctionFilter")).Text = "3";
        ((CheckBox)timeline.FindName("ErrorsOnly")).IsChecked = true;
        var messages = (DataGrid)timeline.FindName("Messages");
        Require(messages.Items.Count == 2, "Timeline FC filter includes exception and correlated request");
        messages.SelectedItem = request;
        Require(((TextBlock)timeline.FindName("TransactionDetails")).Text.Contains("25.0 ms")
            || ((TextBlock)timeline.FindName("TransactionDetails")).Text.Contains("25,0 ms"), "Timeline shows correlated transaction duration");
        ((TextBox)timeline.FindName("UnitFilter")).Text = "999";
        Require(messages.Items.Count == 0 && ((TextBlock)timeline.FindName("FilterFeedback")).Text.Contains("inválido"), "Invalid timeline filter is explicit");
        ((TextBox)timeline.FindName("UnitFilter")).Text = "1";
        messages.SelectedItem = request;
        window.UpdateLayout();
        Require(messages.Columns[3].ActualWidth >= 150, "Timeline endpoint column remains readable after opening the tab");
        SaveFrame(window, "InvestigateModbus");
        window.Width = 1100; window.Height = 720; window.UpdateLayout();
        SaveFrame(window, "CompactInvestigateModbus");
        window.Width = 1600; window.Height = 950; window.UpdateLayout();
        ((TextBox)timeline.FindName("SessionFilter")).Clear();
        ((TextBox)timeline.FindName("UnitFilter")).Clear();
        ((TextBox)timeline.FindName("FunctionFilter")).Clear();
        ((CheckBox)timeline.FindName("ErrorsOnly")).IsChecked = false;
        vm.Traffic.Remove(request); vm.Traffic.Remove(reply);

        var failure = new FullTestStep(90, "Mapa Modbus - CLP bancada · 172.27.30.84:1501", "Valida leituras")
        { Status = "Falha", Result = "Bloco HR: 14/15 leituras válidas. Uma exceção 02 no endereço 10.", Recommendation = "Conferir o endereço inicial e a quantidade do bloco.", FinishedAt = now };
        var gap = new FullTestStep(91, "Topologia inferida", "Identifica infraestrutura")
        { Status = "Inconclusivo", Result = "Nenhum anúncio LLDP/CDP observado; infraestrutura física não verificada.", FinishedAt = now };
        var conclusion = new FullTestStep(92, "Conclusao", "Agrega") { Status = "Falha", FinishedAt = now };
        vm.FullTestSteps.Add(failure); vm.FullTestSteps.Add(gap); vm.FullTestSteps.Add(conclusion);
        vm.RefreshTestExperience();
        Require(vm.TestFindings.Contains(failure) && !vm.TestFindings.Contains(gap) && !vm.TestFindings.Contains(conclusion)
            && vm.TestCoverageGaps.Contains(gap), "Findings separate actual failures from coverage gaps and avoid duplicate conclusion");
        tabs.SelectedItem = window.FindName("FullTestTab");
        ((TabControl)window.FindName("FullTestSections")).SelectedItem = window.FindName("FullTestSummaryTab");
        window.UpdateLayout();
        SaveFrame(window, "ActionableSummary");
        window.Width = 1100; window.Height = 720; window.UpdateLayout();
        SaveFrame(window, "CompactActionableSummary");
        window.Width = 1600; window.Height = 950; window.UpdateLayout();
        var evidenceButton = Descendants(window).OfType<Button>().First(x => ReferenceEquals(x.DataContext, failure) && Equals(x.Content, "Consultar etapa e evidências"));
        evidenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ReferenceEquals(vm.SelectedFullTestStep, failure)
            && ((TabControl)window.FindName("FullTestSections")).SelectedItem == window.FindName("FullTestExecutionTab"), "Finding opens its exact diagnostic stage");
        vm.FullTestSteps.Remove(failure); vm.FullTestSteps.Remove(gap); vm.FullTestSteps.Remove(conclusion);
        vm.SelectedFullTestStep = vm.FullTestSteps.FirstOrDefault();
        vm.RefreshTestExperience();

        var dialog = new ConnectionSettingsDialog(true, "127.0.0.1", 1502, 1, 1000);
        ((TextBox)dialog.FindName("AddressTextBox")).Text = "invalid";
        ((TextBox)dialog.FindName("PortTextBox")).Text = "0";
        ((TextBox)dialog.FindName("UnitIdTextBox")).Text = "256";
        ((TextBox)dialog.FindName("ScanRateTextBox")).Text = "1";
        Require(!((Button)dialog.FindName("ConfirmButton")).IsEnabled
            && ((TextBlock)dialog.FindName("AddressError")).Text.Length > 0, "Connection popup validates fields individually");
        CheckDialog(dialog);
    }

    private static void CheckDeviceConfiguration(MainWindow window, MainViewModel vm)
    {
        var first = vm.SelectedClientSession;
        var second = vm.AddClientSession("127.0.0.3", 1503, 3, 1000, true);
        vm.SelectedClientSession = first;
        first.RecordValues(first.Rows[0], new ushort[] { 99 }, "OK");
        var configure = vm.ConfigureDeviceAsync(true, first, "PLC bancada", "127.0.0.2", 1501, 2, 500, false);
        WaitFor(() => configure.IsCompleted, "Device configuration completes");
        configure.GetAwaiter().GetResult();
        Require(first.Name == "PLC bancada" && first.Endpoint == "127.0.0.2:1501" && first.UnitId == 2
            && first.ScanRateMs == 500 && !first.Client.KeepConnectionOpen, "Device settings update the selected endpoint");
        Require(second.Endpoint == "127.0.0.3:1503" && first.Rows.Count == 2
            && first.Points.All(x => x.Quality == "Nao lido"), "Endpoint change preserves maps, invalidates old values, and isolates targets");
        var invalid = vm.ConfigureDeviceAsync(true, first, "Invalid", "not-an-ip", 502, 1, 1000, true);
        Require(invalid.IsFaulted && first.Name == "PLC bancada", "Invalid settings leave device unchanged");
        first.IsScanning = true;
        var busy = vm.ConfigureDeviceAsync(true, first, "Changed", "127.0.0.1", 502, 1, 1000, true);
        Require(busy.IsFaulted && first.Name == "PLC bancada", "Running target blocks endpoint changes");
        first.IsScanning = false;
        vm.IsServerRunning = true;
        var serverBusy = vm.ConfigureDeviceAsync(false, first, "Changed", "0.0.0.0", 502, 1, 1000, true);
        Require(serverBusy.IsFaulted && vm.ServerName == "Servidor local", "Running local server blocks configuration");
        vm.IsServerRunning = false;
        var server = vm.ConfigureDeviceAsync(false, first, "PLC simulado", "127.0.0.1", 1502, 4, 1000, true);
        server.GetAwaiter().GetResult();
        Require(vm.ServerName == "PLC simulado" && vm.ServerUnitId == 4, "Local server can be renamed independently");
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedItem = window.FindName("ClientStationTab");
        window.UpdateLayout();
        Require(((DataGrid)window.FindName("StationTargetsGrid")).Items.Count == 2, "Station lists all configured targets");
        Require(((DataGrid)window.FindName("StationTargetsGrid")).Columns[1].ActualWidth >= 140,
            "Station IP column remains readable when opening a previously hidden tab");
        SaveFrame(window, "ClientStation");
        vm.SelectedMode = "Client";
        tabs.SelectedItem = window.FindName("CommunicationMapTab");
        ((TabControl)window.FindName("MapPresentationTabs")).SelectedItem = window.FindName("DeviceConfigurationTab");
        var settings = (DeviceSettingsView)window.FindName("DeviceSettings");
        settings.Reload();
        window.UpdateLayout();
        var name = (TextBox)settings.FindName("NameInput");
        name.Text = "Draft only";
        Require(first.Name == "PLC bancada", "Typing a draft does not change live configuration");
        settings.Reload();
        Require(name.Text == "Draft only", "Routine reload preserves an unapplied device draft");
        name.Text = first.Name;
        settings.Reload();
        foreach (var input in Descendants(settings).OfType<TextBox>().Where(x => x.IsVisible))
            Require(input.ActualHeight >= 25, "Device inputs are not clipped");
        SaveFrame(window, "DeviceConfiguration");
        window.Width = 1100;
        window.Height = 720;
        window.UpdateLayout();
        SaveFrame(window, "CompactDeviceConfiguration");
        window.Width = 1600;
        window.Height = 950;
        vm.SelectedClientSession = second;
        settings.Reload();
        Require(name.Text == second.Name, "Changing selection loads the correct device draft");
        vm.RemoveClientSessionCommand.Execute(null);
        WaitFor(() => vm.ClientSessions.Count == 1, "Temporary test target removed");
        var reset = vm.ConfigureDeviceAsync(true, first, "Alvo 1", "127.0.0.1", 1502, 1, 1000, true);
        WaitFor(() => reset.IsCompleted, "Test target reset");
        reset.GetAwaiter().GetResult();
        vm.ServerName = "Servidor local";
        vm.ServerUnitId = 1;
    }

    private static void WaitFor(Func<bool> condition, string message, int maxMilliseconds = 8000)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < maxMilliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        Require(condition(), message);
    }

    private static void CheckConcurrentSessions(MainViewModel vm)
    {
        vm.ReadValidationAttempts = 3;
        vm.ReadValidationIntervalMs = 100;
        vm.LocalIp = "127.0.0.1";
        vm.ServerPort = FreePort();
        vm.StartServerCommand.Execute(null);
        WaitFor(() => vm.IsServerRunning, "Local server started");
        var externalMap = new ModbusDataMap();
        for (ushort i = 0; i < 10; i++) externalMap.AddPoint(ModbusPointType.HoldingRegister, i, (ushort)(4321 + i));
        var external = new ModbusTcpServer(externalMap);
        using var cancellation = new CancellationTokenSource();
        var externalPort = FreePort();
        var externalTask = external.StartAsync(IPAddress.Loopback, externalPort, cancellation.Token);
        var first = vm.SelectedClientSession;
        first.Address = "127.0.0.1";
        first.Port = vm.ServerPort;
        first.ScanRateMs = 100;
        vm.StartClientScanCommand.Execute(null);
        var second = vm.AddClientSession("127.0.0.1", externalPort, 1, 100, true);
        Require(vm.StartClientScanCommand.CanExecute(null), "Second scan can start while first scan command is running");
        vm.StartClientScanCommand.Execute(null);
        try
        {
            WaitFor(() => first.Points[0].Quality == "OK" && second.Points[0].Quality == "OK", "Two clients read concurrently");
            Require(vm.IsServerRunning && vm.RunningClientCount == 2, "Server and both clients are active together");
            Require(first.Points[0].Value == 1000 && second.Points[0].Value == 4321, "Session values are isolated");
            vm.SelectedMode = "Client";
            vm.FullTestReport = "";
            vm.TestMode = "Client";
            vm.SetFullTestTargets(vm.ClientSessions);
            Require(vm.FullTestSteps.Count(x => x.Name.StartsWith("Mapa Modbus")) == 2, "Full Test plans separate map stages for both servers");
            foreach (var target in new[] { first, second })
            {
                var validate = typeof(MainViewModel).GetMethod("RunClientMapValidationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var validation = (Task<FullTestStepResult>)validate.Invoke(vm, new object[] { CancellationToken.None, target })!;
                WaitFor(() => validation.IsCompleted, "Per-target map validation completed");
                Require(validation.GetAwaiter().GetResult().Status == "OK", "Full Test uses each target's map and connection");
            }
            var mapValidation = typeof(MainViewModel).GetMethod("RunClientMapValidationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            second.Rows[0].Quantity = 11;
            var badMap = (Task<FullTestStepResult>)mapValidation.Invoke(vm, new object[] { CancellationToken.None, second })!;
            WaitFor(() => badMap.IsCompleted, "Invalid target map is diagnosed");
            Require(badMap.GetAwaiter().GetResult().Status == "Falha", "A missing range fails its target's map stage");
            second.Rows[0].Quantity = 10;
            var goodMap = (Task<FullTestStepResult>)mapValidation.Invoke(vm, new object[] { CancellationToken.None, first })!;
            WaitFor(() => goodMap.IsCompleted, "Healthy target still tested after another target fails");
            Require(goodMap.GetAwaiter().GetResult().Status == "OK", "A bad target does not contaminate another target's map validation");
            vm.PassiveObservationSeconds = 1;
            vm.TcpMonitoringSeconds = 1;
            vm.EnableActiveSubnetScan = false;
            var fullTest = vm.StartFullTestCommand.ExecuteAsync(null);
            WaitFor(() => fullTest.IsCompleted, "End-to-end Full Test on two local servers completed", 30000);
            fullTest.GetAwaiter().GetResult();
            Require(vm.FullTestReport.Contains("## Apendice tecnico") && vm.FullTestReport.Contains("3 / 3 / 3"), "Report includes measured repeats and separates technical appendix");
            Require(vm.FullTestReport.Contains("### Checklist da execucao") && vm.FullTestReport.Contains("## Desempenho das leituras")
                && !vm.FullTestReport.Contains("### Conversas observadas") && !vm.FullTestReport.Contains("## Dispositivos / rede"),
                "Report summarizes successful stages and excludes raw generic traffic and ARP inventory");
            var detailedReport = (string)typeof(MainViewModel).GetMethod("BuildDetailedFullTestReport",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, [vm.FullTestReport])!;
            Require(detailedReport.Contains("## Evidencias completas da execucao")
                && detailedReport.Contains("### Inventario de IPs observado")
                && detailedReport.Contains("### Conversas capturadas")
                && detailedReport.Contains("Objetivo:") && detailedReport.Length > vm.FullTestReport.Length,
                "Complete export retains each stage and raw inventory while summary stays focused");
            var reportMethod = typeof(MainViewModel).GetMethod("BuildFullTestReport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var reportFault = new FullTestStep(999, "Falha sintetica de regressao", "Objetivo")
            {
                Status = "Falha", Result = "Linha 1\nLinha 2\nLinha 3\nLinha 4\nEVIDENCIA COMPLETA 5", Recommendation = "Verificar endpoint e bloco."
            };
            vm.FullTestSteps.Add(reportFault);
            try
            {
                var faultReport = (string)reportMethod.Invoke(vm, null)!;
                Require(faultReport.Contains("EVIDENCIA COMPLETA 5") && faultReport.Contains("Verificar endpoint e bloco."),
                    "Concise report retains complete failure evidence and technical next action");
            }
            finally { vm.FullTestSteps.Remove(reportFault); }
            Require(vm.FullTestSteps.Any(x => x.Name == "Topologia inferida" && x.Status is "OK" or "Atencao"), "Logical topology is measured without asserting switch health");
            Require(vm.TopologyRouteHops.Any(x => x.Target == "127.0.0.1" && x.Status == "Success"), "ICMP route reaches local targets");
            Require(vm.TopologyCoverage.Contains("nao comprova") && vm.TopologyCoverage.Contains("Switches L2"),
                "Physical topology limitation remains explicit");
            Require(vm.FullTestReport.Contains("## Topologia observada e rotas"), "Report includes topology evidence");
            Require(vm.DiscoveredModbusDevices.Any(x => x.ConfirmedModbusPorts.Contains(externalPort.ToString())), "Nonstandard server port appears in confirmed Modbus category");
            var clientsField = typeof(MainViewModel).GetField("_fullTestClients", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            clientsField.SetValue(vm, new[] { first });
            var endpointsField = typeof(MainViewModel).GetField("_openDiscoveryEndpoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var endpoints = (HashSet<(string Address, int Port)>)endpointsField.GetValue(vm)!;
            endpoints.Clear(); endpoints.Add(("127.0.0.1", externalPort));
            var discovery = typeof(MainViewModel).GetMethod("RunConfirmedModbusDiscoveryAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var discoverTask = (Task<FullTestStepResult>)discovery.Invoke(vm, new object[] { CancellationToken.None })!;
            WaitFor(() => discoverTask.IsCompleted, "Unknown nonstandard Modbus endpoint validated");
            Require(discoverTask.GetAwaiter().GetResult().Status == "OK"
                && discoverTask.Result.Detail.Contains($"127.0.0.1:{externalPort} UID 1 FC3: Resposta Modbus validada"), "Discovery confirms server not in selected target scope");
            var individualProbe = vm.VerifyDeviceAsync("127.0.0.1", externalPort.ToString(), 1, CancellationToken.None);
            WaitFor(() => individualProbe.IsCompleted, "Individual Modbus probe completes");
            Require(individualProbe.GetAwaiter().GetResult().Contains("Resposta Modbus validada")
                && vm.DiscoveredModbusDevices.Any(x => x.ProbedPorts.Contains(externalPort.ToString())), "Explicit IP probe verifies nonstandard ports without subnet scan");
            var refusedPort = FreePort();
            var refusedProbe = vm.VerifyDeviceAsync("127.0.0.2", refusedPort.ToString(), 1, CancellationToken.None);
            WaitFor(() => refusedProbe.IsCompleted, "Closed endpoint probe completes");
            refusedProbe.GetAwaiter().GetResult();
            Require(vm.NetworkDiscoveryRows.Any(x => x.Ip == "127.0.0.2" && x.ProbedPorts == refusedPort.ToString() && !x.IsModbusConfirmed), "Closed endpoint is explicitly marked probed rather than absent Modbus");
            clientsField.SetValue(vm, new[] { first, second });
            Require(vm.FullTestSteps.Any(x => x.Name == "Varredura de hosts" && x.Status == "Nao aplicavel"), "Opt-out prevents additional host probing");
            Require(!vm.FullTestSteps.Any(x => x.Status == "Falha"), "Healthy local targets do not inherit historic errors");
            var reportFolder = Path.Combine("release", "review-validation");
            foreach (var extension in new[] { ".md", ".pdf" })
            {
                var export = ReportExporter.ExportAsync(Path.Combine(reportFolder, "full-test-regression" + extension), vm.FullTestReport,
                    vm.TopologyLinks.ToArray(), vm.NetworkDiscoveryRows.ToArray(), vm.TopologyNeighbors.ToArray(), vm.FullTestIsServerMode);
                WaitFor(() => export.IsCompleted, "Report export completed", 30000);
                export.GetAwaiter().GetResult();
            }
            var exportedMarkdown = File.ReadAllText(Path.Combine(reportFolder, "full-test-regression.md"));
            Require(exportedMarkdown.Contains("## Diagrama visual de topologia") && exportedMarkdown.Contains("## Apendice tecnico"), "Markdown contains diagrams and complete technical evidence");
            Require(!exportedMarkdown.Contains("### Diagrama logico (enlaces de evidencia)"), "Export does not duplicate visual topology as a text diagram");
            var completeExport = ReportExporter.ExportAsync(Path.Combine(reportFolder, "full-test-complete-regression.pdf"), detailedReport,
                vm.TopologyLinks.ToArray(), vm.NetworkDiscoveryRows.ToArray(), vm.TopologyNeighbors.ToArray(), vm.FullTestIsServerMode);
            WaitFor(() => completeExport.IsCompleted, "Complete PDF export completed", 120000);
            completeExport.GetAwaiter().GetResult();
            using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(Path.Combine(reportFolder, "full-test-complete-regression.pdf"), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                Require(pdf.PageCount > 1, "Complete PDF with raw evidence is valid");
            var completeMarkdownExport = ReportExporter.ExportAsync(Path.Combine(reportFolder, "full-test-complete-regression.md"), detailedReport,
                vm.TopologyLinks.ToArray(), vm.NetworkDiscoveryRows.ToArray(), vm.TopologyNeighbors.ToArray(), vm.FullTestIsServerMode);
            WaitFor(() => completeMarkdownExport.IsCompleted, "Complete Markdown export completed", 30000);
            completeMarkdownExport.GetAwaiter().GetResult();
            Require(File.ReadAllText(Path.Combine(reportFolder, "full-test-complete-regression.md")).Contains("### Inventario de IPs observado"),
                "Complete Markdown export preserves the IP inventory");
            var filteredPath = Path.Combine(reportFolder, "report-filter-regression.md");
            var filteredExport = ReportExporter.ExportAsync(filteredPath, vm.FullTestReport,
                [new("203.0.113.200", "198.51.100.200", "Captura UDP", 999999, "Generic internet flow"),
                 new("Aplicacao / Cliente", "203.0.113.20:1502", "Modbus confirmado", 1, "Confirmed")],
                 [new NetworkDiscoveryRow { Ip = "203.0.113.20", IsModbusConfirmed = true, ConfirmedModbusPorts = "1502" }]);
            WaitFor(() => filteredExport.IsCompleted, "Focused report diagram export completed", 30000);
            filteredExport.GetAwaiter().GetResult();
            var filteredReport = File.ReadAllText(filteredPath);
            Require(filteredReport.Contains("## Diagrama visual de topologia Modbus")
                && !filteredReport.Contains("Generic internet flow"), "Generic high-volume traffic is excluded from report diagram");
            Require(File.Exists(Path.Combine(reportFolder, "full-test-regression.topologia-01.png")), "Markdown diagram image is exported alongside the report");
            using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(Path.Combine(reportFolder, "full-test-regression.pdf"), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                Require(pdf.PageCount > 1, "PDF report is valid and paginated");
            vm.SetFullTestTargets([first]);
            vm.SelectedMode = "Server";
            Require(vm.Port == vm.ServerPort && second.Port == externalPort, "Server port is independent of selected target");
            var write = vm.WriteRangeFromMapAsync(first.Rows[0], 0, 0, 1357);
            WaitFor(() => write.IsCompleted, "Write finished on original session while another view is selected");
            write.GetAwaiter().GetResult();
            Require(externalMap.ToPoints()[0].Value == 4321, "Write did not reach the other server");
            WaitFor(() => first.Points[0].Value == 1357, "Write reached intended server");
            Require(vm.Traffic.Any(x => x.SessionId == first.Id) && vm.Traffic.Any(x => x.SessionId == second.Id) && vm.Traffic.Any(x => x.SessionId == "local-server"), "Timeline identifies all session origins");
            vm.SelectedClientSession = second;
            var disconnect = second.DisconnectAsync();
            WaitFor(() => disconnect.IsCompleted, "Second session disconnected");
            Require(first.IsScanning && vm.IsServerRunning && !second.IsScanning, "Disconnect does not stop other sessions");
        }
        finally
        {
            var stopFirst = first.DisconnectAsync();
            var stopSecond = second.DisconnectAsync();
            WaitFor(() => stopFirst.IsCompleted && stopSecond.IsCompleted, "Client cleanup completed");
            vm.StopServerCommand.Execute(null);
            WaitFor(() => !vm.IsServerRunning, "Local server cleanup completed");
            external.Stop();
            cancellation.Cancel();
            WaitFor(() => externalTask.IsCompleted, "External server cleanup completed");
            vm.SelectedClientSession = first;
            Require(first.Points[0].Quality == "Leitura parada", "Stopped scans retain their last sample without an OK quality label");
        }
    }

    private static void CheckFullTestWarningScope(MainViewModel vm)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var prepare = typeof(MainViewModel).GetMethod("PrepareFullTestScope", flags)!;
        var consolidate = typeof(MainViewModel).GetMethod("RunObservedFailuresAsync", flags)!;
        var upsert = typeof(MainViewModel).GetMethod("UpsertImportantWarning", flags, null,
            new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset) }, null)!;
        void Warning(string id, DateTimeOffset timestamp) => upsert.Invoke(vm,
            new object[] { id + "|regression", "Erro", "Conexao recusada", "Recusada", "Verificar listener", timestamp });
        FullTestStepResult Result() => ((Task<FullTestStepResult>)consolidate.Invoke(vm, new object[] { CancellationToken.None })!).GetAwaiter().GetResult();
        var selected = vm.SelectedClientSession;
        Warning(selected.Id, DateTimeOffset.Now.AddMinutes(-5));
        prepare.Invoke(vm, null);
        vm.IsFullTestRunning = true;
        Warning("outro-cliente", DateTimeOffset.Now);
        Warning(selected.Id, DateTimeOffset.Now.AddMinutes(-2));
        vm.VerificationChecks[0].Status = "Falha";
        Require(Result().Status == "OK", "Historic warnings and global/other-client checks must not fail current target");
        Warning(selected.Id, DateTimeOffset.Now);
        Require(Result().Status == "Falha", "Fresh errors from tested target fail current run");
        prepare.Invoke(vm, null);
        Require(Result().Status == "OK", "A new run does not inherit errors from the previous run");
        var another = new ClientConnectionSession();
        vm.ClientSessions.Add(another);
        vm.IsFullTestRunning = false;
        vm.SetFullTestTargets(vm.ClientSessions);
        prepare.Invoke(vm, null);
        vm.IsFullTestRunning = true;
        Warning(another.Id, DateTimeOffset.Now);
        Require(Result().Status == "Falha", "All-server mode includes fresh errors from another configured target");
        vm.ClientSessions.Remove(another);
        vm.IsFullTestRunning = false;
        vm.SetFullTestTargets([selected]);
        vm.ClearTimelineCommand.Execute(null);
    }

    private static void CheckDeviceGroups(MainViewModel vm)
    {
        Require(MainViewModel.TryParseDiscoveryPorts("502,1501;1502", out var ports) && ports.Contains(1501), "Additional Modbus discovery ports parsed");
        Require(!MainViewModel.TryParseDiscoveryPorts("0,65536", out _) && !MainViewModel.TryParseDiscoveryPorts("", out _), "Invalid discovery ports rejected");
        var planner = typeof(MainViewModel).GetMethod("DiscoveryPortsForTest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Require(((int[])planner.Invoke(vm, null)!).Contains(1501), "Default TCP discovery checks 1501");
        var arp = new NetworkDiscoveryRow { Ip = "10.0.0.10", Source = "ARP" };
        var host = new NetworkDiscoveryRow { Ip = "10.0.0.11", Source = "Referencia passiva" };
        var tcp = new NetworkDiscoveryRow { Ip = "10.0.0.12", Source = "Sondagem autorizada", OpenTcpPorts = "1501" };
        vm.NetworkDiscoveryRows.Add(arp); vm.NetworkDiscoveryRows.Add(host); vm.NetworkDiscoveryRows.Add(tcp);
        Require(vm.DiscoveredIpv4Neighbors.Contains(arp) && vm.DiscoveredOtherHosts.Contains(host)
            && vm.DiscoveredTcpServices.Contains(tcp) && !vm.DiscoveredModbusDevices.Contains(tcp), "ARP, observed hosts and open TCP do not imply Modbus");
        var register = typeof(MainViewModel).GetMethod("RegisterModbusDevice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        register.Invoke(vm, new object[] { "10.0.0.12:1501", true, "Validated Modbus response" });
        register.Invoke(vm, new object[] { "10.0.0.12:1502", false, "Passive Modbus response" });
        Require(vm.DiscoveredModbusDevices.Contains(tcp) && !vm.DiscoveredTcpServices.Contains(tcp)
            && tcp.ConfirmedModbusPorts == "1501" && tcp.ObservedModbusPorts == "1502", "Confirmed and passive evidence stay separate per port");
        var upsert = typeof(MainViewModel).GetMethod("UpsertDiscovery", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        upsert.Invoke(vm, new object[] { tcp.Ip, "001122334455", "ARP", "Vizinho IPv4", "", "Later ARP observation" });
        Require(tcp.RoleGuess == "Servidor Modbus confirmado", "Later ARP does not overwrite confirmed Modbus identity");
        vm.NetworkDiscoveryRows.Clear();
    }

    private static void CheckNeighborDecoding()
    {
        var frame = Convert.FromHexString("0180C200000E00112233445588CC0207040011223344550406054769312F31060200780A04535730310000");
        var neighbor = NeighborDiscovery.Parse(frame, DateTimeOffset.Now);
        Require(neighbor is { Name: "SW01", Port: "Gi1/1", Ttl: 120, Protocol: "LLDP" }, "LLDP mandatory TLVs and advertised identity decoded");
        Require(NeighborDiscovery.Parse(frame[..^2], DateTimeOffset.Now) is null, "Truncated LLDP is not trusted");
        var tagged = frame.Take(12).Concat(Convert.FromHexString("8100006488CC")).Concat(frame.Skip(14)).ToArray();
        Require(NeighborDiscovery.Parse(tagged, DateTimeOffset.Now)?.Name == "SW01", "VLAN-tagged LLDP decoded");
        var body = Convert.FromHexString("023C00000001000853573032000300094769302F31");
        uint sum = 0;
        for (var i = 0; i < body.Length; i += 2) sum += (uint)(body[i] << 8) + (i + 1 < body.Length ? body[i + 1] : 0u);
        while (sum >> 16 != 0) sum = (sum & 65535) + (sum >> 16);
        var checksum = (ushort)~sum;
        body[2] = (byte)(checksum >> 8); body[3] = (byte)checksum;
        var cdp = Convert.FromHexString("01000CCCCCCC001122334455001DAAAA0300000C2000").Concat(body).ToArray();
        Require(NeighborDiscovery.Parse(cdp, DateTimeOffset.Now) is { Protocol: "CDP", Name: "SW02", Port: "Gi0/1" }, "CDP checksum and TLVs decoded");
        body[2] = body[3] = 0;
        sum = body[^1];
        for (var i = 0; i + 1 < body.Length; i += 2) sum += (uint)(body[i] << 8) + body[i + 1];
        while (sum >> 16 != 0) sum = (sum & 65535) + (sum >> 16);
        checksum = (ushort)~sum;
        body[2] = (byte)(checksum >> 8); body[3] = (byte)checksum;
        cdp = Convert.FromHexString("01000CCCCCCC001122334455001DAAAA0300000C2000").Concat(body).ToArray();
        Require(NeighborDiscovery.Parse(cdp, DateTimeOffset.Now)?.Name == "SW02", "Cisco odd-octet CDP checksum accepted");
        cdp[^1] ^= 1;
        Require(NeighborDiscovery.Parse(cdp, DateTimeOffset.Now) is null, "Corrupt CDP checksum rejected");
        var random = new Random(123);
        for (var i = 0; i < 1000; i++) { var bytes = new byte[random.Next(100)]; random.NextBytes(bytes); _ = NeighborDiscovery.Parse(bytes, DateTimeOffset.Now); }
    }

    private static void CheckAnalysisWindows(MainViewModel vm)
    {
        CheckDeviceGroups(vm);
        CheckNeighborDecoding();
        vm.EnableActiveSubnetScan = true;
        vm.AutomaticNetworkScope = true;
        vm.EnableActiveSubnetScan = false; // Regression suite never scans the user's LAN.
        vm.AutomaticNetworkScope = false;
        vm.ActiveScanCidr = "172.27.30.0/24";
        vm.EnableActiveSubnetScan = true;
        var candidatesMethod = typeof(MainViewModel).GetMethod("AuthorizedCandidates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var allCandidates = (List<string>)candidatesMethod.Invoke(vm, null)!;
        Require(allCandidates.Contains("172.27.30.84") && allCandidates.Count(x => x.StartsWith("172.27.30.")) == 254, "Every subnet IP is a candidate, including hosts absent from ARP");
        vm.EnableActiveSubnetScan = false;
        vm.AutomaticNetworkScope = true;
        vm.ActiveScanCidr = "";
        Require(MainViewModel.TryParseScanScope("192.168.1.0/24", out _, out _), "Explicit CIDR accepted");
        Require(MainViewModel.TryParseScanScope("192.168.1.0/16", out _, out _) && !MainViewModel.TryParseScanScope("192.168.1.0/15", out _, out _) && !MainViewModel.TryParseScanScope("224.0.0.0/24", out _, out _), "Entire /16 network supported; broader/multicast scan scope rejected");
        Require(MainViewModel.Percentile(new double[] { 1, 2, 3, 4, 10 }, .95) == 10, "p95 uses ordered sample nearest rank");
        var analyzer = new TrafficWindowAnalysis();
        analyzer.Observe(new TcpTimelineRow { Length = 999 });
        analyzer.Begin();
        analyzer.Observe(new TcpTimelineRow { Timestamp = DateTimeOffset.Now.AddMinutes(-1), Length = 9999 });
        for (var i = 0; i < 5000; i++) analyzer.Observe(new TcpTimelineRow
        {
            Length = 60, Source = "10.0.0.1:502", Destination = "10.0.0.2:44000",
            SourceHost = "10.0.0.1", DestinationHost = "10.0.0.2", Protocol = "TCP", IsTcp = true,
            TcpReset = i == 0, TcpAcknowledgment = true, TcpWindow = 100,
            TcpSequence = (uint)i, TcpPayloadLength = 0
        });
        var summary = analyzer.End();
        Require(summary.Packets == 5000 && summary.Bytes == 300000 && summary.Resets == 1, "Counters are independent of timeline row retention");
        Require(summary.ModbusFrames == 0 && summary.ConfirmedServers.Count == 0, "Port 502 alone does not identify Modbus");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var observe = typeof(MainViewModel).GetMethod("ObserveTrafficWindowAsync", flags)!;
        vm.PassiveObservationSeconds = 1;
        vm.IsNetworkCaptureRunning = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var window = (Task<FullTestStepResult>)observe.Invoke(vm, new object[] { true, CancellationToken.None })!;
        var callback = typeof(MainViewModel).GetMethod("OnPassivePacketCaptured", flags)!;
        for (var i = 0; i < 20; i++) callback.Invoke(vm, new object?[] { null, new TcpTimelineRow
        {
            Source = "10.0.0.1:502", Destination = "10.0.0.2:44000", SourceHost = "10.0.0.1", DestinationHost = "10.0.0.2", Length = 60, Protocol = "TCP"
        } });
        WaitFor(() => window.IsCompleted, "Fixed observation window completed");
        Require(clock.ElapsedMilliseconds >= 950, "Observation must not exit after minimum packets arrive");
        Require(window.GetAwaiter().GetResult().Detail.Contains("Pacotes: 20"), "Window contains only new packets");
        vm.IsNetworkCaptureRunning = false;
        var unavailable = (Task<FullTestStepResult>)observe.Invoke(vm, new object[] { false, CancellationToken.None })!;
        Require(unavailable.GetAwaiter().GetResult().Status == "Inconclusivo", "Unavailable capture is not an OK network diagnosis");
        vm.SelectedMode = "Server";
        vm.TestMode = "Server";
        typeof(MainViewModel).GetMethod("PrepareFullTestScope", flags)!.Invoke(vm, null);
        vm.IsFullTestRunning = true;
        var record = typeof(MainViewModel).GetMethod("RecordServerWindowEvent", flags)!;
        for (ushort tid = 0; tid < 2000; tid++)
        {
            var request = new TrafficEvent(DateTimeOffset.Now, TrafficDirection.ClientToServer, "10.0.0.2:44000", tid, 1, 3, 0, 1, "Request FC3", "") { SessionId = "local-server" };
            record.Invoke(vm, new object[] { request });
            record.Invoke(vm, new object[] { request with { Direction = TrafficDirection.ServerToClient, Timestamp = DateTimeOffset.Now } });
        }
        var receive = typeof(MainViewModel).GetMethod("RunSendReceiveValidationAsync", flags)!;
        var received = (Task<FullTestStepResult>)receive.Invoke(vm, new object?[] { CancellationToken.None, null })!;
        Require(received.GetAwaiter().GetResult().Status == "OK" && received.Result.Detail.Contains("2000 requisicoes"), "Server correlations do not depend on 500 retained events");
        vm.IsFullTestRunning = false;
        vm.SelectedMode = "Client";
        vm.TestMode = "Client";
        vm.PassiveObservationSeconds = 12;
        vm.ClearTimelineCommand.Execute(null);
    }

    private static void CheckDialog(Window dialog)
    {
        dialog.Show();
        dialog.UpdateLayout();
        foreach (var control in Descendants(dialog).OfType<Control>().Where(x => x is TextBox or Button && x.IsVisible))
        {
            if (dialog is FullTestScopeDialog or ManualWindow) { control.BringIntoView(); dialog.UpdateLayout(); }
            var bounds = control.TransformToAncestor(dialog).TransformBounds(new Rect(control.RenderSize));
            Require(bounds.Bottom <= dialog.ActualHeight && bounds.Right <= dialog.ActualWidth && bounds.Top >= 0,
                $"Clipped control in {dialog.Title}: {control.Name}");
            Require(control.ActualHeight >= 20, $"Input too small in {dialog.Title}");
        }
        SaveFrame(dialog, dialog.GetType().Name + (dialog is ConnectionSettingsDialog ? dialog.Title : ""));
        dialog.Close();
    }

    private static void SaveFrame(Window window, string name)
    {
        var content = window.Content as FrameworkElement ?? window;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, bounds);
        }
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var folder = Path.Combine("release", "review-validation");
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
