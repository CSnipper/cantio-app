using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Cantio.Views;

public partial class PasteTextWindow : Window
{
    public string ResultText { get; private set; } = string.Empty;
    public bool IsPsalm { get; private set; }

    public PasteTextWindow(string initialText = "", bool isPsalm = false)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputBox.Text = initialText;
            PsalmCheck.IsChecked = isPsalm;
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        IsPsalm = PsalmCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 3) return;
        var tb = (TextBox)sender;
        int pos = tb.GetCharacterIndexFromPoint(e.GetPosition(tb), true);
        var text = tb.Text;
        int start = pos > 0 ? text.LastIndexOf('\n', pos - 1) + 1 : 0;
        int end = text.IndexOf('\n', pos);
        if (end < 0) end = text.Length;
        tb.Select(start, end - start);
        e.Handled = true;
    }
}
