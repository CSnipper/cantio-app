using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cantio.ViewModels;

public partial class VerseEditorItem : ObservableObject
{
    [ObservableProperty] private string _type = "v";   // v, c, b, p
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _number = 1;

    public string Label => Type switch
    {
        "c" => Number > 1 ? $"R{Number}" : "R",
        "b" => Number > 1 ? $"B{Number}" : "B",
        "p" => Number > 1 ? $"P{Number}" : "P",
        _ => $"{Number}"
    };

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));

    [RelayCommand]
    private void CycleType()
    {
        Type = Type switch
        {
            "v" => "c",
            "c" => "b",
            "b" => "p",
            "p" => "v",
            _ => "v"
        };
    }
}
