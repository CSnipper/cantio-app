using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class VerseEditorItem : ObservableObject
{
    [ObservableProperty] private string _type = "v";   // v, c, b
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _number = 1;

    public string Label => Type switch
    {
        "c" => Number > 1 ? $"R{Number}" : "R",
        "b" => Number > 1 ? $"B{Number}" : "B",
        _ => $"{Number}"
    };

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));
}
