using System.Windows;

namespace Cantio.Views;

public enum OverwriteChoice { Overwrite, AddNew, Cancel }

public partial class ConfirmOverwriteWindow : Window
{
    public OverwriteChoice Result { get; private set; } = OverwriteChoice.Cancel;

    public ConfirmOverwriteWindow(string setlistName)
    {
        InitializeComponent();
        QuestionText.Text = $"Nadpisać zestaw \"{setlistName}\"?";
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Result = OverwriteChoice.Overwrite;
        Close();
    }

    private void AddNew_Click(object sender, RoutedEventArgs e)
    {
        Result = OverwriteChoice.AddNew;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Result = OverwriteChoice.Cancel;
        Close();
    }
}
