using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public partial class NetworkDataView : UserControl
{
    private readonly ObservableCollection<ProtocolTrafficRow> _protocols = [];
    private readonly ObservableCollection<HostTrafficRow> _hosts = [];
    private readonly ObservableCollection<ConversationTrafficRow> _conversations = [];
    private MainViewModel? _vm;
    private TcpTimelineRow[] _captureSnapshot = [];
    private DateTimeOffset? _snapshotStartedAt;

    public NetworkDataView()
    {
        InitializeComponent();
        ProtocolList.ItemsSource = _protocols;
        HostGrid.ItemsSource = _hosts;
        ConversationGrid.ItemsSource = _conversations;
        DataContextChanged += (_, e) => Attach(e.NewValue as MainViewModel);
        Loaded += (_, _) => { Attach(DataContext as MainViewModel); UpdateCaptureState(); };
        Unloaded += (_, _) => Detach();
        PeriodPicker.SelectedIndex = 1;
    }

    private void Attach(MainViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;
        Detach();
        _vm = vm;
        if (vm is not null) vm.PropertyChanged += ViewModelPropertyChanged;
        UpdateCaptureState();
    }

    private void Detach()
    {
        if (_vm is not null) _vm.PropertyChanged -= ViewModelPropertyChanged;
        _vm = null;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasCompletedNetworkCapture) or nameof(MainViewModel.IsNetworkCaptureRunning))
            UpdateCaptureState();
    }

    private void UpdateCaptureState()
    {
        if (!IsLoaded || _vm is null) return;
        if (_vm.IsNetworkCaptureRunning || !_vm.HasCompletedNetworkCapture)
        {
            _captureSnapshot = [];
            _snapshotStartedAt = _vm.NetworkCaptureStartedAt;
            ClearAnalysis(_vm.IsNetworkCaptureRunning
                ? "Captura ativa. Pare a captura para consolidar a amostra e gerar os gráficos."
                : "Inicie e pare uma captura para gerar a análise de tráfego nesta aba.");
            return;
        }

        if (_snapshotStartedAt != _vm.NetworkCaptureStartedAt || _captureSnapshot.Length == 0)
        {
            _snapshotStartedAt = _vm.NetworkCaptureStartedAt;
            _captureSnapshot = _vm.TcpTimeline
                .Where(row => _snapshotStartedAt is null || row.Timestamp >= _snapshotStartedAt.Value)
                .OrderBy(row => row.Timestamp)
                .ToArray();
        }
        RefreshData();
    }

    private void ClearAnalysis(string message)
    {
        _protocols.Clear(); _hosts.Clear(); _conversations.Clear();
        PacketCount.Text = ByteCount.Text = AveragePackets.Text = AverageBytes.Text = "—";
        PeakPackets.Text = PeakBytes.Text = "—";
        SampleDescription.Text = message;
        Interpretation.Text = message;
        CaptureQuality.Text = "Os contadores e gráficos serão calculados após parar a captura.";
        HostHint.Text = "Sem amostra consolidada.";
        PacketChart.SetSeries([], "pkt/s", "#2878A8", 60);
        ByteChart.SetSeries([], "KB/s", "#2F8B62", 60);
    }

    private void PeriodChanged(object sender, SelectionChangedEventArgs e)
    { if (IsLoaded && _vm?.HasCompletedNetworkCapture == true && !_vm.IsNetworkCaptureRunning) RefreshData(); }

    private int SelectedSeconds => PeriodPicker.SelectedItem is ComboBoxItem { Tag: string tag }
        && int.TryParse(tag, out var seconds) ? seconds : 60;

    private void RefreshData()
    {
        if (_vm is null || !_vm.HasCompletedNetworkCapture || _vm.IsNetworkCaptureRunning) return;
        var all = _captureSnapshot;
        var now = all.LastOrDefault()?.Timestamp ?? DateTimeOffset.Now;
        var selection = SelectedSeconds;
        var selected = selection == 0 ? all
            : all.Where(x => x.Timestamp >= now.AddSeconds(-selection)).ToArray();
        var first = selected.FirstOrDefault()?.Timestamp;
        var last = selected.LastOrDefault()?.Timestamp;
        var duration = first is not null && last is not null ? (last.Value - first.Value).TotalSeconds : 0;
        var packetCount = selected.LongLength;
        var byteCount = selected.Sum(x => (long)x.Length);
        var avgPackets = duration > 0 ? $"{packetCount / duration:0.0} pkt/s" : "—";
        var avgBytes = duration > 0 ? $"{FormatBytesPerSecond(byteCount / duration)}/s" : "—";

        var buckets = selected.GroupBy(x => x.Timestamp.ToUnixTimeSeconds())
            .ToDictionary(x => x.Key, x => (Packets: x.Count(), Bytes: x.Sum(y => (long)y.Length)));
        var completeBuckets = duration >= 1 && first is not null && last is not null
            ? buckets.Where(x => x.Key * 1000 >= first.Value.ToUnixTimeMilliseconds()
                && (x.Key + 1) * 1000 <= last.Value.ToUnixTimeMilliseconds()).ToArray()
            : [];
        var peakPps = completeBuckets.Length == 0 ? 0 : completeBuckets.Max(x => x.Value.Packets);
        var peakBps = completeBuckets.Length == 0 ? 0 : completeBuckets.Max(x => x.Value.Bytes);

        PacketCount.Text = packetCount.ToString("N0", CultureInfo.CurrentCulture);
        ByteCount.Text = FormatBytes(byteCount);
        AveragePackets.Text = avgPackets;
        AverageBytes.Text = avgBytes;
        PeakPackets.Text = completeBuckets.Length == 0 ? "—" : $"{peakPps:N0} pkt/s";
        PeakBytes.Text = completeBuckets.Length == 0 ? "—" : $"{FormatBytesPerSecond(peakBps)}/s";
        var periodText = selection == 0 ? "todos os pacotes ainda retidos (máximo de 2.000)"
            : $"últimos {selection:N0} s; {selected.Length:N0} quadro(s) observados em {duration:0.0} s entre o primeiro e o último";
        SampleDescription.Text = $"Período: {periodText}. Interface: {_vm.SelectedCaptureDevice?.Description ?? "nenhuma selecionada"}. Captura concluída; BPF: {_vm.GeneratedCaptureFilter}.";

        var protocolRows = selected.GroupBy(x => x.Protocol).Select(g => new ProtocolTrafficRow(
                g.Key, g.LongCount(), 100d * g.LongCount() / Math.Max(1, packetCount), g.Sum(x => (long)x.Length)))
            .OrderByDescending(x => x.Packets).ToArray();
        Replace(_protocols, protocolRows);

        var hostStats = new Dictionary<string, HostAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in selected)
        {
            AddHost(hostStats, HostOf(row.SourceHost, row.Source), true, row);
            AddHost(hostStats, HostOf(row.DestinationHost, row.Destination), false, row);
        }
        var hostRows = hostStats.Values.Select(x => new HostTrafficRow(x.Host, x.SentPackets, x.ReceivedPackets,
                x.SentBytes, x.ReceivedBytes, 100d * (x.SentPackets + x.ReceivedPackets) / Math.Max(1, packetCount),
                completeBuckets.Length == 0 ? null : x.Buckets.Where(b => completeBuckets.Any(c => c.Key == b.Key)).Select(b => b.Value).DefaultIfEmpty().Max()))
            .OrderByDescending(x => x.TotalBytes).Take(50).ToArray();
        Replace(_hosts, hostRows);
        HostHint.Text = $"{hostStats.Count:N0} endpoint(s) IP/MAC com tráfego observado. Participação calculada por pacotes que envolveram o endpoint; cada pacote tem origem e destino, então os percentuais entre dispositivos se sobrepõem. Ordenação por volume total recebido + enviado.";

        var conversationStats = new Dictionary<string, ConversationAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in selected)
        {
            var source = string.IsNullOrWhiteSpace(row.Source) ? "(origem indisponível)" : row.Source;
            var destination = string.IsNullOrWhiteSpace(row.Destination) ? "(destino indisponível)" : row.Destination;
            var a = string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) <= 0 ? source : destination;
            var b = a == source ? destination : source;
            var key = $"{a}\u001f{b}";
            if (!conversationStats.TryGetValue(key, out var item))
                conversationStats[key] = item = new ConversationAccumulator(a, b);
            item.Packets++; item.Bytes += row.Length; item.Protocols.Add(row.Protocol);
            var bucket = row.Timestamp.ToUnixTimeSeconds();
            item.Buckets[bucket] = item.Buckets.GetValueOrDefault(bucket) + 1;
        }
        var conversationRows = conversationStats.Values.Select(x => new ConversationTrafficRow(x.EndpointA, x.EndpointB,
                string.Join(", ", x.Protocols.Order(StringComparer.Ordinal)), x.Packets, x.Bytes,
                100d * x.Packets / Math.Max(1, packetCount),
                completeBuckets.Length == 0 ? null : x.Buckets.Where(b => completeBuckets.Any(c => c.Key == b.Key)).Select(b => b.Value).DefaultIfEmpty().Max()))
            .OrderByDescending(x => x.Bytes).Take(200).ToArray();
        Replace(_conversations, conversationRows);

        var packetSeries = BuildSeries(buckets, selection, last ?? now, x => x.Packets);
        var byteSeries = BuildSeries(buckets, selection, last ?? now, x => x.Bytes / 1024d);
        var chartSeconds = Math.Min(selection == 0 ? 60 : selection, 60);
        PacketChart.SetSeries(packetSeries, "pkt/s", "#2878A8", chartSeconds);
        ByteChart.SetSeries(byteSeries, "KB/s", "#2F8B62", chartSeconds);

        if (packetCount == 0)
            Interpretation.Text = "Nenhum quadro correspondente foi observado neste período. Confirme se a captura está ativa, se a interface selecionada transporta esse tráfego e se o BPF inclui os protocolos, IPs e portas relevantes.";
        else if (duration <= 0)
            Interpretation.Text = $"A amostra contém {packetCount:N0} quadro(s), mas cobre menos de duas marcas de tempo distintas; ainda não há intervalo confiável para calcular taxa por segundo.";
        else
        {
            var top = hostRows.FirstOrDefault();
            var topConversation = conversationRows.FirstOrDefault();
            var modbus = selected.LongCount(x => x.Protocol == "Modbus/TCP");
            Interpretation.Text = $"A captura registrou {packetCount:N0} quadros e {FormatBytes(byteCount)} em {duration:0.0} s: média {avgPackets}, {avgBytes}. "
                + (completeBuckets.Length == 0 ? "A janela ainda não contém um segundo completo para reportar pico." : $"Pico em segundo completo: {peakPps:N0} pkt/s e {FormatBytesPerSecond(peakBps)}/s. ")
                + $"Maior participante por volume: {top?.Host ?? "—"} ({top?.ShareText ?? "—"} dos quadros observados). Conversa principal: {topConversation?.EndpointA} ↔ {topConversation?.EndpointB} ({topConversation?.ShareText ?? "—"}). "
                + $"Modbus/TCP identificado estruturalmente: {modbus:N0} quadro(s).";
        }

        var stats = _vm.ReadCaptureStatistics();
        var loss = stats is null ? "Estatísticas do driver: indisponíveis para esta captura."
            : $"Contadores acumulados do driver desde o início da captura: recebidos {stats.Value.Received:N0}, descartados {stats.Value.Dropped:N0}, descartados pela interface {stats.Value.InterfaceDropped:N0}.";
        CaptureQuality.Text = $"{loss}\nFila pendente para a interface: {_vm.QueuedPassivePackets:N0}. Descartes na fila da aplicação desde sua abertura: {_vm.PassivePacketsDropped:N0}. "
            + "A contagem usa quadros entregues à captura e mantidos na lista (limite de 2.000). BPF, limite de retenção, fila e descartes do driver afetam a amostra. "
            + "Bytes/s são a soma dos comprimentos dos quadros observados por tempo amostrado; não são banda útil, utilização percentual do link nem prova de saturação. "
            + "Para confirmar saturação, compare com a velocidade negociada e os contadores de porta do switch/NIC.";
    }

    private static double[] BuildSeries(Dictionary<long, (int Packets, long Bytes)> buckets, int period, DateTimeOffset now, Func<(int Packets, long Bytes), double> select)
    {
        var points = Math.Min(period == 0 ? 60 : period, 60);
        if (buckets.Count > 0)
        {
            var last = now.ToUnixTimeSeconds();
            return Enumerable.Range(0, points)
                .Select(index => select(buckets.GetValueOrDefault(last - points + 1 + index))).ToArray();
        }
        return new double[points];
    }

    private static void AddHost(Dictionary<string, HostAccumulator> values, string host, bool sent, TcpTimelineRow row)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        if (!values.TryGetValue(host, out var item)) values[host] = item = new HostAccumulator(host);
        if (sent) { item.SentPackets++; item.SentBytes += row.Length; }
        else { item.ReceivedPackets++; item.ReceivedBytes += row.Length; }
        var second = row.Timestamp.ToUnixTimeSeconds();
        item.Buckets[second] = item.Buckets.GetValueOrDefault(second) + 1;
        item.PeakPackets = Math.Max(item.PeakPackets, item.Buckets[second]);
    }

    private static string HostOf(string host, string endpoint) => !string.IsNullOrWhiteSpace(host) ? host
        : endpoint.Contains(':') ? endpoint[..endpoint.LastIndexOf(':')] : endpoint;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    { target.Clear(); foreach (var row in rows) target.Add(row); }

    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.00} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes:N0} B";
    private static string FormatBytesPerSecond(double bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:0.00} MB" : $"{bytes / 1024d:0.0} KB";

    private sealed class HostAccumulator(string host)
    {
        public string Host { get; } = host;
        public long SentPackets, ReceivedPackets, SentBytes, ReceivedBytes;
        public long TotalBytes => SentBytes + ReceivedBytes;
        public int PeakPackets;
        public Dictionary<long, int> Buckets { get; } = [];
    }

    private sealed class ConversationAccumulator(string a, string b)
    {
        public string EndpointA { get; } = a;
        public string EndpointB { get; } = b;
        public long Packets, Bytes;
        public HashSet<string> Protocols { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<long, int> Buckets { get; } = [];
    }
}

