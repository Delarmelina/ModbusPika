using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModbusTcpTroubleshooter.App;

public static partial class ReportTrafficCharts
{
    public static byte[] Render(TcpTimelineRow[] rows)
    {
        var last = rows[^1].Timestamp.ToUnixTimeSeconds();
        var seconds = Math.Min(60, Math.Max(2, (int)Math.Ceiling((rows[^1].Timestamp - rows[0].Timestamp).TotalSeconds) + 1));
        var buckets = rows.GroupBy(x => x.Timestamp.ToUnixTimeSeconds())
            .ToDictionary(g => g.Key, g => (Packets: g.Count(), Bytes: g.Sum(x => (long)x.Length)));
        var packets = Enumerable.Range(0, seconds).Select(i => (double)buckets.GetValueOrDefault(last - seconds + 1 + i).Packets).ToArray();
        var bytes = Enumerable.Range(0, seconds).Select(i => buckets.GetValueOrDefault(last - seconds + 1 + i).Bytes / 1024d).ToArray();
        var panel = new Grid { Background = Brushes.White, Width = 1200, Height = 300 };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        AddChart(panel, 0, "Pacotes por segundo (pkt/s)", packets, "pkt/s", "#2878A8", seconds);
        AddChart(panel, 1, "Volume por segundo (KiB/s)", bytes, "KiB/s", "#2F8B62", seconds);
        panel.Measure(new Size(1200, 300));
        panel.Arrange(new Rect(0, 0, 1200, 300));
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1800, 450, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void AddChart(Grid panel, int column, string title, double[] values, string unit, string color, int seconds)
    {
        var container = new DockPanel { Margin = new Thickness(15) };
        var label = new TextBlock { Text = title, FontFamily = new FontFamily("Segoe UI"), FontSize = 16,
            FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(24, 53, 75)), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(label, Dock.Top);
        container.Children.Add(label);
        var chart = new TrafficSparkline { EndLabel = "fim" };
        chart.SetSeries(values, unit, color, seconds);
        container.Children.Add(chart);
        Grid.SetColumn(container, column);
        panel.Children.Add(container);
    }
}
