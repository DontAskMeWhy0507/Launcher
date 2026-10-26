using System.IO;

namespace Launcher.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Launcher");

    public static string IconDirectory { get; } = Path.Combine(DataDirectory, "Icons");

    public static string ConfigPath { get; } = Path.Combine(DataDirectory, "launchers.json");

    public static string ErrorLogPath { get; } = Path.Combine(DataDirectory, "Launcher-error.log");

    public static string SeedConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "Data", "launchers.json");

    public static string DefaultIconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "apps.png");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IconDirectory);
    }
}
