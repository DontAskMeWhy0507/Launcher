using System.IO;
using Launcher.Models;
using Launcher.Services;
using Xunit;

namespace Launcher.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempConfigPath;

    public SettingsServiceTests()
    {
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"launcher_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempConfigPath))
            {
                File.Delete(_tempConfigPath);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void LauncherSettings_ShouldHaveSensibleDefaults()
    {
        var settings = new LauncherSettings();

        Assert.Equal("ỨNG DỤNG", settings.DefaultTab);
        Assert.True(settings.OpenFullscreenOnStartup);
        Assert.False(settings.MinimizeAfterLaunch);
        Assert.False(settings.IsLightTheme);
        Assert.NotNull(settings.Launchers);
        Assert.Empty(settings.Launchers);
    }

    [Fact]
    public async Task SettingsService_SaveAndLoad_ShouldPreserveAllSettingsAndVietnameseTexts()
    {
        var service = new SettingsService(_tempConfigPath);

        var originalSettings = new LauncherSettings
        {
            DefaultTab = "KHO GAME",
            OpenFullscreenOnStartup = false,
            MinimizeAfterLaunch = true,
            IsLightTheme = true,
            Launchers =
            [
                new LauncherItem
                {
                    Name = "Liên Minh Huyền Thoại",
                    Category = "KHO GAME",
                    ExecutablePath = @"D:\Games\LOL\LeagueClient.exe",
                    IsFavorite = true
                },
                new LauncherItem
                {
                    Name = "Bộ gõ Tiếng Việt",
                    Category = "ỨNG DỤNG",
                    ExecutablePath = @"C:\Program Files\UniKey\UniKeyNT.exe",
                    IsFavorite = false
                }
            ]
        };

        await service.SaveAsync(originalSettings);
        Assert.True(File.Exists(_tempConfigPath));

        var loaded = await service.LoadAsync();

        Assert.Equal("KHO GAME", loaded.DefaultTab);
        Assert.False(loaded.OpenFullscreenOnStartup);
        Assert.True(loaded.MinimizeAfterLaunch);
        Assert.True(loaded.IsLightTheme);
        Assert.Equal(2, loaded.Launchers.Count);

        Assert.Equal("Liên Minh Huyền Thoại", loaded.Launchers[0].Name);
        Assert.Equal("KHO GAME", loaded.Launchers[0].Category);
        Assert.True(loaded.Launchers[0].IsFavorite);

        Assert.Equal("Bộ gõ Tiếng Việt", loaded.Launchers[1].Name);
        Assert.Equal("ỨNG DỤNG", loaded.Launchers[1].Category);
        Assert.False(loaded.Launchers[1].IsFavorite);
    }

    [Fact]
    public async Task SettingsService_LoadNonExistent_ShouldReturnDefaultSettings()
    {
        var service = new SettingsService(_tempConfigPath);
        var settings = await service.LoadAsync();

        Assert.NotNull(settings);
        Assert.Equal("ỨNG DỤNG", settings.DefaultTab);
    }
}
