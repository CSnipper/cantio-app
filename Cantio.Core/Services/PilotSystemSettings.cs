using System.Globalization;
using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Ustawienia SYSTEMOWE (tryb pracy, ekran projekcji, język, diecezja, lekcjonarz, interwał
/// pętli, wczytywanie ostatniego zestawu, autostart Windows) przez protokół WS — wyjście
/// z pułapki trybu serwerowego.
///
/// Po co: w trybie serwerowym okno główne jest ukryte, a mini PC w zakrystii nie ma
/// klawiatury, więc skrót ratunkowy Ctrl+Alt+Shift+C jest bezużyteczny. Efekt do tej pory:
/// trybu serwerowego nie dało się opuścić ani zmienić ekranu projekcji ŻADNYM sposobem.
/// <see cref="PilotDisplaySettings"/> świadomie tych kluczy nie dopuszcza („zdalna zmiana
/// ekranu potrafi odciąć operatora od obrazu") — to rozumowanie zakłada jednak operatora
/// PRZY komputerze, a tu takiego nie ma.
///
/// <para><b>PUŁAPKA, dla której ta klasa w ogóle musi dotykać kluczy pilota.</b>
/// W trybie <c>dual</c> serwer pilota startuje TYLKO gdy <c>pilot_remember=1</c> i
/// <c>pilot_was_running=1</c> (<see cref="AppModeRules.ShouldAutoStartPilotServer"/>);
/// w trybie serwerowym startuje zawsze. Naiwne przełączenie <c>server → dual</c> z tabletu
/// kończyłoby się więc maszyną, która po restarcie nie ma ŻADNEGO interfejsu — ani okna,
/// ani klawiatury, ani pilota. Dlatego każdy zdalny zapis <c>app_mode</c> wymusza oba klucze
/// autostartu na <c>"1"</c>. To niezmiennik dowiedziony sabotażem w harnessie.</para>
///
/// <para><b>Bezpiecznik zmiany ekranu.</b> <c>projection_screen</c> wchodzi od razu, ale
/// desktop startuje odliczanie (<see cref="SystemSettingsTrial"/>) i bez potwierdzenia
/// z tabletu wraca na poprzedni ekran. <c>app_mode</c> bezpiecznika nie potrzebuje (działa
/// dopiero po restarcie, a restart zleca tablet osobną komendą <c>restart_app</c>),
/// <c>language</c> nie potrzebuje niczego.</para>
///
/// <para><b>Etap 2 (2026-09-14).</b> Doszły zwykłe ustawienia, dla których parafia musiałaby
/// inaczej podłączać klawiaturę do mini PC: diecezja, wydanie lekcjonarza, interwał pętli,
/// wczytywanie ostatniego zestawu i autostart Windows. Dwa pierwsze mają skutki uboczne poza
/// tabelą <c>settings</c> (dzień liturgiczny na pasku, treść na projekcji) — rdzeń tylko
/// STWIERDZA zmianę (<c>Result.DioceseChanged</c>/<c>LectionaryChanged</c>), a wykonuje ją
/// gospodarz istniejącymi zdarzeniami. Autostart nie jest wierszem tabeli, tylko wpisem
/// w rejestrze — stąd <see cref="RunOnStartupPort"/>. Doszła też komenda
/// <see cref="RevertCommand"/> (natychmiastowe cofnięcie próby zmiany ekranu; cofanie ma
/// JEDNO miejsce — <c>ApplyRevertAsync</c>).</para>
///
/// <para><b>Etap 3 (2026-09-14) — serwer pilota.</b> Doszły <c>pilot_pin</c>,
/// <c>pilot_require_pin</c>, <c>pilot_port</c> i komenda <see cref="ForgetDevicesCommand"/>.
/// Wszystko, co dotyka żywego serwera (PIN w pamięci, tokeny, przeładowanie na nowym porcie),
/// wchodzi portem <see cref="PilotServerPort"/> — rdzeń zostaje czysty, a gospodarz wykonuje
/// to ISTNIEJĄCYMI ścieżkami z <c>RemoteControlViewModel</c>.</para>
///
/// <para><b>Asymetria PIN-u (świadoma, nie do „naprawienia").</b> Wymaganie PIN-u wolno
/// zdalnie WŁĄCZYĆ, ale nie wyłączyć — próba kończy się <c>ok:false, reason:"not_allowed"</c>
/// i niczego nie zmienia. Nic w scenariuszu ratunkowym nie wymaga zdjęcia uwierzytelniania,
/// a to jedyne ustawienie, którego zdalna zmiana może WYŁĄCZNIE obniżyć bezpieczeństwo
/// (brak uwierzytelniania był „jedyną realną dziurą" zamkniętą w v1.6). Wyłączenie zostaje
/// przy komputerze — ta sama asymetria co przy autostarcie serwera pilota.</para>
///
/// <para><b>Zmiana PIN-u to NIE „nowy PIN".</b> Zapis <c>pilot_pin</c> zostawia tokeny
/// w spokoju, więc sparowane tablety zostają połączone, a nowy PIN dotyczy kolejnych parowań.
/// Kasowanie tokenów robi WYŁĄCZNIE <see cref="ForgetDevicesCommand"/> (odpowiednik przycisku
/// „nowy PIN" w oknie). Pierwsza operacja jest codzienna, druga awaryjna — skręcenie ich
/// w jedno wyrzucałoby parafię z połączenia przy każdej zmianie kodu.</para>
///
/// <para>Reszta wzorca 1:1 jak w <see cref="PilotDisplaySettings"/>: klucze komunikatu =
/// klucze tabeli <c>settings</c>, walidacja ATOMOWA (jeden zły klucz = nic nie zapisane),
/// <c>ack</c> do nadawcy + broadcast do wszystkich, komunikaty składa WYŁĄCZNIE ta klasa.</para>
///
/// <para><b>Rdzeń nie zna ekranów.</b> <c>WpfScreenHelper</c> żyje w projekcie WPF, więc listę
/// monitorów gospodarz podaje jako zwykłe dane (<see cref="ScreenInfo"/>).</para>
/// </summary>
public static class PilotSystemSettings
{
    public const string GetCommand     = "get_system_settings";
    public const string SetCommand     = "set_system_settings";
    public const string ConfirmCommand = "system_settings_confirm";
    public const string RevertCommand  = "system_settings_revert";
    public const string DataType       = "system_settings_data";

    /// <summary>
    /// Odpowiednik przycisku „nowy PIN" w oknie: losuje PIN, KASUJE wszystkie tokeny i rozłącza
    /// klientów. Do użycia, gdy tablet zginął — po tym każde urządzenie musi sparować się od nowa
    /// (nadawca też). Świadomie osobna komenda, nie skutek uboczny zapisu <c>pilot_pin</c>.
    /// </summary>
    public const string ForgetDevicesCommand = "pilot_forget_devices";

    // Klucze tabeli settings — te same, których używa reszta aplikacji (żadnego drugiego słownika nazw).
    public const string KeyMode     = AppMode.SettingKey;   // "app_mode"
    public const string KeyScreen   = "projection_screen";
    public const string KeyLanguage = "language";

    // Etap 2 (2026-09-14): pozostałe zwykłe ustawienia — żeby parafia nie musiała podłączać
    // klawiatury do mini PC dla zmiany diecezji albo wydania lekcjonarza.
    public const string KeyDiocese         = "diocese";
    public const string KeyLectionary      = "lectionary";
    public const string KeyLoopInterval    = "loop_interval";
    public const string KeyLoadLastSetlist = "load_last_setlist";

    /// <summary>
    /// Autostart Windows. <b>NIE jest wierszem tabeli <c>settings</c></b> — stoi w rejestrze
    /// (<c>HKCU\…\Run</c>), zob. <see cref="RunOnStartupPort"/>.
    /// </summary>
    public const string KeyRunOnStartup = "run_on_startup";

    /// <summary>Klucze autostartu serwera pilota wymuszane przy zdalnej zmianie trybu (zob. opis klasy).</summary>
    public const string KeyPilotRemember   = "pilot_remember";
    public const string KeyPilotWasRunning = "pilot_was_running";

    // Etap 3 (2026-09-14): serwer pilota. `pilot_remember`/`pilot_was_running` świadomie NIE są
    // wystawione do zapisu — ich wyłączenie daje mini PC, które po restarcie wstaje bez serwera
    // pilota, czyli dokładnie ten lockout, przed którym broni etap 1.
    public const string KeyPilotPin        = "pilot_pin";
    public const string KeyPilotRequirePin = "pilot_require_pin";
    public const string KeyPilotPort       = "pilot_port";

    /// <summary>Port serwera pilota, gdy w bazie nie ma zapisanego (ta sama wartość co domyślna w serwerze).</summary>
    public const int DefaultPilotPort = 8765;

    /// <summary>Dolna granica portu — poniżej 1024 są porty uprzywilejowane.</summary>
    public const int MinPilotPort = 1024;
    public const int MaxPilotPort = 65535;

    // Powody odmowy (pole `reason` w acku) — te same nazwy co przy ustawieniach wyglądu.
    public const string ReasonUnknownKey   = "unknown_key";
    public const string ReasonInvalidValue = "invalid_value";
    public const string ReasonEmpty        = "empty_payload";

    /// <summary>Operacja z zasady niedozwolona zdalnie (dziś: wyłączenie wymagania PIN-u).</summary>
    public const string ReasonNotAllowed   = "not_allowed";

    /// <summary>Gospodarz nie wstrzyknął portu serwera pilota — nie ma czego wykonać (udawanie sukcesu odpada).</summary>
    public const string ReasonUnavailable  = "unavailable";

    /// <summary>Języki interfejsu — tyle, ile jest plików <c>Strings.*.xaml</c>.</summary>
    public static readonly string[] Languages = ["pl", "en", "es"];

    /// <summary>Wydania lekcjonarza: nowy / stary (druk 1975). Klucz <c>lectionary</c>.</summary>
    public static readonly string[] Lectionaries = ["N", "S"];

    /// <summary>
    /// Autostart Windows widziany przez rdzeń: odczyt i zapis wstrzykuje GOSPODARZ, bo
    /// <c>Cantio.Core</c> jest czystym <c>net10.0</c> i rejestru nie zna (ten sam wzorzec co
    /// <c>PilotImages.Scaler</c>). Zapis MUSI iść tą samą ścieżką co checkbox w oknie
    /// (<c>SzablonViewModel.RunOnStartup</c>), inaczej interfejs kłamałby o stanie rejestru.
    /// </summary>
    public sealed record RunOnStartupPort(Func<bool> Read, Action<bool> Write);

    /// <summary>
    /// Wstrzyknięty przez gospodarza dostęp do autostartu. Brak portu (host bez rejestru)
    /// = klucz w komunikacie jest <c>false</c>, a próba zapisu kończy się <c>invalid_value</c>;
    /// świadomie NIE udajemy, że zapis się udał.
    /// </summary>
    public static RunOnStartupPort? RunOnStartup { get; set; }

    /// <summary>
    /// Żywy serwer pilota widziany przez rdzeń. Wszystkie cztery operacje MAJĄ iść istniejącymi
    /// ścieżkami <c>RemoteControlViewModel</c> (PIN w pamięci serwera, tokeny, QR, ekran parowania
    /// na projekcji, kolejność zwalniania gniazda przy zmianie portu) — rdzeń tylko mówi CO,
    /// nigdy JAK.
    /// </summary>
    /// <param name="IsRunning">Czy serwer pilota REALNIE działa (bez niego zmiana portu jest zwykłym zapisem).</param>
    /// <param name="PairedDevices">Liczba sparowanych urządzeń (tokenów) — pole tylko do odczytu w danych.</param>
    /// <param name="SetPin">Zmiana PIN-u BEZ kasowania tokenów + odświeżenie QR/ekranu parowania.</param>
    /// <param name="EnableRequirePin">Włączenie wymagania PIN-u (wyłączenia zdalnie NIE MA — zob. opis klasy).</param>
    /// <param name="ApplyPort">Przeładowanie serwera na podanym porcie (zwalnia gniazdo, potem podnosi).</param>
    /// <param name="ForgetDevices">
    /// „Nowy PIN": ustawia PODANY PIN, kasuje tokeny, rozłącza klientów. PIN losuje rdzeń
    /// (zob. <see cref="ForgetDevicesAsync"/>), bo musi trafić do acka ZANIM nadawca zostanie
    /// rozłączony — gdyby gospodarz losował własny, tablet pokazałby kod, który nie obowiązuje.
    /// </param>
    public sealed record PilotServerPort(
        Func<bool>   IsRunning,
        Func<int>    PairedDevices,
        Action<string> SetPin,
        Action       EnableRequirePin,
        Action<int>  ApplyPort,
        Action<string> ForgetDevices);

    /// <summary>
    /// Wstrzyknięty przez gospodarza dostęp do serwera pilota. Brak portu = pola stanu w danych
    /// są „serwer nie działa, zero urządzeń", zmiana portu nie uruchamia próby (nie ma czego
    /// przeładować), a <see cref="ForgetDevicesCommand"/> odmawia z <c>unavailable</c> zamiast
    /// udawać, że skasował tokeny.
    /// </summary>
    public static PilotServerPort? PilotServer { get; set; }

    /// <summary>Ekran w postaci, w jakiej rozumie go rdzeń (bez WPF-owego <c>Screen</c>).</summary>
    /// <param name="Label">Gotowy do pokazania na tablecie, np. „Ekran 1 (główny) 1920×1080".</param>
    public readonly record struct ScreenInfo(int Index, string Label, int Width, int Height, bool Primary);

    /// <param name="Response">JSON do NADAWCY (ack albo dane), null = nic nie odsyłamy</param>
    /// <param name="Broadcast">JSON do WSZYSTKICH klientów po zapisie, null = nic nie zmieniono</param>
    /// <param name="ApplyScreen">Nowy indeks ekranu do przestawienia okna projekcji; null = ekranu nie ruszano</param>
    /// <param name="Refresh">Czy gospodarz ma odświeżyć zakładkę USTAWIENIA (stan bazy się zmienił)</param>
    /// <param name="DioceseChanged">Zmieniono diecezję — gospodarz odpala skutki uboczne (dzień liturgiczny na pasku)</param>
    /// <param name="LectionaryChanged">Zmieniono wydanie lekcjonarza — gospodarz przebudowuje slajdy (inna treść na projekcji)</param>
    /// <param name="ApplyPort">Port, na którym gospodarz ma przeładować serwer pilota; null = portu nie ruszano</param>
    /// <param name="ApplyForgetPin">
    /// PIN, na który gospodarz ma odpiąć wszystkie urządzenia (kasacja tokenów + rozłączenie
    /// klientów); null = nic nie odpinamy. Wykonanie jest ODROCZONE do czasu wyjścia acka —
    /// jak przy <paramref name="ApplyPort"/>, bo odpięcie zrywa połączenie nadawcy.
    /// </param>
    public readonly record struct Result(
        string? Response,
        string? Broadcast,
        int? ApplyScreen = null,
        bool Refresh = false,
        bool DioceseChanged = false,
        bool LectionaryChanged = false,
        int? ApplyPort = null,
        string? ApplyForgetPin = null);

    private static readonly Result Ignored = new(null, null);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) =>
        type is GetCommand or SetCommand or ConfirmCommand or RevertCommand or ForgetDevicesCommand;

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>
    /// Komunikat <c>system_settings_data</c>. Używają go WSZYSTKIE ścieżki: odpowiedź na
    /// <c>get_system_settings</c>, broadcast po zapisie z tabletu i broadcast po samoczynnym
    /// cofnięciu ekranu.
    /// </summary>
    /// <param name="pairedDevicesOverride">
    /// Liczba sparowanych urządzeń do pokazania zamiast stanu żywego serwera. Potrzebne
    /// dokładnie w jednym miejscu: przy <c>pilot_forget_devices</c> komunikat powstaje ZANIM
    /// tokeny realnie znikną (zob. <see cref="ForgetDevicesAsync"/>), więc serwer podałby tu
    /// jeszcze starą liczbę.
    /// </param>
    public static string BuildDataJson(DatabaseService db, IReadOnlyList<ScreenInfo> screens,
                                       int? pairedDevicesOverride = null)
    {
        var mode   = ReadMode(db);
        var screen = ReadScreen(db, screens);
        var lang   = ReadLanguage(db);

        return JsonSerializer.Serialize(new
        {
            type     = DataType,
            settings = new Dictionary<string, object?>
            {
                [KeyMode]            = AppMode.ToSettingValue(mode),
                [KeyScreen]          = screen,
                [KeyLanguage]        = lang,
                [KeyDiocese]         = ReadDiocese(db),
                [KeyLectionary]      = LectionaryFilter.Normalize(db.GetSettingSync(KeyLectionary)),
                [KeyLoopInterval]    = SlideLoop.ParseInterval(db.GetSettingSync(KeyLoopInterval)),
                [KeyLoadLastSetlist] = db.GetSettingSync(KeyLoadLastSetlist) == "1",
                [KeyRunOnStartup]    = RunOnStartup?.Read() ?? false,
                // PIN leci normalnym kluczem: tablet jest już sparowany, a PIN i tak siedzi
                // w kodzie QR, więc nie jest przed nim tajemnicą.
                [KeyPilotPin]        = ReadPin(db),
                [KeyPilotRequirePin] = ReadRequirePin(db),
                [KeyPilotPort]       = ReadPilotPort(db),
            },
            screens = screens.Select(s => new
            {
                index   = s.Index,
                label   = s.Label,
                width   = s.Width,
                height  = s.Height,
                primary = s.Primary
            }).ToArray(),
            languages       = Languages,
            // Tablet nie ma skąd wziąć listy diecezji — kanoniczna jest w rdzeniu desktopu.
            dioceses        = DiocesanCalendarService.Diecezje,
            restartRequired = RestartRequired(mode),
            trialSeconds    = SystemSettingsTrial.DefaultSeconds,
            // Stan serwera pilota TYLKO DO ODCZYTU — tabletowi potrzebny do pokazania, ile
            // urządzeń straci parowanie przy `pilot_forget_devices`, i czy zmiana portu
            // w ogóle uruchomi próbę (przy wyłączonym serwerze nie ma czego rozłączać).
            pilotRunning    = PilotServer?.IsRunning() ?? false,
            pairedDevices   = pairedDevicesOverride ?? PilotServer?.PairedDevices() ?? 0
        });
    }

    /// <summary>Czy zapisany tryb różni się od tego, w którym proces REALNIE działa.</summary>
    private static bool RestartRequired(AppModeKind saved) => saved != AppMode.Current;

    private static AppModeKind ReadMode(DatabaseService db) =>
        AppMode.Parse(db.GetSettingSync(KeyMode));

    /// <summary>
    /// Indeks ekranu PRZYCIĘTY do zakresu — dokładnie tak zachowuje się otwieranie projekcji
    /// (indeks poza zakresem spada na ostatni ekran). Tablet ma widzieć stan faktyczny,
    /// a nie liczbę, której nie da się wybrać z listy.
    /// </summary>
    private static int ReadScreen(DatabaseService db, IReadOnlyList<ScreenInfo> screens)
    {
        var raw = db.GetSettingSync(KeyScreen);
        int idx = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
        if (screens.Count == 0) return idx < 0 ? 0 : idx;
        return Math.Clamp(idx, 0, screens.Count - 1);
    }

    /// <summary>
    /// Diecezja z bazy, ale WYŁĄCZNIE jeśli jest na kanonicznej liście — inaczej pusty string
    /// (kalendarz ogólny). Tablet ma widzieć wartość, którą da się wybrać z listy `dioceses`.
    /// </summary>
    private static string ReadDiocese(DatabaseService db)
    {
        var raw = (db.GetSettingSync(KeyDiocese) ?? "").Trim();
        return DiocesanCalendarService.Diecezje.Contains(raw) ? raw : "";
    }

    /// <summary>PIN z bazy; pusty, gdy zapisana wartość nie jest czterocyfrowa (tablet ma widzieć stan faktyczny).</summary>
    private static string ReadPin(DatabaseService db)
    {
        var raw = (db.GetSettingSync(KeyPilotPin) ?? "").Trim();
        return IsValidPin(raw) ? raw : "";
    }

    /// <summary>Wymaganie PIN-u — domyślnie WŁĄCZONE, dokładnie jak czyta to <c>RemoteControlViewModel.InitAsync</c>.</summary>
    private static bool ReadRequirePin(DatabaseService db) =>
        db.GetSettingSync(KeyPilotRequirePin) != "0";

    private static int ReadPilotPort(DatabaseService db) =>
        int.TryParse(db.GetSettingSync(KeyPilotPort), NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out var p) && p is >= MinPilotPort and <= MaxPilotPort
            ? p
            : DefaultPilotPort;

    /// <summary>Dokładnie cztery cyfry ASCII — „12a4", „12345" i „１２３４" to nie PIN.</summary>
    private static bool IsValidPin(string pin) =>
        pin.Length == 4 && pin.All(char.IsAsciiDigit);

    private static string ReadLanguage(DatabaseService db)
    {
        var raw = (db.GetSettingSync(KeyLanguage) ?? "").Trim();
        return Languages.Contains(raw) ? raw : "pl";
    }

    // ─── Wejście ─────────────────────────────────────────────────────────

    public static async Task<Result> HandleAsync(
        DatabaseService db, string rawJson,
        IReadOnlyList<ScreenInfo> screens, SystemSettingsTrial trial)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
            return Ignored;

        return typeEl.GetString() switch
        {
            GetCommand     => new Result(BuildDataJson(db, screens), null),
            SetCommand     => await SetAsync(db, root, screens, trial),
            ConfirmCommand => new Result(
                                  PilotStatus.BuildAckJson(ConfirmCommand, true,
                                      ("confirmed", trial.Confirm(DateTime.UtcNow))),
                                  null),
            // „Cofnij" z okna próbnego — ta sama droga co przy wygaśnięciu odliczania
            // (jedno miejsce cofania, zob. ApplyRevertAsync), tylko bez czekania.
            RevertCommand  => await ApplyRevertAsync(db, screens, trial.Revert(), trial.RevertPort(), RevertCommand),
            ForgetDevicesCommand => await ForgetDevicesAsync(db, screens),
            _              => Ignored
        };
    }

    private static async Task<Result> SetAsync(
        DatabaseService db, JsonElement root,
        IReadOnlyList<ScreenInfo> screens, SystemSettingsTrial trial)
    {
        if (!root.TryGetProperty("settings", out var settings) ||
            settings.ValueKind != JsonValueKind.Object)
            return Deny(ReasonEmpty);

        // FAZA 1 — walidacja CAŁEGO pakietu. Nic nie dotyka bazy, dopóki nie wiadomo, że
        // przejdzie w całości: częściowy zapis mógłby zostawić maszynę w trybie dual
        // z wyłączonym autostartem pilota, czyli bez żadnego interfejsu.
        var pending = new List<(string Key, string Value)>();
        foreach (var prop in settings.EnumerateObject())
        {
            switch (prop.Name)
            {
                case KeyMode:
                    if (!TryText(prop.Value, out var mode) || mode is not ("dual" or "server"))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyMode, mode));
                    break;

                case KeyScreen:
                    // Wyłącznie liczba JSON w zakresie realnie podłączonych monitorów.
                    // Indeks poza zakresem wpadłby na ostatni ekran po cichu, a tablet
                    // pokazywałby wtedy wybór, którego nie dokonał.
                    if (prop.Value.ValueKind != JsonValueKind.Number ||
                        !prop.Value.TryGetInt32(out var idx) ||
                        idx < 0 || idx >= Math.Max(screens.Count, 1))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyScreen, idx.ToString(CultureInfo.InvariantCulture)));
                    break;

                case KeyLanguage:
                    if (!TryText(prop.Value, out var lang) || !Languages.Contains(lang))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyLanguage, lang));
                    break;

                case KeyDiocese:
                    // "" = kalendarz ogólny. Poza tym WYŁĄCZNIE nazwa z kanonicznej listy:
                    // literówka z tabletu dałaby kalendarz bez obchodów diecezjalnych,
                    // wyglądający na poprawnie ustawiony.
                    if (!TryText(prop.Value, out var diocese) ||
                        (diocese.Length > 0 && !DiocesanCalendarService.Diecezje.Contains(diocese)))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyDiocese, diocese));
                    break;

                case KeyLectionary:
                    // Bez Normalize: ono zamienia śmieci na wartość domyślną, a cicha podmiana
                    // wydania lekcjonarza wygląda jak samowolna zmiana treści na projekcji.
                    if (!TryText(prop.Value, out var lect) || !Lectionaries.Contains(lect))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyLectionary, lect));
                    break;

                case KeyLoopInterval:
                    // Odrzucamy zamiast dociąć (ClampInterval) — tablet ma dostać odmowę,
                    // a nie inną liczbę niż wybrał.
                    if (prop.Value.ValueKind != JsonValueKind.Number ||
                        !prop.Value.TryGetInt32(out var interval) ||
                        interval < SlideLoop.MinIntervalSeconds ||
                        interval > SlideLoop.MaxIntervalSeconds)
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyLoopInterval, interval.ToString(CultureInfo.InvariantCulture)));
                    break;

                case KeyLoadLastSetlist:
                    // Format "1"/"0" jak zapisuje SzablonViewModel.SaveAsync — nie "true"/"false".
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyLoadLastSetlist, prop.Value.GetBoolean() ? "1" : "0"));
                    break;

                case KeyRunOnStartup:
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                        RunOnStartup is null)
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyRunOnStartup, prop.Value.GetBoolean() ? "1" : "0"));
                    break;

                case KeyPilotPin:
                    // Dokładnie 4 cyfry — serwer porównuje PIN znak w znak, a PIN innej długości
                    // zabetonowałby parowanie (ekran parowania pokazywałby kod, który nie działa).
                    if (!TryText(prop.Value, out var pin) || !IsValidPin(pin))
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyPilotPin, pin));
                    break;

                case KeyPilotRequirePin:
                    // ASYMETRIA (zob. opis klasy): włączyć wolno, wyłączyć NIE — i to nawet
                    // wtedy, gdy PIN i tak jest już wyłączony. Odmowa jest regułą, nie skutkiem
                    // porównania ze stanem bazy, żeby nie dało się jej ominąć kolejnością zapisów.
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return Deny(ReasonInvalidValue, prop.Name);
                    if (!prop.Value.GetBoolean())
                        return Deny(ReasonNotAllowed, prop.Name);
                    pending.Add((KeyPilotRequirePin, "1"));
                    break;

                case KeyPilotPort:
                    if (prop.Value.ValueKind != JsonValueKind.Number ||
                        !prop.Value.TryGetInt32(out var pilotPort) ||
                        pilotPort < MinPilotPort || pilotPort > MaxPilotPort)
                        return Deny(ReasonInvalidValue, prop.Name);
                    pending.Add((KeyPilotPort, pilotPort.ToString(CultureInfo.InvariantCulture)));
                    break;

                default:
                    return Deny(ReasonUnknownKey, prop.Name);
            }
        }

        if (pending.Count == 0) return Deny(ReasonEmpty);

        // FAZA 2 — zapis. Stan „przed" zbieramy jeszcze przed pierwszym SaveSettingAsync,
        // bo od niego zależy i bezpiecznik ekranu, i to, czy w ogóle coś się zmieniło.
        int screenBefore = ReadScreen(db, screens);
        int portBefore   = ReadPilotPort(db);
        int? applyScreen = null, applyPort = null;
        bool dioceseChanged = false, lectionaryChanged = false;
        bool portTrial = false;

        foreach (var (key, value) in pending)
        {
            if (key == KeyRunOnStartup)
            {
                // Rejestr, nie tabela `settings`. Zapis idzie ścieżką gospodarza (checkbox
                // w oknie), więc broadcast niżej odczyta REALNY stan rejestru, a nie życzenie.
                RunOnStartup!.Write(value == "1");
                continue;
            }

            // Skutki uboczne (odświeżenie dnia liturgicznego, przebudowa slajdów) tylko przy
            // REALNEJ zmianie — porównujemy po znormalizowanej wartości, tak jak widzi ją
            // reszta programu.
            var before = key switch
            {
                KeyDiocese    => ReadDiocese(db),
                KeyLectionary => LectionaryFilter.Normalize(db.GetSettingSync(key)),
                _             => null
            };
            await db.SaveSettingAsync(key, value);

            if (key == KeyDiocese)         dioceseChanged    = before != value;
            else if (key == KeyLectionary) lectionaryChanged = before != value;

            if (key == KeyMode)
            {
                // NIEZMIENNIK (zob. opis klasy): po zdalnej zmianie trybu serwer pilota MUSI
                // wstać przy następnym starcie, inaczej maszyna zostaje bez żadnego interfejsu.
                await db.SaveSettingAsync(KeyPilotRemember, "1");
                await db.SaveSettingAsync(KeyPilotWasRunning, "1");
            }
            else if (key == KeyScreen)
            {
                int now = int.Parse(value, CultureInfo.InvariantCulture);
                if (now != screenBefore)
                {
                    applyScreen = now;
                    trial.Start(screenBefore, DateTime.UtcNow);
                }
            }
            else if (key == KeyPilotPin)
            {
                // Tokeny zostają NIETKNIĘTE — połączone tablety mają zostać połączone.
                // Kasuje je wyłącznie ForgetDevicesCommand (zob. opis klasy).
                PilotServer?.SetPin(value);
            }
            else if (key == KeyPilotRequirePin)
            {
                // Zawsze „włącz" — wyłączenie odpadło już w walidacji.
                PilotServer?.EnableRequirePin();
            }
            else if (key == KeyPilotPort)
            {
                int now = int.Parse(value, CultureInfo.InvariantCulture);
                if (now != portBefore)
                {
                    applyPort = now;
                    // Próba tylko wtedy, gdy serwer REALNIE działa: przy wyłączonym nie ma czego
                    // rozłączać ani czym potwierdzić, więc zmiana portu jest zwykłym zapisem.
                    if (PilotServer?.IsRunning() == true)
                    {
                        trial.StartPort(portBefore, DateTime.UtcNow);
                        portTrial = true;
                    }
                }
            }
        }

        return new Result(
            PilotStatus.BuildAckJson(SetCommand, true,
                ("keys", pending.Count),
                ("trial", applyScreen != null || portTrial),
                ("restartRequired", RestartRequired(ReadMode(db)))),
            BuildDataJson(db, screens),
            applyScreen,
            Refresh: true,
            DioceseChanged: dioceseChanged,
            LectionaryChanged: lectionaryChanged,
            ApplyPort: applyPort);
    }

    /// <summary>
    /// Odliczanie minęło bez potwierdzenia — przywróć poprzedni ekran w bazie i powiedz
    /// gospodarzowi, gdzie ma wrócić oknem projekcji. <c>null</c> w <c>ApplyScreen</c> =
    /// nie było czego cofać (potwierdzono albo termin jeszcze trwa).
    /// </summary>
    public static Task<Result> ExpireTrialAsync(
        DatabaseService db, IReadOnlyList<ScreenInfo> screens, SystemSettingsTrial trial)
        // Broadcast bez acka: nikt o to nie prosił, a tablet ma zobaczyć powrót bez pytania.
        // Oba przedmioty próby mają WŁASNE terminy, więc budzik jednego nie rusza drugiego.
        => ApplyRevertAsync(db, screens,
                            trial.Expire(DateTime.UtcNow), trial.ExpirePort(DateTime.UtcNow),
                            ackCommand: null);

    /// <summary>
    /// JEDYNE miejsce cofania ekranu próbnego. Dwie drogi tu prowadzą: wygaśnięcie odliczania
    /// (<see cref="ExpireTrialAsync"/>, bez acka) i natychmiastowe „cofnij" z tabletu
    /// (<see cref="RevertCommand"/>, z ackiem). Dwie niezależne implementacje cofania to
    /// dokładnie ten układ, który w tym projekcie gubił już dane.
    /// </summary>
    /// <param name="back">Ekran do przywrócenia; <c>null</c> = nie było trwającej próby ekranu</param>
    /// <param name="backPort">Port do przywrócenia; <c>null</c> = nie było trwającej próby portu</param>
    /// <param name="ackCommand">Komenda do potwierdzenia nadawcy; <c>null</c> = nikt nie pytał</param>
    private static async Task<Result> ApplyRevertAsync(
        DatabaseService db, IReadOnlyList<ScreenInfo> screens, int? back, int? backPort, string? ackCommand)
    {
        if (back is null && backPort is null)
            return ackCommand is null
                ? Ignored
                : new Result(PilotStatus.BuildAckJson(ackCommand, true, ("reverted", false)), null);

        if (back is not null)
            await db.SaveSettingAsync(KeyScreen, back.Value.ToString(CultureInfo.InvariantCulture));
        if (backPort is not null)
            await db.SaveSettingAsync(KeyPilotPort, backPort.Value.ToString(CultureInfo.InvariantCulture));

        return new Result(
            ackCommand is null ? null : PilotStatus.BuildAckJson(ackCommand, true, ("reverted", true)),
            BuildDataJson(db, screens),
            back,
            Refresh: true,
            ApplyPort: backPort);
    }

    /// <summary>
    /// „Nowy PIN" z tabletu: losowanie PIN-u, kasacja WSZYSTKICH tokenów i rozłączenie klientów
    /// (nadawcy też — o ostrzeżeniu użytkownika przed wysłaniem decyduje tablet). Wykonuje to
    /// gospodarz istniejącą ścieżką, bo kasacja tokenów dotyczy żywego serwera, nie tylko bazy.
    ///
    /// KOLEJNOŚĆ JEST ISTOTNA (ten sam układ co <c>restart_app</c> i co zmiana portu):
    /// PIN losujemy TUTAJ i tylko zapowiadamy odpięcie (<c>ApplyForgetPin</c>), żeby ack z nowym
    /// kodem zdążył wyjść na łącze — odpięcie rozłącza nadawcę, więc drugiej szansy nie ma.
    /// Gospodarz dostaje gotowy PIN i go USTAWIA (nie losuje własnego), inaczej tablet pokazałby
    /// kod inny niż obowiązujący.
    /// </summary>
    private static async Task<Result> ForgetDevicesAsync(DatabaseService db, IReadOnlyList<ScreenInfo> screens)
    {
        if (PilotServer is null)
            return new Result(
                PilotStatus.BuildAckJson(ForgetDevicesCommand, false, ("reason", ReasonUnavailable)), null);

        var pin = RemoteControlServer.GeneratePin();
        // Zapis do bazy robimy tu, bo komunikat niżej czyta ją SYNCHRONICZNIE — bez tego tablet
        // dostałby w danych jeszcze stary PIN. Gospodarz zapisze tę samą wartość drugi raz
        // (fire-and-forget, jak przycisk w oknie) i to nie szkodzi.
        await db.SaveSettingAsync(KeyPilotPin, pin);
        // Broadcast poleci do klientów, którzy przeżyją rozłączenie (i tak muszą sparować się
        // od nowa); liczbę urządzeń podajemy wprost 0, bo tokeny znikną za chwilę.
        return new Result(
            PilotStatus.BuildAckJson(ForgetDevicesCommand, true,
                ("pin", pin),
                ("pairedDevices", 0)),
            BuildDataJson(db, screens, pairedDevicesOverride: 0),
            Refresh: true,
            ApplyForgetPin: pin);
    }

    private static bool TryText(JsonElement value, out string result)
    {
        result = "";
        if (value.ValueKind != JsonValueKind.String) return false;
        result = (value.GetString() ?? "").Trim();
        return true;
    }

    private static Result Deny(string reason, string? key = null) =>
        new(PilotStatus.BuildAckJson(SetCommand, false, ("reason", reason), ("key", key)), null);
}
