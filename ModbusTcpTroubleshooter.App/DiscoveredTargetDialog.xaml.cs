using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace ModbusTcpTroubleshooter.App;

public partial class DiscoveredTargetDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _cancellation = new();
    public ClientConnectionSession? AddedTarget { get; private set; }

    public DiscoveredTargetDialog(MainViewModel viewModel, NetworkDiscoveryRow host)
    {
        InitializeComponent();
        _viewModel = viewModel;
        var suggested = viewModel.SuggestDiscoveredTarget(host);
        AddressBox.Text = suggested.Address;
        PortBox.Text = suggested.Port.ToString();
        UnitBox.Text = suggested.UnitId.ToString();
        DeviceHint.Text = host.IsModbusClientObserved && !host.IsModbusConfirmed && !host.IsModbusObserved
            ? "Este IP foi observado enviando requisicoes Modbus. Isso nao comprova que aceite conexoes como servidor. Confirme a porta antes de cadastrar um alvo."
            : "Revise o endpoint encontrado. Apenas mapas de leitura ativa deste mesmo IP, porta e Unit ID podem ser importados.";
        DiscoveryScopeHint.Text = $"Escopo: enderecos 0 a {viewModel.MapDiscoveryMaxAddress}, blocos de {viewModel.MapDiscoveryBlockSize}; FCs habilitados em Descoberta de mapa. Apenas este endpoint/UID.";
        Closed += (_, _) => _cancellation.Cancel();
        UpdateMapHint();
    }

    private void EndpointChanged(object sender, TextChangedEventArgs e)
    {
        if (_viewModel is not null) UpdateMapHint();
    }

    private void UpdateMapHint()
    {
        var count = byte.TryParse(UnitBox.Text, out var uid)
            ? _viewModel.GetReusableDiscoveredMap($"{AddressBox.Text.Trim()}:{PortBox.Text.Trim()}", uid).Count : 0;
        MapHint.Text = count > 0
            ? $"Mapa disponivel: {count} faixa(s) validada(s) por leitura ativa neste endpoint/UID. Sera importado sem executar nova descoberta."
            : "Nenhum mapa validado para este endpoint/UID. Marque a descoberta abaixo ou cadastre sem mapa para configurar blocos depois.";
        DiscoverMapCheck.IsEnabled = count == 0;
        if (count > 0) DiscoverMapCheck.IsChecked = false;
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressBox.Text.Trim();
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || ip.GetAddressBytes()[0] is 0 or >= 224)
        { FeedbackText.Text = "Informe um endereço IPv4 unicast válido."; return; }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        { FeedbackText.Text = "Porta TCP: 1 a 65535."; return; }
        if (!byte.TryParse(UnitBox.Text, out var uid))
        { FeedbackText.Text = "Unit ID: 0 a 255."; return; }
        if (!int.TryParse(ScanBox.Text, out var scanMs) || scanMs < 100)
        { FeedbackText.Text = "Intervalo de scan: no mínimo 100 ms."; return; }
        var endpoint = $"{ip}:{port}";
        IReadOnlyList<MapDiscoveryRow> map = _viewModel.GetReusableDiscoveredMap(endpoint, uid);
        if (map.Count == 0 && DiscoverMapCheck.IsChecked != true
            && MessageBox.Show(this, "Nenhum mapa validado para este endpoint/UID. Cadastrar sem blocos de leitura?",
                "Alvo sem mapa", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        AddButton.IsEnabled = false;
        SettingsGrid.IsEnabled = false;
        DiscoverMapCheck.IsEnabled = false;
        try
        {
            if (map.Count == 0 && DiscoverMapCheck.IsChecked == true)
            {
                FeedbackText.Text = "Lendo o mapa neste endpoint; nenhuma escrita sera enviada...";
                map = await _viewModel.DiscoverMapForTargetAsync(ip.ToString(), port, uid, _cancellation.Token);
                if (map.Count == 0)
                {
                    FeedbackText.Text = "Nenhuma faixa respondeu com leitura valida. Confira IP, porta, Unit ID e escopo. Desmarque a descoberta se quiser cadastrar sem mapa.";
                    return;
                }
            }
            AddedTarget = _viewModel.AddDiscoveredTarget(ip.ToString(), port, uid, scanMs, map);
            DialogResult = true;
        }
        catch (OperationCanceledException) { FeedbackText.Text = "Descoberta cancelada; nenhum alvo foi cadastrado."; }
        catch (Exception ex) { FeedbackText.Text = ex.Message; }
        finally
        {
            AddButton.IsEnabled = true;
            SettingsGrid.IsEnabled = true;
            UpdateMapHint();
        }
    }
}
