using Cantio.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cantio.Views;

public partial class CategoryEditorWindow : Window
{
    private readonly DisplayViewModel _vm;

    public CategoryEditorWindow(DisplayViewModel vm, Window owner)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        Owner = owner;
        Loaded += (_, _) => NameBox.Focus();
        // Wiersz w trakcie zmiany nazwy po zamknięciu okna zostałby „IsEditing" — a lista główna
        // ignoruje kliknięcia w takie pozycje.
        Closed += (_, _) => { foreach (var c in _vm.CategoryItems) c.IsEditing = false; };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Enter działa tylko w polach tekstowych (dodaj / zapisz nazwę przez KeyBinding);
    // na przycisku z fokusem nie klika go — usuwanie nie może się odpalić przypadkiem.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.OriginalSource is not TextBox)
            e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
