using System.Globalization;
using System.Windows.Data;

namespace ModbusTcpTroubleshooter.App;

public sealed class RequiredSelectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value;
    // Collection replacement/removal temporarily clears each Selector independently.
    // The view model keeps its current target or an inactive placeholder when the list is empty.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value ?? Binding.DoNothing;
}
