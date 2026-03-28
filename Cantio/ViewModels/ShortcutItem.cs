using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class ShortcutItem : ObservableObject
{
    public string ActionId { get; init; } = string.Empty;
    public string Name     { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    [ObservableProperty] private string _value     = string.Empty;
    [ObservableProperty] private bool   _isVisible = true;
}
