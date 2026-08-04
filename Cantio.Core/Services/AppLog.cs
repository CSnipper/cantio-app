using System.IO;
using System.Reflection;

namespace Cantio.Services;

/// <summary>
/// Prosty log do pliku (%LocalAppData%\Cantio\logs\cantio.log).
/// Powód istnienia: <c>Debug.WriteLine</c> znika w kompilacji release, więc u użytkownika
/// nie było ŻADNEGO śladu po awarii sieciowej — diagnostyka sprowadzała się do zgadywania.
/// Zapis nigdy nie rzuca i nigdy nie blokuje sterowania urządzeniami.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1024 * 1024; // 1 MB — potem rotacja do cantio.log.1

    // Zapisy lecą z równoległych zadań sieciowych (Task.WhenAll po urządzeniach).
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cantio", "logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "cantio.log");

    /// <summary>Dopisuje jedną linię: <c>data godzina [kategoria] treść</c>.</summary>
    public static void Write(string category, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                Rotate();
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch { /* log nie może wywrócić aplikacji ani przeszkodzić w sterowaniu TV */ }
    }

    /// <summary>Granica sesji w logu — bez tego nie widać, gdzie kończy się poprzednie uruchomienie.</summary>
    public static void WriteSessionStart()
    {
        // GetEntryAssembly = Cantio.exe. GetExecutingAssembly to Cantio.Core, który nie ma własnej
        // wersji produktu i wypisywał w logu użytkownika stałe „1.0.0.0" (ta sama reguła co
        // PilotStatus.AppVersion).
        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString() ?? "?";
        Write("App", $"─── Start Cantio {version} ───");
    }

    /// <summary>Trzymamy najwyżej dwa pliki — log u użytkownika nie może rosnąć bez końca.</summary>
    private static void Rotate()
    {
        try
        {
            var info = new FileInfo(LogFilePath);
            if (!info.Exists || info.Length < MaxBytes) return;
            var old = LogFilePath + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(LogFilePath, old);
        }
        catch { /* nieudana rotacja = piszemy dalej do bieżącego pliku */ }
    }
}
