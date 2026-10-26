using System.Globalization;
using System.Windows.Data;

namespace Launcher.Converters;

public sealed class ActiveTabConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
    }
}
