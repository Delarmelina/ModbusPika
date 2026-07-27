using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
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

        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column.Header?.ToString() != "Ultimo valor")
        {
            return;
        }

        if (row.FunctionCode != ModbusProtocol.ReadHoldingRegisters)
        {
            MessageBox.Show(
                this,
                "Escrita pela tabela usa FC06 e esta disponivel apenas para linhas FC03 Holding Registers.",
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
        var context = $"{viewModel.TargetIp}:{viewModel.Port} | UID {viewModel.UnitId} | {row.Name} | HR {row.StartAddress}";
        var dialog = new WriteRegisterDialog(context, row.StartAddress, initialValue)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.WriteHoldingRegisterFromMapAsync(row, dialog.Address, dialog.Value);
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
