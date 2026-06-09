using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using RevoltLauncher.Models;
using RevoltLauncher.ViewModels;
using RevoltLauncher.Services;

namespace RevoltLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IControllerService _controllerService;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExplicitExit;
    private Point _dragStartPoint;
    private Point _dragOffset;
    private readonly System.Windows.Threading.DispatcherTimer _controllerAHoldTimer;
    private bool _controllerAIsDown;
    private bool _controllerDragActive;
    private LauncherItem? _controllerDragItem;

    public MainWindow(MainViewModel viewModel, IControllerService controllerService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _controllerService = controllerService;
        DataContext = viewModel;
        SetupTrayIcon();
        _controllerService.ButtonPressed += OnControllerButton;
        _controllerService.ButtonReleased += OnControllerButtonReleased;
        _controllerAHoldTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _controllerAHoldTimer.Tick += OnControllerAHoldTick;
        Opacity = 1;
        Visibility = Visibility.Visible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) { }

    private void OnControllerButton(object? sender, ControllerButton button)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive && WindowState == WindowState.Minimized) return;
            switch (button)
            {
                case ControllerButton.DPadLeft: MoveFocusOrDraggedItem(FocusNavigationDirection.Left); break;
                case ControllerButton.DPadRight: MoveFocusOrDraggedItem(FocusNavigationDirection.Right); break;
                case ControllerButton.DPadUp: MoveFocusOrDraggedItem(FocusNavigationDirection.Up); break;
                case ControllerButton.DPadDown: MoveFocusOrDraggedItem(FocusNavigationDirection.Down); break;
                case ControllerButton.LeftShoulder: CycleTab(-1); break;
                case ControllerButton.RightShoulder: CycleTab(1); break;
                case ControllerButton.A: StartControllerAPress(); break;
            }
        }), System.Windows.Threading.DispatcherPriority.Send);
    }

    private void OnControllerButtonReleased(object? sender, ControllerButton button)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (button == ControllerButton.A)
            {
                EndControllerAPress();
            }
        }), System.Windows.Threading.DispatcherPriority.Send);
    }

    public void BringToFront()
    {
        if (!CheckAccess()) { Dispatcher.BeginInvoke(new Action(BringToFront)); return; }
        try
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate(); Topmost = true; Topmost = false; Focus();
        }
        catch { }
    }

    private void ShiftFocus(FocusNavigationDirection direction)
    {
        var focused = FocusManager.GetFocusedElement(this) as FrameworkElement;
        if (focused == null || !IsDescendantOf(focused, LaunchersList)) { EnsureFocus(true); return; }
        var next = focused.PredictFocus(direction);
        if (next is Button b && IsDescendantOf(b, LaunchersList)) { b.Focus(); b.BringIntoView(); }
        else focused.MoveFocus(new TraversalRequest(direction));
    }

    private void MoveFocusOrDraggedItem(FocusNavigationDirection direction)
    {
        if (_controllerDragActive)
        {
            MoveControllerDraggedItem(direction);
            return;
        }

        ShiftFocus(direction);
    }

    private void StartControllerAPress()
    {
        if (_controllerAIsDown) return;
        _controllerAIsDown = true;
        _controllerAHoldTimer.Stop();
        _controllerAHoldTimer.Start();
    }

    private async void EndControllerAPress()
    {
        if (!_controllerAIsDown) return;

        _controllerAIsDown = false;
        _controllerAHoldTimer.Stop();

        if (_controllerDragActive)
        {
            var item = _controllerDragItem;
            EndControllerDrag();
            if (item is not null)
            {
                await _viewModel.SaveAsync();
            }
            return;
        }

        if (FocusManager.GetFocusedElement(this) is Button b)
        {
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private void OnControllerAHoldTick(object? sender, EventArgs e)
    {
        _controllerAHoldTimer.Stop();
        if (!_controllerAIsDown || _controllerDragActive) return;

        if (FocusManager.GetFocusedElement(this) is Button { DataContext: LauncherItem item } button && IsDescendantOf(button, LaunchersList))
        {
            BeginControllerDrag(item, button);
        }
    }

    private void BeginControllerDrag(LauncherItem item, FrameworkElement card)
    {
        _controllerDragActive = true;
        _controllerDragItem = item;
        item.IsDragging = true;

        DragShadow.DataContext = item;
        DragShadow.Visibility = Visibility.Visible;
        DragShadow.UpdateLayout();

        var cardPos = card.TranslatePoint(new Point(0, 0), this);
        _dragOffset = new Point(DragShadow.Width / 2, DragShadow.Height / 2);
        UpdateDragShadowPosition(new Point(cardPos.X + card.ActualWidth / 2, cardPos.Y + card.ActualHeight / 2));
    }

    private void EndControllerDrag()
    {
        if (_controllerDragItem is not null)
        {
            _controllerDragItem.IsDragging = false;
        }

        _controllerDragActive = false;
        _controllerDragItem = null;
        DragShadow.Visibility = Visibility.Collapsed;
    }

    private void MoveControllerDraggedItem(FocusNavigationDirection direction)
    {
        if (_controllerDragItem is null) return;

        var focused = FocusManager.GetFocusedElement(this) as FrameworkElement;
        if (focused is null || !IsDescendantOf(focused, LaunchersList))
        {
            FocusLauncherItem(_controllerDragItem);
            return;
        }

        var next = focused.PredictFocus(direction) as Button;
        if (next?.DataContext is not LauncherItem target || ReferenceEquals(target, _controllerDragItem)) return;

        _viewModel.Reorder(_controllerDragItem, target);
        _viewModel.FilteredLaunchers.Refresh();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var button = FocusLauncherItem(_controllerDragItem);
            if (button is not null)
            {
                var cardPos = button.TranslatePoint(new Point(0, 0), this);
                UpdateDragShadowPosition(new Point(cardPos.X + button.ActualWidth / 2, cardPos.Y + button.ActualHeight / 2));
                button.BringIntoView();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private Button? FocusLauncherItem(LauncherItem item)
    {
        var button = FindLauncherButton(LaunchersList, item);
        if (button is not null)
        {
            button.Focus();
            Keyboard.Focus(button);
        }
        return button;
    }

    private bool IsDescendantOf(DependencyObject child, DependencyObject parent)
    {
        while (child != null) { if (ReferenceEquals(child, parent)) return true; child = VisualTreeHelper.GetParent(child); }
        return false;
    }

    private void EnsureFocus(bool forceFirst = false)
    {
        if (forceFirst || FocusManager.GetFocusedElement(this) is not Button)
        {
            var firstButton = FindVisualChild<Button>(LaunchersList);
            if (firstButton != null) { firstButton.Focus(); Keyboard.Focus(firstButton); }
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && (name is null || typed.Name == name)) return typed;
            var nested = FindVisualChild<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private Button? FindLauncherButton(DependencyObject parent, LauncherItem item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { DataContext: LauncherItem candidate } button && ReferenceEquals(candidate, item))
            {
                return button;
            }

            var nested = FindLauncherButton(child, item);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void CycleTab(int direction)
    {
        var tabs = _viewModel.Tabs;
        var index = Array.IndexOf(tabs, _viewModel.ActiveTab);
        if (index == -1) index = 0;
        var newIndex = (index + direction + tabs.Length) % tabs.Length;
        _viewModel.SelectTab(tabs[newIndex]);
        Dispatcher.BeginInvoke(new Action(() => EnsureFocus(true)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else DragMove(); }

    private void SetupTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Visible = true, Text = "Revolt Launcher" };
        _notifyIcon.DoubleClick += (s, e) => BringToFront();
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Mở Launcher", null, (s, e) => BringToFront());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Thoát", null, (s, e) => ExitApplication());
        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ExitApplication() { _isExplicitExit = true; _notifyIcon?.Dispose(); System.Windows.Application.Current.Shutdown(); }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { if (!_isExplicitExit) { e.Cancel = true; Hide(); } else base.OnClosing(e); }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SendSystemCommand(ScMinimize);
    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void ToggleMaximize() { if (WindowState == WindowState.Maximized) SendSystemCommand(ScRestore); else SendSystemCommand(ScMaximize); }

    private async void OnLauncherClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is LauncherItem item)
        {
            await ShowLaunchLoadingAsync();
            await _viewModel.OpenLauncherAsync(item);
        }
    }

    private async Task ShowLaunchLoadingAsync()
    {
        ResetLaunchLoadingAnimation();
        FullscreenRippleOverlay.Visibility = Visibility.Visible;
        FullscreenRippleOverlay.Opacity = 1;
        FullscreenRipple.Opacity = 0;
        FullscreenRippleCore.Opacity = 0;

        var storyboard = new Storyboard();

        AddRippleAnimation(storyboard, FullscreenRipple, OpacityProperty, 0.62, 0, 0, 1000);
        AddRippleAnimation(storyboard, FullscreenRipple, "RenderTransform.(ScaleTransform.ScaleX)", 0.2, 4.2, 0, 1000);
        AddRippleAnimation(storyboard, FullscreenRipple, "RenderTransform.(ScaleTransform.ScaleY)", 0.2, 4.2, 0, 1000);
        AddRippleAnimation(storyboard, FullscreenRippleCore, OpacityProperty, 0.9, 0, 0, 520);
        AddRippleAnimation(storyboard, FullscreenRippleCore, "RenderTransform.(ScaleTransform.ScaleX)", 0.8, 1.5, 0, 520);
        AddRippleAnimation(storyboard, FullscreenRippleCore, "RenderTransform.(ScaleTransform.ScaleY)", 0.8, 1.5, 0, 520);
        AddRippleAnimation(storyboard, FullscreenRippleOverlay, OpacityProperty, 1, 0, 760, 240);

        storyboard.Begin();
        await Task.Delay(1000);
        ResetLaunchLoadingAnimation();
        FullscreenRippleOverlay.Visibility = Visibility.Collapsed;
    }

    private void ResetLaunchLoadingAnimation()
    {
        FullscreenRippleOverlay.BeginAnimation(OpacityProperty, null);
        FullscreenRipple.BeginAnimation(OpacityProperty, null);
        FullscreenRippleCore.BeginAnimation(OpacityProperty, null);

        if (FullscreenRipple.RenderTransform is ScaleTransform rippleScale)
        {
            rippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            rippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            rippleScale.ScaleX = 0.2;
            rippleScale.ScaleY = 0.2;
        }

        if (FullscreenRippleCore.RenderTransform is ScaleTransform coreScale)
        {
            coreScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            coreScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            coreScale.ScaleX = 0.8;
            coreScale.ScaleY = 0.8;
        }

        FullscreenRippleOverlay.Opacity = 0;
        FullscreenRipple.Opacity = 0;
        FullscreenRippleCore.Opacity = 0;
    }

    private static void AddRippleAnimation(Storyboard storyboard, DependencyObject target, DependencyProperty property, double from, double to, int delayMs, int durationMs)
    {
        var animation = CreateRippleAnimation(from, to, delayMs, durationMs);
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private static void AddRippleAnimation(Storyboard storyboard, DependencyObject target, string propertyPath, double from, double to, int delayMs, int durationMs)
    {
        var animation = CreateRippleAnimation(from, to, delayMs, durationMs);
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(propertyPath));
        storyboard.Children.Add(animation);
    }

    private static DoubleAnimation CreateRippleAnimation(double from, double to, int delayMs, int durationMs)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }
    private void OnFavoriteClick(object sender, RoutedEventArgs e) => e.Handled = true;
    private void OnLauncherPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStartPoint = e.GetPosition(this);

    private void OnLauncherPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement card || card.DataContext is not LauncherItem item) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var cardPos = card.TranslatePoint(new Point(0, 0), this);
        var scale = card.ActualWidth > 0 ? DragShadow.Width / card.ActualWidth : 1.0;
        _dragOffset = new Point((position.X - cardPos.X) * scale, (position.Y - cardPos.Y) * scale);
        DragShadow.DataContext = item; DragShadow.Visibility = Visibility.Visible; UpdateDragShadowPosition(position); DragShadow.UpdateLayout();
        item.IsDragging = true; try { DragDrop.DoDragDrop(this, item, DragDropEffects.Move); } finally { item.IsDragging = false; DragShadow.Visibility = Visibility.Collapsed; }
    }

    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e) { e.UseDefaultCursors = true; UpdateDragShadowPosition(PointFromScreen(GetMousePosition())); e.Handled = true; }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this); UpdateDragShadowPosition(position);
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; return; }
        if (e.Data.GetDataPresent(DataFormats.StringFormat)) { var data = e.Data.GetData(DataFormats.StringFormat) as string; if (!string.IsNullOrWhiteSpace(data)) { var cleanedData = data.Trim('"', ' ', '@'); if (cleanedData.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || (cleanedData.Length > 3 && cleanedData[1] == ':' && cleanedData[2] == '\\')) { e.Effects = DragDropEffects.Copy; e.Handled = true; return; } } }
        if (e.Data.GetData(typeof(LauncherItem)) is LauncherItem source) { var target = FindLauncherItem(e.OriginalSource as DependencyObject); if (target is not null && !ReferenceEquals(source, target)) _viewModel.Reorder(source, target); }
    }

    private void UpdateDragShadowPosition(Point p) { Canvas.SetLeft(DragShadow, p.X - _dragOffset.X); Canvas.SetTop(DragShadow, p.Y - _dragOffset.Y); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Win32Point lpPoint);
    private struct Win32Point { public int X; public int Y; }
    private Point GetMousePosition() { GetCursorPos(out var w32Mouse); return new Point(w32Mouse.X, w32Mouse.Y); }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        DragShadow.Visibility = Visibility.Collapsed;
        var paths = DeepExtractPaths(e.Data);
        if (paths.Any()) { foreach (var path in paths) await _viewModel.AddFileAsync(path); e.Handled = true; return; }
        if (e.Data.GetDataPresent(typeof(LauncherItem))) { var source = (LauncherItem)e.Data.GetData(typeof(LauncherItem))!; var target = FindLauncherItem(e.OriginalSource as DependencyObject); if (target != null && !ReferenceEquals(source, target)) await _viewModel.MoveLauncherAsync(source, target); }
    }

    private IEnumerable<string> DeepExtractPaths(IDataObject data)
    {
        var foundPaths = new List<string>();
        if (data.GetDataPresent(DataFormats.FileDrop)) { if (data.GetData(DataFormats.FileDrop) is string[] files) foundPaths.AddRange(files); }
        string[] textFormats = { DataFormats.StringFormat, DataFormats.UnicodeText, DataFormats.Text };
        foreach (var format in textFormats) { if (data.GetDataPresent(format)) { var content = data.GetData(format) as string; if (!string.IsNullOrWhiteSpace(content)) { var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); foreach (var line in lines) { var cleaned = line.Trim('"', ' ', '@', '\'', '\t'); if (IsLikelyPath(cleaned)) foundPaths.Add(cleaned); } } } }
        if (data.GetDataPresent("FileName")) { if (data.GetData("FileName") is string[] fileNames) foundPaths.AddRange(fileNames); }
        return foundPaths.Distinct();
    }

    private bool IsLikelyPath(string input) { if (string.IsNullOrWhiteSpace(input)) return false; if (input.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true; if (input.Length >= 3 && ((char.IsLetter(input[0]) && input[1] == ':' && input[2] == '\\') || input.StartsWith("\\\\"))) return true; return false; }
    private async void OnLauncherDrop(object sender, DragEventArgs e) { if (e.Handled) return; OnWindowDrop(sender, e); }

    private static LauncherItem? FindLauncherItem(DependencyObject? source) { while (source is not null) { if (source is FrameworkElement { DataContext: LauncherItem item }) return item; source = VisualTreeHelper.GetParent(source); } return null; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDwmWindowFrame();
    }

    private void ApplyDwmWindowFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeWindowCornerPreference, ref cornerPreference, sizeof(int));
    }

    private void SendSystemCommand(IntPtr command) { var hwnd = new WindowInteropHelper(this).Handle; if (hwnd != IntPtr.Zero) SendMessage(hwnd, WmSysCommand, command, IntPtr.Zero); }
    private const int WmSysCommand = 0x0112;
    private static readonly IntPtr ScMinimize = new(0xF020);
    private static readonly IntPtr ScMaximize = new(0xF030);
    private static readonly IntPtr ScRestore = new(0xF120);
    private const int DwmWindowAttributeWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
