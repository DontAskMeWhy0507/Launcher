using Microsoft.Win32;
using System.Diagnostics;

namespace RevoltLauncher.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "launcher";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppName) is string value &&
                   value.Contains(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
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
}
