using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ModbusTcpTroubleshooter.App;

public partial class DeviceSettingsView : UserControl
{
    private MainViewModel? _vm;
    private ClientConnectionSession? _session;
    private bool _client;
    private bool _loading, _restoring, _dirty, _applying;
    private string _snapshot = "";

    private string FormSignature => string.Join("|", NameInput.Text, AddressInput.Text, PortInput.Text,
        UnitInput.Text, ScanInput.Text, PersistentInput.IsChecked);

    public bool ConfirmLeave()
    {
        if (_applying) return false;
        if (!_dirty) return true;
        var discard = MessageBox.Show(Window.GetWindow(this),
            "Existem alterações não aplicadas neste dispositivo. Descartar e continuar?\nPara salvar, cancele e use Aplicar configuração.",
            "Alterações pendentes", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
        if (discard) { _dirty = false; Reload(); }
        return discard;
    }

    private void InputChanged(object sender, TextChangedEventArgs e) => UpdateForm();
    private void PersistentChanged(object sender, RoutedEventArgs e) => UpdateForm();
    private void UpdateForm()
    {
        if (_loading || ApplyButton is null || _vm is null) return;
        _dirty = FormSignature != _snapshot;
        Feedback.Text = _dirty ? "Alterações ainda não aplicadas." : "";
        UpdateLock();
    }

    private bool ValidateFields()
    {
        NameError.Text = string.IsNullOrWhiteSpace(NameInput.Text) ? "Informe um nome." : "";
        AddressError.Text = !System.Net.IPAddress.TryParse(AddressInput.Text.Trim(), out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ? "Informe um IPv4 válido, por exemplo 172.27.30.84." : "";
        PortError.Text = !int.TryParse(PortInput.Text, out var port) || port is < 1 or > 65535 ? "Porta: inteiro entre 1 e 65535." : "";
        UnitError.Text = !byte.TryParse(UnitInput.Text, out _) ? "ID da unidade: inteiro entre 0 e 255." : "";
        ScanError.Text = _client && (!int.TryParse(ScanInput.Text, out var scan) || scan is < 100 or > 86400000)
            ? "Intervalo: inteiro entre 100 e 86400000 ms." : "";
        foreach (var (input, error) in new[] { (NameInput, NameError), (AddressInput, AddressError), (PortInput, PortError), (UnitInput, UnitError), (ScanInput, ScanError) })
        {
            input.BorderBrush = error.Text.Length > 0 ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.SlateGray;
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return new[] { NameError, AddressError, PortError, UnitError, ScanError }.All(x => x.Text.Length == 0);
    }

    public DeviceSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= ViewModelChanged;
            _vm = e.NewValue as MainViewModel;
            if (_vm is not null) _vm.PropertyChanged += ViewModelChanged;
            Reload();
        };
        Loaded += (_, _) => Reload();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring) return;
        if (e.PropertyName is nameof(MainViewModel.SelectedClientSession) or nameof(MainViewModel.SelectedMode))
        {
            if (_dirty && !ConfirmLeave() && _vm is not null && _session is not null)
            {
                _restoring = true;
                try { _vm.SelectedClientSession = _session; _vm.SelectedMode = _client ? "Client" : "Server"; }
                finally { _restoring = false; }
                Dispatcher.BeginInvoke(() => { if (_vm is not null) _vm.SelectedClientMapRow = _vm.SelectedClientSession.Rows.FirstOrDefault(); });
                return;
            }
            _dirty = false;
            Reload();
        }
        else if (e.PropertyName is nameof(MainViewModel.CanConfigureClient) or nameof(MainViewModel.CanConfigureServer)
            or nameof(MainViewModel.IsDeviceProbeRunning)) UpdateLock();
    }

    public void Reload()
    {
        if (_vm is null || _dirty || _applying) return;
        _loading = true;
        _client = _vm.IsClientMode;
        _session = _vm.SelectedClientSession;
        NameInput.Text = _client ? _session.Name : _vm.ServerName;
        AddressInput.Text = _client ? _session.Address : _vm.LocalIp;
        PortInput.Text = (_client ? _session.Port : _vm.ServerPort).ToString();
        UnitInput.Text = (_client ? _session.UnitId : _vm.ServerUnitId).ToString();
        ScanInput.Text = _session.ScanRateMs.ToString();
        PersistentInput.IsChecked = _session.KeepConnectionOpen;
        RoleLabel.Text = _client ? "Servidor-alvo da estacao cliente. Estes parametros afetam somente este alvo."
            : "Servidor simulado local. Estes parametros definem o endpoint exposto aos clientes da rede.";
        AddressLabel.Content = _client ? "Endereco _IP do alvo" : "Endereco _IP local";
        foreach (var field in new UIElement[] { ScanLabel, ScanFields, PersistentInput })
            field.Visibility = _client ? Visibility.Visible : Visibility.Collapsed;
        Feedback.Text = "";
        _snapshot = FormSignature;
        _loading = false;
        UpdateLock();
    }

    private void UpdateLock()
    {
        if (_vm is null) return;
        Fields.IsEnabled = !_applying && !_vm.IsDeviceProbeRunning
            && (_client ? _vm.CanConfigureClient : _vm.CanConfigureServer);
        ApplyButton.IsEnabled = Fields.IsEnabled && ValidateFields() && _dirty;
        LockMessage.Text = Fields.IsEnabled ? "Edite os campos e aplique para confirmar."
            : "Configuracao bloqueada: pare a comunicacao deste dispositivo e aguarde os testes.";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _session is null || !ValidateFields()) return;
        if (!int.TryParse(PortInput.Text, out var port) || !byte.TryParse(UnitInput.Text, out var unit)
            || _client && !int.TryParse(ScanInput.Text, out _))
        {
            Feedback.Text = "Porta, ID da unidade e intervalo devem ser numeros inteiros validos.";
            return;
        }
        var vm = _vm;
        var session = _session;
        var client = _client;
        ApplyButton.IsEnabled = false;
        _applying = true;
        try
        {
            await vm.ConfigureDeviceAsync(client, session, NameInput.Text, AddressInput.Text.Trim(), port, unit,
                client ? int.Parse(ScanInput.Text) : 1000, PersistentInput.IsChecked == true);
            _applying = false;
            _dirty = false;
            Reload();
            Feedback.Text = "Configuracao aplicada.";
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
        finally { _applying = false; UpdateLock(); }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) { if (!ConfirmLeave()) return; _dirty = false; Reload(); }
}
