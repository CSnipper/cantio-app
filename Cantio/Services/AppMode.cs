using System.IO;

namespace Cantio.Services;

/// <summary>Tryb pracy aplikacji — wybierany w instalatorze, zmienialny w USTAWIENIACH.</summary>
public enum AppModeKind
{
    /// <summary>Dzisiejszy tryb: operator siedzi przy komputerze, projekcja na drugim ekranie.</summary>
    Dual,

    /// <summary>Mini PC w zakrystii: jedno wyjście HDMI = projekcja, sterowanie wyłącznie z CantioPilota.</summary>
    Server
}

/// <summary>
/// Tryb pracy odczytany RAZ przy starcie (<see cref="Initialize"/> w <c>App.OnStartup</c>).
/// Reszta kodu czyta <see cref="Current"/>/<see cref="IsServer"/> — nikt nie zagląda po to do bazy,
/// bo tryb nie może się zmienić w trakcie sesji (zmiana w USTAWIENIACH działa po restarcie).
/// Miejsce spójne z <see cref="AppLog"/> i <c>LocalizationManager</c>: statyczny stan globalny
/// ustawiany na starcie, bez wstrzykiwania przez konstruktory kilkunastu ViewModeli.
/// </summary>
public static class AppMode
{
    /// <summary>Klucz w tabeli settings (ma pierwszeństwo nad plikiem).</summary>
    public const string SettingKey = "app_mode";

    /// <summary>Plik pisany przez instalator — analogicznie do <c>initial_lang.cfg</c>.</summary>
    public const string ConfigFileName = "initial_mode.cfg";

    public static AppModeKind Current { get; private set; } = AppModeKind.Dual;

    public static bool IsServer => Current == AppModeKind.Server;

    /// <summary>Ustawia tryb sesji. Wołane wyłącznie ze startu aplikacji (i z testów).</summary>
    public static void Initialize(AppModeKind mode) => Current = mode;

    /// <summary>Nieznana/pusta/uszkodzona wartość → <see cref="AppModeKind.Dual"/>.</summary>
    public static AppModeKind Parse(string? raw) =>
        string.Equals(raw?.Trim(), "server", StringComparison.OrdinalIgnoreCase)
            ? AppModeKind.Server
            : AppModeKind.Dual;

    public static string ToSettingValue(AppModeKind mode) =>
        mode == AppModeKind.Server ? "server" : "dual";

    /// <summary>
    /// Czysta reguła rozstrzygania trybu — ten sam wzorzec co <c>initial_lang.cfg</c>:
    /// ustawienie w bazie ma pierwszeństwo, plik jest tylko wartością początkową (nie kasujemy go).
    /// </summary>
    /// <param name="settingValue">wartość klucza <c>app_mode</c> z bazy (null = brak klucza)</param>
    /// <param name="fileValue">zawartość <c>initial_mode.cfg</c> (null = brak pliku)</param>
    public static AppModeKind Resolve(string? settingValue, string? fileValue) =>
        settingValue is null ? Parse(fileValue) : Parse(settingValue);

    /// <summary>Odczyt <c>initial_mode.cfg</c> z katalogu danych; brak pliku/błąd = null.</summary>
    public static string? ReadConfigFile(string appDataFolder)
    {
        try
        {
            var path = Path.Combine(appDataFolder, ConfigFileName);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch { return null; }
    }
}
