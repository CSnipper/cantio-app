using System.IO;
using System.Linq;
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
        var dbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cantio");

        // Tryb wstępny z pliku instalatora — musi być znany ZANIM cokolwiek może rzucić,
        // inaczej awaria startu na mini PC skończyłaby się wiszącym MessageBoxem.
        var modeFileValue = AppMode.ReadConfigFile(dbFolder);
        AppMode.Initialize(AppMode.Resolve(null, modeFileValue));

        // Rdzeń (Cantio.Core) nie zna WPF — czcionki systemowe wstrzykuje warstwa okienkowa.
        // Bez tej rejestracji protokół wysyła pustą listę systemFonts (serwer na Linuksie
        // zrobi to samo celowo). Leniwie: enumeracja odpala się przy pierwszym odczycie.
        FontCatalog.RegisterSystemFonts(() =>
        {
            try
            {
                return System.Windows.Media.Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .Where(n => !string.IsNullOrWhiteSpace(n));
            }
            catch { return []; }
        });

        try
        {
            base.OnStartup(e);
            AppLog.WriteSessionStart();
            Directory.CreateDirectory(dbFolder); // ← musi być PRZED sprawdzeniem pliku
            var dbPath = Path.Combine(dbFolder, "cantio.db");
            bool isFirstRun = !File.Exists(dbPath);
            if (isFirstRun)
            {
                var seedDb = Path.Combine(AppContext.BaseDirectory, "cantio.db");
                if (File.Exists(seedDb))
                    File.Copy(seedDb, dbPath);
            }

            // Migracje EF Core. Część z nich (np. ZmienCategoryIdNaNullableWSong) to w SQLite
            // przebudowa tabeli — przerwana w połowie zostawia użytkownika bez śpiewnika,
            // więc PRZED nią leci kopia bazy. Kontekst domykamy przed kopiowaniem, żeby nic
            // nie trzymało dziennika.
            List<string> pending;
            await using (var ctx = new CantioDbContext())
                pending = (await ctx.Database.GetPendingMigrationsAsync()).ToList();

            // Pierwsze uruchomienie: baza jest świeża (albo prosto z instalatora) — nie ma czego
            // tracić, a kopia byłaby bliźniakiem pliku sprzed sekundy.
            string? migrationBackup = isFirstRun
                ? null
                : DbBackup.CreateBeforeMigration(dbPath, pending);

            try
            {
                await using var ctx = new CantioDbContext();
                await ctx.Database.MigrateAsync();
            }
            catch (Exception mex)
            {
                // Awaria SAMEJ migracji ma być głośna — inaczej użytkownik zobaczy „coś nie działa"
                // i nie dowie się, że obok leży kompletna kopia sprzed przebudowy.
                var where = migrationBackup is null
                    ? "Kopia zapasowa NIE powstała (szczegóły w logu)."
                    : $"Kopia bazy sprzed migracji:\n{migrationBackup}";
                AppLog.Write("App", $"BŁĄD MIGRACJI: {mex.Message}\n{where}\n{mex.StackTrace}");
                if (AppModeRules.CanShowBlockingDialog(AppMode.Current))
                    MessageBox.Show(
                        $"Nie udało się zaktualizować bazy danych:\n{mex.Message}\n\n{where}\n\n" +
                        "Kopię można wskazać w USTAWIENIA → Przywróć bazę danych.",
                        "Cantio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var db = new DatabaseService();

            if (isFirstRun)
            {
                await db.SaveSettingAsync("load_last_setlist", "0");
                await db.SaveSettingAsync("last_setlist_id", "");
            }
            var lang = db.GetSettingAsync("language").Result;
            if (lang == null)
            {
                var cfgFile = Path.Combine(dbFolder, "initial_lang.cfg");
                if (File.Exists(cfgFile))
                    lang = File.ReadAllText(cfgFile).Trim();
                lang ??= "pl";
            }
            LocalizationManager.SetLanguage(lang);

            // Tryb pracy — ten sam wzorzec co język: ustawienie z bazy ma pierwszeństwo,
            // plik initial_mode.cfg jest wartością początkową z instalatora (nie kasujemy go).
            AppMode.Initialize(AppMode.Resolve(await db.GetSettingAsync(AppMode.SettingKey), modeFileValue));
            AppLog.Write("App", $"Tryb pracy: {AppMode.ToSettingValue(AppMode.Current)}");

            var window = new MainWindow(db);
            if (AppModeRules.ShouldShowMainWindow(AppMode.Current))
            {
                window.Show();
            }
            else
            {
                // Okno MUSI powstać (handlery komend WS są w MainWindow), ale nie może zasłaniać
                // projekcji ani kusić na pasku zadań. Powrót dla technika: Ctrl+Alt+Shift+C.
                window.WindowState = WindowState.Minimized;
                window.ShowInTaskbar = false;
                window.Show();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("App", $"BŁĄD STARTU: {ex.Message}\n{ex.StackTrace}");
            if (AppModeRules.CanShowBlockingDialog(AppMode.Current))
                MessageBox.Show($"Błąd startu:\n{ex.Message}\n\n{ex.StackTrace}");
            Shutdown();
        }
    }
}