using System.Globalization;
using System.Windows.Data;

namespace Launcher.Converters;

public sealed class FavoriteHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Remove from Favorites" : "Add to Favorites";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
