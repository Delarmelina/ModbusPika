using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModbusTcpTroubleshooter.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void TcpTimelineGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid { SelectedItem: TcpTimelineRow row })
        {
            return;
        }

        MessageBox.Show(this, row.Details, $"Detalhes do pacote #{row.Number}", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
