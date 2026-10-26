using Launcher.Models;

namespace Launcher.Services;

public interface ILauncherService
{
    Task LaunchAsync(LauncherItem item);
    Task OpenFileLocationAsync(LauncherItem item);
    Task<string> GetIconPathAsync(string filePath);
    bool IsValid(LauncherItem item);
}
