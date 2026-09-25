using System.Windows;

namespace ModbusTcpTroubleshooter.App;

public partial class FullTestScopeDialog : Window
{
    public FullTestScopeDialog(bool enableActiveSubnetScan, int timeoutMs, int concurrency, int observationSeconds)
    {
        InitializeComponent();
        EnableActiveSubnetScan = enableActiveSubnetScan;
        ActiveScanTimeoutMs = timeoutMs;
        ActiveScanConcurrency = concurrency;
        PassiveObservationSeconds = observationSeconds;

        ActiveSubnetScanCheckBox.IsChecked = enableActiveSubnetScan;
        TimeoutTextBox.Text = timeoutMs.ToString();
        ConcurrencyTextBox.Text = concurrency.ToString();
        ObservationTextBox.Text = observationSeconds.ToString();
        UiLocalization.Apply(this);
    }

    public bool EnableActiveSubnetScan { get; private set; }
    public int ActiveScanTimeoutMs { get; private set; }
    public int ActiveScanConcurrency { get; private set; }
    public int PassiveObservationSeconds { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutTextBox.Text, out var timeout) || timeout is < 200 or > 3000)
        {
            ValidationText.Text = "O tempo limite deve estar entre 200 e 3000 ms.";
            return;
        }

        if (!int.TryParse(ConcurrencyTextBox.Text, out var concurrency) || concurrency is < 4 or > 128)
        {
            ValidationText.Text = "A quantidade de sondagens paralelas deve estar entre 4 e 128.";
            return;
        }

        if (!int.TryParse(ObservationTextBox.Text, out var observation) || observation is < 3 or > 60)
        {
            ValidationText.Text = "A janela de observacao deve estar entre 3 e 60 segundos.";
            return;
        }

        EnableActiveSubnetScan = ActiveSubnetScanCheckBox.IsChecked == true;
        ActiveScanTimeoutMs = timeout;
        ActiveScanConcurrency = concurrency;
        PassiveObservationSeconds = observation;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
