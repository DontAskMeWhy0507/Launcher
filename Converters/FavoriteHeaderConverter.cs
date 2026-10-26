using System.Globalization;
using System.Windows.Data;

namespace Launcher.Converters;

public sealed class FavoriteHeaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Xóa yêu thích" : "Thêm yêu thích";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
