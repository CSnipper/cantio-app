using System.Windows;
using System.Windows.Controls;
using Cantio.ViewModels;

namespace Cantio.Views;

public partial class RemoteControlTab : UserControl
{
    public RemoteControlTab()
    {
        InitializeComponent();
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteControlViewModel vm && !string.IsNullOrEmpty(vm.LocalUrl))
            Clipboard.SetText(vm.LocalUrl);
    }
}
 
