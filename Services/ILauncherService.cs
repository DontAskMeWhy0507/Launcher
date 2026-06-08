using RevoltLauncher.Models;

namespace RevoltLauncher.Services;

public interface ILauncherService
{
    Task LaunchAsync(LauncherItem item);
    Task OpenFileLocationAsync(LauncherItem item);
    Task<string> GetIconPathAsync(string filePath);
}
