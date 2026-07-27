using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class WriteRegisterDialog : Window
{
    public WriteRegisterDialog(string context, ushort address, ushort value)
    {
        InitializeComponent();
        ContextText.Text = context;
        AddressTextBox.Text = address.ToString();
        ValueTextBox.Text = value.ToString();
        AddressTextBox.SelectAll();
        AddressTextBox.Focus();
    }

    public ushort Address { get; private set; }
    public ushort Value { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!ushort.TryParse(AddressTextBox.Text.Trim(), out var address))
        {
            ValidationText.Text = "Endereco deve ser um numero entre 0 e 65535.";
            return;
        }

        if (!ushort.TryParse(ValueTextBox.Text.Trim(), out var value))
        {
            ValidationText.Text = "Valor deve ser um numero entre 0 e 65535.";
            return;
        }

        Address = address;
        Value = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
