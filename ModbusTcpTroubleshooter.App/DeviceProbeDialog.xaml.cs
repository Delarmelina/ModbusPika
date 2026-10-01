using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class DeviceProbeDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _cancellation = new();
    public DeviceProbeDialog(MainViewModel viewModel, string address)
    {
        InitializeComponent();
        _viewModel = viewModel;
        AddressBox.Text = address;
        PortsBox.Text = viewModel.ModbusDiscoveryPorts;
        Closed += (_, _) => _cancellation.Cancel();
    }
    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (!byte.TryParse(UnitBox.Text, out var uid)) { ResultBox.Text = "ID da unidade: 0 a 255."; return; }
        VerifyButton.IsEnabled = false;
        SettingsGrid.IsEnabled = false;
        ResultBox.Text = "Verificando portas e respostas Modbus...";
        try { ResultBox.Text = await _viewModel.VerifyDeviceAsync(AddressBox.Text.Trim(), PortsBox.Text, uid, _cancellation.Token); }
        catch (OperationCanceledException) { ResultBox.Text = "Verificacao cancelada."; }
        catch (Exception ex) { ResultBox.Text = ex.Message; }
        finally { VerifyButton.IsEnabled = true; SettingsGrid.IsEnabled = true; }
    }
}
