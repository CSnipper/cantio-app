using System.IO;
using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Operacje konserwacyjne z tabletu: podgląd FOLDERU WYMIANY i długie operacje na plikach —
/// kopia zapasowa bazy, eksport archiwum, import psalmów (etap 4A) oraz przywrócenie bazy,
/// import archiwum, wyczyszczenie danych i importy pieśni (etap 4B).
///
/// <para><b>Trzy zasady operacji NIEODWRACALNYCH</b> (<c>restore_db</c>, <c>import_zip</c>,
/// <c>clear_db</c>): (1) <c>confirm:true</c> jest OBOWIĄZKOWE — komenda kasująca bazę parafii
/// nie może wyjść z przypadkowego dotknięcia ekranu; (2) AUTOMATYCZNA KOPIA bieżącej bazy
/// powstaje PRZED zniszczeniem, a jej nazwa wraca w <c>result.backupFile</c> (po restarcie nie
/// ma już komu powiedzieć, skąd wracać); (3) plik do przywrócenia jest SPRAWDZANY, zanim
/// nadpisze działającą bazę — podmiana na śmieć daje program, którego przy mini PC bez
/// klawiatury nikt nie postawi z powrotem.</para>
///
/// <para><b>Po co.</b> Mini PC w zakrystii pracuje bez klawiatury i z ukrytym oknem, a tablet
/// jest jedynym interfejsem. Wszystko, co wymaga PLIKU, było dotąd poza jego zasięgiem, bo
/// tablet nie widzi dysku komputera. Rozwiązanie: <see cref="ExchangeFolder"/> — jeden katalog,
/// do którego pliki wkłada człowiek, a tablet widzi wyłącznie jego zawartość.</para>
///
/// <para><b>Ack NATYCHMIAST, wynik broadcastem.</b> Kopiowanie bazy i pakowanie archiwum trwa,
/// a łącze służy do sterowania projekcją — <c>maintenance_run</c> odpowiada więc od razu
/// identyfikatorem zadania, a postęp i wynik lecą osobnymi komunikatami
/// <c>maintenance_progress</c> do WSZYSTKICH klientów (drugi tablet w zakrystii ma widzieć,
/// że ktoś właśnie robi kopię).</para>
///
/// <para><b>NIEZMIENNIK: operacja ZAWSZE kończy się komunikatem terminalnym</b>
/// (<c>done</c> albo <c>failed</c>) — także wtedy, gdy rzuci wyjątkiem. Tablet po acku CZEKA,
/// więc cisza to zawieszony ekran bez wyjścia. To ten sam wniosek, co przy <c>get_songs</c>
/// („desktop NIGDY nie milczy", v1.64): tam połknięty <c>catch</c> potrafił zawiesić Pilota
/// na stronie 0 biblioteki. Gwarancję daje <see cref="ExecuteAsync"/>: całe ciało operacji
/// siedzi w <c>try</c>, każda ścieżka wyjścia wysyła komunikat terminalny, a <c>finally</c>
/// zwalnia blokadę „jedna operacja naraz" niezależnie od wyniku.</para>
///
/// <para><b>Jedna operacja naraz.</b> Druga dostaje <c>ok:false, reason:"busy"</c>. Mini PC
/// w zakrystii bywa słaby, a dwie operacje dyskowe naraz to najprostszy sposób, żeby obie
/// trwały dwa razy dłużej i żeby archiwum powstało z bazy kopiowanej w tle.</para>
///
/// <para><b>Tablet nie podaje ścieżki zapisu.</b> Kopia i eksport lądują w folderze wymiany
/// pod nazwą z datą i godziną (<see cref="MaintenanceOps"/>). Gdyby ścieżkę podawał tablet,
/// miałby wpływ na to, gdzie komputer zapisuje pliki — a to jest dokładnie ta władza, której
/// folder wymiany ma nie dawać.</para>
///
/// <para>Wzorzec 1:1 jak w <see cref="PilotSystemSettings"/>: komunikaty składa WYŁĄCZNIE
/// ta klasa, handler u gospodarza jest głupi, wszystko za bramą auth.</para>
/// </summary>
public static class PilotMaintenance
{
    public const string GetFilesCommand = "get_exchange_files";
    public const string FilesDataType   = "exchange_files_data";
    public const string RunCommand      = "maintenance_run";
    public const string ProgressType    = "maintenance_progress";

