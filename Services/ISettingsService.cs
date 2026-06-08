using RevoltLauncher.Models;

namespace RevoltLauncher.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync();
    Task SaveAsync(LauncherSettings settings);
}
