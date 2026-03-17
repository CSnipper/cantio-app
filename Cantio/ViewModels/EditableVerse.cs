using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class EditableVerse : ObservableObject
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;
}
