using Cantio.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;

namespace Cantio.ViewModels;

public partial class VerseEditorItem : ObservableObject
{
    [ObservableProperty] private string _type = "v";   // v, c, b, p, img
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _number = 1;
    [ObservableProperty] private string? _imagePath;

    public string Label => Type switch
    {
        "c" => Number > 1 ? $"R{Number}" : "R",
        "b" => Number > 1 ? $"B{Number}" : "B",
        "p" => Number > 1 ? $"P{Number}" : "P",
        "img" => "🖼",
        _ => $"{Number}"
    };

    public string ImageFileName => string.IsNullOrEmpty(ImagePath) ? string.Empty : Path.GetFileName(ImagePath);

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));
    partial void OnImagePathChanged(string? value) => OnPropertyChanged(nameof(ImageFileName));

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

    [RelayCommand]
    private void BrowseImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Obrazki (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Wszystkie pliki (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        ImagePath = ImageStorage.Import(dlg.FileName);
    }
}
