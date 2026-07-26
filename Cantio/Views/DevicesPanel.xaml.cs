using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Cantio.Views;

public partial class DevicesPanel : UserControl
{
    public DevicesPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Enter w polu oznaczenia = zatwierdzenie (binding jest na LostFocus, więc bez tego wpisana
    /// wartość czekałaby na kliknięcie gdzie indziej).
    /// </summary>
    private void DeviceLabel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>Awaryjne domknięcie: pole znika z ekranu (zamknięcie ustawień) nie może zjeść wpisanej wartości.</summary>
    private void DeviceLabel_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
