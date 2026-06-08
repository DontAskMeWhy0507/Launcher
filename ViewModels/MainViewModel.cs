using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RevoltLauncher.Models;
using RevoltLauncher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace RevoltLauncher.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILauncherService _launcherService;
    private readonly IStartupService _startupService;
    private List<ScanCandidate> _lastScanResults = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _activeTab = "YÊU THÍCH";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _isLightTheme;

    [ObservableProperty]
    private bool _isScanning;

    private bool _isLoaded;
    private bool _isInitializing = true;

    public ObservableCollection<LauncherItem> Launchers { get; } = [];
    public ICollectionView FilteredLaunchers { get; }
    public string[] Tabs { get; } = ["YÊU THÍCH", "KHO GAME", "ỨNG DỤNG"];

    public MainViewModel(ISettingsService settingsService, ILauncherService launcherService, IStartupService startupService)
    {
        _settingsService = settingsService;
        _launcherService = launcherService;
        _startupService = startupService;
        FilteredLaunchers = CollectionViewSource.GetDefaultView(Launchers);
        FilteredLaunchers.Filter = FilterLauncher;
        StartWithWindows = _startupService.IsEnabled();

        _isInitializing = true;
        ActiveTab = "YÊU THÍCH";
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isLoaded) return;
        var settings = await _settingsService.LoadAsync();
        IsLightTheme = settings.IsLightTheme;

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            Launchers.Clear();
            foreach (var item in settings.Launchers.OrderBy(x => x.Order))
            {
                // Repair missing icons
                if (string.IsNullOrEmpty(item.IconPath) || item.IconPath == "Assets/Icons/apps.png" || 
                    (item.IconPath.StartsWith("Assets/Icons/") && !File.Exists(Path.Combine(AppContext.BaseDirectory, item.IconPath))))
                {
                    item.IconPath = await _launcherService.GetIconPathAsync(item.ExecutablePath);
                }
                Launchers.Add(item);
            }
            _isLoaded = true;
            _isInitializing = false;
            FilteredLaunchers.Refresh();

            await SaveAsync();
        });
    }

    private bool FilterLauncher(object value)
    {
        if (value is not LauncherItem item)
        {
            return false;
        }

        var matchesTab = ActiveTab switch
        {
            "YÊU THÍCH" => item.IsFavorite,
            "KHO GAME" => item.Category == "KHO GAME",
            _ => item.Category == "ỨNG DỤNG"
        };

        return matchesTab && (string.IsNullOrWhiteSpace(SearchText)
               || item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
    }

    partial void OnSearchTextChanged(string value) => FilteredLaunchers.Refresh();

    partial void OnActiveTabChanged(string value)
    {
        FilteredLaunchers.Refresh();
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    partial void OnStartWithWindowsChanged(bool value) => _startupService.SetEnabled(value);

    partial void OnIsLightThemeChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    [RelayCommand]
    public void SelectTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private async Task LaunchAsync(LauncherItem item) => await _launcherService.LaunchAsync(item);

    public async Task OpenLauncherAsync(LauncherItem item) => await _launcherService.LaunchAsync(item);

    [RelayCommand]
    private async Task OpenFileLocationAsync(LauncherItem item) => await _launcherService.OpenFileLocationAsync(item);

    [RelayCommand]
    private async Task AddLauncherAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn shortcut hoặc file chạy",
            Filter = "Shortcut/Executable (*.lnk;*.exe)|*.lnk;*.exe|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await AddFileAsync(dialog.FileName);
    }

    [RelayCommand]
    private async Task ScanLaunchersAsync()
    {
        if (_lastScanResults.Count > 0)
        {
            var cachedSelection = ShowScanSelectionDialog(_lastScanResults, out var rescanRequested);
            if (!rescanRequested)
            {
                await AddSelectedScanCandidatesAsync(cachedSelection);
                return;
            }
        }

        var options = ShowScanOptionsDialog();
        if (options is null)
        {
            var cachedSelection = _lastScanResults.Count > 0
                ? ShowScanSelectionDialog(_lastScanResults, out _)
                : [];
            await AddSelectedScanCandidatesAsync(cachedSelection);
            return;
        }

        IsScanning = true;
        var selectedCandidates = Array.Empty<ScanCandidate>() as IReadOnlyList<ScanCandidate>;
        var existingKeys = GetExistingLauncherKeys();

        try
        {
            var candidates = await Task.Run(() => FindInstalledLauncherCandidates(options));
            var pendingCandidates = FilterNewCandidates(candidates, existingKeys);

            if (pendingCandidates.Count == 0)
            {
                MessageBox.Show("Không tìm thấy ứng dụng/game mới.", "Scan", MessageBoxButton.OK, MessageBoxImage.Information);
                _lastScanResults = [];
                return;
            }

            var previewCandidates = new List<ScanCandidate>();
            foreach (var candidate in pendingCandidates)
            {
                var iconPath = await _launcherService.GetIconPathAsync(candidate.ExecutablePath);
                previewCandidates.Add(candidate with { IconPath = iconPath });
            }

            _lastScanResults = previewCandidates;
            IsScanning = false;

            selectedCandidates = ShowScanSelectionDialog(_lastScanResults, out var rescanRequested);
            if (rescanRequested)
            {
                _lastScanResults = [];
                await ScanLaunchersAsync();
                return;
            }
        }
        finally
        {
            IsScanning = false;
        }

        await AddSelectedScanCandidatesAsync(selectedCandidates);
    }

    private HashSet<string> GetExistingLauncherKeys()
    {
        return Launchers
            .SelectMany(item => new[] { item.ExecutablePath, ResolveShortcutTarget(item.ExecutablePath) })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePathKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<ScanCandidate> FilterNewCandidates(IReadOnlyList<ScanCandidate> candidates, ISet<string> existingKeys)
    {
        var pendingCandidates = new List<ScanCandidate>();
        foreach (var candidate in candidates)
        {
            var key = NormalizePathKey(candidate.ExecutablePath);
            var targetKey = NormalizePathKey(ResolveShortcutTarget(candidate.ExecutablePath));
            if ((key.Length > 0 && existingKeys.Contains(key)) ||
                (targetKey.Length > 0 && existingKeys.Contains(targetKey)))
            {
                continue;
            }

            pendingCandidates.Add(candidate);
        }

        return pendingCandidates;
    }

    private async Task AddSelectedScanCandidatesAsync(IReadOnlyList<ScanCandidate> selectedCandidates)
    {
        if (selectedCandidates.Count == 0)
        {
            return;
        }

        IsScanning = true;
        var existingKeys = Launchers
            .SelectMany(item => new[] { item.ExecutablePath, ResolveShortcutTarget(item.ExecutablePath) })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePathKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        try
        {
            foreach (var candidate in selectedCandidates)
            {
                var key = NormalizePathKey(candidate.ExecutablePath);
                var targetKey = NormalizePathKey(ResolveShortcutTarget(candidate.ExecutablePath));

                Launchers.Add(new LauncherItem
                {
                    Name = candidate.Name,
                    ExecutablePath = candidate.ExecutablePath,
                    IconPath = string.IsNullOrWhiteSpace(candidate.IconPath)
                        ? await _launcherService.GetIconPathAsync(candidate.ExecutablePath)
                        : candidate.IconPath,
                    Category = candidate.Category,
                    Order = Launchers.Count
                });

                if (key.Length > 0) existingKeys.Add(key);
                if (targetKey.Length > 0) existingKeys.Add(targetKey);
                added++;
            }

            if (added > 0)
            {
                FilteredLaunchers.Refresh();
                await SaveAsync();
            }

            MessageBox.Show(
                added > 0 ? $"Đã thêm {added} ứng dụng/game mới." : "Chưa thêm ứng dụng/game nào.",
                "Scan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task AddFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            // Aggressive path cleaning: trim quotes, spaces, and common artifacts like '@'
            filePath = filePath.Trim('"', ' ', '@', '\'', '\t', '\n', '\r');
            
            if (filePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(filePath);
                filePath = uri.LocalPath;
            }

            if (!File.Exists(filePath) && !Directory.Exists(filePath))
            {
                MessageBox.Show($"Không tìm thấy file hoặc thư mục tại đường dẫn:\n\"{filePath}\"\n\nVui lòng kiểm tra lại quyền truy cập hoặc file gốc.", 
                                "Lỗi thêm ứng dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(filePath).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "ỨNG DỤNG MỚI";

            string category = ActiveTab == "KHO GAME" ? "KHO GAME" : "ỨNG DỤNG";
            string iconPath = await _launcherService.GetIconPathAsync(filePath);

            var newItem = new LauncherItem
            {
                Name = fileName,
                ExecutablePath = filePath,
                IconPath = iconPath,
                Category = category,
                Order = Launchers.Count
            };

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Launchers.Add(newItem);
                FilteredLaunchers.Refresh();
            });

            await SaveAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Có lỗi xảy ra khi xử lý file:\n{ex.Message}", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static ScanOptions? ShowScanOptionsDialog()
    {
        var startMenu = new System.Windows.Controls.CheckBox { Content = "Start Menu và Desktop", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var profile = new System.Windows.Controls.CheckBox { Content = "C Profile / AppData / Downloads", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var registry = new System.Windows.Controls.CheckBox { Content = "Registry installed apps", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        var drives = new System.Windows.Controls.CheckBox { Content = "Các ổ đĩa và thư mục game phổ biến", IsChecked = false, Margin = new Thickness(0, 0, 0, 8) };

        var driveHint = new TextBlock
        {
            Text = "Quét ổ đĩa có thể lâu hơn vì phải duyệt nhiều thư mục.",
            Foreground = WpfBrushes.DimGray,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 16)
        };

        var scanButton = new Button { Content = "Bắt đầu scan", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelButton = new Button { Content = "Hủy", MinWidth = 72, IsCancel = true };
        var buttons = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        buttons.Children.Add(scanButton);
        buttons.Children.Add(cancelButton);

        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "Chọn nguồn muốn scan",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });
        root.Children.Add(startMenu);
        root.Children.Add(profile);
        root.Children.Add(registry);
        root.Children.Add(drives);
        root.Children.Add(driveHint);
        root.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "Tuỳ chọn scan",
            Width = 420,
            Height = 300,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Content = root
        };

        scanButton.Click += (_, _) => dialog.DialogResult = true;

        return dialog.ShowDialog() == true
            ? new ScanOptions(
                startMenu.IsChecked == true,
                profile.IsChecked == true,
                registry.IsChecked == true,
                drives.IsChecked == true)
            : null;
    }

    private static IReadOnlyList<ScanCandidate> FindInstalledLauncherCandidates(ScanOptions options)
    {
        var candidates = new Dictionary<string, ScanCandidate>(StringComparer.OrdinalIgnoreCase);

        if (options.ScanStartMenuAndDesktop)
        {
            foreach (var shortcut in EnumerateShortcutLocations())
            {
                AddCandidate(candidates, shortcut);
            }
        }

        if (options.ScanRegistry)
        {
            foreach (var registryPath in EnumerateRegistryInstallEntries())
            {
                AddCandidate(candidates, registryPath);
            }
        }

        if (options.ScanUserProfile)
        {
            foreach (var profilePath in EnumerateUserProfileApplications())
            {
                AddCandidate(candidates, profilePath);
            }
        }

        if (options.ScanDrives)
        {
            foreach (var drivePath in EnumerateKnownDriveApplications())
            {
                AddCandidate(candidates, drivePath);
            }
        }

        return candidates.Values
            .OrderBy(candidate => candidate.Category == "KHO GAME" ? 0 : 1)
            .ThenBy(candidate => candidate.Name)
            .ToList();
    }

    private static IReadOnlyList<ScanCandidate> ShowScanSelectionDialog(IReadOnlyList<ScanCandidate> candidates, out bool rescanRequested)
    {
        rescanRequested = false;
        var choices = candidates.Select(candidate => new ScanCandidateChoice(candidate)).ToList();
        var list = new WpfListBox
        {
            BorderThickness = new Thickness(0),
            Background = WpfBrushes.Transparent,
            Margin = new Thickness(0, 12, 0, 12)
        };

        foreach (var choice in choices)
        {
            var textPanel = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = $"{choice.Candidate.Category}  •  {choice.Candidate.Source}  •  {choice.Candidate.Drive}",
                Foreground = choice.Candidate.Category == "KHO GAME" ? WpfBrushes.DarkOrange : WpfBrushes.SteelBlue,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            });

            textPanel.Children.Add(new TextBlock
            {
                Text = choice.Candidate.Name,
                Foreground = WpfBrushes.Black,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });

            textPanel.Children.Add(new TextBlock
            {
                Text = choice.Candidate.ExecutablePath,
                Foreground = WpfBrushes.DimGray,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var row = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(CreateScanIconImage(choice.Candidate.IconPath));
            row.Children.Add(textPanel);

            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked = false,
                Margin = new Thickness(8),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = row
            };
            choice.CheckBox = checkbox;

            list.Items.Add(new Border
            {
                Child = checkbox,
                Tag = choice,
                BorderBrush = WpfBrushes.Gainsboro,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4)
            });
        }

        var summary = new TextBlock
        {
            Text = $"Tìm thấy {choices.Count} ứng dụng/game mới. Tick những mục muốn thêm.",
            Foreground = WpfBrushes.Black,
            FontWeight = FontWeights.SemiBold
        };

        var sourceFilter = new System.Windows.Controls.ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 8, 0) };
        sourceFilter.Items.Add("Tất cả nguồn");
        foreach (var source in choices.Select(choice => choice.Candidate.Source).Distinct().OrderBy(source => source))
        {
            sourceFilter.Items.Add(source);
        }
        sourceFilter.SelectedIndex = 0;

        var driveFilter = new System.Windows.Controls.ComboBox { MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        driveFilter.Items.Add("Tất cả ổ");
        foreach (var drive in choices.Select(choice => choice.Candidate.Drive).Where(drive => drive.Length > 0).Distinct().OrderBy(drive => drive))
        {
            driveFilter.Items.Add(drive);
        }
        driveFilter.SelectedIndex = 0;

        var searchBox = new System.Windows.Controls.TextBox
        {
            MinWidth = 220,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Tìm trong kết quả scan"
        };

        void ApplyFilters()
        {
            var selectedSource = sourceFilter.SelectedItem?.ToString();
            var selectedDrive = driveFilter.SelectedItem?.ToString();
            var search = searchBox.Text.Trim();
            foreach (Border item in list.Items)
            {
                if (item.Tag is not ScanCandidateChoice choice) continue;
                var sourceMatches = selectedSource == "Tất cả nguồn" || choice.Candidate.Source == selectedSource;
                var driveMatches = selectedDrive == "Tất cả ổ" || choice.Candidate.Drive == selectedDrive;
                var searchMatches = search.Length == 0 ||
                                    choice.Candidate.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                    choice.Candidate.ExecutablePath.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                    choice.Candidate.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                    choice.Candidate.Source.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                    choice.Candidate.Drive.Contains(search, StringComparison.CurrentCultureIgnoreCase);
                item.Visibility = sourceMatches && driveMatches && searchMatches ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        sourceFilter.SelectionChanged += (_, _) => ApplyFilters();
        driveFilter.SelectionChanged += (_, _) => ApplyFilters();
        searchBox.TextChanged += (_, _) => ApplyFilters();

        var selectAllButton = new Button { Content = "Chọn tất cả", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        selectAllButton.Click += (_, _) =>
        {
            foreach (var choice in choices)
            {
                if (choice.CheckBox is not null) choice.CheckBox.IsChecked = true;
            }
        };

        var clearButton = new Button { Content = "Bỏ chọn", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        clearButton.Click += (_, _) =>
        {
            foreach (var choice in choices)
            {
                if (choice.CheckBox is not null) choice.CheckBox.IsChecked = false;
            }
        };

        var addButton = new Button { Content = "Thêm đã chọn", MinWidth = 112, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var rescanButton = new Button { Content = "Scan lại", MinWidth = 82, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "Hủy", MinWidth = 72, IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        buttons.Children.Add(sourceFilter);
        buttons.Children.Add(driveFilter);
        buttons.Children.Add(searchBox);
        buttons.Children.Add(selectAllButton);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(addButton);
        buttons.Children.Add(rescanButton);
        buttons.Children.Add(cancelButton);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(summary, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(summary);
        root.Children.Add(buttons);
        root.Children.Add(list);

        var dialog = new Window
        {
            Title = "Scan ứng dụng và game",
            Width = 780,
            Height = 560,
            MinWidth = 620,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            Content = root
        };

        var shouldRescan = false;
        addButton.Click += (_, _) => dialog.DialogResult = true;
        rescanButton.Click += (_, _) =>
        {
            shouldRescan = true;
            dialog.DialogResult = false;
        };

        var accepted = dialog.ShowDialog() == true;
        rescanRequested = shouldRescan;

        return accepted
            ? choices.Where(choice => choice.CheckBox?.IsChecked == true).Select(choice => choice.Candidate).ToList()
            : [];
    }

    private static System.Windows.Controls.Image CreateScanIconImage(string iconPath)
    {
        var image = new System.Windows.Controls.Image
        {
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var resolvedPath = ResolveIconPath(iconPath);
        if (resolvedPath is not null)
        {
            image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(resolvedPath));
        }

        return image;
    }

    private static string? ResolveIconPath(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        if (File.Exists(iconPath)) return iconPath;

        var appRelative = Path.Combine(AppContext.BaseDirectory, iconPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(appRelative)) return appRelative;

        var sourceRelative = Path.Combine(Directory.GetCurrentDirectory(), iconPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(sourceRelative) ? sourceRelative : null;
    }

    private static IEnumerable<string> EnumerateShortcutLocations()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };

        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateRegistryInstallEntries()
    {
        var uninstallKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            foreach (var keyPath in uninstallKeys)
            {
                using var root = hive.OpenSubKey(keyPath);
                if (root is null) continue;

                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    using var appKey = root.OpenSubKey(subKeyName);
                    if (appKey is null) continue;

                    var displayName = appKey.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    var displayIcon = CleanRegistryPath(appKey.GetValue("DisplayIcon")?.ToString());
                    if (displayIcon is not null && IsUsableExecutable(displayIcon))
                    {
                        yield return displayIcon;
                        continue;
                    }

                    var installLocation = CleanRegistryPath(appKey.GetValue("InstallLocation")?.ToString());
                    var exe = FindPrimaryExecutable(installLocation, displayName);
                    if (exe is not null)
                    {
                        yield return exe;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateUserProfileApplications()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new[]
        {
            Path.Combine(profile, "AppData", "Local", "Programs"),
            Path.Combine(profile, "AppData", "Local", "Microsoft", "WindowsApps"),
            Path.Combine(profile, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs")
        };

        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in EnumerateCandidateFiles(folder, SearchOption.AllDirectories, maxDepth: 4))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateKnownDriveApplications()
    {
        var folderNames = new[]
        {
            "Games", "Game", "SteamLibrary", "Epic Games", "Riot Games",
            "Battle.net", "GOG Games", "XboxGames", "Program Files", "Program Files (x86)"
        };

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            foreach (var folderName in folderNames)
            {
                var folder = Path.Combine(drive.RootDirectory.FullName, folderName);
                if (!Directory.Exists(folder)) continue;

                foreach (var file in EnumerateCandidateFiles(folder, SearchOption.AllDirectories, maxDepth: 5))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root, SearchOption searchOption, int maxDepth)
    {
        var rootDepth = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var currentDepth = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - rootDepth;
            if (currentDepth > maxDepth) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current)
                    .Where(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (searchOption != SearchOption.AllDirectories || currentDepth == maxDepth) continue;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current)
                    .Where(path => !IsIgnoredDirectory(Path.GetFileName(path)));
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static void AddCandidate(IDictionary<string, ScanCandidate> candidates, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var target = ResolveShortcutTarget(path);
        if (!string.IsNullOrWhiteSpace(target) && IsIgnoredExecutable(target)) return;

        var key = NormalizePathKey(!string.IsNullOrWhiteSpace(target) ? target : path);
        if (key.Length == 0 || candidates.ContainsKey(key)) return;

        var name = CleanDisplayName(Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrWhiteSpace(name)) return;

        candidates[key] = new ScanCandidate(
            name,
            path,
            DetectCategory(name, path, target),
            Source: DetectSource(path, target),
            Drive: DetectDrive(path, target));
    }

    private static string? FindPrimaryExecutable(string? folder, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        try
        {
            var normalizedName = NormalizeName(displayName ?? string.Empty);
            return Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(IsUsableExecutable)
                .OrderByDescending(path => NormalizeName(Path.GetFileNameWithoutExtension(path)) == normalizedName)
                .ThenBy(path => Path.GetFileName(path).Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanRegistryPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();
        if (value.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1) return value[1..end];
        }

        var commaIndex = value.IndexOf(',');
        if (commaIndex > 0) value = value[..commaIndex];

        return value.Trim('"', ' ');
    }

    private static bool IsUsableExecutable(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
               File.Exists(path) &&
               !IsIgnoredExecutable(path);
    }

    private static bool IsIgnoredExecutable(string path)
    {
        var fileName = Path.GetFileName(path);
        var ignoredNames = new[]
        {
            "unins", "uninstall", "setup", "install", "update", "crash", "helper",
            "service", "elevate", "bootstrap", "redist", "vcredist"
        };

        return ignoredNames.Any(token => fileName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnoredDirectory(string name)
    {
        var ignoredNames = new[]
        {
            "Windows", "System32", "SysWOW64", "$Recycle.Bin", "Temp", "Cache", "Logs",
            "node_modules", "packages", "Installer", "WinSxS", "Common Files"
        };

        return ignoredNames.Any(token => name.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetectSource(string path, string? target)
    {
        var text = $"{path} {target}".ToLowerInvariant();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).ToLowerInvariant();
        if (text.Contains(@"\start menu\")) return "Start Menu";
        if (text.Contains(@"\desktop\")) return "Desktop";
        if (text.Contains(@"\appdata\")) return "C Profile";
        if (text.Contains(profile)) return "C Profile";
        if (text.Contains(@"\steamlibrary\") || text.Contains(@"\games\") || text.Contains(@"\epic games\") || text.Contains(@"\riot games\")) return "Game Folder";
        if (text.Contains(@"\program files")) return "Program Files";
        return "Registry/Other";
    }

    private static string DetectDrive(string path, string? target)
    {
        var drive = Path.GetPathRoot(!string.IsNullOrWhiteSpace(target) ? target : path);
        return string.IsNullOrWhiteSpace(drive) ? string.Empty : drive.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
    }

    private static string DetectCategory(string name, string path, string? target)
    {
        var text = $"{name} {path} {target}".ToLowerInvariant();
        var gameTokens = new[]
        {
            "steam", "steamapps", "epic games", "riot games", "valorant", "league of legends",
            "battle.net", "battlenet", "ubisoft", "gog galaxy", "ea app", "origin",
            "hoyoplay", "garena", "xbox games", "game", "games"
        };

        return gameTokens.Any(text.Contains) ? "KHO GAME" : "ỨNG DỤNG";
    }

    private static string ResolveShortcutTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            var shell = shellType is null ? null : Activator.CreateInstance(shellType);
            if (shellType is null || shell is null) return path;

            try
            {
                var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
                return shortcut?.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString() ?? path;
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return path;
        }
    }

    private static string NormalizePathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private static string CleanDisplayName(string name)
    {
        return name
            .Replace(" - Shortcut", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToUpperInvariant();
    }

    private static string NormalizeName(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private sealed record ScanCandidate(string Name, string ExecutablePath, string Category, string Source, string Drive, string IconPath = "");

    private sealed record ScanOptions(bool ScanStartMenuAndDesktop, bool ScanUserProfile, bool ScanRegistry, bool ScanDrives);

    private sealed class ScanCandidateChoice(ScanCandidate candidate)
    {
        public ScanCandidate Candidate { get; } = candidate;
        public System.Windows.Controls.CheckBox? CheckBox { get; set; }
    }

    [RelayCommand]
    private async Task RemoveLauncherAsync(LauncherItem item)
    {
        if (MessageBox.Show($"Xóa {item.Name}?", "Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Launchers.Remove(item);
        await SaveAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(LauncherItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        await SaveAsync();
        FilteredLaunchers.Refresh();
    }

    [RelayCommand]
    private async Task ToggleCategoryAsync(LauncherItem item)
    {
        item.Category = item.Category == "KHO GAME" ? "ỨNG DỤNG" : "KHO GAME";
        await SaveAsync();
        FilteredLaunchers.Refresh();
    }

    [RelayCommand]
    private async Task EditIconAsync(LauncherItem item)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn icon launcher",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.IconPath = dialog.FileName;
        await SaveAsync();
        FilteredLaunchers.Refresh();
    }

    public void Reorder(LauncherItem source, LauncherItem target)
    {
        var oldIndex = Launchers.IndexOf(source);
        var newIndex = Launchers.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        Launchers.Move(oldIndex, newIndex);
    }

    public async Task MoveLauncherAsync(LauncherItem source, LauncherItem target)
    {
        Reorder(source, target);
        await SaveAsync();
        FilteredLaunchers.Refresh();
    }

    public async Task SaveAsync()
    {
        if (_isInitializing) return;

        for (var i = 0; i < Launchers.Count; i++)
        {
            Launchers[i].Order = i;
        }

        await _settingsService.SaveAsync(new LauncherSettings
        {
            Launchers = Launchers.ToList(),
            DefaultTab = ActiveTab,
            IsLightTheme = IsLightTheme
        });
    }
}
