using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ModbusTcpTroubleshooter.App;

public sealed class TopologyDiagram : FrameworkElement
{
    public static readonly DependencyProperty LinksProperty = DependencyProperty.Register(nameof(Links),
        typeof(IEnumerable<TopologyLink>), typeof(TopologyDiagram), new FrameworkPropertyMetadata(null, Changed));
    public static readonly DependencyProperty DevicesProperty = DependencyProperty.Register(nameof(Devices),
        typeof(IEnumerable<NetworkDiscoveryRow>), typeof(TopologyDiagram), new FrameworkPropertyMetadata(null, Changed));
    public static readonly DependencyProperty NeighborsProperty = DependencyProperty.Register(nameof(Neighbors),
        typeof(IEnumerable<NeighborAdvertisement>), typeof(TopologyDiagram), new FrameworkPropertyMetadata(null, Changed));
    public static readonly DependencyProperty ServerModeProperty = DependencyProperty.Register(nameof(ServerMode),
        typeof(bool), typeof(TopologyDiagram), new FrameworkPropertyMetadata(false, Changed));

    public IEnumerable<TopologyLink>? Links { get => (IEnumerable<TopologyLink>?)GetValue(LinksProperty); set => SetValue(LinksProperty, value); }
    public IEnumerable<NetworkDiscoveryRow>? Devices { get => (IEnumerable<NetworkDiscoveryRow>?)GetValue(DevicesProperty); set => SetValue(DevicesProperty, value); }
    public IEnumerable<NeighborAdvertisement>? Neighbors { get => (IEnumerable<NeighborAdvertisement>?)GetValue(NeighborsProperty); set => SetValue(NeighborsProperty, value); }
    public bool ServerMode { get => (bool)GetValue(ServerModeProperty); set => SetValue(ServerModeProperty, value); }

    private static readonly Brush Ink = ColorBrush("#18354B");
    private static readonly Brush Muted = ColorBrush("#587083");
    private static readonly Brush Green = ColorBrush("#27875C");
    private static readonly Brush Blue = ColorBrush("#3289B8");
    private static readonly Brush Amber = ColorBrush("#B48036");
    private static readonly Brush Border = ColorBrush("#CAD8E2");

    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        var view = (TopologyDiagram)source;
        if (e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= view.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged next) next.CollectionChanged += view.CollectionChanged;
        view.InvalidateMeasure();
        view.InvalidateVisual();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private ModbusTopologyOverview Overview() => ModbusTopologyOverview.Build(Devices ?? [], Links ?? [], Neighbors ?? [], ServerMode);

