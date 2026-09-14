using System.Text.Json;
using Cantio.Models;
using Cantio.Services.Devices;

namespace Cantio.Services;

/// <summary>
/// Zarządzanie telewizorami i projektorami z tabletu: lista, włączanie pojedynczego urządzenia,
/// oznaczenie, usunięcie, test łączności, wykrywanie, parowanie Samsunga (<c>device_pair</c>)
/// oraz dodanie projektora PJLink / telewizora Sony / budzika Wake-on-LAN (<c>device_add</c>).
///
/// <para><b>Po co.</b> Do v1.69 z tabletu dało się wyłącznie włączyć i wyłączyć WSZYSTKIE
/// urządzenia naraz (<c>devices_power_all</c>). Parafia w trybie serwerowym (mini PC bez
/// klawiatury, ukryte okno) nie miała jak podłączyć nowego telewizora — ani go sparować, ani
/// nazwać, ani usunąć. Odpowiednikiem w oknie jest <c>DevicesViewModel</c> i to jego ścieżki
/// wykonują tu całą robotę; ta klasa wyłącznie rozstrzyga i składa komunikaty.</para>
///
/// <para><b>NIGDY nie wysyłamy poświadczeń.</b> Ustawienie <c>projection_devices</c> trzyma
/// token parowania Samsunga (<see cref="ProjectionDevice.Token"/>) i klucz PSK Sony
/// (<see cref="ProjectionDevice.Password"/>). To są poświadczenia dostępu do sprzętu w sieci
/// parafialnej i nie mają prawa wyjść ani na łącze, ani do logu. Dlatego
/// <see cref="BuildDevicesJson"/> dostaje PEŁNE encje, ale wypisuje z nich RĘCZNIE i wyłącznie
/// sześć pól — nigdzie w tym pliku nie ma serializacji całego <c>ProjectionDevice</c>.
/// Strażnikiem jest asercja harnessu na PEŁNĄ listę pól pozycji: dopisanie pola do modelu
/// (albo do buildera) zapala test, zamiast po cichu przemycić token.</para>
///
/// <para><b>Wykrywanie MUSI mieć ścieżkę ręczną po IP.</b> Lekcja z hotfiksu v1.55 robionego
/// z kościoła: SSDP nie przechodzi w sieciach z izolacją klientów, a telewizor jest wtedy
/// normalnie osiągalny po adresie. Dlatego <c>device_pair</c> przyjmuje SAMO <c>ip</c>, bez
/// wcześniejszego wykrycia — wykrywanie jest wygodą, nie jedyną drogą.</para>
///
/// <para><b>Ack NATYCHMIAST, wynik broadcastem.</b> Wykrywanie i parowanie trwają sekundy
/// (a włączanie po Wake-on-LAN nawet dziesięć), więc potwierdzenie przyjęcia komendy wychodzi
/// od razu, a wynik osobnym komunikatem do WSZYSTKICH. <b>NIEZMIENNIK:</b> operacja ZAWSZE
/// kończy się komunikatem końcowym — także gdy rzuci wyjątkiem. Po acku tablet CZEKA, więc
/// cisza to zawieszony ekran bez wyjścia (ten sam wniosek co przy <c>get_songs</c> w v1.64
/// i przy operacjach konserwacyjnych).</para>
///
/// <para><b>Sterowniki wchodzą PORTEM.</b> Rdzeń mówi CO zrobić, a gospodarz wykonuje to
/// swoimi ISTNIEJĄCYMI ścieżkami z <c>DevicesViewModel</c> (ten sam wzorzec co
/// <c>PilotImages.Scaler</c>, <c>PilotSystemSettings.RunOnStartup</c> i <c>PilotServer</c>).
/// Dzięki temu lista w oknie, odpytywanie w tle i pasek górny widzą zmiany od razu, a harness
/// podstawia atrapę i NIE wysyła pakietów do prawdziwej sieci.</para>
///
/// <para><b>Zgodność wsteczna:</b> wyłącznie DOPISANE typy. <c>devices</c> (stan zbiorczy)
/// i <c>devices_power_all</c> zostają BEZ ZMIAN — stary Pilot ma na nich swój przycisk.</para>
/// </summary>
public static class PilotDevices
{
    // ─── Komendy P→D ─────────────────────────────────────────────────────
    public const string GetCommand      = "get_devices";
    public const string PowerCommand    = "device_power";
    public const string RenameCommand   = "device_rename";
    public const string RemoveCommand   = "device_remove";
    public const string TestCommand     = "device_test";
    public const string DiscoverCommand = "device_discover";
    public const string PairCommand     = "device_pair";
    public const string AddCommand      = "device_add";

