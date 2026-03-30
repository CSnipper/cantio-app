using System.IO;
using Cantio.Helpers;
using Cantio.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using Application = System.Windows.Application;
namespace Cantio;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);
            var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cantio");
            Directory.CreateDirectory(dbFolder); // ← musi być PRZED sprawdzeniem pliku
            var dbPath = Path.Combine(dbFolder, "cantio.db");
            if (!File.Exists(dbPath))
            {
                var seedDb = Path.Combine(AppContext.BaseDirectory, "cantio.db");
                if (File.Exists(seedDb))
                    File.Copy(seedDb, dbPath);
            }

            // Zastosuj pending migracje EF Core
            await using (var ctx = new CantioDbContext())
                await ctx.Database.MigrateAsync();

            var db = new DatabaseService();
            var lang = db.GetSettingAsync("language").Result;
            if (lang == null)
            {
                var cfgFile = Path.Combine(dbFolder, "initial_lang.cfg");
                if (File.Exists(cfgFile))
                    lang = File.ReadAllText(cfgFile).Trim();
                lang ??= "pl";
            }
            LocalizationManager.SetLanguage(lang);
            var window = new MainWindow(db);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd startu:\n{ex.Message}\n\n{ex.StackTrace}");
            Shutdown();
        }
    }
}