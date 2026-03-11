using System.Windows;
using Cantor.Services;
using Application = System.Windows.Application;
namespace Cantor;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        var db = new DatabaseService();
        var window = new MainWindow(db);
        window.Show();
    }
}