    // ─── Komunikaty D→P ──────────────────────────────────────────────────
    public const string DataType       = "devices_data";
    public const string FoundType      = "devices_found";
    public const string PairResultType = "device_pair_result";

    // ─── Powody odmowy ───────────────────────────────────────────────────
    /// <summary>Nie ma urządzenia o podanym identyfikatorze (albo zniknęło z listy).</summary>
    public const string ReasonNotFound     = "not_found";
    /// <summary>Brak obowiązkowego pola albo wartość złego typu.</summary>
    public const string ReasonInvalidValue = "invalid_value";
    /// <summary>Oznaczenie dłuższe niż 4 znaki — ODRZUCAMY, zamiast dociąć (zob. niżej).</summary>
    public const string ReasonTooLong      = "too_long";
    /// <summary>Wykrywanie już trwa — drugie nie startuje.</summary>
    public const string ReasonBusy         = "busy";
    /// <summary>Parowanie obsługuje dziś wyłącznie Samsunga (zob. uwaga przy <see cref="Pair"/>).</summary>
    public const string ReasonUnsupported  = "unsupported_kind";
    /// <summary>Ten sam adres IP (albo MAC przy Wake-on-LAN) już jest na liście.</summary>
    public const string ReasonDuplicate    = "duplicate";
    /// <summary>Gospodarz nie wstrzyknął portu — nie udajemy, że komenda się wykonała.</summary>
    public const string ReasonUnavailable  = "unavailable";

    /// <summary>Maksymalna długość własnego oznaczenia — dokładnie tyle, ile mieści przycisk paska.</summary>
    public const int MaxLabelLength = 4;

    // ─── Port do gospodarza ──────────────────────────────────────────────

    /// <summary>Jedna pozycja listy: encja z bazy + OSTATNIO ZNANY stan zasilania (bez ruchu w sieci).</summary>
    public readonly record struct DeviceEntry(ProjectionDevice Device, DevicePowerState State);

    /// <summary>Wynik wykrywania — surowe dane sprzętu; identyfikator nadaje ta klasa.</summary>
    public sealed record Found(string Name, string Ip, string Mac, string Kind);

    /// <summary>
    /// Urządzenie do dodania BEZ parowania (PJLink / Sony / Wake-on-LAN). Wszystko jest już
    /// sprawdzone i przycięte przez <see cref="Add"/> — gospodarz ma tylko zbudować encję
    /// i zapisać ją swoją istniejącą ścieżką.
    ///
    /// <para><b>Poświadczenia jadą TYLKO w tę stronę.</b> <paramref name="Password"/> to hasło
    /// PJLink albo klucz PSK Sony: przychodzi z tabletu, ląduje w <c>projection_devices</c>
    /// i NIGDY nie wraca (zob. <see cref="BuildDevicesJson"/>).</para>
    /// </summary>
    public sealed record AddRequest(
        string Kind, string Ip, string Mac, string? Name, string Label, string? Password, int Port);

