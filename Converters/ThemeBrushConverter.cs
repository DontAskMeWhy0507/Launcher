using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Launcher.Converters;

public sealed class ThemeBrushConverter : IValueConverter, IMultiValueConverter
{
    private static readonly ConcurrentDictionary<(bool isLight, string key), SolidColorBrush> _brushCache = new();
    private static readonly ConcurrentDictionary<string, SolidColorBrush> _multiBrushCache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isLight = value is true;
        var key = parameter?.ToString() ?? string.Empty;

        return _brushCache.GetOrAdd((isLight, key), k =>
        {
            var (light, itemKey) = k;
            var color = itemKey switch
            {
                "Window" => light ? "#FFFFFF" : "#0A0A0F",
                "Root" => light ? "#FFFFFF" : "#0A0A0F",
                "RootBorder" => light ? "#111827" : "#F9FAFB",
                "Header" => light ? "#FFFFFF" : "#0C0C13",
                "HeaderPill" => light ? "#FFFFFF" : "#0D0D13",
                "HeaderPillBorder" => light ? "#D1D5DB" : "#2A2A36",
                "Search" => light ? "#FFFFFF" : "#111118",
                "SearchBorder" => light ? "#D1D5DB" : "#2A2A36",
                "SearchText" => light ? "#111827" : "#E5E7EB",
                "Placeholder" => light ? "#7A8496" : "#5F6272",
                "ListPanel" => light ? "#FFFFFF" : "#08080D",
                "ListBorder" => light ? "#D1D5DB" : "#2A2A36",
                "Card" => light ? "#FFFFFF" : "#12121A",
                "CardBorder" => light ? "#D1D5DB" : "#2A2A36",
                "CardIcon" => light ? "#15151F" : "#15151F",
                "CardName" => light ? "#253044" : "#A9A9B3",
                "CardFooter" => light ? "#FFFFFF" : "#0D0D13",
                "CardHoverBorder" => light ? "#F59E0B" : "#F2FFFFFF",
                "CardCornerAccent" => light ? "#F59E0B" : "#FFFFFFFF",
                "PlayButton" => light ? "#F8FAFC" : "#F2FFFFFF",
                "PlayIcon" => light ? "#111827" : "#111827",
                "Fab" => light ? "#111827" : "#F5F5F7",
                "FabIcon" => light ? "#FFFFFF" : "#111111",
                "SectionHeader" => light ? "#374151" : "#9CA3AF",
                "SettingCard" => light ? "#FFFFFF" : "#12121A",
                "SettingCardBorder" => light ? "#E5E7EB" : "#242433",
                "ButtonPrimary" => light ? "#2563EB" : "#3B82F6",
                "ButtonSecondary" => light ? "#F3F4F6" : "#1A1A24",
                "ButtonSecondaryBorder" => light ? "#D1D5DB" : "#2E2E3F",
                "ButtonSecondaryText" => light ? "#1F2937" : "#E5E7EB",
                "CleanButtonBg" => light ? "#FEF2F2" : "#1C1318",
                "CleanButtonBorder" => light ? "#FECACA" : "#381E28",
                "CleanButtonText" => light ? "#DC2626" : "#F87171",
                _ => light ? "#FFFFFF" : "#000000"
            };

            var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        });
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isLight = values.Length > 0 && values[0] is true;
        var isChecked = values.Length > 1 && values[1] is true;
        var isMouseOver = values.Length > 2 && values[2] is true;

        var key = parameter?.ToString() ?? string.Empty;
        var cacheKey = $"{isLight}_{isChecked}_{isMouseOver}_{key}";

        return _multiBrushCache.GetOrAdd(cacheKey, _ =>
        {
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

            if (color == "Transparent") return System.Windows.Media.Brushes.Transparent;

            var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        });
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
