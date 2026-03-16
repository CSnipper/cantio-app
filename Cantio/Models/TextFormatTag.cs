using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.Models;

public partial class TextFormatTag : ObservableObject
{
    [ObservableProperty] string _name = string.Empty;
    [ObservableProperty] string _color = string.Empty;
    [ObservableProperty] bool _bold;
    [ObservableProperty] bool _italic;
    [ObservableProperty] bool _underline;
    [ObservableProperty] string _shortcutKey = string.Empty; // np. "Z" → Ctrl+Z
}
