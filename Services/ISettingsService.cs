using Launcher.Models;

namespace Launcher.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync();
    Task SaveAsync(LauncherSettings settings);
}
