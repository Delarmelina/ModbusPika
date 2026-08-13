using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class ConnectionSettingsDialog : Window
{
    private readonly bool _isClient;

    public ConnectionSettingsDialog(bool isClient, string address, int port, byte unitId, int scanRateMs)
    {
        InitializeComponent();
        _isClient = isClient;
        Title = isClient ? "Configure Modbus TCP Client" : "Configure Modbus TCP Server";
        HeadingText.Text = isClient ? "Modbus TCP Client" : "Modbus TCP Server";
        DescriptionText.Text = isClient
            ? "Configure the remote Modbus TCP endpoint used for reads and writes."
            : "Configure the local endpoint exposed by the simulated Modbus TCP server.";
        AddressLabel.Text = isClient ? "Target IP address" : "Listen IP address";
        AddressTextBox.Text = address;
        PortTextBox.Text = port.ToString();
        UnitIdTextBox.Text = unitId.ToString();
        ScanRateTextBox.Text = scanRateMs.ToString();
        ScanRateLabel.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        ScanRateTextBox.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        Height = isClient ? 330 : 285;
        AddressTextBox.SelectAll();
        AddressTextBox.Focus();
    }

    public string Address { get; private set; } = "";
    public int Port { get; private set; }
    public byte UnitId { get; private set; }
    public int ScanRateMs { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            ValidationText.Text = "IP address is required.";
            return;
        }

        if (!int.TryParse(PortTextBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ValidationText.Text = "Service port must be between 1 and 65535.";
            return;
        }

        if (!byte.TryParse(UnitIdTextBox.Text.Trim(), out var unitId))
        {
            ValidationText.Text = "Unit ID must be between 0 and 255.";
            return;
        }

        var scanRateMs = 1000;
        if (_isClient && (!int.TryParse(ScanRateTextBox.Text.Trim(), out scanRateMs) || scanRateMs < 100))
        {
            ValidationText.Text = "Scan rate must be at least 100 ms.";
            return;
        }

        Address = address;
        Port = port;
        UnitId = unitId;
        ScanRateMs = scanRateMs;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
