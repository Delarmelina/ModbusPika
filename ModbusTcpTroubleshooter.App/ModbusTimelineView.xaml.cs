using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed class TrafficDirectionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value switch
    {
        TrafficDirection.ClientToServer => "Requisição",
        TrafficDirection.ServerToClient => "Resposta",
        _ => "Sistema"
    };
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}

public partial class ModbusTimelineView : UserControl
{
    private MainViewModel? _vm;
    private ICollectionView? _view;
    private readonly Dictionary<TrafficEvent, TrafficEvent> _pairs = [];

    public ModbusTimelineView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as MainViewModel);
        Loaded += (_, _) => Attach(DataContext as MainViewModel);
        Unloaded += (_, _) => Detach();
    }

    private void Detach()
    {
        if (_vm is not null) _vm.Traffic.CollectionChanged -= TrafficChanged;
        _vm = null;
    }

    private void Attach(MainViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        Detach(); _vm = vm;
        if (vm is null) return;
        _view = new ListCollectionView(vm.Traffic) { Filter = Matches };
        Messages.ItemsSource = _view;
        vm.Traffic.CollectionChanged += TrafficChanged;
        Refresh();
    }

    private void TrafficChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void FilterChanged(object sender, RoutedEventArgs e) { if (_view is not null) Refresh(); }

    private void Refresh()
    {
        _pairs.Clear();
        if (_vm is null || _view is null) return;
        var pending = new Dictionary<(string Session, string Endpoint, ushort Tid, byte Uid), TrafficEvent>();
        foreach (var item in _vm.Traffic.OrderBy(x => x.Timestamp))
        {
            if (item.TransactionId is not ushort tid || item.UnitId is not byte uid) continue;
            var key = (item.SessionId, item.Endpoint, tid, uid);
            if (item.Direction == TrafficDirection.ClientToServer) pending[key] = item;
            else if (item.Direction == TrafficDirection.ServerToClient && pending.Remove(key, out var request)
                && (item.FunctionCode.GetValueOrDefault() & 0x7F) == (request.FunctionCode.GetValueOrDefault() & 0x7F))
            { _pairs[request] = item; _pairs[item] = request; }
        }
        _view.Refresh();
        FilterFeedback.Text = !ValidNumber(UnitFilter.Text, 255) || !ValidNumber(FunctionFilter.Text, 127)
            ? "Filtro inválido: Unit ID de 0 a 255; FC de 0 a 127. A lista permanece vazia até corrigir."
            : $"{_view.Cast<object>().Count()} de {_vm.Traffic.Count} mensagens retidas. Filtros apenas de visualização; não alteram a captura. Erros incluem exceções e mensagens de falha/timeout.";
        ShowDetails();
    }

    private static bool ValidNumber(string text, int maximum) => string.IsNullOrWhiteSpace(text)
        || int.TryParse(text, out var value) && value >= 0 && value <= maximum;
    private static bool ExceptionResponse(TrafficEvent item) => item.FunctionCode is >= 128
        || item.Direction == TrafficDirection.ServerToClient && item.Summary.Contains("exception", StringComparison.OrdinalIgnoreCase);
    private bool HasError(TrafficEvent item) => ExceptionResponse(item)
        || _pairs.TryGetValue(item, out var other) && ExceptionResponse(other)
        || new[] { "falha", "timeout", "erro", "invalida", "inválida" }.Any(x => item.Summary.Contains(x, StringComparison.OrdinalIgnoreCase));

    private bool Matches(object value)
    {
        if (value is not TrafficEvent item || !ValidNumber(UnitFilter.Text, 255) || !ValidNumber(FunctionFilter.Text, 127)) return false;
        var session = SessionFilter.Text.Trim();
        return (session.Length == 0 || item.Origin.Contains(session, StringComparison.OrdinalIgnoreCase) || item.Endpoint.Contains(session, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(UnitFilter.Text) || item.UnitId == int.Parse(UnitFilter.Text))
            && (string.IsNullOrWhiteSpace(FunctionFilter.Text) || (item.FunctionCode.GetValueOrDefault() & 0x7F) == int.Parse(FunctionFilter.Text) && item.FunctionCode is not null)
            && (ErrorsOnly.IsChecked != true || HasError(item));
    }

    private void MessageSelected(object sender, SelectionChangedEventArgs e) => ShowDetails();
    private void ShowDetails()
    {
        if (Messages.SelectedItem is not TrafficEvent item)
        {
            TransactionDetails.Text = "Selecione uma mensagem para investigar requisição, resposta, tempo e exceção.";
            RawDetails.Text = ""; return;
        }
        _pairs.TryGetValue(item, out var pair);
        var request = item.Direction == TrafficDirection.ClientToServer ? item : pair?.Direction == TrafficDirection.ClientToServer ? pair : null;
        var response = item.Direction == TrafficDirection.ServerToClient ? item : pair?.Direction == TrafficDirection.ServerToClient ? pair : null;
        var timing = request is not null && response is not null ? $"{(response.Timestamp - request.Timestamp).TotalMilliseconds:0.0} ms entre eventos correlacionados" : "indisponível: par não presente na janela retida; não comprova timeout";
        var outcome = response is null ? "Sem resposta correlacionada nesta lista" : ExceptionResponse(response) ? "Exceção Modbus: " + response.Summary : "Resposta observada: " + response.Summary;
        TransactionDetails.Text = $"Sessão: {item.Origin} · {item.Endpoint}\nTID: {item.TransactionId} · Unit ID: {item.UnitId} · FC: {item.FunctionCode}\n"
            + $"Requisição: {request?.Summary ?? "não presente nesta janela"}\nResultado: {outcome}\nTempo: {timing}\n"
            + "Correlação por sessão, endpoint, TID, UID e função. Tempo entre eventos não representa exclusivamente o processamento do PLC.";
        if (item.Direction == TrafficDirection.System) TransactionDetails.Text = $"Evento da sessão {item.Origin}: {item.Summary}";
        RawDetails.Text = $"Requisição:\n{request?.Hex ?? "—"}\n\nResposta:\n{response?.Hex ?? "—"}";
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    { SessionFilter.Clear(); UnitFilter.Clear(); FunctionFilter.Clear(); ErrorsOnly.IsChecked = false; Refresh(); }
}
