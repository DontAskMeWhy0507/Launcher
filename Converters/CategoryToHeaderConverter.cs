using System.Globalization;
using System.Windows.Data;

namespace RevoltLauncher.Converters;

public sealed class CategoryToHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value as string == "KHO GAME" ? "Chuyển sang ỨNG DỤNG" : "Chuyển sang KHO GAME";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
