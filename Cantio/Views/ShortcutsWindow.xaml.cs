using Cantio.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Cantio.Views;

public partial class ShortcutsWindow : Window
{
    private readonly ShortcutsViewModel _vm;

    public ShortcutsWindow(ShortcutsViewModel vm, Window owner)
    {
        InitializeComponent();
        _vm = vm;
        _vm.ReloadFromService();
        DataContext = _vm;
        Owner = owner;
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SaveCommand.CanExecute(null))
            _vm.SaveCommand.Execute(null);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _vm.ReloadFromService();
        Close();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetCommand.Execute(null);
    }

    private void ClearShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ShortcutItem item)
            item.Value = string.Empty;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.ReloadFromService();
            Close();
            e.Handled = true;
        }
    }
}
