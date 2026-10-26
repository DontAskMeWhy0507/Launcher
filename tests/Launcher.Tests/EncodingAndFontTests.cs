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
    public void MainWindowXaml_MustContainCorrectVietnameseTexts()
    {
        var rootDir = GetProjectRootDir();
        var xamlPath = Path.Combine(rootDir, "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), "MainWindow.xaml file not found");

        var content = File.ReadAllText(xamlPath, Encoding.UTF8);

        // Assert crucial Vietnamese texts are properly rendered without encoding bugs
        Assert.Contains("Text=\"TẮT\"", content);
        Assert.Contains("Value=\"BẬT\"", content);
        Assert.Contains("Text=\"Cài đặt hệ thống\"", content);
        Assert.Contains("Text=\"QUẢN LÝ THƯ VIỆN &amp; ỨNG DỤNG\"", content);
        Assert.Contains("Text=\"Quét tự động &amp; Dọn dẹp ứng dụng\"", content);
        Assert.Contains("Text=\"DỌN DẸP APP ĐÃ GỠ\"", content);
        Assert.Contains("Text=\"QUÉT TỰ ĐỘNG\"", content);
        Assert.Contains("Text=\"DỪNG\"", content);
        Assert.Contains("Content=\"Phần mềm gần đây\"", content);
        Assert.Contains("Content=\"Ổ đĩa game (Steam/Epic/D:)\"", content);
        Assert.Contains("Text=\"CHỌN THƯ MỤC\"", content);
        Assert.Contains("Text=\"CHỌN HẾT\"", content);
        Assert.Contains("Text=\"BỎ CHỌN\"", content);
        Assert.Contains("Text=\"THÊM MỤC ĐÃ CHỌN\"", content);
        Assert.Contains("Text=\"HÀNH VI &amp; KHỞI ĐỘNG\"", content);
        Assert.Contains("Text=\"Khởi động cùng Windows\"", content);
        Assert.Contains("Text=\"Tự động mở launcher khi đăng nhập vào hệ điều hành.\"", content);
        Assert.Contains("Text=\"Mở toàn màn hình khi khởi động\"", content);
        Assert.Contains("Text=\"Tự thu nhỏ sau khi khởi chạy ứng dụng\"", content);
        Assert.Contains("Text=\"GIAO DIỆN &amp; BẢN CẬP NHẬT\"", content);
        Assert.Contains("Text=\"Giao diện Sáng (Light Theme)\"", content);
        Assert.Contains("Text=\"Cập nhật Launcher\"", content);
        Assert.Contains("Text=\"KIỂM TRA\"", content);
        Assert.Contains("Text=\"UPDATE\"", content);
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