    /// <summary>
    /// Wykonawca po stronie gospodarza. Każda funkcja to ISTNIEJĄCA ścieżka
    /// <c>DevicesViewModel</c>, nie druga implementacja tego samego.
    /// </summary>
    /// <param name="List">Lista urządzeń z ostatnio znanym stanem.</param>
    /// <param name="Power">Włącz/wyłącz jedno urządzenie; <c>false</c> = nie ma takiego identyfikatora.</param>
    /// <param name="Rename">Zapis własnego oznaczenia; <c>false</c> = nie ma takiego identyfikatora.</param>
    /// <param name="Remove">Usunięcie z listy; <c>false</c> = nie ma takiego identyfikatora.</param>
    /// <param name="Test">Test łączności: czy znaleziono, czy odpowiada i krótki opis stanu.</param>
    /// <param name="Discover">Skan sieci (SSDP).</param>
    /// <param name="Pair">Parowanie i dodanie: adres IP oraz — o ile znane z wykrycia — nazwa i MAC.</param>
    /// <param name="Add">Dodanie urządzenia bez parowania (PJLink/Sony/WoL); zwraca nadany identyfikator.</param>
    public sealed record DevicesPort(
        Func<Task<IReadOnlyList<DeviceEntry>>> List,
        Func<string, bool, Task<bool>> Power,
        Func<string, string, Task<bool>> Rename,
        Func<string, Task<bool>> Remove,
        Func<string, Task<(bool Found, bool Ok, string Message)>> Test,
        Func<Task<IReadOnlyList<Found>>> Discover,
        Func<string, string?, string?, Task<(bool Ok, string? Error)>> Pair,
        Func<AddRequest, Task<string>> Add);

    /// <summary>Wstrzykiwany przez gospodarza (<c>MainWindow</c>) i przez harness (atrapa).</summary>
    public static DevicesPort? Port { get; set; }

    /// <param name="Response">JSON do NADAWCY (ack albo dane); <c>null</c> = nic nie odsyłamy.</param>
    /// <param name="Work">
    /// Rzecz do zrobienia DOPIERO PO wysłaniu <paramref name="Response"/> — dostaje funkcję
    /// broadcastu. Gospodarz woła to BEZ <c>await</c>: ack ma wyjść natychmiast.
    /// </param>
    public readonly record struct Result(string? Response, Func<Func<string, Task>, Task>? Work);

    private static readonly Result Ignored = new(null, null);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    /// <remarks>
    /// <c>devices_power_all</c> NIE JEST tu wymieniony świadomie — stary komunikat zostaje
    /// na swojej dotychczasowej ścieżce w serwerze, żeby stary Pilot nie zauważył zmiany.
    /// </remarks>
    public static bool IsCommand(string? type) => type is
        GetCommand or PowerCommand or RenameCommand or RemoveCommand
        or TestCommand or DiscoverCommand or PairCommand or AddCommand;

    // ─── Stan wykrywania (jeden na aplikację, nie per klient) ────────────

    private static readonly System.Threading.Lock _lock = new();
    private static bool _discovering;
    /// <summary>Ostatni wynik wykrywania: <c>discoveredId</c> → sprzęt. Identyfikatory nadaje rdzeń,
    /// żeby gospodarz nie musiał pamiętać listy między komendami.</summary>
    private static readonly Dictionary<string, Found> _discovered = [];

    /// <summary>Czy trwa skan sieci (pole <c>discovering</c> komunikatu <c>devices_data</c>).</summary>
    public static bool IsDiscovering { get { lock (_lock) { return _discovering; } } }

    /// <summary>Czyści stan między testami harnessu; w aplikacji niepotrzebne.</summary>
    public static void ResetDiscovery()
    {
        lock (_lock) { _discovering = false; _discovered.Clear(); }
    }

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>Nazwa stanu zasilania w protokole.</summary>
    public static string StateName(DevicePowerState state) => state switch
    {
        DevicePowerState.On  => "on",
        DevicePowerState.Off => "off",
        _                    => "unknown"
    };