    // ── Operacje etapu 4A (niczego nie niszczą) ──────────────────────────
    /// <summary>Kopia pliku bazy do folderu wymiany.</summary>
    public const string OpBackupDb     = "backup_db";
    /// <summary>Archiwum ZIP (baza + obrazki) do folderu wymiany.</summary>
    public const string OpExportZip    = "export_zip";
    /// <summary>Ponowny import wbudowanych psalmów responsoryjnych.</summary>
    public const string OpImportPsalms = "import_psalms";

    // ── Operacje etapu 4B ────────────────────────────────────────────────
    // Trzy pierwsze są NIEODWRACALNE: wymagają `confirm:true`, robią automatyczną kopię
    // zapasową i kończą się RESTARTEM programu.
    /// <summary>Podmiana <c>cantio.db</c> plikiem z folderu wymiany → RESTART.</summary>
    public const string OpRestoreDb   = "restore_db";
    /// <summary>Rozpakowanie archiwum (baza + obrazki) do katalogu danych → RESTART.</summary>
    public const string OpImportZip   = "import_zip";
    /// <summary>Wyczyszczenie danych → RESTART.</summary>
    public const string OpClearDb     = "clear_db";
    /// <summary>Import pieśni z pliku (OpenLP/OpenSong/OSZ). BEZ restartu i bez kopii.</summary>
    public const string OpImportSongs = "import_songs";

    // Powody odmowy w acku.
    public const string ReasonBusy            = "busy";
    public const string ReasonUnknownOp       = "unknown_op";
    /// <summary>Operacja nieodwracalna bez <c>confirm:true</c> — nic się nie dzieje.</summary>
    public const string ReasonConfirmRequired = "confirm_required";
    /// <summary>Brakująca albo niebezpieczna nazwa pliku (musi być GOŁĄ nazwą z folderu wymiany).</summary>
    public const string ReasonBadFile         = "bad_file";
    /// <summary>Nieznana wartość pola <c>format</c> przy imporcie pieśni.</summary>
    public const string ReasonUnknownFormat   = "unknown_format";

    // Stany w `maintenance_progress`. `done` i `failed` są TERMINALNE.
    public const string StateRunning = "running";
    public const string StateDone    = "done";
    public const string StateFailed  = "failed";

    /// <summary>Kod niepowodzenia importu psalmów: w bazie nie ma kategorii „Psalmy responsoryjne".</summary>
    public const string MessageCategoryMissing = "psalms_category_missing";
    /// <summary>Plik zniknął z folderu wymiany między listowaniem a operacją.</summary>
    public const string MessageFileNotFound    = "file_not_found";
    /// <summary>Plik podany do przywrócenia NIE JEST bazą SQLite — działająca baza NIE została tknięta.</summary>
    public const string MessageNotADatabase    = "not_a_database";
    /// <summary>Archiwum zawiera wpis wychodzący poza katalog danych („zip slip") — ODRZUCONE w całości.</summary>
    public const string MessageUnsafeEntry     = "zip_entry_outside_target";
    /// <summary>Kategoria zastępcza o podanej nazwie nie istnieje (nie zgadujemy, do której trafią pieśni).</summary>
    public const string MessageCategoryUnknown = "category_not_found";

