using Launcher.Models;
using Xunit;

namespace Launcher.Tests;

public class LauncherItemTests
{
    [Fact]
    public void LauncherItem_Properties_ShouldInitializeAndNotifyChanges()
    {
        var item = new LauncherItem
        {
            Name = "Steam Game",
            Category = "Trò chơi",
            ExecutablePath = "steam://rungameid/730",
            IconPath = @"C:\icons\cs2.png",
            IsFavorite = false
        };

        var changedProperties = new List<string>();
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        item.Name = "Counter-Strike 2";
        item.IsFavorite = true;
        item.Category = "Game";

        Assert.Contains(nameof(LauncherItem.Name), changedProperties);
        Assert.Contains(nameof(LauncherItem.IsFavorite), changedProperties);
        Assert.Contains(nameof(LauncherItem.Category), changedProperties);

        Assert.Equal("Counter-Strike 2", item.Name);
        Assert.True(item.IsFavorite);
        Assert.Equal("Game", item.Category);
    }
}
