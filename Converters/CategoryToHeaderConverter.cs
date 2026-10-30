using System.Globalization;
using System.Windows.Data;

namespace Launcher.Converters;

public sealed class CategoryToHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var category = value as string;
        var isGame = string.Equals(category, "GAMES", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(category, "KHO GAME", StringComparison.OrdinalIgnoreCase);
        return isGame ? "Move to APPS" : "Move to GAMES";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
