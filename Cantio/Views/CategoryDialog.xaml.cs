using Cantio.Models;
using Cantio.Services;
using Cantio.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Cantio.Views;

public partial class CategoryDialog : Window
{
    private readonly CategoryDialogViewModel _vm;

    public CategoryDialog(ObservableCollection<Category> categories, DatabaseService db)
    {
        InitializeComponent();
        _vm = new CategoryDialogViewModel(categories, db);
        DataContext = _vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void CategoryName_LostFocus(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("LostFocus!");

        if (sender is TextBox tb && tb.DataContext is Category cat)
            await _vm.UpdateCategoryAsync(cat);
    }
}