    protected override Size MeasureOverride(Size availableSize)
    {
        var peers = Math.Max(1, Overview().Peers.Count);
        return new Size(Math.Max(920, double.IsInfinity(availableSize.Width) ? 920 : availableSize.Width),
            Math.Max(350, 165 + peers * 100));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var overview = Overview();
        var width = RenderSize.Width;
        dc.DrawRectangle(ColorBrush("#F5F8FA"), null, new Rect(RenderSize));
        DrawText(dc, "ESTAÇÃO LOCAL", 24, 26, 11, 230, Muted, true);
        DrawText(dc, "REDE / INFRAESTRUTURA", width / 2 - 125, 26, 11, 250, Muted, true);
        DrawText(dc, "PARTICIPANTES MODBUS", width - 300, 26, 11, 270, Muted, true);
        DrawText(dc, "Enlaces pontilhados representam relação lógica; não comprovam portas ou cabeamento.", 24, 52, 11, width - 48, Muted);

        var peerCount = Math.Max(1, overview.Peers.Count);
        var firstY = 105d;
        var centerY = firstY + (peerCount - 1) * 50 + 44;
        var local = new Rect(24, centerY - 49, 230, 98);
        var middle = new Rect(width / 2 - 125, centerY - 60, 250, 120);
        var rightX = width - 300;
        var connector = new Pen(Border, 1.7) { DashStyle = DashStyles.Dash };
        dc.DrawLine(connector, new Point(local.Right, centerY), new Point(middle.Left, centerY));
        for (var i = 0; i < overview.Peers.Count; i++)
            dc.DrawLine(connector, new Point(middle.Right, centerY), new Point(rightX, firstY + i * 100 + 44));

        DrawCard(dc, local, Blue);
        DrawBadge(dc, local.X + 17, local.Y + 17, "PC", Blue);
        DrawText(dc, "ESTE COMPUTADOR", local.X + 58, local.Y + 14, 11, 165, Muted, true);
        DrawText(dc, overview.LocalRole, local.X + 58, local.Y + 35, 14, 165, Ink, true);
        DrawText(dc, "Interface de captura / teste", local.X + 17, local.Y + 69, 11, 205, Muted);

        DrawCard(dc, middle, overview.InfrastructureAnnounced ? Amber : Border);
        DrawBadge(dc, middle.X + 17, middle.Y + 17, "L2", overview.InfrastructureAnnounced ? Amber : Muted);
        DrawText(dc, overview.InfrastructureTitle, middle.X + 58, middle.Y + 14, 13, 180, Ink, true);
        DrawText(dc, overview.InfrastructureDetail, middle.X + 17, middle.Y + 53, 11, 220, Muted);
        DrawText(dc, $"{overview.OtherHosts} outros hosts (não exibidos)", middle.X + 17, middle.Y + 94, 10, 220, Muted);

        if (overview.Peers.Count == 0)
        {
            var empty = new Rect(rightX, firstY, 270, 88);
            DrawCard(dc, empty, Border);
            DrawText(dc, "Nenhum participante Modbus remoto", empty.X + 14, empty.Y + 17, 13, 245, Ink, true);
            DrawText(dc, "Execute o teste ou confira a captura.", empty.X + 14, empty.Y + 46, 11, 245, Muted);
        }
        for (var i = 0; i < overview.Peers.Count; i++)
        {
            var peer = overview.Peers[i];
            var card = new Rect(rightX, firstY + i * 100, 270, 88);
            var color = peer.Confirmed ? Green : Blue;
            DrawCard(dc, card, color);
            DrawBadge(dc, card.X + 15, card.Y + 15, peer.Confirmed ? "SV" : "CL", color);
            DrawText(dc, peer.Ip, card.X + 55, card.Y + 10, 14, 207, Ink, true);
            DrawText(dc, peer.Role, card.X + 55, card.Y + 31, 11, 207, Muted);
            DrawText(dc, peer.Ports, card.X + 55, card.Y + 48, 10.5, 207, Muted);
            DrawText(dc, peer.Evidence, card.X + 15, card.Y + 69, 10.5, 245, color);
        }
        if (overview.HiddenPeers > 0)
            DrawText(dc, $"+ {overview.HiddenPeers} participantes adicionais na tabela Dispositivos / rede",
                rightX, firstY + overview.Peers.Count * 100, 11, 270, Muted);
        DrawText(dc, "Verde: resposta Modbus validada  ·  Azul: observado  ·  Âmbar: infraestrutura anunciada",
            24, RenderSize.Height - 30, 11, width - 48, Muted);
    }

    private static void DrawCard(DrawingContext dc, Rect rect, Brush accent)
    {
        dc.DrawRoundedRectangle(Brushes.White, new Pen(Border, 1), rect, 7, 7);
        dc.DrawRoundedRectangle(accent, null, new Rect(rect.X, rect.Y, 4, rect.Height), 2, 2);
    }

    private void DrawBadge(DrawingContext dc, double x, double y, string label, Brush brush)
    {
        dc.DrawEllipse(brush, null, new Point(x + 16, y + 16), 16, 16);
        DrawText(dc, label, x + 4, y + 8, 11, 24, Brushes.White, true);
    }

    private void DrawText(DrawingContext dc, string value, double x, double y, double size, double width, Brush brush, bool bold = false)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = width, MaxTextHeight = 32, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new Point(x, y));
    }

    private static Brush ColorBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
