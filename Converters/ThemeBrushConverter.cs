using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RevoltLauncher.Converters;

public sealed class ThemeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isLight = value is true;
        var key = parameter?.ToString() ?? string.Empty;
        var color = key switch
        {
            "Window" => isLight ? "#FFFFFF" : "#0A0A0F",
            "Root" => isLight ? "#FFFFFF" : "#0A0A0F",
            "Header" => isLight ? "#FFFFFF" : "#0C0C13",
            "HeaderPill" => isLight ? "#F4F5F7" : "#0D0D13",
            "HeaderPillBorder" => isLight ? "#E5E7EB" : "#171720",
            "Search" => isLight ? "#FFFFFF" : "#111118",
            "SearchBorder" => isLight ? "#E5E7EB" : "#202839",
            "SearchText" => isLight ? "#111827" : "#E5E7EB",
            "Placeholder" => isLight ? "#7A8496" : "#5F6272",
            "ListPanel" => isLight ? "#F6F7F9" : "#08080D",
            "ListBorder" => isLight ? "#E5E7EB" : "#1D1D27",
            "Card" => isLight ? "#FFFFFF" : "#12121A",
            "CardBorder" => isLight ? "#A8B3C4" : "#242433",
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
