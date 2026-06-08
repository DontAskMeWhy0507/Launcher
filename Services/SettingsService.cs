using RevoltLauncher.Models;
using System.IO;
using System.Text.Json;

namespace RevoltLauncher.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _configPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SettingsService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "launchers.json");
    }

    public async Task<LauncherSettings> LoadAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "launchers.json");
            if (!File.Exists(_configPath) ||
                (File.Exists(seedPath) && File.GetLastWriteTimeUtc(seedPath) > File.GetLastWriteTimeUtc(_configPath)))
            {
                if (File.Exists(seedPath))
                {
                    File.Copy(seedPath, _configPath, overwrite: true);
                }
            }

            if (!File.Exists(_configPath))
            {
                return new LauncherSettings();
            }

            await using var stream = File.OpenRead(_configPath);
            return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, _jsonOptions)
                   ?? new LauncherSettings();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _fileLock.WaitAsync();
        try
        {
            await using var stream = File.Create(_configPath);
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
