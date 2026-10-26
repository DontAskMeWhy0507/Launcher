using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.Models;

public sealed partial class LauncherItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _category = "ỨNG DỤNG";

    [ObservableProperty]
    private bool _isDragging;
}
