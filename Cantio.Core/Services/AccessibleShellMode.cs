namespace Cantio.Services;

/// <summary>
/// Czy uruchomić pulpit dla niewidomego operatora zamiast zwykłego okna.
///
/// Dwie drogi, bo niewidomy musi móc wejść do swojego pulpitu BEZ pomocy widzącego:
/// 1. argument wiersza poleceń (<c>--dostepny</c> / <c>--accessible</c>) — pozwala zrobić drugi
///    skrót w menu Start, obok zwykłego („Cantio — pulpit organisty niewidomego");
/// 2. ustawienie w bazie (<c>accessible_shell</c> = 1) — gdy ktoś uruchamia zwykły skrót.
///
/// Argument wygrywa nad ustawieniem w obie strony: <c>--zwykly</c> / <c>--normal</c> pozwala
/// widzącemu (np. serwisantowi) wejść w normalne okno, choćby ustawienie mówiło inaczej —
/// inaczej włączony przełącznik zamykałby drogę powrotną.
/// </summary>
public static class AccessibleShellMode
{
    public const string SettingKey = "accessible_shell";

    private static readonly string[] On = ["--dostepny", "--accessible", "/dostepny", "/accessible"];
    private static readonly string[] Off = ["--zwykly", "--normal", "/zwykly", "/normal"];

    public static bool IsRequested(IEnumerable<string>? args, string? settingValue)
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
        return settingValue is "1" or "true" or "True";
    }
}