public sealed record ProtocolTrafficRow(string Protocol, long Packets, double Percent, long Bytes)
{
    public string Summary => $"{Packets:N0} quadro(s) · {Percent:0.0}% · {Bytes:N0} B";
    public double PercentBar => Math.Clamp(Percent, 0, 100);
}

public sealed record HostTrafficRow(string Host, long SentPackets, long ReceivedPackets, long SentBytes, long ReceivedBytes, double Share, int? PeakPackets)
{
    public long TotalBytes => SentBytes + ReceivedBytes;
    public string SentBytesText => Format(SentBytes);
    public string ReceivedBytesText => Format(ReceivedBytes);
    public string ShareText => $"{Share:0.0}%";
    public string PeakText => PeakPackets is int peak ? $"{peak:N0} pkt/s" : "—";
    private static string Format(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.00} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes:N0} B";
}

public sealed record ConversationTrafficRow(string EndpointA, string EndpointB, string Protocols, long Packets, long Bytes, double Share, int? PeakPackets)
{
    public string BytesText => Bytes >= 1024 * 1024 ? $"{Bytes / 1024d / 1024d:0.00} MB" : Bytes >= 1024 ? $"{Bytes / 1024d:0.0} KB" : $"{Bytes:N0} B";
    public string ShareText => $"{Share:0.0}%";
    public string PeakText => PeakPackets is int peak ? $"{peak:N0} pkt/s" : "—";
}