    /// <summary>
    /// <c>devices_data</c> — lista urządzeń dla tabletu.
    ///
    /// <para><b>Tu przebiega granica poświadczeń.</b> Wejście ma pełne encje (z tokenem
    /// Samsunga i kluczem PSK Sony), wyjście ma DOKŁADNIE sześć pól wypisanych z ręki.
    /// Nie zamieniać tego na serializację obiektu ani na `...with` — przy dopisaniu pola
    /// do modelu poświadczenie wyszłoby na łącze bez jednej linijki zmiany tutaj.</para>
    /// </summary>
    public static string BuildDevicesJson(IReadOnlyList<DeviceEntry> devices) =>
        JsonSerializer.Serialize(new
        {
            type    = DataType,
            devices = devices.Select(e => new
            {
                id    = e.Device.Id,
                label = e.Device.Label,
                name  = e.Device.Name,
                kind  = e.Device.Type,
                ip    = e.Device.Ip,
                state = StateName(e.State)
            }).ToArray(),
            discovering = IsDiscovering
        });

    /// <summary>
    /// <c>devices_found</c> — wynik wykrywania. <c>error</c> pojawia się tylko przy awarii;
    /// <c>done:true</c> jest ZAWSZE, bo to komunikat KOŃCOWY (tablet po nim przestaje czekać).
    /// </summary>
    public static string BuildFoundJson(IReadOnlyList<(string Id, Found Device)> found, string? error = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"]    = FoundType,
            ["devices"] = found.Select(f => new
            {
                discoveredId = f.Id,
                name         = f.Device.Name,
                ip           = f.Device.Ip,
                kind         = f.Device.Kind
            }).ToArray(),
            ["done"] = true
        };
        if (error != null) payload["error"] = error;
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// <c>device_pair_result</c> — komunikat KOŃCOWY parowania. Wychodzi zawsze: i po sukcesie,
    /// i po odmowie telewizora, i po wyjątku.
    /// </summary>
    public static string BuildPairResultJson(bool ok, string ip, string? error = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = PairResultType,
            ["ok"]   = ok,
            ["ip"]   = ip
        };
        if (error != null) payload["error"] = error;
        return JsonSerializer.Serialize(payload);
    }

    // ─── Wejście ─────────────────────────────────────────────────────────

    public static async Task<Result> HandleAsync(string rawJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
            return Ignored;

        var type = typeEl.GetString();
        if (!IsCommand(type)) return Ignored;

        var port = Port;
        if (port is null)
            return new Result(PilotStatus.BuildAckJson(type!, false, ("reason", ReasonUnavailable)), null);

        try
        {
            return type switch
            {
                GetCommand      => new Result(BuildDevicesJson(await port.List()), null),
                PowerCommand    => await PowerAsync(port, root),
                RenameCommand   => await RenameAsync(port, root),
                RemoveCommand   => await RemoveAsync(port, root),
                TestCommand     => await TestAsync(port, root),
                DiscoverCommand => Discover(port),
                AddCommand      => await AddAsync(port, root),
                _               => Pair(port, root)
            };
        }
        catch (Exception ex)
        {
            // Awaria bazy ani sieci nie może wywrócić handlera ani zostawić tabletu w ciszy.
            return new Result(PilotStatus.BuildAckJson(type!, false, ("reason", ex.GetType().Name)), null);
        }
    }

    // ─── Komendy krótkie ─────────────────────────────────────────────────

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static Result Refuse(string command, string reason, params (string Key, object? Value)[] extra)
    {
        var fields = new List<(string, object?)> { ("reason", reason) };
        fields.AddRange(extra);
        return new Result(PilotStatus.BuildAckJson(command, false, [.. fields]), null);
    }

    /// <summary>Broadcast świeżej listy — po każdej zmianie, jednym sposobem.</summary>
    private static Func<Func<string, Task>, Task> BroadcastList(DevicesPort port) =>
        async broadcast => await SafeSendAsync(broadcast, BuildDevicesJson(await port.List()));

    /// <summary>
    /// Włączenie/wyłączenie JEDNEGO urządzenia. Ack wychodzi od razu, bo Wake-on-LAN wysyła
    /// serię pakietów przez kilkanaście sekund, a Samsung potrafi wisieć na timeoucie;
    /// wynik widać w rozgłoszonej liście.
    /// </summary>
    private static async Task<Result> PowerAsync(DevicesPort port, JsonElement root)
    {
        var id = Text(root, "id");
        if (string.IsNullOrWhiteSpace(id)) return Refuse(PowerCommand, ReasonInvalidValue, ("key", "id"));
        if (!root.TryGetProperty("on", out var onEl) ||
            onEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Refuse(PowerCommand, ReasonInvalidValue, ("key", "on"));
        bool on = onEl.GetBoolean();

        // Istnienie sprawdzamy PRZED ackiem — „nie ma takiego urządzenia" to odpowiedź na
        // komendę, a nie wynik operacji, i tablet ma ją dostać od razu.
        var list = await port.List();
        if (!list.Any(e => e.Device.Id == id)) return Refuse(PowerCommand, ReasonNotFound, ("id", id));

        return new Result(
            PilotStatus.BuildAckJson(PowerCommand, true, ("id", id), ("on", on)),
            async broadcast =>
            {
                try { await port.Power(id!, on); }
                catch { /* sterowniki i tak nie rzucają; lista poniżej pokaże stan faktyczny */ }
                await SafeSendAsync(broadcast, BuildDevicesJson(await port.List()));
            });
    }

    /// <summary>
    /// Zmiana własnego oznaczenia (to samo pole co w oknie: maks. 4 znaki, puste = numer).
    ///
    /// <para>Za długie oznaczenie ODRZUCAMY, zamiast dociąć — dokładnie jak
    /// <c>loop_interval</c> w ustawieniach systemowych. Ciche docięcie pokazałoby na pasku
    /// coś innego, niż tablet wysłał, a to wygląda jak samowolna zmiana.</para>
    /// </summary>
    private static async Task<Result> RenameAsync(DevicesPort port, JsonElement root)
    {
        var id = Text(root, "id");
        if (string.IsNullOrWhiteSpace(id)) return Refuse(RenameCommand, ReasonInvalidValue, ("key", "id"));

        var label = Text(root, "label");
        if (label is null) return Refuse(RenameCommand, ReasonInvalidValue, ("key", "label"));
        label = label.Trim();
        if (label.Length > MaxLabelLength)
            return Refuse(RenameCommand, ReasonTooLong, ("key", "label"), ("max", MaxLabelLength));

        if (!await port.Rename(id!, label)) return Refuse(RenameCommand, ReasonNotFound, ("id", id));

        return new Result(
            PilotStatus.BuildAckJson(RenameCommand, true, ("id", id), ("label", label)),
            BroadcastList(port));
    }

    private static async Task<Result> RemoveAsync(DevicesPort port, JsonElement root)
    {
        var id = Text(root, "id");
        if (string.IsNullOrWhiteSpace(id)) return Refuse(RemoveCommand, ReasonInvalidValue, ("key", "id"));

        if (!await port.Remove(id!)) return Refuse(RemoveCommand, ReasonNotFound, ("id", id));

        return new Result(
            PilotStatus.BuildAckJson(RemoveCommand, true, ("id", id)),
            BroadcastList(port));
    }

    /// <summary>
    /// Test łączności z JEDNYM urządzeniem. Wynik wraca w ACKU (tak mówi kontrakt i tak działa
    /// przycisk „testuj" w oknie) — to pojedyncze zapytanie z timeoutem, nie skan sieci.
    /// Przy okazji rozgłaszamy listę: świeżo odczytany stan ma trafić także do drugiego tabletu.
    /// </summary>
    private static async Task<Result> TestAsync(DevicesPort port, JsonElement root)
    {
        var id = Text(root, "id");
        if (string.IsNullOrWhiteSpace(id)) return Refuse(TestCommand, ReasonInvalidValue, ("key", "id"));

        var (found, ok, message) = await port.Test(id!);
        if (!found) return Refuse(TestCommand, ReasonNotFound, ("id", id));

        return new Result(
            PilotStatus.BuildAckJson(TestCommand, ok, ("id", id), ("message", message)),
            BroadcastList(port));
    }

    // ─── Wykrywanie ──────────────────────────────────────────────────────

    /// <summary>
    /// Skan sieci. Ack od razu, wynik broadcastem <c>devices_found</c> — SSDP czeka na
    /// odpowiedzi kilka sekund. Drugie wykrywanie w trakcie pierwszego dostaje <c>busy</c>
    /// (to samo rozstrzygnięcie co przy operacjach konserwacyjnych).
    /// </summary>
    private static Result Discover(DevicesPort port)
    {
        lock (_lock)
        {
            if (_discovering) return Refuse(DiscoverCommand, ReasonBusy);
            _discovering = true;
        }

        return new Result(
            PilotStatus.BuildAckJson(DiscoverCommand, true),
            async broadcast =>
            {
                // Lista z `discovering:true` leci PRZED skanem — tablet, który właśnie się
                // podłączył, ma wiedzieć, że coś trwa.
                await SafeSendAsync(broadcast, BuildDevicesJson(await SafeListAsync(port)));

                string? error = null;
                var found = new List<(string, Found)>();
                try
                {
                    var devices = await port.Discover();
                    lock (_lock)
                    {
                        _discovered.Clear();
                        foreach (var d in devices)
                        {
                            var key = Guid.NewGuid().ToString("N")[..8];
                            _discovered[key] = d;
                            found.Add((key, d));
                        }
                    }
                }
                catch (Exception ex) { error = Describe(ex); }
                finally { lock (_lock) { _discovering = false; } }

                // NIEZMIENNIK: komunikat KOŃCOWY wychodzi także po wyjątku — bez niego tablet
                // zostaje z kręciołkiem, którego nic nie zatrzyma.
                await SafeSendAsync(broadcast, BuildFoundJson(found, error));
                await SafeSendAsync(broadcast, BuildDevicesJson(await SafeListAsync(port)));
            });
    }

    // ─── Parowanie ───────────────────────────────────────────────────────

    /// <summary>
    /// Parowanie i dodanie urządzenia. Przyjmuje SAMO <c>ip</c> (ścieżka ręczna, lekcja v1.55)
    /// albo <c>discoveredId</c> z ostatniego wykrywania.
    ///
    /// <para>Dziś sparować da się wyłącznie Samsunga — inne <c>kind</c> dostaje jawną odmowę
    /// <c>unsupported_kind</c>, bo sparowanie projektora PJLink „jako Samsunga" dałoby wpis,
    /// który wygląda poprawnie i nigdy nie zadziała.</para>
    ///
    /// <para>Telewizor Samsung pyta o zgodę NA SWOIM EKRANIE i ktoś musi ją kliknąć — tablet
    /// ma to powiedzieć PRZED wysłaniem komendy, a nie pokazywać kręciołek i po czasie
    /// „nie udało się".</para>
    /// </summary>
    private static Result Pair(DevicesPort port, JsonElement root)
    {
        var kind = Text(root, "kind");
        if (kind != null && kind.Trim().Length > 0 && kind.Trim() != "samsung")
            return Refuse(PairCommand, ReasonUnsupported, ("kind", kind.Trim()));

        string? ip = Text(root, "ip")?.Trim();
        string? name = null, mac = null;

        var discoveredId = Text(root, "discoveredId")?.Trim();
        if (!string.IsNullOrEmpty(discoveredId))
        {
            Found? hit;
            lock (_lock) hit = _discovered.TryGetValue(discoveredId, out var f) ? f : null;
            if (hit is null) return Refuse(PairCommand, ReasonNotFound, ("discoveredId", discoveredId));
            ip = hit.Ip;
            name = hit.Name;
            mac = hit.Mac;
        }

        if (string.IsNullOrWhiteSpace(ip)) return Refuse(PairCommand, ReasonInvalidValue, ("key", "ip"));

        return new Result(
            PilotStatus.BuildAckJson(PairCommand, true, ("ip", ip)),
            async broadcast =>
            {
                bool ok = false;
                string? error = null;
                try
                {
                    (ok, error) = await port.Pair(ip!, name, mac);
                }
                catch (Exception ex) { error = Describe(ex); }

                // Komunikat KOŃCOWY zawsze — także po wyjątku (zob. niezmiennik w opisie klasy).
                await SafeSendAsync(broadcast, BuildPairResultJson(ok, ip!, ok ? null : error));
                await SafeSendAsync(broadcast, BuildDevicesJson(await SafeListAsync(port)));
            });
    }

    // ─── Dodanie bez parowania (PJLink / Sony / Wake-on-LAN) ─────────────

    /// <summary>Typy, które dodaje się SAMYM opisem — bez pytania sprzętu o zgodę.</summary>
    private static readonly string[] AddableKinds = ["pjlink", "sony", "wol"];

    /// <summary>
    /// Dodanie urządzenia INNEGO niż Samsung. Odpowiednik przycisku „Dodaj" z sekcji
    /// „Urządzenia projekcyjne" w oknie — i tamta ścieżka to wykonuje.
    ///
    /// <para><b>Po co osobna komenda.</b> <c>device_pair</c> robi handshake z telewizorem
    /// Samsung, który pyta o zgodę NA SWOIM EKRANIE. Projektor PJLink o nic nie pyta i nie ma
    /// czego potwierdzać, więc to nie jest ten sam czasownik z innym parametrem, tylko inna
    /// operacja: tu nic nie leci do sieci, dopisujemy wpis do listy. Dlatego wynik jest
    /// w ACKU od razu, bez komunikatu końcowego. Samsung dostaje stąd jawne
    /// <c>unsupported_kind</c> — wpis „Samsung bez tokenu" wyglądałby poprawnie i nigdy
    /// by nie zadziałał.</para>
    ///
    /// <para><b>Projektor kościelny to najczęściej PJLink</b> — bez tej komendy parafia
    /// w trybie serwerowym nadal musiałaby podpiąć klawiaturę do mini PC, żeby podłączyć
    /// nowy projektor. To był powód całego etapu.</para>
    ///
    /// <para><b>Poświadczenia wchodzą, nie wychodzą:</b> <c>password</c> (PJLink) i <c>psk</c>
    /// (Sony) zapisujemy w <c>projection_devices</c>, a <see cref="BuildDevicesJson"/> ich
    /// nie wypisuje. Nie logujemy ich też przy odmowie — powód odmowy to sama nazwa pola.</para>
    /// </summary>
    private static async Task<Result> AddAsync(DevicesPort port, JsonElement root)
    {
        var kind = Text(root, "kind")?.Trim();
        if (string.IsNullOrEmpty(kind)) return Refuse(AddCommand, ReasonInvalidValue, ("key", "kind"));
        if (!AddableKinds.Contains(kind)) return Refuse(AddCommand, ReasonUnsupported, ("kind", kind));

        // Oznaczenie ODRZUCAMY za długie, zamiast dociąć — dokładnie jak device_rename.
        var label = (Text(root, "label") ?? "").Trim();
        if (label.Length > MaxLabelLength)
            return Refuse(AddCommand, ReasonTooLong, ("key", "label"), ("max", MaxLabelLength));

        var name = Text(root, "name")?.Trim();
        var ip = (Text(root, "ip") ?? "").Trim();
        var mac = (Text(root, "mac") ?? "").Trim();

        // ── pola obowiązkowe zależne od typu ──
        if (kind == "wol")
        {
            if (mac.Length == 0) return Refuse(AddCommand, ReasonInvalidValue, ("key", "mac"));
        }
        else if (ip.Length == 0) return Refuse(AddCommand, ReasonInvalidValue, ("key", "ip"));

        if (ip.Length > 0 && !IsAddress(ip)) return Refuse(AddCommand, ReasonInvalidValue, ("key", "ip"));
        if (mac.Length > 0 && !IsMac(mac)) return Refuse(AddCommand, ReasonInvalidValue, ("key", "mac"));

        string? password = null;
        int tcpPort = 4352;
        if (kind == "pjlink")
        {
            password = Text(root, "password");   // hasło PJLink bywa puste — to normalne
            if (root.TryGetProperty("port", out var portEl))
            {
                if (portEl.ValueKind != JsonValueKind.Number || !portEl.TryGetInt32(out tcpPort) ||
                    tcpPort < 1 || tcpPort > 65535)
                    return Refuse(AddCommand, ReasonInvalidValue, ("key", "port"));
            }
        }
        else if (kind == "sony")
        {
            // PSK ustawia się w telewizorze; bez niego sterownik dostanie 403 przy każdej komendzie,
            // więc puste pole to błąd, a nie „na razie bez hasła".
            password = Text(root, "psk")?.Trim();
            if (string.IsNullOrEmpty(password)) return Refuse(AddCommand, ReasonInvalidValue, ("key", "psk"));
        }

        // ── duplikat: ten sam sprzęt nie ma trafić na listę drugi raz ──
        var list = await port.List();
        if (ip.Length > 0 &&
            list.Any(e => string.Equals(e.Device.Ip.Trim(), ip, StringComparison.OrdinalIgnoreCase)))
            return Refuse(AddCommand, ReasonDuplicate, ("key", "ip"));
        if (mac.Length > 0 &&
            list.Any(e => NormalizeMac(e.Device.Mac) == NormalizeMac(mac)))
            return Refuse(AddCommand, ReasonDuplicate, ("key", "mac"));

        var id = await port.Add(new AddRequest(kind, ip, mac, name, label, password, tcpPort));

        return new Result(
            PilotStatus.BuildAckJson(AddCommand, true, ("id", id), ("kind", kind)),
            BroadcastList(port));
    }

    /// <summary>
    /// Adres urządzenia: literał IPv4/IPv6 albo nazwa hosta. Sprzęt w parafii adresuje się
    /// zwykle po IP, ale nazwa z DNS-u też jest poprawna — odsiewamy śmieci (spacje, puste,
    /// przypadkowy tekst), nie zawężamy do jednej postaci.
    /// </summary>
    private static bool IsAddress(string value)
    {
        if (System.Net.IPAddress.TryParse(value, out _)) return true;
        if (value.Length > 253 || value.Any(char.IsWhiteSpace)) return false;
        return value.Split('.').All(part =>
            part.Length is > 0 and <= 63 &&
            char.IsLetterOrDigit(part[0]) && char.IsLetterOrDigit(part[^1]) &&
            part.All(ch => char.IsLetterOrDigit(ch) || ch == '-'));
    }

    /// <summary>Adres MAC: sześć par szesnastkowych po dwukropkach/myślnikach albo 12 znaków ciągiem.</summary>
    private static bool IsMac(string value)
    {
        var bare = NormalizeMac(value);
        return bare.Length == 12 && bare.All(Uri.IsHexDigit);
    }

    /// <summary>Postać porównywalna: bez separatorów, wielkimi literami (AA-BB-… = aa:bb:…).</summary>
    private static string NormalizeMac(string? value) =>
        new string([.. (value ?? "").Where(ch => ch is not (':' or '-' or ' ' or '.'))]).ToUpperInvariant();

    // ─── Drobiazgi ───────────────────────────────────────────────────────

    /// <summary>Lista, która nie wywróci komunikatu końcowego, gdy baza akurat padnie.</summary>
    private static async Task<IReadOnlyList<DeviceEntry>> SafeListAsync(DevicesPort port)
    {
        try { return await port.List(); } catch { return []; }
    }

    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Wysyłka, która NIE MOŻE przewrócić operacji: zerwane łącze w trakcie parowania jest
    /// normalne, a wyjątek stąd zjadłby komunikat końcowy.
    /// </summary>
    private static async Task SafeSendAsync(Func<string, Task> broadcast, string json)
    {
        try { await broadcast(json); } catch { }
    }
}
