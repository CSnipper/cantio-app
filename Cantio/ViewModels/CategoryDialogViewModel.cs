using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cantio.ViewModels;

public partial class CategoryDialogViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ObservableCollection<Category> _sharedCategories;

    [ObservableProperty] private ObservableCollection<Category> _categories = [];
    [ObservableProperty] private string _newCategoryName = string.Empty;
    [ObservableProperty] private int _newCategoryNumber = 0;

    public CategoryDialogViewModel(ObservableCollection<Category> sharedCategories, DatabaseService db)
    {
        _db = db;
        _sharedCategories = sharedCategories;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var cats = await _db.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(cats);
        // Zaproponuj kolejny numer
        NewCategoryNumber = Categories.Count > 0 ? Categories.Max(c => c.Number) + 1 : 1;
    }

    [RelayCommand]
    private async Task AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        var cat = new Category
        {
            Name = NewCategoryName.Trim(),
            Number = NewCategoryNumber
        };

        await _db.SaveCategoryAsync(cat);
        await LoadAsync();

        // Odśwież shared collection w SongEditorViewModel
        _sharedCategories.Clear();
        foreach (var c in Categories) _sharedCategories.Add(c);

        NewCategoryName = string.Empty;
        NewCategoryNumber = Categories.Max(c => c.Number) + 1;
    }

    public async Task UpdateCategoryAsync(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.Name)) return;
        await _db.SaveCategoryAsync(category);
        _sharedCategories.Clear();
        foreach (var c in Categories) _sharedCategories.Add(c);
    }

    [RelayCommand]
    private async Task SaveCategories()
    {
        foreach (var cat in Categories)
            await _db.SaveCategoryAsync(cat);
        _sharedCategories.Clear();
        foreach (var c in Categories) _sharedCategories.Add(c);
    }

    [RelayCommand]
    private async Task UpdateCategory(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.Name)) return;
        await _db.SaveCategoryAsync(category);
        _sharedCategories.Clear();
        foreach (var c in Categories) _sharedCategories.Add(c);
    }

    [RelayCommand]
    private async Task DeleteCategory(Category category)
    {
        var r = MessageBox.Show($"Usunąć kategorię \"{category.Name}\"?\nPieśni w tej kategorii pozostaną bez kategorii.",
            "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        await _db.DeleteCategoryAsync(category.Id);
        await LoadAsync();

        _sharedCategories.Clear();
        foreach (var c in Categories) _sharedCategories.Add(c);
    }
}
