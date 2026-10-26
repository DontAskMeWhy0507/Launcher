namespace Launcher.Services;

internal static class UserDialog
{
    public static void ShowInfo(string message) => Show("Launcher", message, "OK", null);

    public static void ShowWarning(string message) => Show("Launcher", message, "OK", null);

    public static void ShowError(string message) => Show("Launcher error", message, "OK", null);

    private static bool Show(string title, string message, string primaryText, string? secondaryText)
    {
        if (Application.Current is null)
        {
            return false;
        }

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => Show(title, message, primaryText, secondaryText));
        }

        var palette = CreatePalette();
        var mainWin = Application.Current.MainWindow;
        var hasValidOwner = mainWin is { IsLoaded: true, IsVisible: true };

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 240,
            WindowStartupLocation = hasValidOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Owner = hasValidOwner ? mainWin : null,
            Background = palette.BackgroundBrush
        };

        var root = new Grid
        {
            Margin = new Thickness(18),
            Background = palette.BackgroundBrush
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = palette.TextBrush,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            var secondary = CreateButton(secondaryText, palette, isPrimary: false);
            secondary.Margin = new Thickness(0, 0, 8, 0);
            secondary.IsCancel = true;
            buttons.Children.Add(secondary);
        }

        var primary = CreateButton(primaryText, palette, isPrimary: true);
        primary.IsDefault = true;
        primary.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(primary);

        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);

        dialog.Content = root;
        return dialog.ShowDialog() == true;
    }

    private static Button CreateButton(string content, DialogPalette palette, bool isPrimary)
    {
        return new Button
        {
            Content = content,
            Width = 86,
            MinHeight = 34,
            Background = isPrimary ? palette.AccentBrush : palette.PanelBrush,
            Foreground = isPrimary ? System.Windows.Media.Brushes.White : palette.TextBrush,
            BorderBrush = isPrimary ? palette.AccentBrush : palette.BorderBrush
        };
    }

    private static DialogPalette CreatePalette()
    {
        var isLightTheme = Application.Current.MainWindow?.DataContext?
            .GetType()
            .GetProperty("IsLightTheme")
            ?.GetValue(Application.Current.MainWindow.DataContext) as bool? == true;

        return new DialogPalette(
            new SolidColorBrush(isLightTheme ? Color.FromRgb(248, 250, 252) : Color.FromRgb(15, 15, 22)),
            new SolidColorBrush(isLightTheme ? Color.FromRgb(255, 255, 255) : Color.FromRgb(24, 24, 34)),
            new SolidColorBrush(isLightTheme ? Color.FromRgb(17, 24, 39) : Color.FromRgb(229, 231, 235)),
            new SolidColorBrush(isLightTheme ? Color.FromRgb(203, 213, 225) : Color.FromRgb(55, 65, 81)),
            new SolidColorBrush(Color.FromRgb(59, 130, 246)));
    }

    private sealed record DialogPalette(
        System.Windows.Media.Brush BackgroundBrush,
        System.Windows.Media.Brush PanelBrush,
        System.Windows.Media.Brush TextBrush,
        System.Windows.Media.Brush BorderBrush,
        System.Windows.Media.Brush AccentBrush);
}
