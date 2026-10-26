using Microsoft.Win32;
using System.Diagnostics;

namespace Launcher.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Launcher";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var exePath = GetCurrentExecutablePath();
            if (key?.GetValue(AppName) is not string value ||
                string.IsNullOrWhiteSpace(exePath) ||
                !value.Contains(exePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                var exePath = GetCurrentExecutablePath();
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppName, GetStartupCommand(exePath));
                }
                return;
            }

            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch
        {
            // Registry policy should not prevent the launcher from opening.
        }
    }

    private static string? GetCurrentExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
    }

    private static string GetStartupCommand(string exePath)
    {
        return $"\"{exePath}\" --background";
    }
}