    /// <summary>
    /// Restart programu po operacji NIEODWRACALNEJ — port od gospodarza (rdzeń kompiluje się
    /// też pod Androida i nie zna ani WPF, ani <c>Application.Shutdown</c>). Ten sam wzorzec
    /// co <c>PilotImages.Scaler</c> i <c>PilotSystemSettings.RunOnStartup</c>.
    ///
    /// <para><b>Wołany DOPIERO PO wysłaniu komunikatu terminalnego</b> — proces zaraz zniknie
    /// i drugiej szansy na powiadomienie tabletu nie ma (ta sama reguła co przy
    /// <c>restart_app</c> i <c>pilot_forget_devices</c>; w tej serii zadań błąd odwrotnej
    /// kolejności już raz powstał i wymagał osobnej rundy naprawczej).</para>
    /// </summary>
    public static Action? Restart { get; set; }

    /// <summary>
    /// Blokada „jedna operacja naraz" i zarazem identyfikator trwającego zadania. Stan żyje
    /// u gospodarza (jedna instancja na aplikację), a NIE przy gnieździe klienta: operacja
    /// trwa dalej, gdy tablet się rozłączy, i drugi tablet ma wtedy dostać <c>busy</c>.
    /// </summary>
    public sealed class Runner
    {
        private readonly System.Threading.Lock _lock = new();
        private string? _taskId;

        /// <summary>Identyfikator trwającego zadania albo <c>null</c>.</summary>
        public string? Current
        {
            get { lock (_lock) { return _taskId; } }
        }

        public bool IsBusy => Current != null;

        /// <summary>
        /// Rezerwuje wyłączność. <c>true</c> = <paramref name="taskId"/> to NOWE zadanie,
        /// <c>false</c> = zajęte, a <paramref name="taskId"/> niesie identyfikator zadania,
        /// które właśnie TRWA (tablet ma czym podpisać odmowę).
        ///
        /// <para>Sprawdzenie, rezerwacja i odczyt trwającego identyfikatora są w JEDNEJ sekcji
        /// krytycznej. Dwa tablety naciskające naraz zaczęłyby inaczej obie operacje, a odczyt
        /// „kto zajmuje" POZA blokadą trafiał w <c>null</c>, gdy operacja kończyła się w tej
        /// samej milisekundzie (złapane w harnessie).</para>
        /// </summary>
        public bool TryStart(out string taskId)
        {
            lock (_lock)
            {
                if (_taskId != null) { taskId = _taskId; return false; }
                _taskId = Guid.NewGuid().ToString("N")[..12];
                taskId = _taskId;
                return true;
            }
        }

        /// <summary>Zwalnia blokadę, o ile trwa DOKŁADNIE to zadanie (spóźniony sprzątacz nic nie psuje).</summary>
        public void Finish(string taskId)
        {
            lock (_lock)
            {
                if (_taskId == taskId) _taskId = null;
            }
        }
    }

    /// <param name="Response">JSON do NADAWCY (ack albo dane); <c>null</c> = nic nie odsyłamy</param>
    /// <param name="Work">
    /// Długa operacja do uruchomienia DOPIERO PO wysłaniu <paramref name="Response"/> — dostaje
    /// funkcję broadcastu i sama rozgłasza <c>maintenance_progress</c>. <c>null</c> = nie ma co
    /// uruchamiać. Gospodarz woła to bez <c>await</c>: ack ma wyjść natychmiast, a operacja
    /// trwa w tle.
    /// </param>
    public readonly record struct Result(string? Response, Func<Func<string, Task>, Task>? Work);

