using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public partial class MapAddressView : UserControl
{
    private const int PageSize = 96;
    private int _page;
    private bool _refreshQueued;
    private List<MapCellViewModel> _pageCells = [];
    private INotifyCollectionChanged? _observed;
    public event EventHandler<MapPointEventArgs>? PointActivated;
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(MapAddressView), new PropertyMetadata(null, OnSourceChanged));
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public MapAddressView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Observe(); RefreshPage(); };
        Unloaded += (_, _) => { Unobserve(); ClearCells(); };
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MapAddressView)d;
        view._page = 0;
        view.Unobserve();
        if (view.IsLoaded) view.Observe();
        view.RefreshPage();
    }

    private void Observe()
    {
        Unobserve();
        _observed = ItemsSource as INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += Source_Changed;
    }
    private void Unobserve() { if (_observed is not null) _observed.CollectionChanged -= Source_Changed; _observed = null; }
    private void Source_Changed(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { _refreshQueued = false; if (IsLoaded) RefreshPage(); }));
    }
    private void ClearCells() { foreach (var cell in _pageCells) cell.Dispose(); _pageCells.Clear(); }

    private List<object> FilteredPoints()
    {
        var type = (TypeFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        return (ItemsSource?.Cast<object>() ?? []).Where(x => MapCellViewModel.TypeOf(x) == type).OrderBy(MapCellViewModel.AddressOf).ToList();
    }

    private void RefreshPage()
    {
        if (Cells is null) return;
        var points = FilteredPoints();
        var pageCount = Math.Max(1, (points.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        ClearCells();
        _pageCells = points.Skip(_page * PageSize).Take(PageSize).Select(x => new MapCellViewModel(x)).ToList();
        Cells.ItemsSource = _pageCells;
        PageLabel.Text = $"{_page + 1}/{pageCount} · {points.Count} pontos";
        PreviousPage.IsEnabled = _page > 0;
        NextPage.IsEnabled = _page < pageCount - 1;
        EmptyMessage.Visibility = points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { _page = 0; RefreshPage(); }
    private void Previous_Click(object sender, RoutedEventArgs e) { _page--; RefreshPage(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _page++; RefreshPage(); }
    private void Jump_Click(object sender, RoutedEventArgs e)
    {
        if (!ushort.TryParse(AddressSearch.Text, out var address)) { FilterMessage.Text = "Endereço: 0 a 65535."; return; }
        var index = FilteredPoints().FindIndex(x => MapCellViewModel.AddressOf(x) >= address);
        if (index < 0) { FilterMessage.Text = "Endereço fora do mapa."; return; }
        FilterMessage.Text = "";
        _page = index / PageSize;
        RefreshPage();
    }
    private void Cell_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: MapCellViewModel cell }) PointActivated?.Invoke(this, new MapPointEventArgs(cell.Point));
        e.Handled = true;
    }
    private void Cell_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Button { DataContext: MapCellViewModel cell }) return;
        PointActivated?.Invoke(this, new MapPointEventArgs(cell.Point));
        e.Handled = true;
    }
}

public sealed class MapPointEventArgs(object point) : EventArgs { public object Point { get; } = point; }

public sealed class MapCellViewModel : ObservableObject, IDisposable
{
    public object Point { get; }
    private readonly INotifyPropertyChanged? _observable;
    public MapCellViewModel(object point) { Point = point; _observable = point as INotifyPropertyChanged; if (_observable is not null) _observable.PropertyChanged += Point_Changed; }
    public static ushort AddressOf(object point) => point is ServerPointRow server ? server.Address : ((ClientCommunicationPointRow)point).Address;
    public static string TypeOf(object point) => point is ServerPointRow server ? server.TypeLabel : ((ClientCommunicationPointRow)point).Type;
    public string AddressLabel => $"{TypeOf(Point) switch { "Coil" => "COIL", "Discrete Input" => "DI", "Input Register" => "IR", _ => "HR" }} {AddressOf(Point)}";
    public string Name => Point is ServerPointRow server ? server.Name : ((ClientCommunicationPointRow)Point).SourceLine;
    public string Access => Point is ServerPointRow server ? server.Access : ((ClientCommunicationPointRow)Point).Writable ? "R/W" : "R";
    private string Quality => Point is ServerPointRow ? "Local" : ((ClientCommunicationPointRow)Point).Quality;
    public string ValueText => Quality is "Nao lido" or "Não lido" or "" ? "-" : (Point is ServerPointRow server ? server.Value : ((ClientCommunicationPointRow)Point).Value).ToString();
    public string QualityLabel => Quality switch { "Local" => "Memória local", "OK" or "Escrita OK" or "Leitura parada" => Quality, "Nao lido" or "Não lido" or "" => "Não lido", _ => "Falha de leitura" };
    public string QualityColor => Quality switch { "Local" => "#377DAD", "OK" or "Escrita OK" => "#2D9B62", "Leitura parada" => "#A57B22", "Nao lido" or "Não lido" or "" => "#8699A8", _ => "#C34D45" };
    public string Detail => $"{AddressLabel} | {Name} | {Access}\n{Quality}\n{(Point is ServerPointRow server ? server.LastUpdatedAt : ((ClientCommunicationPointRow)Point).LastUpdatedAt)}";
    private void Point_Changed(object? sender, PropertyChangedEventArgs e) { OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(QualityLabel)); OnPropertyChanged(nameof(QualityColor)); OnPropertyChanged(nameof(Detail)); }
    public void Dispose() { if (_observable is not null) _observable.PropertyChanged -= Point_Changed; }
}
