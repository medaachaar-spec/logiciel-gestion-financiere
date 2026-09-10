using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FinancialManagement.Desktop.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var inverted = value is bool booleanValue && !booleanValue;
        return targetType == typeof(Visibility)
            ? inverted ? Visibility.Visible : Visibility.Collapsed
            : inverted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
