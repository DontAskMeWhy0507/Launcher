namespace Launcher.Models;

public sealed class LauncherSettings
{
    public List<LauncherItem> Launchers { get; set; } = [];
    public string DefaultTab { get; set; } = "ỨNG DỤNG";
    public bool IsLightTheme { get; set; }
    public bool OpenFullscreenOnStartup { get; set; } = true;
    public bool MinimizeAfterLaunch { get; set; }
}
