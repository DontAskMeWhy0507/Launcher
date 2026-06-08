using CommunityToolkit.Mvvm.ComponentModel;

namespace RevoltLauncher.Models;

public sealed partial class LauncherItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _category = "ỨNG DỤNG";

    [ObservableProperty]
    private bool _isDragging;
}
