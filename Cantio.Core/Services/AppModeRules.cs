using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Czyste reguły „co robi tryb serwerowy". Bez WPF, bez bazy — wyłącznie decyzje,
/// żeby dało się je sprawdzić testem zamiast klikać po mini PC bez klawiatury.
/// Miejsca wywołania: <c>DisplayViewModel.OpenProjectionWindowAsync</c>, <c>App.OnStartup</c>,
/// <c>RemoteControlViewModel.InitAsync</c>, <c>AboutViewModel.CheckAndPromptAsync</c>.
/// </summary>
public static class AppModeRules
{
    /// <summary>
    /// Minimalizacja okna projekcji. W trybie dual przy JEDNYM monitorze projekcja przykrywałaby
    /// operatora (fix z v1.1). W trybie serwerowym operatora przy komputerze nie ma —
    /// jedyny ekran JEST projekcją, więc minimalizować nie wolno.
    /// </summary>
    public static bool ShouldMinimizeProjection(AppModeKind mode, int screenCount) =>
        mode == AppModeKind.Dual && screenCount == 1;

    /// <summary>Na mini PC nic nie może wejść nad projekcję (dialogi Windows, aktualizator itp.).</summary>
    public static bool ShouldProjectionBeTopmost(AppModeKind mode) =>
        mode == AppModeKind.Server;

    /// <summary>Po otwarciu projekcji wracamy do okna operatora — ale tylko gdy operator istnieje.</summary>
    public static bool ShouldActivateMainWindow(AppModeKind mode) =>
        mode == AppModeKind.Dual;

    /// <summary>W trybie serwerowym okno główne powstaje, ale schodzi z oczu (i z paska zadań).</summary>
    public static bool ShouldShowMainWindow(AppModeKind mode) =>
        mode == AppModeKind.Dual;

    /// <summary>
    /// Serwer pilota. W trybie dual startuje tylko, gdy użytkownik kazał go pamiętać i działał
    /// przy zamknięciu; w trybie serwerowym pilot to JEDYNY interfejs — musi wstać zawsze.
    /// </summary>
    public static bool ShouldAutoStartPilotServer(AppModeKind mode, bool rememberState, bool wasRunning) =>
        mode == AppModeKind.Server || (rememberState && wasRunning);

    /// <summary>
    /// Blokujące okno dialogowe. Na maszynie bez operatora <c>MessageBox</c> wisi w nieskończoność
    /// (a przy Topmost projekcji bywa niewidoczny) — w trybie serwerowym zamiast dialogu idzie log.
    /// </summary>
    public static bool CanShowBlockingDialog(AppModeKind mode) =>
        mode == AppModeKind.Dual;

    /// <summary>
    /// Ekran parowania na projektorze (problem kury i jajka: nikt nie widzi okna Cantio,
    /// więc PIN musi wyjść na jedyne wyjście HDMI). Pokazujemy TYLKO w trybie serwerowym
    /// i TYLKO dopóki nie ma ani jednego sparowanego urządzenia — pierwsze udane parowanie
    /// gasi ekran bez restartu, a „nowy PIN" (czyszczenie tokenów) przywraca go.
    /// W trybie dual operator ma panel pilota na ekranie i ekran startowy tylko by przeszkadzał.
    /// </summary>
    public static bool ShouldShowPairingScreen(AppModeKind mode, int pairedDeviceCount) =>
        mode == AppModeKind.Server && pairedDeviceCount <= 0;

    /// <inheritdoc cref="ShouldShowPairingScreen(AppModeKind,int)"/>
    /// <param name="tokensJson">surowa wartość ustawienia <c>pilot_tokens</c> (JSON array)</param>
    public static bool ShouldShowPairingScreen(AppModeKind mode, string? tokensJson) =>
        ShouldShowPairingScreen(mode, CountPairedDevices(tokensJson));

    /// <summary>
    /// Liczba sparowanych urządzeń z ustawienia <c>pilot_tokens</c>.
    /// Pusty / uszkodzony / nie-tablicowy JSON = 0 (czyli „nikt nie jest sparowany") —
    /// na mini PC bez klawiatury lepiej pokazać ekran parowania o jeden raz za dużo
    /// niż zostawić parafię z ciemnym ekranem bez PIN-u.
    /// </summary>
    public static int CountPairedDevices(string? tokensJson)
    {
        if (string.IsNullOrWhiteSpace(tokensJson)) return 0;
        try
        {
            var list = JsonSerializer.Deserialize<List<string?>>(tokensJson);
            return list?.Count(t => !string.IsNullOrWhiteSpace(t)) ?? 0;
        }
        catch { return 0; }
    }
}
