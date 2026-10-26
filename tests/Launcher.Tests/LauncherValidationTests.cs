using Launcher.Models;
using Launcher.Services;
using Xunit;

namespace Launcher.Tests;

public class LauncherValidationTests
{
    private readonly LauncherService _service = new();

    [Fact]
    public void IsValid_WithExistingFile_ShouldReturnTrue()
    {
        var testPath = typeof(LauncherValidationTests).Assembly.Location;
        var item = new LauncherItem { Name = "Test App", ExecutablePath = testPath };

        Assert.True(_service.IsValid(item));
    }

    [Fact]
    public void IsValid_WithExistingSystemFolder_ShouldReturnTrue()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var item = new LauncherItem { Name = "Windows Folder", ExecutablePath = winDir };

        Assert.True(_service.IsValid(item));
    }

    [Fact]
    public void IsValid_WithNonExistentPath_ShouldReturnFalse()
    {
        var item = new LauncherItem { Name = "Ghost App", ExecutablePath = @"C:\NonExistent_Dir_12345\ghost.exe" };

        Assert.False(_service.IsValid(item));
    }

    [Fact]
    public void IsValid_WithEmptyOrNullPath_ShouldReturnFalse()
    {
        Assert.False(_service.IsValid(new LauncherItem { Name = "Empty", ExecutablePath = "" }));
        Assert.False(_service.IsValid(new LauncherItem { Name = "Whitespace", ExecutablePath = "   " }));
        Assert.False(_service.IsValid(new LauncherItem { Name = "Null", ExecutablePath = null! }));
    }

    [Theory]
    [InlineData("steam://rungameid/730")]
    [InlineData("steam://rungameid/570")]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch")]
    [InlineData("battlenet://WoW")]
    [InlineData("https://store.steampowered.com")]
    [InlineData("http://localhost:8080")]
    public void IsValid_WithUriProtocols_ShouldReturnTrue(string protocolUri)
    {
        var item = new LauncherItem { Name = "Game Protocol", ExecutablePath = protocolUri };
        Assert.True(_service.IsValid(item));
    }

    [Theory]
    [InlineData("calc.exe")]
    [InlineData("calc")]
    [InlineData("explorer.exe")]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("control.exe")]
    public void IsValid_WithWellKnownShellCommands_ShouldReturnTrue(string command)
    {
        var item = new LauncherItem { Name = "Shell Utility", ExecutablePath = command };
        Assert.True(_service.IsValid(item));
    }
}
