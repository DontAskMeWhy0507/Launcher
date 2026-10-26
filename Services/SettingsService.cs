using Launcher.Models;
using System.IO;
using System.Text.Json;

namespace Launcher.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _configPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SettingsService()
    {
        _configPath = AppPaths.ConfigPath;
    }

    public async Task<LauncherSettings> LoadAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            AppPaths.EnsureDataDirectories();

            var legacyConfigPath = Path.Combine(AppContext.BaseDirectory, "launchers.json");
            if (!File.Exists(_configPath) && File.Exists(legacyConfigPath))
            {
                File.Copy(legacyConfigPath, _configPath, overwrite: false);
            }

            var seedPath = AppPaths.SeedConfigPath;
            if (!File.Exists(_configPath) && File.Exists(seedPath))
            {
                File.Copy(seedPath, _configPath, overwrite: false);
            }

            if (!File.Exists(_configPath))
            {
                return new LauncherSettings();
            }

            return await ReadSettingsAsync(_configPath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        AppPaths.EnsureDataDirectories();

        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _fileLock.WaitAsync();
        try
        {
            var tempPath = _configPath + ".tmp";
            var backupPath = _configPath + ".bak";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions);
            }

            if (File.Exists(_configPath))
            {
                File.Copy(_configPath, backupPath, overwrite: true);
            }

            File.Move(tempPath, _configPath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<LauncherSettings> ReadSettingsAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, _jsonOptions)
                   ?? new LauncherSettings();
        }
        catch (JsonException) when (File.Exists(_configPath + ".bak"))
        {
            await using var backupStream = File.OpenRead(_configPath + ".bak");
            return await JsonSerializer.DeserializeAsync<LauncherSettings>(backupStream, _jsonOptions)
                   ?? new LauncherSettings();
        }
    }
}
