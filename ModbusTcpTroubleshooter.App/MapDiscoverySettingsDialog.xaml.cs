using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class MapDiscoverySettingsDialog : Window
{
    public MapDiscoverySettingsDialog(bool enableDiscovery, bool unitSweep, int unitStart, int unitEnd, int maxAddress, int blockSize, bool fc01, bool fc02, bool fc03, bool fc04, bool pointFallback)
    {
        InitializeComponent();
        EnableDiscovery = enableDiscovery;
        EnableUnitSweep = unitSweep;
        UnitIdStart = unitStart;
        UnitIdEnd = unitEnd;
        MaxAddress = maxAddress;
        BlockSize = blockSize;
        Fc01 = fc01;
        Fc02 = fc02;
        Fc03 = fc03;
        Fc04 = fc04;
        PointFallback = pointFallback;

        EnableDiscoveryCheckBox.IsChecked = enableDiscovery;
        UnitSweepCheckBox.IsChecked = unitSweep;
        UnitStartTextBox.Text = unitStart.ToString();
        UnitEndTextBox.Text = unitEnd.ToString();
        MaxAddressTextBox.Text = maxAddress.ToString();
        BlockSizeTextBox.Text = blockSize.ToString();
        Fc01CheckBox.IsChecked = fc01;
        Fc02CheckBox.IsChecked = fc02;
        Fc03CheckBox.IsChecked = fc03;
        Fc04CheckBox.IsChecked = fc04;
        PointFallbackCheckBox.IsChecked = pointFallback;
        UiLocalization.Apply(this);
    }

    public bool EnableDiscovery { get; private set; }
    public bool EnableUnitSweep { get; private set; }
    public int UnitIdStart { get; private set; }
    public int UnitIdEnd { get; private set; }
    public int MaxAddress { get; private set; }
    public int BlockSize { get; private set; }
    public bool Fc01 { get; private set; }
    public bool Fc02 { get; private set; }
    public bool Fc03 { get; private set; }
    public bool Fc04 { get; private set; }
    public bool PointFallback { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxAddressTextBox.Text, out var maxAddress) || maxAddress is < 0 or > 65535)
        {
            ValidationText.Text = "O endereco maximo deve estar entre 0 e 65535.";
            return;
        }

        if (!int.TryParse(BlockSizeTextBox.Text, out var blockSize) || blockSize is < 1 or > 120)
        {
            ValidationText.Text = "O tamanho do bloco deve estar entre 1 e 120.";
            return;
        }

        if (!int.TryParse(UnitStartTextBox.Text, out var unitStart) || unitStart is < 0 or > 255 ||
            !int.TryParse(UnitEndTextBox.Text, out var unitEnd) || unitEnd is < 0 or > 255)
        {
            ValidationText.Text = "Os IDs de unidade devem estar entre 0 e 255.";
            return;
        }

        if (unitStart > unitEnd)
        {
            ValidationText.Text = "O ID de unidade inicial nao pode ser maior que o ID final.";
            return;
        }

        if (Fc01CheckBox.IsChecked != true && Fc02CheckBox.IsChecked != true &&
            Fc03CheckBox.IsChecked != true && Fc04CheckBox.IsChecked != true)
        {
            ValidationText.Text = "Selecione pelo menos uma funcao de leitura.";
            return;
        }

        EnableDiscovery = EnableDiscoveryCheckBox.IsChecked == true;
        EnableUnitSweep = UnitSweepCheckBox.IsChecked == true;
        UnitIdStart = unitStart;
        UnitIdEnd = unitEnd;
        MaxAddress = maxAddress;
        BlockSize = blockSize;
        Fc01 = Fc01CheckBox.IsChecked == true;
        Fc02 = Fc02CheckBox.IsChecked == true;
        Fc03 = Fc03CheckBox.IsChecked == true;
        Fc04 = Fc04CheckBox.IsChecked == true;
        PointFallback = PointFallbackCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