    private static readonly Result Ignored = new(null, null);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) => type is GetFilesCommand or RunCommand;

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>
    /// <c>exchange_files_data</c>: zawartość folderu wymiany + PEŁNA ŚCIEŻKA katalogu.
    /// Ścieżkę podajemy, żeby operator wiedział, gdzie na komputerze wrzucić plik — bez niej
    /// instrukcja „włóż plik do folderu wymiany" jest nie do wykonania.
    /// </summary>
    public static string BuildFilesJson()
    {
        var files = ExchangeFolder.List();
        return JsonSerializer.Serialize(new
        {
            type  = FilesDataType,
            path  = ExchangeFolder.Path,
            files = files.Select(f => new
            {
                name     = f.Name,
                size     = f.Size,
                modified = f.Modified,
                kind     = f.Kind
            }).ToArray()
        });
    }

    /// <summary>Komunikat postępu/wyniku. Jedyne miejsce, w którym powstaje <c>maintenance_progress</c>.</summary>
    public static string BuildProgressJson(string taskId, string op, string state,
                                           int percent, string? message = null,
                                           IReadOnlyDictionary<string, object?>? result = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"]    = ProgressType,
            ["taskId"]  = taskId,
            ["op"]      = op,
            ["state"]   = state,
            ["percent"] = percent
        };
        if (message != null) payload["message"] = message;
        if (result  != null) payload["result"]  = result;
        return JsonSerializer.Serialize(payload);
    }

    // ─── Wejście ─────────────────────────────────────────────────────────

    public static Result Handle(Runner runner, DatabaseService db, string rawJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
            return Ignored;

        switch (typeEl.GetString())
        {
            case GetFilesCommand:
                // Listowanie samo tworzy katalog przy pierwszym użyciu; pusty katalog daje
                // PUSTĄ LISTĘ, nie błąd. Awaria dysku nie może wywrócić handlera.
                try { return new Result(BuildFilesJson(), null); }
                catch (Exception ex)
                {
                    return new Result(
                        PilotStatus.BuildAckJson(GetFilesCommand, false, ("reason", ex.GetType().Name)), null);
                }

            case RunCommand:
                return Run(runner, db, root);

            default:
                return Ignored;
        }
    }

    /// <summary>Czy operacja jest NIEODWRACALNA: wymaga <c>confirm:true</c>, kopii zapasowej i restartu.</summary>
    private static bool IsDestructive(string op) => op is OpRestoreDb or OpImportZip or OpClearDb;

    /// <summary>Czy operacja bierze plik z folderu wymiany.</summary>
    private static bool NeedsFile(string op) => op is OpRestoreDb or OpImportZip or OpImportSongs;

    /// <summary>Sparsowane wejście jednej operacji — wszystko, co potrzebne, wyjęte z JSON-a raz.</summary>
    private sealed record Request(
        string Op,
        string? FilePath,
        MaintenanceOps.SongImportFormat Format,
        Import.ImportOptions Options,
        string? FallbackCategory);

    private static Result Run(Runner runner, DatabaseService db, JsonElement root)
    {
        var op = root.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String
            ? (opEl.GetString() ?? "").Trim()
            : "";

        // Nieznaną operację odsiewamy PRZED rezerwacją blokady — inaczej literówka z tabletu
        // blokowałaby maszynę na czas, którego nikt nie zwolni. Tą samą drogą idą pozostałe
        // odmowy „z kształtu komendy": nic nie startuje, więc nie ma czego zwalniać.
        if (op is not (OpBackupDb or OpExportZip or OpImportPsalms
                       or OpRestoreDb or OpImportZip or OpClearDb or OpImportSongs))
            return Refuse(ReasonUnknownOp, op);

        // POTWIERDZENIE operacji nieodwracalnej. Komenda, która kasuje całą bazę parafii, nie
        // może wyjść z przypadkowego dotknięcia ekranu ani ze zgubionego bitu w starym kliencie
        // — dlatego brak `confirm:true` to odmowa, a nie „domyślnie tak".
        if (IsDestructive(op) && !(root.TryGetProperty("confirm", out var cEl) &&
                                   cEl.ValueKind == JsonValueKind.True))
            return Refuse(ReasonConfirmRequired, op);

        // Nazwa pliku: WYŁĄCZNIE goła nazwa z folderu wymiany (`ExchangeFolder.Resolve` jest
        // jedynym miejscem, w którym nazwa z protokołu zamienia się w ścieżkę na dysku).
        string? filePath = null;
        if (NeedsFile(op))
        {
            var name = root.TryGetProperty("file", out var fEl) && fEl.ValueKind == JsonValueKind.String
                ? fEl.GetString()
                : null;
            filePath = ExchangeFolder.Resolve(name);
            if (filePath is null) return Refuse(ReasonBadFile, op);
        }

        var format = MaintenanceOps.SongImportFormat.OpenLpSqlite;
        var options = new Import.ImportOptions();
        string? fallbackCategory = null;

        if (op == OpImportSongs)
        {
            var parsed = MaintenanceOps.ParseSongFormat(
                root.TryGetProperty("format", out var fmtEl) && fmtEl.ValueKind == JsonValueKind.String
                    ? fmtEl.GetString()
                    : null);
            if (parsed is null) return Refuse(ReasonUnknownFormat, op);
            format = parsed.Value;

            options.ImportCategories = Flag(root, "importCategories", true);
            options.OverwriteExisting = Flag(root, "overwrite", false);
            options.OpenLpCategorySource =
                root.TryGetProperty("categorySource", out var csEl) && csEl.ValueKind == JsonValueKind.String
                && csEl.GetString() == "topics"
                    ? Import.OpenLpCategorySource.Topics
                    : Import.OpenLpCategorySource.SongBooks;
            if (root.TryGetProperty("fallbackCategory", out var fcEl) &&
                fcEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(fcEl.GetString()))
                fallbackCategory = fcEl.GetString()!.Trim();
        }

        if (!runner.TryStart(out var taskId))
            return new Result(
                PilotStatus.BuildAckJson(RunCommand, false,
                    ("reason", ReasonBusy), ("op", op), ("taskId", taskId)), null);

        var request = new Request(op, filePath, format, options, fallbackCategory);
        return new Result(
            PilotStatus.BuildAckJson(RunCommand, true, ("op", op), ("taskId", taskId)),
            broadcast => ExecuteAsync(runner, db, taskId, request, broadcast));
    }

    private static Result Refuse(string reason, string op) =>
        new(PilotStatus.BuildAckJson(RunCommand, false, ("reason", reason), ("op", op)), null);

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : fallback;

    /// <summary>
    /// Wykonanie operacji z GWARANCJĄ komunikatu terminalnego (zob. opis klasy). Każda ścieżka
    /// wyjścia — sukces, niepowodzenie logiczne, wyjątek — kończy się <c>done</c> albo
    /// <c>failed</c>; <c>finally</c> zwalnia blokadę niezależnie od wszystkiego.
    /// </summary>
    private static async Task ExecuteAsync(Runner runner, DatabaseService db,
                                           string taskId, Request request, Func<string, Task> broadcast)
    {
        var op = request.Op;
        bool restart = false;
        try
        {
            await SafeSendAsync(broadcast, BuildProgressJson(taskId, op, StateRunning, 0));

            int percent = 0;
            using var pumpCts = new CancellationTokenSource();
            // Pompa postępu: operacje są SYNCHRONICZNE i blokujące, więc procent zgłaszają
            // do zmiennej, a rozgłasza go osobne zadanie. Pompę zatrzymujemy PRZED komunikatem
            // terminalnym, żeby spóźniony `running` nie przyszedł po `done`.
            var pump = Task.Run(async () =>
            {
                int last = 0;
                while (!pumpCts.IsCancellationRequested)
                {
                    try { await Task.Delay(250, pumpCts.Token); } catch { return; }
                    int now = Volatile.Read(ref percent);
                    if (now > last && now < 100)
                    {
                        last = now;
                        await SafeSendAsync(broadcast, BuildProgressJson(taskId, op, StateRunning, now));
                    }
                }
            });

            string? failure = null;
            Dictionary<string, object?>? result = null;

            try
            {
                switch (op)
                {
                    case OpBackupDb:
                    {
                        var name = MaintenanceOps.BackupFileName(DateTime.Now);
                        var dest = Path.Combine(ExchangeFolder.EnsureCreated(), name);
                        await Task.Run(() => MaintenanceOps.BackupDatabase(dest));
                        result = new Dictionary<string, object?> { ["file"] = name };
                        break;
                    }
                    case OpExportZip:
                    {
                        var name = MaintenanceOps.ExportFileName(DateTime.Now);
                        var dest = Path.Combine(ExchangeFolder.EnsureCreated(), name);
                        await Task.Run(() => MaintenanceOps.ExportZip(dest, p => Volatile.Write(ref percent, p)));
                        result = new Dictionary<string, object?> { ["file"] = name };
                        break;
                    }
                    case OpImportPsalms:
                    {
                        int count = await MaintenanceOps.ImportPsalmsAsync(db);
                        // -1 = brak kategorii „Psalmy responsoryjne" (okno pokazuje wtedy
                        // ostrzeżenie). To niepowodzenie, nie sukces z zerem psalmów.
                        if (count < 0) failure = MessageCategoryMissing;
                        else result = new Dictionary<string, object?> { ["count"] = count };
                        break;
                    }
                    case OpImportSongs:
                    {
                        if (!File.Exists(request.FilePath!)) { failure = MessageFileNotFound; break; }

                        // Kategoria zastępcza przychodzi NAZWĄ (tablet nie zna identyfikatorów
                        // z bazy desktopu). Nieznana nazwa = odmowa, nie „wrzućmy gdziekolwiek":
                        // pieśni w cudzej kategorii wyglądają poprawnie i nikt tego nie zauważy.
                        if (request.FallbackCategory != null)
                        {
                            var match = (await db.GetCategoriesAsync())
                                .FirstOrDefault(c => DatabaseService.NameEquals(c.Name, request.FallbackCategory));
                            if (match is null) { failure = MessageCategoryUnknown; break; }
                            request.Options.FallbackCategoryId = match.Id;
                        }

                        var outcome = await MaintenanceOps.ImportSongsAsync(
                            db, request.FilePath!, request.Format, request.Options,
                            p => Volatile.Write(ref percent, p));
                        result = new Dictionary<string, object?>
                        {
                            ["imported"] = outcome.Imported,
                            ["skipped"]  = outcome.Skipped,
                            ["errors"]   = outcome.Errors,
                            ["setlists"] = outcome.Setlists
                        };
                        break;
                    }
                    default:
                    {
                        // Operacje NIEODWRACALNE. Kolejność jest treścią bezpieczeństwa tej rundy:
                        // najpierw sprawdzamy plik (żeby nie zrobić kopii „na zapas" przy pomyłce),
                        // potem robimy KOPIĘ ZAPASOWĄ, dopiero na końcu niszczymy.
                        var (fail, backupName) = await RunDestructiveAsync(
                            db, request, p => Volatile.Write(ref percent, p));
                        if (fail != null) { failure = fail; break; }

                        result = new Dictionary<string, object?>
                        {
                            // Nazwa kopii wraca do tabletu, bo operator MUSI wiedzieć, skąd
                            // wracać — po restarcie nie ma już komu tego powiedzieć.
                            ["backupFile"] = backupName,
                            ["restart"]    = true
                        };
                        if (request.FilePath != null) result["file"] = Path.GetFileName(request.FilePath);
                        restart = true;
                        break;
                    }
                }
            }
            finally
            {
                pumpCts.Cancel();
                try { await pump; } catch { /* pompa jest ozdobą, nie może przykryć wyniku */ }
            }

            if (failure != null) restart = false;   // nic nie zniszczono — nie ma po co restartować

            await SafeSendAsync(broadcast, failure is null
                ? BuildProgressJson(taskId, op, StateDone, 100, result: result)
                : BuildProgressJson(taskId, op, StateFailed, 0, failure));
        }
        catch (Exception ex)
        {
            // ZAWSZE komunikat terminalny — cisza po acku zawiesza tablet (zob. opis klasy).
            restart = false;
            await SafeSendAsync(broadcast, BuildProgressJson(taskId, op, StateFailed, 0, Describe(ex)));
        }
        finally
        {
            runner.Finish(taskId);
        }

        // RESTART NA SAMYM KOŃCU, już PO komunikacie terminalnym i po zwolnieniu blokady.
        // Odwrotna kolejność znaczy, że tablet nigdy się nie dowie, czy operacja się udała —
        // proces znika razem z nieodesłanym komunikatem.
        if (restart)
        {
            try { Restart?.Invoke(); }
            catch { /* gospodarz zaloguje; my nie mamy już czego uratować */ }
        }
    }

    /// <summary>
    /// Operacje NIEODWRACALNE w jednym miejscu. Zwraca kod niepowodzenia (<c>null</c> = udało się)
    /// i nazwę AUTOMATYCZNEJ KOPII ZAPASOWEJ.
    ///
    /// <para><b>Kopię robimy zawsze PRZED zniszczeniem</b> i dokładnie tą samą drogą, co operacja
    /// <c>backup_db</c> (<see cref="MaintenanceOps.BackupDatabase"/> do folderu wymiany, nazwa
    /// z datą i godziną). Operacja nieodwracalna wywołana zdalnie, bez człowieka przy komputerze,
    /// MUSI zostawić drogę powrotu — projekt robi tak samo przy migracjach schematu bazy.</para>
    ///
    /// <para><b>Sprawdzenie wyprzedza kopię.</b> Plik, który nie jest bazą SQLite, albo archiwum
    /// z wpisem wychodzącym poza katalog danych — to POMYŁKA, nie operacja: wracamy z kodem
    /// niepowodzenia, nic nie jest tknięte i nie zaśmiecamy folderu wymiany kopią „na zapas".</para>
    /// </summary>
    private static async Task<(string? Failure, string? BackupFile)> RunDestructiveAsync(
        DatabaseService db, Request request, Action<int> progress)
    {
        // ── 1. Kontrola tego, co przyszło z zewnątrz ──
        if (request.Op is OpRestoreDb or OpImportZip)
        {
            if (!File.Exists(request.FilePath!)) return (MessageFileNotFound, null);

            if (request.Op == OpRestoreDb && !MaintenanceOps.LooksLikeSqliteDatabase(request.FilePath!))
                return (MessageNotADatabase, null);

            if (request.Op == OpImportZip)
            {
                // Sprawdzenie „zip slipa" PRZED kopią zapasową i przed rozpakowaniem czegokolwiek.
                // Samo rozpakowanie robi tę kontrolę drugi raz (jest jedynym strażnikiem dla
                // przycisku w oknie) — tu chodzi o to, żeby archiwum-pułapka nie kosztowało nawet
                // kopii bazy.
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(request.FilePath!);
                    foreach (var entry in zip.Entries)
                        if (MaintenanceOps.SafeExtractPath(Helpers.AppPaths.Root, entry.FullName) is null)
                            return (MessageUnsafeEntry, null);
                }
                catch (Exception ex) { return (Describe(ex), null); }
            }
        }

        // ── 2. Automatyczna kopia zapasowa ──
        var backupName = MaintenanceOps.BackupFileName(DateTime.Now);
        var backupPath = Path.Combine(ExchangeFolder.EnsureCreated(), backupName);
        await Task.Run(() => MaintenanceOps.BackupDatabase(backupPath));

        // ── 3. Zniszczenie ──
        switch (request.Op)
        {
            case OpRestoreDb:
                await Task.Run(() => MaintenanceOps.RestoreDatabase(request.FilePath!));
                break;
            case OpImportZip:
                await Task.Run(() => MaintenanceOps.ImportZip(request.FilePath!, progress));
                break;
            default:
                await MaintenanceOps.ClearDatabaseAsync(db);
                break;
        }
        return (null, backupName);
    }

    /// <summary>Krótki opis błędu dla tabletu — typ wyjątku niesie sens, gdy komunikat jest po angielsku.</summary>
    private static string Describe(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>
    /// Wysyłka, która NIE MOŻE przewrócić operacji: zerwane łącze w trakcie kopiowania bazy
    /// jest normalne (tablet uśpiono), a wyjątek stąd zjadłby komunikat terminalny.
    /// </summary>
    private static async Task SafeSendAsync(Func<string, Task> broadcast, string json)
    {
        try { await broadcast(json); } catch { }
    }
}
