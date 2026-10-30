using System.IO;
using System.Text;
using Xunit;

namespace Launcher.Tests;

public class EncodingAndFontTests
{
    private static readonly string[] Extensions = [".xaml", ".cs", ".json", ".iss", ".manifest"];
    private static readonly string[] ExcludeDirs = ["bin", "obj", "dist", "Output", ".git", ".vs", "scripts", "tests"];

    private static readonly string[] MojibakePatterns =
    [
        "áº", "á»", "Ä‘", "Æ°", "Â·",
        "\u00C3\u00A0", "\u00C3\u00A1", "\u00C3\u00AA", "\u00C3\u00B4",
        "\u00C3\u00A9", "\u00C3\u00A8", "\u00C3\u00BD", "\u00C3\u00B2",
        "\u00C3\u00B3", "\u00C3\u00B9", "\u00C3\u00BA"
    ];

    [Fact]
    public void AllSourceFiles_ShouldNotContainMojibakeOrCorruptedCharacters()
    {
        var rootDir = GetProjectRootDir();
        Assert.True(Directory.Exists(rootDir), $"Root directory not found: {rootDir}");

        var files = Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => !ExcludeDirs.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar) || f.Contains("/" + d + "/")))
            .ToList();

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootDir, file);
            var lines = File.ReadAllLines(file, Encoding.UTF8);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Check for C1 control codes (0x80 to 0x9F) which indicate mis-decoded bytes
                for (var j = 0; j < line.Length; j++)
                {
                    var ch = line[j];
                    if (ch >= 0x80 && ch <= 0x9F)
                    {
                        violations.Add($"{relativePath}:{i + 1} - Contains C1 Control char U+{((int)ch):X4}: {line.Trim()}");
                        break;
                    }
                }

                // Check for known double-encoded UTF-8 sequences
                foreach (var pattern in MojibakePatterns)
                {
                    if (line.Contains(pattern, StringComparison.Ordinal))
                    {
                        // Exclude the detector code & comment in MainViewModel.cs
                        if (relativePath.Contains("MainViewModel.cs") && (line.Contains("Contains('Ã')") || line.Contains("// Only attempt mojibake repair")))
                        {
                            continue;
                        }

                        violations.Add($"{relativePath}:{i + 1} - Contains mojibake pattern '{pattern}': {line.Trim()}");
                        break;
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Found {violations.Count} font/mojibake errors in source files:\n" + string.Join("\n", violations.Take(15)));
    }

    [Fact]
    public void MainWindowXaml_MustContainCorrectEnglishTexts()
    {
        var rootDir = GetProjectRootDir();
        var xamlPath = Path.Combine(rootDir, "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), "MainWindow.xaml file not found");

        var content = File.ReadAllText(xamlPath, Encoding.UTF8);

        // Assert crucial UI texts are properly rendered in English without encoding bugs
        Assert.Contains("Text=\"OFF\"", content);
        Assert.Contains("Value=\"ON\"", content);
        Assert.Contains("Text=\"System Settings\"", content);
        Assert.Contains("Text=\"LIBRARY &amp; APP MANAGEMENT\"", content);
        Assert.Contains("Text=\"Auto-Scan &amp; Cleanup Applications\"", content);
        Assert.Contains("Text=\"CLEAN MISSING APPS\"", content);
        Assert.Contains("Text=\"AUTO SCAN\"", content);
        Assert.Contains("Text=\"STOP\"", content);
        Assert.Contains("Content=\"Recent Software\"", content);
        Assert.Contains("Content=\"Game Drives (Steam/Epic/D:)\"", content);
        Assert.Contains("Text=\"BROWSE FOLDER\"", content);
        Assert.Contains("Text=\"SELECT ALL\"", content);
        Assert.Contains("Text=\"DESELECT ALL\"", content);
        Assert.Contains("Text=\"ADD SELECTED\"", content);
        Assert.Contains("Text=\"BEHAVIOR &amp; STARTUP\"", content);
        Assert.Contains("Text=\"Start with Windows\"", content);
        Assert.Contains("Text=\"Automatically open Launcher when logging into Windows.\"", content);
        Assert.Contains("Text=\"Launch in Fullscreen\"", content);
        Assert.Contains("Text=\"Minimize After Launch\"", content);
        Assert.Contains("Text=\"APPEARANCE &amp; UPDATES\"", content);
        Assert.Contains("Text=\"Light Theme\"", content);
        Assert.Contains("Text=\"Launcher Updates\"", content);
        Assert.Contains("Text=\"CHECK NOW\"", content);
        Assert.Contains("Text=\"UPDATE\"", content);
        Assert.Contains("Text=\"No favorite applications yet.\"", content);
    }

    private static string GetProjectRootDir()
    {
        var current = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(current);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Launcher.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
