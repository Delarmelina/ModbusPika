using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public partial class MainWindow : Window
{
    private readonly GridLength _defaultConfigWidth = new(360);
    private readonly GridLength _defaultMapWidth = new(1.35, GridUnitType.Star);
    private readonly GridLength _defaultDiagnosticsWidth = new(1, GridUnitType.Star);
    private readonly GridLength _defaultTopHeight = new(1.05, GridUnitType.Star);
    private readonly GridLength _defaultTimelineHeight = new(1.25, GridUnitType.Star);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void ShowConfigMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLayoutVisibility();
    }

    private void ShowMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLayoutVisibility();
    }

    private void ShowDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLayoutVisibility();
    }

    private void ShowTimelineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyLayoutVisibility();
    }

    private void ToggleConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ShowConfigMenuItem.IsChecked = !ShowConfigMenuItem.IsChecked;
        ApplyLayoutVisibility();
    }

    private void FocusMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowConfigMenuItem.IsChecked = false;
        ShowMapMenuItem.IsChecked = true;
        ShowDiagnosticsMenuItem.IsChecked = false;
        ShowTimelineMenuItem.IsChecked = false;
        ApplyLayoutVisibility();
    }

    private void FocusDiagnosticsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowConfigMenuItem.IsChecked = false;
        ShowMapMenuItem.IsChecked = false;
        ShowDiagnosticsMenuItem.IsChecked = true;
        ShowTimelineMenuItem.IsChecked = false;
        ApplyLayoutVisibility();
    }

    private void FocusTimelineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowConfigMenuItem.IsChecked = false;
        ShowMapMenuItem.IsChecked = false;
        ShowDiagnosticsMenuItem.IsChecked = false;
        ShowTimelineMenuItem.IsChecked = true;
        ApplyLayoutVisibility();
    }

    private void LargeTimelineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowMapMenuItem.IsChecked = true;
        ShowDiagnosticsMenuItem.IsChecked = true;
        ShowTimelineMenuItem.IsChecked = true;
        ApplyLayoutVisibility();
        TopPanelsRow.Height = new GridLength(0.55, GridUnitType.Star);
        TimelineRow.Height = new GridLength(1.45, GridUnitType.Star);
    }

    private void ResetLayoutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowConfigMenuItem.IsChecked = true;
        ShowMapMenuItem.IsChecked = true;
        ShowDiagnosticsMenuItem.IsChecked = true;
        ShowTimelineMenuItem.IsChecked = true;
        ApplyLayoutVisibility();
        SetTabPlacement(Dock.Top);
    }

    private void TabsTopMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetTabPlacement(Dock.Top);
    }

    private void TabsLeftMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetTabPlacement(Dock.Left);
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

    private void ApplyLayoutVisibility()
    {
        var showConfig = ShowConfigMenuItem.IsChecked;
        var showMap = ShowMapMenuItem.IsChecked;
        var showDiagnostics = ShowDiagnosticsMenuItem.IsChecked;
        var showTimeline = ShowTimelineMenuItem.IsChecked;

        if (!showMap && !showDiagnostics && !showTimeline)
        {
            showMap = true;
            ShowMapMenuItem.IsChecked = true;
        }

        ConfigPanel.Visibility = showConfig ? Visibility.Visible : Visibility.Collapsed;
        ConfigSplitter.Visibility = showConfig ? Visibility.Visible : Visibility.Collapsed;
        ConfigColumn.Width = showConfig ? _defaultConfigWidth : new GridLength(0);
        ConfigSplitterColumn.Width = showConfig ? new GridLength(6) : new GridLength(0);

        MapPane.Visibility = showMap ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPane.Visibility = showDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        TimelinePane.Visibility = showTimeline ? Visibility.Visible : Visibility.Collapsed;

        var hasTopPanel = showMap || showDiagnostics;
        TopPanelsRow.Height = hasTopPanel ? _defaultTopHeight : new GridLength(0);
        TimelineSplitterRow.Height = hasTopPanel && showTimeline ? new GridLength(6) : new GridLength(0);
        TimelineSplitter.Visibility = hasTopPanel && showTimeline ? Visibility.Visible : Visibility.Collapsed;
        TimelineRow.Height = showTimeline ? _defaultTimelineHeight : new GridLength(0);

        if (showMap && showDiagnostics)
        {
            MapColumn.Width = _defaultMapWidth;
            MapSplitterColumn.Width = new GridLength(6);
            DiagnosticsColumn.Width = _defaultDiagnosticsWidth;
            MapDiagnosticsSplitter.Visibility = Visibility.Visible;
        }
        else if (showMap)
        {
            MapColumn.Width = new GridLength(1, GridUnitType.Star);
            MapSplitterColumn.Width = new GridLength(0);
            DiagnosticsColumn.Width = new GridLength(0);
            MapDiagnosticsSplitter.Visibility = Visibility.Collapsed;
        }
        else if (showDiagnostics)
        {
            MapColumn.Width = new GridLength(0);
            MapSplitterColumn.Width = new GridLength(0);
            DiagnosticsColumn.Width = new GridLength(1, GridUnitType.Star);
            MapDiagnosticsSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            MapColumn.Width = new GridLength(0);
            MapSplitterColumn.Width = new GridLength(0);
            DiagnosticsColumn.Width = new GridLength(0);
            MapDiagnosticsSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void SetTabPlacement(Dock placement)
    {
        foreach (var tabControl in FindVisualChildren<TabControl>(this))
        {
            tabControl.TabStripPlacement = placement;
        }
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
