using System.Windows;

namespace Cantio.Views;

public partial class PasteTextWindow : Window
{
    public string ResultText { get; private set; } = string.Empty;

    public PasteTextWindow(string initialText = "")
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            InputBox.Text = initialText;
            InputBox.Focus();
            InputBox.CaretIndex = InputBox.Text.Length;
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
