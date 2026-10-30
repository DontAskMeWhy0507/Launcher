using System.Text;
using Launcher.ViewModels;
using Xunit;

namespace Launcher.Tests;

public class VietnameseNormalizationTests
{
    [Theory]
    [InlineData("Ứng dụng", "Ứng dụng")]
    [InlineData("Trò chơi", "Trò chơi")]
    [InlineData("Yêu thích", "Yêu thích")]
    [InlineData("Cài đặt hệ thống", "Cài đặt hệ thống")]
    [InlineData("Khởi động cùng Windows", "Khởi động cùng Windows")]
    [InlineData("  Ứng dụng  ", "Ứng dụng")]
    public void NormalizeVietnameseText_ShouldPreserveCorrectDiacritics(string input, string expected)
    {
        var result = MainViewModel.NormalizeVietnameseText(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeVietnameseText_ShouldHandleNullAndEmpty()
    {
        Assert.Equal(string.Empty, MainViewModel.NormalizeVietnameseText(null));
        Assert.Equal(string.Empty, MainViewModel.NormalizeVietnameseText("   "));
    }

    [Fact]
    public void NormalizeVietnameseText_ShouldRepairDoubleEncodedUtf8()
    {
        // Simulate UTF-8 bytes decoded as Latin-1 (the classic mojibake bug)
        var original = "Ứng dụng";
        var doubleEncoded = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(original));

        var repaired = MainViewModel.NormalizeVietnameseText(doubleEncoded);
        Assert.Equal(original, repaired);
    }

    [Theory]
    [InlineData("FAVORITES", MainViewModel.FavoriteTab)]
    [InlineData("favorites", MainViewModel.FavoriteTab)]
    [InlineData("Yêu thích", MainViewModel.FavoriteTab)]
    [InlineData("YEU THICH", MainViewModel.FavoriteTab)]
    [InlineData("yeu thich", MainViewModel.FavoriteTab)]
    [InlineData("GAMES", MainViewModel.GameCategory)]
    [InlineData("games", MainViewModel.GameCategory)]
    [InlineData("KHO GAME", MainViewModel.GameCategory)]
    [InlineData("Trò chơi", MainViewModel.GameCategory)]
    [InlineData("GAME", MainViewModel.GameCategory)]
    [InlineData("game", MainViewModel.GameCategory)]
    [InlineData("APPS", MainViewModel.AppCategory)]
    [InlineData("apps", MainViewModel.AppCategory)]
    [InlineData("Ứng dụng", MainViewModel.AppCategory)]
    [InlineData("App", MainViewModel.AppCategory)]
    [InlineData("Unknown", MainViewModel.AppCategory)]
    [InlineData("", MainViewModel.AppCategory)]
    [InlineData(null, MainViewModel.AppCategory)]
    public void NormalizeTab_ShouldMapCorrectly(string? input, string expected)
    {
        var result = MainViewModel.NormalizeTab(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("GAMES", MainViewModel.GameCategory)]
    [InlineData("games", MainViewModel.GameCategory)]
    [InlineData("KHO GAME", MainViewModel.GameCategory)]
    [InlineData("Trò chơi", MainViewModel.GameCategory)]
    [InlineData("Game", MainViewModel.GameCategory)]
    [InlineData("GAME", MainViewModel.GameCategory)]
    [InlineData("APPS", MainViewModel.AppCategory)]
    [InlineData("apps", MainViewModel.AppCategory)]
    [InlineData("Ứng dụng", MainViewModel.AppCategory)]
    [InlineData("App", MainViewModel.AppCategory)]
    [InlineData("Utility", MainViewModel.AppCategory)]
    [InlineData(null, MainViewModel.AppCategory)]
    public void NormalizeCategory_ShouldMapCorrectly(string? input, string expected)
    {
        var result = MainViewModel.NormalizeCategory(input);
        Assert.Equal(expected, result);
    }
}
