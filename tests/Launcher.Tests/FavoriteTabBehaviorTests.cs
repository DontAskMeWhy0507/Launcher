using System.Collections.ObjectModel;
using Launcher.Models;
using Launcher.Services;
using Launcher.ViewModels;
using Xunit;

namespace Launcher.Tests;

public class FavoriteTabBehaviorTests
{
    [Fact]
    public void LauncherSettings_DefaultTab_MustBeFavoriteTab()
    {
        var settings = new LauncherSettings();
        Assert.Equal(MainViewModel.FavoriteTab, settings.DefaultTab);
        Assert.Equal("FAVORITES", settings.DefaultTab);
    }

    [Fact]
    public void IsFavoriteTab_ShouldBeTrueOnlyWhenActiveTabIsFavorite()
    {
        var vm = CreateTestViewModel();

        vm.ActiveTab = MainViewModel.FavoriteTab;
        Assert.True(vm.IsFavoriteTab);

        vm.ActiveTab = MainViewModel.GameCategory;
        Assert.False(vm.IsFavoriteTab);

        vm.ActiveTab = MainViewModel.AppCategory;
        Assert.False(vm.IsFavoriteTab);
    }

    [Fact]
    public void IsFavoriteTabEmpty_WhenNoFavorites_ShouldBeTrue()
    {
        var vm = CreateTestViewModel();
        vm.ActiveTab = MainViewModel.FavoriteTab;
        vm.Launchers = new ObservableCollection<LauncherItem>
        {
            new() { Name = "App 1", IsFavorite = false, Category = MainViewModel.AppCategory }
        };

        Assert.True(vm.IsFavoriteTabEmpty);
    }

    [Fact]
    public void IsFavoriteTabEmpty_WhenHasFavorite_ShouldBeFalse()
    {
        var vm = CreateTestViewModel();
        vm.ActiveTab = MainViewModel.FavoriteTab;
        vm.Launchers = new ObservableCollection<LauncherItem>
        {
            new() { Name = "App 1", IsFavorite = true, Category = MainViewModel.AppCategory }
        };

        Assert.False(vm.IsFavoriteTabEmpty);
    }

    [Fact]
    public void IsFavoriteTabEmpty_WhenNotInFavoriteTab_ShouldBeFalse()
    {
        var vm = CreateTestViewModel();
        vm.ActiveTab = MainViewModel.GameCategory;
        vm.Launchers = new ObservableCollection<LauncherItem>();

        Assert.False(vm.IsFavoriteTabEmpty);
    }

    private static MainViewModel CreateTestViewModel()
    {
        return new MainViewModel(new FakeSettingsService(), new FakeLauncherService(), new FakeStartupService());
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync() => Task.FromResult(new LauncherSettings());
        public Task SaveAsync(LauncherSettings settings) => Task.CompletedTask;
    }

    private sealed class FakeLauncherService : ILauncherService
    {
        public Task LaunchAsync(LauncherItem item) => Task.CompletedTask;
        public Task OpenFileLocationAsync(LauncherItem item) => Task.CompletedTask;
        public Task<string> GetIconPathAsync(string filePath) => Task.FromResult(string.Empty);
        public bool IsValid(LauncherItem item) => true;
    }

    private sealed class FakeStartupService : IStartupService
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
