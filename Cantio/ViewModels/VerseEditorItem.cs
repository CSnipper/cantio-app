using Cantio.Helpers;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;

namespace Cantio.ViewModels;

public partial class VerseEditorItem : ObservableObject, IVerseDraft
{
    [ObservableProperty] private string _type = "v";   // v, c, b, p, img
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _number = 1;
    [ObservableProperty] private string? _imagePath;
    /// <summary>Obrazek POD tekstem zwrotki (zwrotka zostaje tekstowa) — nie mylić z <see cref="ImagePath"/>.</summary>
    [ObservableProperty] private string? _backgroundImagePath;
    /// <summary>Wydanie lekcjonarza zwrotki — edytor go nie zmienia, ale MUSI go przenieść przez zapis.</summary>
    [ObservableProperty] private string? _lekcjonarz;

    public string Label => Type switch
    {
        "c" => Number > 1 ? $"R{Number}" : "R",
        "b" => Number > 1 ? $"B{Number}" : "B",
        "p" => Number > 1 ? $"P{Number}" : "P",
        "img" => "🖼",
        _ => $"{Number}"
    };

    public string ImageFileName => string.IsNullOrEmpty(ImagePath) ? string.Empty : Path.GetFileName(ImagePath);

    // Ścieżki w bazie są WZGLĘDNE ("images\plik.jpg") — do pokazania nazwy idziemy przez Resolve,
    // żeby tak samo zadziałały stare ścieżki absolutne (pułapka z v1.52).
    public string BackgroundFileName => string.IsNullOrEmpty(BackgroundImagePath)
        ? string.Empty
        : Path.GetFileName(ImageStorage.Resolve(BackgroundImagePath));

    public bool HasBackground => !string.IsNullOrEmpty(BackgroundImagePath);

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));
    partial void OnImagePathChanged(string? value) => OnPropertyChanged(nameof(ImageFileName));

    partial void OnBackgroundImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(BackgroundFileName));
        OnPropertyChanged(nameof(HasBackground));
    }

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

    [RelayCommand]
    private void BrowseBackground()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Obrazy|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
            Title = "Wybierz tło zwrotki"
        };
        if (dlg.ShowDialog() != true) return;
        BackgroundImagePath = ImageStorage.Import(dlg.FileName);
    }

    [RelayCommand]
    private void ClearBackground() => BackgroundImagePath = null;
}
