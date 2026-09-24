using System.Globalization;
using System.Windows.Data;

namespace JelloClient.UI.Converters;

/// True when the bound count is above zero. Drives the enabled state of the delete
/// button from the grid selection, the way Voidstrap does with a DataTrigger.
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
