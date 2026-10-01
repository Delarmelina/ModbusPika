using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public partial class FullTestTargetsDialog : Window
{
    private readonly List<TestTargetChoice> _choices;
    public IReadOnlyList<ClientConnectionSession> SelectedTargets =>
        _choices.Where(x => x.Included).Select(x => x.Session).ToArray();

    public FullTestTargetsDialog(IEnumerable<ClientConnectionSession> sessions)
    {
        InitializeComponent();
        _choices = sessions.Select(x => new TestTargetChoice(x) { Included = x.IncludeInFullTest }).ToList();
        TargetsGrid.ItemsSource = _choices;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var choice in _choices) choice.Included = true; }
    private void Clear_Click(object sender, RoutedEventArgs e) { foreach (var choice in _choices) choice.Included = false; }
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        TargetsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        TargetsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DialogResult = true;
    }
}

public sealed partial class TestTargetChoice(ClientConnectionSession session) : ObservableObject
{
    public ClientConnectionSession Session { get; } = session;
    [ObservableProperty] private bool included;
}
