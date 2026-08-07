using System.IO;

namespace Cantio.Services;

/// <summary>
/// Czy uruchomić pulpit dla niewidomego operatora zamiast zwykłego okna.
///
/// Dwie drogi, bo niewidomy musi móc wejść do swojego pulpitu BEZ pomocy widzącego:
/// 1. argument wiersza poleceń (<c>--dostepny</c> / <c>--accessible</c>) — pozwala zrobić drugi
///    skrót w menu Start, obok zwykłego („Cantio — pulpit organisty niewidomego");
/// 2. ustawienie w bazie (<c>accessible_shell</c> = 1) — gdy ktoś uruchamia zwykły skrót;
/// 3. plik <c>initial_accessible.cfg</c> pisany przez instalator (znacznik „Uruchamiaj domyślnie
///    pulpit dla niewidomych") — wartość POCZĄTKOWA, dokładnie tak jak <c>initial_mode.cfg</c>
///    dla trybu serwerowego. Ustawienie w bazie ma nad nim pierwszeństwo, więc późniejsze
///    przestawienie w programie nie wraca samo po aktualizacji.
///
/// Argument wygrywa nad ustawieniem w obie strony: <c>--zwykly</c> / <c>--normal</c> pozwala
/// widzącemu (np. serwisantowi) wejść w normalne okno, choćby ustawienie mówiło inaczej —
/// inaczej włączony przełącznik zamykałby drogę powrotną.
/// </summary>
public static class AccessibleShellMode
{
    public const string SettingKey = "accessible_shell";

    /// <summary>Plik pisany przez instalator — analogicznie do <c>initial_mode.cfg</c>.</summary>
    public const string ConfigFileName = "initial_accessible.cfg";

    private static readonly string[] On = ["--dostepny", "--accessible", "/dostepny", "/accessible"];
    private static readonly string[] Off = ["--zwykly", "--normal", "/zwykly", "/normal"];

    public static bool IsRequested(IEnumerable<string>? args, string? settingValue)
        => IsRequested(args, settingValue, fileValue: null);

    public static bool IsRequested(IEnumerable<string>? args, string? settingValue, string? fileValue)
    {
        if (args is not null)
        {
            foreach (var raw in args)
            {
                var a = (raw ?? string.Empty).Trim().ToLowerInvariant();
                if (Off.Contains(a)) return false;
                if (On.Contains(a)) return true;
            }
        }
        return settingValue is null ? Parse(fileValue) : Parse(settingValue);
    }

    /// <summary>Pusta/nieznana wartość = pulpit zwykły.</summary>
    public static bool Parse(string? raw)
    {
        var v = raw?.Trim();
        return v is "1" or "true" or "True" or "TRUE";
    }

    /// <summary>Odczyt <c>initial_accessible.cfg</c> z katalogu danych; brak pliku/błąd = null.</summary>
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
