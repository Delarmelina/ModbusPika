using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class WriteRegisterDialog : Window
{
    public WriteRegisterDialog(string context, ushort address, ushort endAddress, ushort value, string title, string valueLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        ContextText.Text = context;
        ValueLabel.Text = valueLabel;
        AddressTextBox.Text = address.ToString();
        EndAddressTextBox.Text = endAddress.ToString();
        ValueTextBox.Text = value.ToString();
        AddressTextBox.SelectAll();
        AddressTextBox.Focus();
    }

    public ushort Address { get; private set; }
    public ushort EndAddress { get; private set; }
    public ushort Value { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!ushort.TryParse(AddressTextBox.Text.Trim(), out var address))
        {
            ValidationText.Text = "Endereco deve ser um numero entre 0 e 65535.";
            return;
        }

        if (!ushort.TryParse(EndAddressTextBox.Text.Trim(), out var endAddress))
        {
            ValidationText.Text = "Endereco final deve ser um numero entre 0 e 65535.";
            return;
        }

        if (endAddress < address)
        {
            ValidationText.Text = "Endereco final deve ser maior ou igual ao endereco inicial.";
            return;
        }

        if (!ushort.TryParse(ValueTextBox.Text.Trim(), out var value))
        {
            ValidationText.Text = "Valor deve ser um numero entre 0 e 65535.";
            return;
        }

        Address = address;
        EndAddress = endAddress;
        Value = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
