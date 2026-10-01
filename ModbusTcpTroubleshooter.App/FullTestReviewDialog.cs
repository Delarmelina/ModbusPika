using System.Windows;
using System.Windows.Controls;

namespace ModbusTcpTroubleshooter.App;

public sealed class FullTestReviewDialog : Window
{
    public FullTestReviewDialog(string review)
    {
        Title = "Revisar execução do teste";
        Width = 680; Height = 620; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var back = new Button { Content = "Voltar à configuração", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var start = new Button { Content = "Confirmar e executar", Padding = new Thickness(12, 6, 12, 6) };
        start.Click += (_, _) => DialogResult = true;
        actions.Children.Add(back); actions.Children.Add(start);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = review, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21 } });
        Content = panel;
    }
}
