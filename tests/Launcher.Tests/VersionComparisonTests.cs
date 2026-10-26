using Launcher.ViewModels;
using Xunit;

namespace Launcher.Tests;

public class VersionComparisonTests
{
    [Theory]
    [InlineData("v4.1.1", 4, 1, 1)]
    [InlineData("V4.1.1", 4, 1, 1)]
    [InlineData("4.1.1", 4, 1, 1)]
    [InlineData("v4.0.0", 4, 0, 0)]
    [InlineData("v3.1", 3, 1, -1)]
    [InlineData("  v4.2.0  ", 4, 2, 0)]
    public void ParseVersion_ValidTags_ShouldParseCorrectly(string tag, int major, int minor, int build)
    {
        var version = MainViewModel.ParseVersion(tag);
        Assert.NotNull(version);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        if (build >= 0)
        {
            Assert.Equal(build, version.Build);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("release-candidate")]
    public void ParseVersion_InvalidTags_ShouldReturnNull(string? tag)
    {
        var version = MainViewModel.ParseVersion(tag);
        Assert.Null(version);
    }

    [Fact]
    public void VersionComparison_ShouldCorrectlyIdentifyNewerVersions()
    {
        var current = MainViewModel.ParseVersion("4.1.0");
        var newer = MainViewModel.ParseVersion("v4.1.1");
        var older = MainViewModel.ParseVersion("v4.0.1");
        var equal = MainViewModel.ParseVersion("4.1.0");

        Assert.NotNull(current);
        Assert.NotNull(newer);
        Assert.NotNull(older);
        Assert.NotNull(equal);

        Assert.True(newer > current);
        Assert.True(older < current);
        Assert.False(equal > current);
    }

    [Fact]
    public void FormatVersionNumber_ShouldFormatAccurately()
    {
        var v3 = new Version(4, 1, 1);
        Assert.Equal("4.1.1", MainViewModel.FormatVersionNumber(v3));

        var v2 = new Version(4, 2);
        Assert.Equal("4.2", MainViewModel.FormatVersionNumber(v2));

        Assert.Null(MainViewModel.FormatVersionNumber(null));
    }
}
