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
        AddressLabel.Text = isClient ? "IP address" : "Listen address";
        AddressTextBox.Text = address;
        PortTextBox.Text = port.ToString();
        UnitIdTextBox.Text = unitId.ToString();
        ScanRateTextBox.Text = scanRateMs.ToString();
        ScanRateLabel.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        ScanRatePanel.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        ScanRateRow.Height = isClient ? new GridLength(32) : new GridLength(0);
        // Window height includes title bar, validation area and button row.
        // Keep enough usable space for every field at Windows' default DPI.
        Height = isClient ? 365 : 330;
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
