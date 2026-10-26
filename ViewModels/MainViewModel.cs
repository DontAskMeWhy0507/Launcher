using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Launcher.Models;
using Launcher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Data;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Launcher.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILauncherService _launcherService;
    private readonly IStartupService _startupService;
    private List<ScanCandidate> _lastScanResults = [];
    private CancellationTokenSource? _scanCts;
    private GitHubReleaseAsset? _pendingUpdateInstaller;
    private string _pendingUpdateVersion = string.Empty;
    private const string FavoriteTab = "YÊU THÍCH";
    private const string GameCategory = "KHO GAME";
    private const string AppCategory = "ỨNG DỤNG";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/DontAskMeWhy0507/Launcher/releases/latest";
    private const string LatestReleaseWebUrl = "https://github.com/DontAskMeWhy0507/Launcher/releases/latest";

    private CancellationTokenSource? _saveDebounceCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _activeTab = FavoriteTab;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _isLightTheme;

    [ObservableProperty]
    private bool _openFullscreenOnStartup;

    [ObservableProperty]
    private bool _minimizeAfterLaunch;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isSettingsPage;

    [ObservableProperty]
    private bool _scanStartMenuAndDesktop = true;

    [ObservableProperty]
    private bool _scanRecentSoftware = true;

    [ObservableProperty]
    private bool _scanRegistry = true;

    [ObservableProperty]
    private bool _scanUserProfile = true;

    [ObservableProperty]
    private bool _scanDrives;

    [ObservableProperty]
    private string _scanCustomPath = string.Empty;

    [ObservableProperty]
    private string _scanStatus = "Chưa scan.";


    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private string _updateStatus = "Sẵn sàng kiểm tra bản mới.";

    [ObservableProperty]
    private bool _hasUpdate;

    private bool _isLoaded;
    private bool _isInitializing = true;
    private bool _startupStateLoaded;

    [ObservableProperty]
    private ObservableCollection<LauncherItem> _launchers = [];

    [ObservableProperty]
    private ObservableCollection<ScanResultItem> _scanResults = [];

    [ObservableProperty]
    private ICollectionView _filteredLaunchers;

    partial void OnLaunchersChanged(ObservableCollection<LauncherItem> value)
    {
        FilteredLaunchers = CollectionViewSource.GetDefaultView(value);
        FilteredLaunchers.Filter = FilterLauncher;
    }
    public string[] Tabs { get; } = [FavoriteTab, GameCategory, AppCategory];
    public bool IsLauncherTab => !IsSettingsPage;
    public string CurrentVersion => $"v{FormatVersionNumber(Assembly.GetExecutingAssembly().GetName().Version) ?? "4.0.1"}";

    public MainViewModel(ISettingsService settingsService, ILauncherService launcherService, IStartupService startupService)
    {
        _settingsService = settingsService;
        _launcherService = launcherService;
        _startupService = startupService;
        FilteredLaunchers = CollectionViewSource.GetDefaultView(Launchers);
        FilteredLaunchers.Filter = FilterLauncher;

        _isInitializing = true;
        ActiveTab = FavoriteTab;
        _ = LoadAsync();
        _ = LoadStartupStateAsync();
    }

    private async Task LoadAsync()
    {
        if (_isLoaded) return;
        var settings = await _settingsService.LoadAsync();
        var normalizedDefaultTab = NormalizeTab(settings.DefaultTab);
        var shouldSaveNormalizedSettings = !string.Equals(settings.DefaultTab, normalizedDefaultTab, StringComparison.Ordinal);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsLightTheme = settings.IsLightTheme;
            OpenFullscreenOnStartup = settings.OpenFullscreenOnStartup;
            MinimizeAfterLaunch = settings.MinimizeAfterLaunch;
            ActiveTab = normalizedDefaultTab;
            var newLaunchers = new ObservableCollection<LauncherItem>();
            foreach (var item in settings.Launchers.OrderBy(x => x.Order))
            {
                var normalizedCategory = NormalizeCategory(item.Category);
                if (!string.Equals(item.Category, normalizedCategory, StringComparison.Ordinal))
                {
                    item.Category = normalizedCategory;
                    shouldSaveNormalizedSettings = true;
                }

                newLaunchers.Add(item);
            }
            Launchers = newLaunchers;
            _isLoaded = true;
            _isInitializing = false;
        });

        if (shouldSaveNormalizedSettings)
        {
            await SaveAsync();
        }

        _ = RepairMissingIconsAsync();
    }

    private async Task LoadStartupStateAsync()
    {
        try
        {
            var enabled = await Task.Run(_startupService.IsEnabled);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StartWithWindows = enabled;
                _startupStateLoaded = true;
            });
        }
        catch
        {
            await Application.Current.Dispatcher.InvokeAsync(() => _startupStateLoaded = true);
        }
    }

    private async Task RepairMissingIconsAsync()
    {
        try
        {
            var repaired = false;
            foreach (var item in Launchers.ToList())
            {
                if (!ShouldRepairIcon(item.IconPath))
                {
                    continue;
                }

                var iconPath = await _launcherService.GetIconPathAsync(item.ExecutablePath);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.IconPath = iconPath;
                    repaired = true;
                }, System.Windows.Threading.DispatcherPriority.Background);
            }

            if (repaired)
            {
                await SaveAsync();
            }
        }
        catch
        {
            // Icon repair is best-effort and must not slow or break opening the launcher.
        }
    }

    private bool FilterLauncher(object value)
    {
        if (value is not LauncherItem item)
        {
            return false;
        }

        var matchesTab = ActiveTab switch
        {
            FavoriteTab => item.IsFavorite,
            GameCategory => string.Equals(item.Category, GameCategory, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(item.Category, AppCategory, StringComparison.OrdinalIgnoreCase)
        };

        return matchesTab && (string.IsNullOrWhiteSpace(SearchText)
               || item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string NormalizeTab(string? tab)
    {
        var normalized = NormalizeVietnameseText(tab);
        if (normalized.Contains("YÊU", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("YEU", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("YÃ", StringComparison.OrdinalIgnoreCase))
        {
            return FavoriteTab;
        }

        if (normalized.Contains(GameCategory, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("GAME", StringComparison.OrdinalIgnoreCase))
        {
            return GameCategory;
        }

        return AppCategory;
    }

    private static string NormalizeCategory(string? category)
    {
        var normalized = NormalizeVietnameseText(category);
        return (normalized.Contains(GameCategory, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("GAME", StringComparison.OrdinalIgnoreCase))
            ? GameCategory
            : AppCategory;
    }

    private static string NormalizeVietnameseText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        // Only attempt mojibake repair if string contains typical UTF-8 misread artifacts (e.g. "Ã", "á»", "Ä")
        if (trimmed.Contains('Ã') || trimmed.Contains("á»", StringComparison.Ordinal) || trimmed.Contains('Ä'))
        {
            try
            {
                var repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(trimmed));
                if (!repaired.Contains('\uFFFD') && !repaired.Contains('?'))
                {
                    return repaired;
                }
            }
            catch
            {
            }
        }

        return trimmed;
    }

    partial void OnSearchTextChanged(string value) => FilteredLaunchers.Refresh();

    partial void OnActiveTabChanged(string value)
    {
        IsSettingsPage = false;
        FilteredLaunchers.Refresh();
        if (!_isInitializing)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts = new CancellationTokenSource();
            var token = _saveDebounceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, token);
                    if (!token.IsCancellationRequested)
                    {
                        await SaveAsync();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    partial void OnIsSettingsPageChanged(bool value) => OnPropertyChanged(nameof(IsLauncherTab));

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_startupStateLoaded)
        {
            return;
        }

        _ = Task.Run(() => _startupService.SetEnabled(value));
    }

    partial void OnIsLightThemeChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    partial void OnOpenFullscreenOnStartupChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    partial void OnMinimizeAfterLaunchChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = SaveAsync();
        }
    }

    [RelayCommand]
    public void SelectTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private void OpenSettings() => IsSettingsPage = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsPage = false;

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
    private async Task AddFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục muốn thêm vào launcher",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await AddFileAsync(dialog.SelectedPath);
        }
    }

    [RelayCommand]
    private async Task AddCalculatorAsync()
    {
        var calculatorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe");
        await AddFileAsync(File.Exists(calculatorPath) ? calculatorPath : "calc.exe", "CALCULATOR");
    }

    [RelayCommand]
    private async Task AddClockAsync()
    {
        await AddFileAsync("ms-clock:", "CLOCK");
    }

    [RelayCommand]
    private async Task ScanLaunchersAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanStatus = "Đang scan...";
        ScanResults.Clear();
        var existingKeys = GetExistingLauncherKeys();
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        var options = new ScanOptions(
            ScanStartMenuAndDesktop,
            ScanUserProfile,
            ScanRegistry,
            ScanDrives,
            ScanRecentSoftware,
            ScanCustomPath);

        try
        {
            var candidates = await Task.Run(() => FindInstalledLauncherCandidates(options, token), token);
            var pendingCandidates = FilterNewCandidates(candidates, existingKeys);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var candidate in pendingCandidates.Take(400))
                {
                    ScanResults.Add(new ScanResultItem(candidate));
                }
            });
            _ = LoadScanResultIconsAsync(token);

            _lastScanResults = pendingCandidates;
            ScanStatus = pendingCandidates.Count == 0
                ? "Không tìm thấy ứng dụng/game mới."
                : $"Tìm thấy {pendingCandidates.Count} mục mới. Hiển thị {ScanResults.Count} mục đầu tiên.";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Đã dừng scan.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task LoadScanResultIconsAsync(CancellationToken cancellationToken)
    {
        var items = ScanResults.ToList();
        using var semaphore = new SemaphoreSlim(4);
        var tasks = items.Select(async result =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    var iconPath = await _launcherService.GetIconPathAsync(result.ExecutablePath);
                    if (!string.IsNullOrWhiteSpace(iconPath) && iconPath != AppPaths.DefaultIconPath)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                result.IconPath = iconPath;
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // Icon preview is best-effort; adding the launcher still resolves icons later.
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch { }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        ScanStatus = "Đang dừng scan...";
    }

    [RelayCommand]
    private void SelectAllScanResults()
    {
        foreach (var result in ScanResults)
        {
            result.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearScanResultsSelection()
    {
        foreach (var result in ScanResults)
        {
            result.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task AddSelectedScanResultsAsync()
    {
        var selectedCandidates = ScanResults
            .Where(result => result.IsSelected)
            .Select(result => result.Candidate)
            .ToList();

        await AddSelectedScanCandidatesAsync(selectedCandidates);
        ScanResults = new ObservableCollection<ScanResultItem>(ScanResults.Where(result => !result.IsSelected));
        ScanStatus = selectedCandidates.Count == 0
            ? "Chưa chọn mục nào."
            : $"Đã thêm {selectedCandidates.Count} mục đã chọn.";
    }

    [RelayCommand]
    private void BrowseScanPath()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục muốn scan",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ScanCustomPath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsUpdating) return;

        IsUpdating = true;
        HasUpdate = false;
        _pendingUpdateInstaller = null;
        _pendingUpdateVersion = string.Empty;
        UpdateStatus = "Đang kiểm tra bản mới...";

        try
        {
            var release = await GetLatestReleaseAsync();
            if (release is null)
            {
                UpdateStatus = "Không đọc được thông tin bản mới.";
                return;
            }

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (latestVersion is not null && currentVersion is not null && latestVersion <= currentVersion)
            {
                UpdateStatus = $"Đang dùng bản mới nhất: hiện tại {FormatVersion(currentVersion)}, mới nhất {FormatReleaseVersion(release.TagName, latestVersion)}.";
                return;
            }

            var installer = release.Assets.FirstOrDefault(asset =>
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

            if (installer is null)
            {
                UpdateStatus = "Release mới chưa có file LauncherSetup.exe.";
                return;
            }

            _pendingUpdateInstaller = installer;
            _pendingUpdateVersion = FormatReleaseVersion(release.TagName, latestVersion);
            HasUpdate = true;
            UpdateStatus = $"Có bản mới: hiện tại {FormatVersion(currentVersion)}, mới nhất {FormatReleaseVersion(release.TagName, latestVersion)}. Bấm UPDATE để tải và cài.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Kiểm tra update lỗi: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        if (IsUpdating) return;

        if (_pendingUpdateInstaller is null)
        {
            await CheckForUpdateAsync();
            if (_pendingUpdateInstaller is null) return;
        }

        IsUpdating = true;
        try
        {
            using var httpClient = CreateGitHubHttpClient();
            var downloadPath = Path.Combine(Path.GetTempPath(), _pendingUpdateInstaller.Name);
            var updateVersion = string.IsNullOrWhiteSpace(_pendingUpdateVersion) ? _pendingUpdateInstaller.Name : _pendingUpdateVersion;
            UpdateStatus = $"Đang tải bản {updateVersion} ({_pendingUpdateInstaller.Name})...";
            await using (var input = await httpClient.GetStreamAsync(_pendingUpdateInstaller.DownloadUrl))
            await using (var output = File.Create(downloadPath))
            {
                await input.CopyToAsync(output);
            }

            UpdateStatus = $"Đang mở installer bản {updateVersion}...";
            Process.Start(new ProcessStartInfo(downloadPath)
            {
                UseShellExecute = true,
                Arguments = "/silent /closeapplications"
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update lỗi: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            using var httpClient = CreateGitHubHttpClient();
            var release = await httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseApiUrl);
            if (release is not null && !string.IsNullOrWhiteSpace(release.TagName))
            {
                return release;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // GitHub REST API rate-limited (403/429) -> seamlessly fallback to GitHub Web redirect
        }
        catch (Exception)
        {
            // Network or parsing error on API -> attempt fallback
        }

        return await GetLatestReleaseFromWebRedirectAsync();
    }

    private static async Task<GitHubRelease?> GetLatestReleaseFromWebRedirectAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Launcher-Updater");

            using var response = await httpClient.GetAsync(LatestReleaseWebUrl);
            if (response.Headers.Location is not null)
            {
                var segments = response.Headers.Location.AbsolutePath.TrimEnd('/').Split('/');
                var tagName = segments.LastOrDefault();
                if (!string.IsNullOrWhiteSpace(tagName))
                {
                    return new GitHubRelease
                    {
                        TagName = tagName,
                        Assets =
                        [
                            new GitHubReleaseAsset
                            {
                                Name = "LauncherSetup.exe",
                                DownloadUrl = $"https://github.com/DontAskMeWhy0507/Launcher/releases/download/{tagName}/LauncherSetup.exe"
                            }
                        ]
                    };
                }
            }
        }
        catch
        {
            // Ignore web fallback errors
        }

        return null;
    }

        private static HttpClient CreateGitHubHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Launcher-Updater");
        return httpClient;
    }

    private static Version? ParseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return null;
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static string FormatVersion(Version? version)
    {
        return version is null ? "không rõ" : $"v{FormatVersionNumber(version)}";
    }

    private static string FormatReleaseVersion(string tagName, Version? parsedVersion)
    {
        if (parsedVersion is null) return tagName;
        var formatted = $"v{FormatVersionNumber(parsedVersion)}";
        return tagName.Equals(formatted, StringComparison.OrdinalIgnoreCase) ? formatted : $"{formatted} ({tagName})";
    }

    private static string? FormatVersionNumber(Version? version)
    {
        if (version is null) return null;

        var fieldCount = version.Build >= 0 ? 3 : 2;
        return version.ToString(fieldCount);
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
                    Category = NormalizeCategory(candidate.Category),
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

            ScanStatus = added > 0
                ? $"Đã thêm {added} ứng dụng/game mới."
                : "Chưa thêm ứng dụng/game nào.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task AddFileAsync(string filePath, string? displayName = null)
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

            if (!File.Exists(filePath) && !Directory.Exists(filePath) && !LooksLikeShellCommand(filePath))
            {
                ShowThemedMessage(
                    "Lỗi thêm ứng dụng",
                    $"Không tìm thấy file hoặc thư mục tại đường dẫn:\n\"{filePath}\"\n\nVui lòng kiểm tra lại quyền truy cập hoặc file gốc.");
                return;
            }

            string fileName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(filePath).ToUpperInvariant()
                : displayName.Trim().ToUpperInvariant();
            if (Path.GetFileName(filePath).Equals("calc.exe", StringComparison.OrdinalIgnoreCase)) fileName = "CALCULATOR";
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "ỨNG DỤNG MỚI";

            string category = ActiveTab == GameCategory ? GameCategory : AppCategory;
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
            ShowThemedMessage("Lỗi hệ thống", $"Có lỗi xảy ra khi xử lý file:\n{ex.Message}");
        }
    }


    private static IReadOnlyList<ScanCandidate> FindInstalledLauncherCandidates(ScanOptions options, CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, ScanCandidate>(StringComparer.OrdinalIgnoreCase);

        if (options.ScanStartMenuAndDesktop)
        {
            foreach (var shortcut in EnumerateShortcutLocations())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, shortcut);
            }
        }

        if (options.ScanRecentSoftware)
        {
            foreach (var recentPath in EnumerateRecentSoftwareEntries())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, recentPath, "Recent");
            }
        }

        if (options.ScanRegistry)
        {
            foreach (var registryPath in EnumerateRegistryInstallEntries())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, registryPath);
            }
        }

        if (options.ScanUserProfile)
        {
            foreach (var profilePath in EnumerateUserProfileApplications())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, profilePath);
            }
        }

        if (options.ScanDrives)
        {
            foreach (var drivePath in EnumerateKnownDriveApplications())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, drivePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.CustomPath) && Directory.Exists(options.CustomPath))
        {
            foreach (var customPath in EnumerateCandidateFiles(options.CustomPath, SearchOption.AllDirectories, maxDepth: 5))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, customPath);
            }
        }

        return candidates.Values
            .OrderBy(candidate => NormalizeCategory(candidate.Category) == GameCategory ? 0 : 1)
            .ThenByDescending(candidate => candidate.Source == "Recent")
            .ThenBy(candidate => candidate.Name)
            .ToList();
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

    private static bool ShouldRepairIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return true;
        if (iconPath.Equals("Assets/Icons/apps.png", StringComparison.OrdinalIgnoreCase)) return true;
        return ResolveIconPath(iconPath) is null;
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

    private static IEnumerable<string> EnumerateRecentSoftwareEntries()
    {
        foreach (var path in EnumerateRecentRegistryInstallEntries())
        {
            yield return path;
        }

        var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (Directory.Exists(recentFolder))
        {
            IEnumerable<string> recentShortcuts;
            try
            {
                recentShortcuts = Directory.EnumerateFiles(recentFolder, "*.lnk", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(80);
            }
            catch
            {
                yield break;
            }

            foreach (var shortcut in recentShortcuts)
            {
                yield return shortcut;
            }
        }
    }

    private static IEnumerable<string> EnumerateRecentRegistryInstallEntries()
    {
        var uninstallKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        var entries = new List<(DateTime Date, string Path)>();
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

                    var installDate = ParseInstallDate(appKey.GetValue("InstallDate")?.ToString());
                    if (installDate is null || installDate.Value < DateTime.Today.AddDays(-120)) continue;

                    var displayIcon = CleanRegistryPath(appKey.GetValue("DisplayIcon")?.ToString());
                    if (displayIcon is not null && IsUsableExecutable(displayIcon))
                    {
                        entries.Add((installDate.Value, displayIcon));
                        continue;
                    }

                    var installLocation = CleanRegistryPath(appKey.GetValue("InstallLocation")?.ToString());
                    var exe = FindPrimaryExecutable(installLocation, displayName);
                    if (exe is not null)
                    {
                        entries.Add((installDate.Value, exe));
                    }
                }
            }
        }

        foreach (var entry in entries.OrderByDescending(entry => entry.Date).Take(80))
        {
            yield return entry.Path;
        }
    }

    private static DateTime? ParseInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8) return null;
        return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
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

    private static void AddCandidate(IDictionary<string, ScanCandidate> candidates, string path, string? sourceOverride = null)
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
            Source: sourceOverride ?? DetectSource(path, target),
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

        return gameTokens.Any(text.Contains) ? GameCategory : AppCategory;
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

    private static bool LooksLikeShellCommand(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme)) return true;
        return !path.Contains(Path.DirectorySeparatorChar) &&
               !path.Contains(Path.AltDirectorySeparatorChar) &&
               path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
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

    private sealed record ScanOptions(
        bool ScanStartMenuAndDesktop,
        bool ScanUserProfile,
        bool ScanRegistry,
        bool ScanDrives,
        bool ScanRecentSoftware,
        string CustomPath);


    [RelayCommand]
    private async Task RemoveLauncherAsync(LauncherItem item)
    {
        if (!ShowThemedConfirmation("Xóa launcher", $"Xóa {item.Name}?"))
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
        item.Category = NormalizeCategory(item.Category) == GameCategory ? AppCategory : GameCategory;
        await SaveAsync();
        FilteredLaunchers.Refresh();
    }

    [RelayCommand]
    private async Task EditLauncherAsync(LauncherItem? item)
    {
        if (item is null)
        {
            return;
        }

        var edited = ShowEditLauncherDialog(item);
        if (edited is null)
        {
            return;
        }

        var pathChanged = !string.Equals(item.ExecutablePath, edited.ExecutablePath, StringComparison.Ordinal);
        item.Name = edited.Name;
        item.ExecutablePath = edited.ExecutablePath;
        item.Category = edited.Category;

        if (pathChanged && ShouldRefreshLauncherIcon(item.IconPath))
        {
            item.IconPath = await _launcherService.GetIconPathAsync(item.ExecutablePath);
        }

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

    private static bool ShowThemedConfirmation(string title, string message)
    {
        var palette = CreateDialogPalette();
        var dialog = CreateBaseDialog(title, 430, 210, palette);
        var root = CreateDialogRoot(palette);
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var text = new System.Windows.Controls.TextBlock
        {
            Text = message,
            Foreground = palette.TextBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        root.Children.Add(text);

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        var noButton = CreateDialogButton("Hủy", palette, isPrimary: false);
        var yesButton = CreateDialogButton("Xóa", palette, isPrimary: true);
        noButton.IsCancel = true;
        yesButton.IsDefault = true;
        noButton.Margin = new Thickness(0, 0, 8, 0);
        yesButton.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(noButton);
        buttons.Children.Add(yesButton);

        System.Windows.Controls.Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        dialog.Content = root;

        return dialog.ShowDialog() == true;
    }

    private static void ShowThemedMessage(string title, string message)
    {
        var palette = CreateDialogPalette();
        var dialog = CreateBaseDialog(title, 500, 240, palette);
        var root = CreateDialogRoot(palette);
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var text = new System.Windows.Controls.TextBlock
        {
            Text = message,
            Foreground = palette.TextBrush,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        root.Children.Add(text);

        var okButton = CreateDialogButton("OK", palette, isPrimary: true);
        okButton.Width = 86;
        okButton.IsDefault = true;
        okButton.Click += (_, _) => dialog.DialogResult = true;
        System.Windows.Controls.Grid.SetRow(okButton, 1);
        okButton.HorizontalAlignment = WpfHorizontalAlignment.Right;
        root.Children.Add(okButton);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static EditLauncherResult? ShowEditLauncherDialog(LauncherItem item)
    {
        var palette = CreateDialogPalette();
        var backgroundBrush = palette.BackgroundBrush;
        var panelBrush = palette.PanelBrush;
        var textBrush = palette.TextBrush;
        var borderBrush = palette.BorderBrush;
        var accentBrush = palette.AccentBrush;

        var dialog = new Window
        {
            Title = "Sửa launcher",
            Width = 560,
            Height = 330,
            WindowStartupLocation = Application.Current.MainWindow is { IsLoaded: true, IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow is { IsLoaded: true, IsVisible: true } ? Application.Current.MainWindow : null,
            Background = backgroundBrush
        };

        var root = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(18),
            Background = backgroundBrush
        };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = item.Name,
            Margin = new Thickness(0, 6, 0, 14),
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CaretBrush = textBrush
        };

        var pathBox = new System.Windows.Controls.TextBox
        {
            Text = item.ExecutablePath,
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush,
            CaretBrush = textBrush
        };

        var browseFileButton = new System.Windows.Controls.Button
        {
            Content = "Chọn file",
            Width = 92,
            Margin = new Thickness(8, 0, 0, 0),
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };

        var browseFolderButton = new System.Windows.Controls.Button
        {
            Content = "Chọn folder",
            Width = 104,
            Margin = new Thickness(8, 0, 0, 0),
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };

        var pathRow = new System.Windows.Controls.Grid { Margin = new Thickness(0, 6, 0, 14) };
        pathRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        pathRow.Children.Add(pathBox);
        System.Windows.Controls.Grid.SetColumn(browseFileButton, 1);
        System.Windows.Controls.Grid.SetColumn(browseFolderButton, 2);
        pathRow.Children.Add(browseFileButton);
        pathRow.Children.Add(browseFolderButton);

        var categoryBox = new System.Windows.Controls.ComboBox
        {
            ItemsSource = new[] { GameCategory, AppCategory },
            SelectedItem = NormalizeCategory(item.Category),
            MinHeight = 34,
            Margin = new Thickness(0, 6, 0, 14),
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        if (categoryBox.SelectedItem is null)
        {
            categoryBox.SelectedIndex = 1;
        }

        var statusText = new System.Windows.Controls.TextBlock
        {
            Foreground = WpfBrushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Hủy",
            Width = 86,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Background = panelBrush,
            Foreground = textBrush,
            BorderBrush = borderBrush
        };
        var saveButton = new System.Windows.Controls.Button
        {
            Content = "Lưu",
            Width = 86,
            IsDefault = true,
            Background = accentBrush,
            Foreground = WpfBrushes.White,
            BorderBrush = accentBrush
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);

        browseFileButton.Click += (_, _) =>
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Chọn shortcut hoặc file chạy",
                Filter = "Shortcut/Executable (*.lnk;*.exe)|*.lnk;*.exe|All files (*.*)|*.*"
            };

            if (fileDialog.ShowDialog(dialog) == true)
            {
                pathBox.Text = fileDialog.FileName;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    nameBox.Text = Path.GetFileNameWithoutExtension(fileDialog.FileName);
                }
            }
        };

        browseFolderButton.Click += (_, _) =>
        {
            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Chọn thư mục cho launcher",
                UseDescriptionForTitle = true
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                pathBox.Text = folderDialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    nameBox.Text = Path.GetFileName(folderDialog.SelectedPath);
                }
            }
        };

        saveButton.Click += (_, _) =>
        {
            var editedName = nameBox.Text.Trim();
            var editedPath = pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(editedName))
            {
                statusText.Text = "Tên launcher không được để trống.";
                return;
            }

            if (string.IsNullOrWhiteSpace(editedPath))
            {
                statusText.Text = "Đường dẫn không được để trống.";
                return;
            }

            dialog.DialogResult = true;
        };

        AddLabeledControl(root, "Tên", nameBox, 0, textBrush);
        AddLabeledControl(root, "Đường dẫn", pathRow, 1, textBrush);
        AddLabeledControl(root, "Loại", categoryBox, 2, textBrush);
        System.Windows.Controls.Grid.SetRow(statusText, 3);
        root.Children.Add(statusText);
        System.Windows.Controls.Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        dialog.Content = root;

        return dialog.ShowDialog() == true
            ? new EditLauncherResult(nameBox.Text.Trim(), pathBox.Text.Trim(), NormalizeCategory(categoryBox.SelectedItem?.ToString()))
            : null;
    }

    private static void AddLabeledControl(
        System.Windows.Controls.Grid root,
        string label,
        UIElement control,
        int row,
        System.Windows.Media.Brush textBrush)
    {
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush
        });
        panel.Children.Add(control);
        System.Windows.Controls.Grid.SetRow(panel, row);
        root.Children.Add(panel);
    }

    private static bool ShouldRefreshLauncherIcon(string iconPath)
    {
        return string.IsNullOrWhiteSpace(iconPath) ||
               iconPath.Equals(AppPaths.DefaultIconPath, StringComparison.OrdinalIgnoreCase) ||
               !File.Exists(iconPath);
    }

    private static DialogPalette CreateDialogPalette()
    {
        var isLightTheme = (Application.Current.MainWindow?.DataContext as MainViewModel)?.IsLightTheme == true;
        return new DialogPalette(
            new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromRgb(248, 250, 252)
                : System.Windows.Media.Color.FromRgb(15, 15, 22)),
            new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromRgb(255, 255, 255)
                : System.Windows.Media.Color.FromRgb(24, 24, 34)),
            new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromRgb(17, 24, 39)
                : System.Windows.Media.Color.FromRgb(229, 231, 235)),
            new System.Windows.Media.SolidColorBrush(isLightTheme
                ? System.Windows.Media.Color.FromRgb(203, 213, 225)
                : System.Windows.Media.Color.FromRgb(55, 65, 81)),
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)));
    }

    private static Window CreateBaseDialog(string title, double width, double height, DialogPalette palette)
    {
        var mainWin = Application.Current.MainWindow;
        var hasValidOwner = mainWin is { IsLoaded: true, IsVisible: true };

        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            WindowStartupLocation = hasValidOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Owner = hasValidOwner ? mainWin : null,
            Background = palette.BackgroundBrush
        };
    }

    private static System.Windows.Controls.Grid CreateDialogRoot(DialogPalette palette)
    {
        return new System.Windows.Controls.Grid
        {
            Margin = new Thickness(18),
            Background = palette.BackgroundBrush
        };
    }

    private static System.Windows.Controls.Button CreateDialogButton(string content, DialogPalette palette, bool isPrimary)
    {
        var brush = isPrimary ? palette.AccentBrush : palette.PanelBrush;
        return new System.Windows.Controls.Button
        {
            Content = content,
            Width = 86,
            MinHeight = 34,
            Background = brush,
            Foreground = isPrimary ? WpfBrushes.White : palette.TextBrush,
            BorderBrush = isPrimary ? palette.AccentBrush : palette.BorderBrush
        };
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
            IsLightTheme = IsLightTheme,
            OpenFullscreenOnStartup = OpenFullscreenOnStartup,
            MinimizeAfterLaunch = MinimizeAfterLaunch
        });
    }
}

internal sealed record EditLauncherResult(string Name, string ExecutablePath, string Category);

internal sealed record DialogPalette(
    System.Windows.Media.Brush BackgroundBrush,
    System.Windows.Media.Brush PanelBrush,
    System.Windows.Media.Brush TextBrush,
    System.Windows.Media.Brush BorderBrush,
    System.Windows.Media.Brush AccentBrush);

public sealed record ScanCandidate(string Name, string ExecutablePath, string Category, string Source, string Drive, string IconPath = "");

public sealed partial class ScanResultItem(ScanCandidate candidate) : ObservableObject
{
    internal ScanCandidate Candidate { get; } = candidate;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _iconPath = string.IsNullOrWhiteSpace(candidate.IconPath)
        ? AppPaths.DefaultIconPath
        : candidate.IconPath;

    public string Name => Candidate.Name;
    public string ExecutablePath => Candidate.ExecutablePath;
    public string Category => Candidate.Category;
    public string Source => Candidate.Source;
    public string Drive => Candidate.Drive;
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;
}
