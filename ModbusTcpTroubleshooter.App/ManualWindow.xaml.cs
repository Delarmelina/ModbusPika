using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Windows.Automation;
using System.Xml.Linq;

namespace ModbusTcpTroubleshooter.App;

public partial class ManualWindow : Window
{
    private readonly ManualCatalog _catalog = ManualCatalog.Load();
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    public ManualTopic CurrentTopic { get; private set; }

    public ManualWindow(string? topicId = null)
    {
        InitializeComponent();
        foreach (var section in _catalog.Sections)
        {
            var parent = new TreeViewItem { Header = new TextBlock { Text = section.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 }, IsExpanded = true, FontWeight = FontWeights.SemiBold };
            foreach (var topic in section.Topics)
                parent.Items.Add(new TreeViewItem { Header = new TextBlock { Text = topic.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 210 }, Tag = topic, FontWeight = FontWeights.Normal });
            TopicTree.Items.Add(parent);
        }
        CurrentTopic = _catalog.Get(topicId);
        ShowTopic(CurrentTopic.Id);
    }

    public void ShowTopic(string? id) => Navigate(id, true);

    private void Navigate(string? id, bool remember)
    {
        CurrentTopic = _catalog.Get(id);
        if (remember && (_historyIndex < 0 || _history[_historyIndex] != CurrentTopic.Id))
        {
            if (_historyIndex + 1 < _history.Count) _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(CurrentTopic.Id);
            _historyIndex = _history.Count - 1;
        }
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex + 1 < _history.Count;
        PageLocation.Text = CurrentTopic.Title;
        ArticlePanel.Children.Clear();
        AddText(CurrentTopic.Section.ToUpperInvariant(), 11, FontWeights.SemiBold, "#347EA8", 0, 0, 0, 5);
        AddText(CurrentTopic.Title, 25, FontWeights.SemiBold, "#18354B", 0, 0, 0, 14);
        foreach (var element in CurrentTopic.Content.Elements()) RenderElement(element);
        FooterText.Text = $"{CurrentTopic.Section}  >  {CurrentTopic.Title}   |   {_catalog.Topics.Count} tópicos";
        ArticleScroll.ScrollToTop();
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) SelectTreeTopic(CurrentTopic.Id);
    }

    private void RenderElement(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "p": AddText(element.Value, 13, FontWeights.Normal, "#243D4E", 0, 0, 0, 10); break;
            case "h": AddText(element.Value, 16, FontWeights.SemiBold, "#18354B", 0, 12, 0, 7); break;
            case "ul":
                foreach (var item in element.Elements("li")) AddText("•  " + item.Value, 13, FontWeights.Normal, "#243D4E", 13, 0, 0, 6);
                break;
            case "ol":
                var number = 1;
                foreach (var item in element.Elements("li")) AddText($"{number++}.  {item.Value}", 13, FontWeights.Normal, "#243D4E", 0, 0, 0, 8);
                break;
            case "table": RenderTable(element); break;
            case "example":
                var example = new Border { Background = Brush("#F4F7F9"), BorderBrush = Brush("#CBD5DC"), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 5, 0, 14) };
                example.Child = new TextBlock { Text = element.Value.Trim(), FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#243D4E"), LineHeight = 19 };
                ArticlePanel.Children.Add(example);
                break;
            case "link":
                var target = (string?)element.Attribute("topic");
                var link = new Button { Content = new TextBlock { Text = "Consultar: " + element.Value.Trim(), TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(10, 5, 10, 5), ToolTip = _catalog.Get(target).Title };
                link.Click += (_, _) => ShowTopic(target);
                ArticlePanel.Children.Add(link);
                break;
            case "image":
                var source = new BitmapImage(new Uri("pack://application:,,,/ModbusTcpTroubleshooter.App;component/" + (string?)element.Attribute("src"), UriKind.Absolute));
                var picture = new Image { Source = source, Stretch = Stretch.Uniform, MaxWidth = source.PixelWidth, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
                AutomationProperties.SetName(picture, element.Value.Trim());
                ArticlePanel.Children.Add(picture);
                AddText(element.Value, 11, FontWeights.Normal, "#657785", 0, 0, 0, 12);
                break;
            case "external":
                var address = (string?)element.Attribute("url");
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    var reference = new Button { Content = new TextBlock { Text = element.Value.Trim(), TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(10, 5, 10, 5), ToolTip = address };
                    reference.Click += (_, _) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    ArticlePanel.Children.Add(reference);
                }
                break;
            case "note":
                var caution = (string?)element.Attribute("kind") == "safety";
                var box = new Border { Background = Brush(caution ? "#FFF4DC" : "#EAF3F8"), BorderBrush = Brush(caution ? "#D9B764" : "#A8C8DB"), BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 5, 0, 14) };
                box.Child = new TextBlock { Text = element.Value.Trim(), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#243D4E"), LineHeight = 20 };
                ArticlePanel.Children.Add(box);
                break;
        }
    }

    private void RenderTable(XElement element)
    {
        var rows = element.Elements("row").ToArray();
        var headers = element.Element("head")?.Elements("cell").ToArray() ?? [];
        var columns = headers.Length;
        if (columns == 0) return;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 16) };
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column == 0 ? 1 : 2, GridUnitType.Star) });
        for (var row = 0; row <= rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cells = row == 0 ? headers : rows[row - 1].Elements("cell").ToArray();
            for (var column = 0; column < columns; column++)
            {
                var text = new TextBlock { Text = cells[column].Value.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19, Foreground = Brush("#243D4E"), FontWeight = row == 0 ? FontWeights.SemiBold : FontWeights.Normal };
                var border = new Border { Child = text, Padding = new Thickness(9, 7, 9, 8), BorderBrush = Brush("#CBD5DC"), BorderThickness = new Thickness(0, 0, column + 1 == columns ? 0 : 1, 1), Background = Brush(row == 0 ? "#E5EDF2" : row % 2 == 0 ? "#F6F9FB" : "#FFFFFF") };
                Grid.SetColumn(border, column); Grid.SetRow(border, row);
                grid.Children.Add(border);
            }
        }
        ArticlePanel.Children.Add(grid);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex > 0) Navigate(_history[--_historyIndex], false);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex + 1 < _history.Count) Navigate(_history[++_historyIndex], false);
    }

    private void AddText(string value, double size, FontWeight weight, string color, double left, double top, double right, double bottom) =>
        ArticlePanel.Children.Add(new TextBlock { Text = value.Trim(), TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight, Foreground = Brush(color), Margin = new Thickness(left, top, right, bottom), LineHeight = size + 7 });

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private void SelectTreeTopic(string id)
    {
        foreach (TreeViewItem parent in TopicTree.Items)
            foreach (TreeViewItem child in parent.Items)
                if (child.Tag is ManualTopic topic && topic.Id == id)
                {
                    child.IsSelected = true;
                    child.BringIntoView();
                    return;
                }
    }

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: ManualTopic topic } && topic.Id != CurrentTopic.Id) ShowTopic(topic.Id);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchResults is null) return;
        var query = SearchBox.Text.Trim();
        SearchResults.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
        TopicTree.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length == 0) { SearchCount.Text = "Índice completo"; return; }
        var matches = _catalog.Search(query);
        SearchResults.ItemsSource = matches;
        SearchCount.Text = matches.Count == 0 ? "Nenhum tópico encontrado" : $"{matches.Count} tópico(s) encontrado(s)";
    }

    private void SearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is ManualTopic topic) ShowTopic(topic.Id);
    }
}
