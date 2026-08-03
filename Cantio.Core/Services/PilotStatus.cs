using System.Diagnostics;
using System.Text.Json;

namespace Cantio.Services;

/// <summary>Stan aplikacji odsyłany Pilotowi w odpowiedzi na komendę <c>status</c>.</summary>
/// <param name="Version">wersja Cantio (z <c>Cantio.csproj</c>)</param>
/// <param name="Mode">tryb pracy: <c>dual</c> albo <c>server</c></param>
/// <param name="ProjectionOpen">czy okno projekcji jest otwarte</param>
/// <param name="ProjectionScreen">indeks ekranu projekcji (-1 = projekcja zamknięta)</param>
/// <param name="ScreenCount">liczba podłączonych ekranów</param>
/// <param name="PairedDevices">liczba sparowanych urządzeń (tokenów)</param>
/// <param name="UptimeSeconds">czas pracy aplikacji w sekundach</param>
public readonly record struct PilotStatusInfo(
    string Version,
    string Mode,
    bool ProjectionOpen,
    int ProjectionScreen,
    int ScreenCount,
    int PairedDevices,
    long UptimeSeconds);

/// <summary>
/// Komendy „ratunkowe" na sprzęt bez klawiatury (mini PC w zakrystii): <c>status</c>,
/// <c>restart_app</c>, <c>open_projection</c>, <c>close_projection</c>.
///
/// Odpowiedzi buduje WYŁĄCZNIE ta klasa — jedna metoda na komunikat, żeby nie powstały
/// dwie niezależne listy pól (ten sam układ zgubił kiedyś notatki pozycji zestawu, v1.6).
/// Kształt komunikatów jest wyłącznie DOPISANY do protokołu: stary Pilot nie zna tych
/// typów, nigdy ich nie wyśle i niczego nie traci.
/// </summary>
public static class PilotStatus
{
    /// <summary>Nazwa typu odpowiedzi na <c>status</c>.</summary>
    public const string StatusType = "status_data";
    /// <summary>Nazwa typu potwierdzenia przyjęcia komendy.</summary>
    public const string AckType = "ack";

    public static string BuildStatusJson(PilotStatusInfo info) =>
        JsonSerializer.Serialize(new
        {
            type             = StatusType,
            version          = info.Version,
            mode             = info.Mode,
            projectionOpen   = info.ProjectionOpen,
            projectionScreen = info.ProjectionScreen,
            screenCount      = info.ScreenCount,
            pairedDevices    = info.PairedDevices,
            uptimeSeconds    = info.UptimeSeconds
        });

    /// <summary>
    /// Potwierdzenie PRZYJĘCIA komendy (nie jej skutku). Wysyłane przez serwer zanim
    /// zadziała handler — przy <c>restart_app</c> to jedyna szansa, żeby Pilot dowiedział
    /// się, że komenda doszła, bo proces zaraz znika.
    /// </summary>
    /// <param name="extra">
    /// Opcjonalne pola DOPISYWANE na końcu obiektu (v1.63+: <c>reason</c>, <c>id</c>, <c>name</c>,
    /// <c>newName</c>, <c>number</c>, <c>songs</c>, <c>setlists</c>). Wywołanie bez nich daje
    /// znak w znak dotychczasowy ack <c>{type,command,ok}</c> — stary Pilot niczego nie traci.
    /// Wartości <c>null</c> są pomijane.
    /// </param>
    public static string BuildAckJson(string command, bool ok = true,
                                      params (string Key, object? Value)[] extra)
    {
        // Dictionary&lt;string,object&gt; serializuje się w kolejności wstawiania — pierwsze trzy
        // pola zostają dokładnie tam, gdzie były przed dopisaniem rozszerzeń.
        var payload = new Dictionary<string, object?>
        {
            ["type"]    = AckType,
            ["command"] = command,
            ["ok"]      = ok
        };
        foreach (var (key, value) in extra)
            if (value != null) payload[key] = value;
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Wersja aplikacji z assembly; przy braku danych „?".</summary>
    public static string AppVersion()
    {
        try
        {
            var v = System.Reflection.Assembly.GetEntryAssembly()?
                .GetName().Version
                ?? typeof(PilotStatus).Assembly.GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}{(v.Build > 0 ? "." + v.Build : "")}";
        }
        catch { return "?"; }
    }

    /// <summary>Czas pracy procesu w sekundach (nigdy ujemny).</summary>
    public static long UptimeSeconds()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            var s = (long)(DateTime.Now - p.StartTime).TotalSeconds;
            return s < 0 ? 0 : s;
        }
        catch { return 0; }
    }
}
