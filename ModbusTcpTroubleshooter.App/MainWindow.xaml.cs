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
