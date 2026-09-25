using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ModbusTcpTroubleshooter.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var originalLanguage = UiLocalization.CurrentLanguage;
        try
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();
            var vm = (MainViewModel)window.DataContext;
            var toolbar = (StackPanel)window.FindName("MainToolbar");
            var shortcuts = Descendants(toolbar).OfType<Button>().ToArray();
            Require(shortcuts.Length == 8, "Toolbar has only New, Save and the six client/server actions");
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
            Require(!vm.StartClientScanCommand.CanExecute(null), "Client blocked by server");
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
            var reportButton = Descendants(window).OfType<Button>().First(x => (x.Content as string) == "Gerar relatório" && x.IsVisible);
            reportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, reportButton));
            Require(ReferenceEquals(testSections.SelectedItem, window.FindName("FullTestReportTab")), "Report shortcut opens its tab");
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
            window.Close();

            CheckDialog(new ConnectionSettingsDialog(true, "127.0.0.1", 1502, 1, 1000));
            CheckDialog(new ConnectionSettingsDialog(false, "0.0.0.0", 1502, 1, 1000));
            CheckDialog(new FullTestScopeDialog(true, 250, 48, 12));
            CheckDialog(new MapDiscoverySettingsDialog(false, false, 1, 10, 120, 10, true, true, true, true, true));
            CheckDialog(new WriteRegisterDialog("127.0.0.1:1502 | HR 0-10", 0, 10, 1, "Write", "Value"));
            Console.WriteLine("UI smoke tests OK: role bindings, test preview/progress, Portuguese UI, five dialog layouts.");
        }
        finally
        {
            UiLocalization.CurrentLanguage = originalLanguage;
            app.Shutdown();
        }
    }

    private static void CheckDialog(Window dialog)
    {
        dialog.Show();
        dialog.UpdateLayout();
        foreach (var control in Descendants(dialog).OfType<Control>().Where(x => x is TextBox or Button && x.IsVisible))
        {
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
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
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