public sealed class TrafficSparkline : FrameworkElement
{
    private double[] _values = [];
    private string _unit = "";
    private int _seconds = 60;
    private Brush _brush = Brushes.SteelBlue;

    public void SetSeries(double[] values, string unit, string color, int seconds)
    {
        _values = values; _unit = unit; _seconds = seconds; _brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth; var h = ActualHeight;
        if (w < 20 || h < 30) return;
        var left = 42d; var right = 5d; var top = 10d; var bottom = 22d;
        var plotW = w - left - right; var plotH = h - top - bottom;
        if (plotW <= 0 || plotH <= 0) return;
        var maximum = _values.Length == 0 ? 0 : _values.Max();
        var scale = maximum > 0 ? maximum : 1d;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 228, 233)), 1);
        var textBrush = new SolidColorBrush(Color.FromRgb(82, 107, 125));
        for (var i = 0; i <= 2; i++)
        {
            var y = top + plotH * i / 2d;
            dc.DrawLine(gridPen, new Point(left, y), new Point(w - right, y));
            DrawLabel(dc, $"{maximum * (2 - i) / 2d:0.#}", new Point(0, y - 7), textBrush, 39);
        }
        DrawLabel(dc, $"−{_seconds} s", new Point(left, h - 18), textBrush, 45);
        DrawLabel(dc, "agora", new Point(w - right - 44, h - 18), textBrush, 44);
        DrawLabel(dc, maximum > 0 ? $"máx. {maximum:0.#} {_unit}" : $"sem amostra de {_unit}", new Point(left + 4, 0), textBrush, Math.Max(80, plotW - 8));
        if (_values.Length < 2 || maximum <= 0) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < _values.Length; i++)
            {
                var point = new Point(left + plotW * i / (_values.Length - 1d), top + plotH * (1 - Math.Clamp(_values[i] / scale, 0, 1)));
                if (i == 0) ctx.BeginFigure(point, false, false); else ctx.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(_brush, 2), geometry);
    }

    private static void DrawLabel(DrawingContext dc, string text, Point point, Brush brush, double width)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, brush, 1d)
        { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, point);
    }
}
