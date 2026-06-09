using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RevoltLauncher.Converters;

public sealed class ThemeBrushConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isLight = value is true;
        var key = parameter?.ToString() ?? string.Empty;
        var color = key switch
        {
            "Window" => isLight ? "#FFFFFF" : "#0A0A0F",
            "Root" => isLight ? "#FFFFFF" : "#0A0A0F",
            "RootBorder" => isLight ? "#111827" : "#F9FAFB",
            "Header" => isLight ? "#FFFFFF" : "#0C0C13",
            "HeaderPill" => isLight ? "#FFFFFF" : "#0D0D13",
            "HeaderPillBorder" => isLight ? "#D1D5DB" : "#2A2A36",
            "Search" => isLight ? "#FFFFFF" : "#111118",
            "SearchBorder" => isLight ? "#D1D5DB" : "#2A2A36",
            "SearchText" => isLight ? "#111827" : "#E5E7EB",
            "Placeholder" => isLight ? "#7A8496" : "#5F6272",
            "ListPanel" => isLight ? "#FFFFFF" : "#08080D",
            "ListBorder" => isLight ? "#D1D5DB" : "#2A2A36",
            "Card" => isLight ? "#FFFFFF" : "#12121A",
            "CardBorder" => isLight ? "#D1D5DB" : "#2A2A36",
            "CardIcon" => isLight ? "#15151F" : "#15151F",
            "CardName" => isLight ? "#253044" : "#A9A9B3",
            "CardFooter" => isLight ? "#FFFFFF" : "#0D0D13",
            "CardHoverBorder" => isLight ? "#F59E0B" : "#F2FFFFFF",
            "CardCornerAccent" => isLight ? "#F59E0B" : "#FFFFFFFF",
            "PlayButton" => isLight ? "#F8FAFC" : "#F2FFFFFF",
            "PlayIcon" => isLight ? "#111827" : "#111827",
            "Fab" => isLight ? "#111827" : "#F5F5F7",
            "FabIcon" => isLight ? "#FFFFFF" : "#111111",
            _ => isLight ? "#FFFFFF" : "#000000"
        };

        return new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isLight = values.Length > 0 && values[0] is true;
        var isChecked = values.Length > 1 && values[1] is true;
        var isMouseOver = values.Length > 2 && values[2] is true;

        var key = parameter?.ToString() ?? string.Empty;
        var color = key switch
        {
            "TabBackground" when isChecked && isMouseOver => isLight ? "#CBD5E1" : "#F3F4F6",
            "TabBackground" when isChecked => isLight ? "#E5E7EB" : "#FFFFFF",
            "TabBackground" when isMouseOver => isLight ? "#F3F4F6" : "#1E1E29",
            "TabBackground" => "Transparent",
            "TabForeground" when isChecked => "#111827",
            "TabForeground" when isMouseOver => isLight ? "#111827" : "#FFFFFF",
            "TabForeground" => isLight ? "#6B7280" : "#858591",
            _ => isLight ? "#FFFFFF" : "#000000"
        };

        return color == "Transparent"
            ? System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}
