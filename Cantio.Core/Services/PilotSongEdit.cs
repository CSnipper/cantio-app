using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Edytor pieśni przez protokół WS (v1.63+): <c>song_get</c>, <c>song_create</c>,
/// <c>song_update</c>, <c>song_delete</c>.
///
/// Parafia z samym tabletem musi móc poprawić literówkę, dodać pieśń i usunąć zbędną —
/// w BAZIE DESKTOPU, bo to ona jest źródłem prawdy dla projekcji. Kierunek prawdy jak przy
/// kategoriach i wyglądzie: tablet WYŁĄCZNIE komenduje, desktop zapisuje, odświeża własne UI
/// tą samą ścieżką co po edycji lokalnej i rozgłasza <c>song_changed</c> do wszystkich klientów.
///
/// Rozgłaszamy MAŁY komunikat <c>song_changed {id, action}</c>, nigdy pełnego <c>songs_data</c> —
/// biblioteka pieśni bywa duża (u użytkownika kilka tysięcy pozycji) i leci wyłącznie na żądanie
/// <c>get_songs</c>. Pilot po broadcaście sam dociąga to, czego potrzebuje.
///
/// Odpowiedzi buduje WYŁĄCZNIE ta klasa (razem z <see cref="PilotStatus.BuildAckJson"/>) —
/// jedna metoda na komunikat, żeby nie powstały dwie niezależne listy pól (ten sam układ
/// zgubił kiedyś notatki pozycji zestawu, v1.6).
/// </summary>
public static class PilotSongEdit
{
    public const string GetCommand    = "song_get";
    public const string CreateCommand = "song_create";
    public const string UpdateCommand = "song_update";
    public const string DeleteCommand = "song_delete";

    /// <summary>Odpowiedź na <c>song_get</c> — pełna treść pieśni do edycji.</summary>
    public const string DataType    = "song_data";
    /// <summary>Broadcast po każdej udanej mutacji.</summary>
    public const string ChangedType = "song_changed";

    // Powody odmowy (pole `reason` w acku)
    public const string ReasonNotFound        = "not_found";
    public const string ReasonEmptyTitle      = "empty_title";
    public const string ReasonUnsupportedType = "unsupported_type";
    public const string ReasonInSetlists      = "in_setlists";
    public const string ReasonInvalidPlayOrder = "invalid_play_order";
    /// <summary>
    /// Jawnie przysłana PUSTA tablica <c>verses</c>. Pieśń bez zwrotek nie ma czego wyświetlić,
    /// więc zamiast cicho ją opróżnić — odmawiamy. Brak pola to co innego: „nie ruszaj".
    /// </summary>
    public const string ReasonEmptyVerses      = "empty_verses";

    /// <summary>
    /// Typy zwrotek przyjmowane z tabletu. <c>img</c> świadomie NIE jest na liście —
    /// obrazek wymaga pliku na dysku komputera, którego telefon nie widzi (odmowa
    /// <see cref="ReasonUnsupportedType"/>). Zwrotki-obrazki edytuje się w oknie Cantio.
    /// </summary>
    public static readonly string[] VerseTypes = ["v", "c", "b", "p"];

    /// <summary>Co się stało z pieśnią — decyduje, jak desktop odświeża własne UI.</summary>
    public enum SongChange { None, Created, Updated, Deleted }

    /// <param name="Response">JSON do NADAWCY (ack albo <c>song_data</c>), null = nic nie odsyłamy</param>
    /// <param name="Broadcast">JSON do WSZYSTKICH klientów po mutacji, null = nic nie zmieniono</param>
    /// <param name="Change">rodzaj zmiany (None = nic nie zapisano)</param>
    /// <param name="SongId">pieśń, której dotyczyła zmiana (0 = żadna)</param>
    public readonly record struct Result(string? Response, string? Broadcast, SongChange Change, int SongId);

    private static readonly Result Ignored = new(null, null, SongChange.None, 0);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) => type switch
    {
        GetCommand or CreateCommand or UpdateCommand or DeleteCommand => true,
        _ => false
    };

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>
    /// Pełna treść pieśni. <c>categoryId</c> = 0 dla pieśni bez kategorii (ta sama konwencja
    /// co <see cref="PilotSongSync.BuildSongsDataJson"/> — stary Pilot ma tam twardy int),
    /// <c>author</c> i <c>playOrderJson</c> nigdy nie są null (pusty string = brak).
    /// </summary>
    public static string BuildSongDataJson(Song song) =>
        JsonSerializer.Serialize(new
        {
            type          = DataType,
            id            = song.Id,
            title         = song.Title,
            number        = song.Number,
            categoryId    = song.CategoryId ?? PilotSongSync.NoCategory,
            author        = song.Author ?? "",
            verses        = song.Verses.OrderBy(v => v.Position)
                                .Select(v => new { type = v.Type, text = v.Text }),
            playOrderJson = song.PlayOrderJson ?? ""
        });

    /// <summary>Broadcast <c>song_changed</c> — <paramref name="action"/> to created/updated/deleted.</summary>
    public static string BuildSongChangedJson(int id, SongChange change) =>
        JsonSerializer.Serialize(new { type = ChangedType, id, action = ActionName(change) });

    private static string ActionName(SongChange change) => change switch
    {
        SongChange.Created => "created",
        SongChange.Updated => "updated",
        SongChange.Deleted => "deleted",
        _ => "none"
    };

    // ─── Wejście ─────────────────────────────────────────────────────────

    public static async Task<Result> HandleAsync(DatabaseService db, string rawJson)
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
            GetCommand    => await GetAsync(db, root),
            CreateCommand => await SaveAsync(db, root, create: true),
            UpdateCommand => await SaveAsync(db, root, create: false),
            DeleteCommand => await DeleteAsync(db, root),
            _ => Ignored
        };
    }

    private static async Task<Result> GetAsync(DatabaseService db, JsonElement root)
    {
        if (!TryInt(root, "id", out var id) || id <= 0)
            return Deny(GetCommand, ReasonNotFound);

        var song = await db.GetSongWithVersesAsync(id);
        if (song == null) return Deny(GetCommand, ReasonNotFound, ("id", id));

        return new Result(BuildSongDataJson(song), null, SongChange.None, 0);
    }

    /// <summary>
    /// Zapis pieśni — <c>song_create</c> i <c>song_update</c> różnią się WYŁĄCZNIE tym, skąd bierze
    /// się rekord. Treść zastępowana jest w CAŁOŚCI (komplet zwrotek), dokładnie jak zapis w edytorze
    /// okna Cantio: <see cref="DatabaseService.SaveSongAsync"/> kasuje stare zwrotki i wstawia nowe.
    ///
    /// Walidacja jest UPRZEDNIA i atomowa: jedna zła zwrotka i NIC nie zapisujemy. Pieśń w pół drogi
    /// (część zwrotek nowych, część starych) wygląda na projektorze jak awaria w środku mszy.
    /// </summary>
    private static async Task<Result> SaveAsync(DatabaseService db, JsonElement root, bool create)
    {
        string cmd = create ? CreateCommand : UpdateCommand;

        Song? existing = null;
        if (!create)
        {
            if (!TryInt(root, "id", out var id) || id <= 0)
                return Deny(cmd, ReasonNotFound);
            existing = await db.GetSongWithVersesAsync(id);
            if (existing == null) return Deny(cmd, ReasonNotFound, ("id", id));
        }

        var title = Str(root, "title");
        if (title.Length == 0)
            return Deny(cmd, ReasonEmptyTitle, ("id", existing?.Id));

        // Kategoria: pole PRZYSŁANE z 0 / wartością ujemną = pieśń „bez kategorii"
        // (konwencja PilotSongSync); pole NIEPRZYSŁANE = zostaw jak było.
        int? categoryId = existing?.CategoryId;
        if (root.TryGetProperty("categoryId", out var catEl) && catEl.ValueKind == JsonValueKind.Number)
        {
            if (catEl.TryGetInt32(out var catId) && catId > 0)
            {
                if (await db.GetCategoryAsync(catId) == null)
                    return Deny(cmd, ReasonNotFound, ("id", existing?.Id), ("categoryId", catId));
                categoryId = catId;
            }
            else categoryId = null;
        }

        // Zwrotki — walidujemy WSZYSTKIE przed jakimkolwiek zapisem.
        // BRAK POLA = NIE RUSZAJ (klient zmieniający sam tytuł nie ma prawa wyczyścić pieśni);
        // jawna PUSTA tablica = odmowa, bo pieśń bez zwrotek nie ma czego wyświetlić.
        bool versesGiven = root.TryGetProperty("verses", out var versesEl) &&
                           versesEl.ValueKind == JsonValueKind.Array;
        List<Verse> verses;
        if (versesGiven)
        {
            verses = [];
            int pos = 0;
            foreach (var v in versesEl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                var vType = Str(v, "type");
                if (vType.Length == 0) vType = "v";          // brak typu = zwykła zwrotka
                if (!VerseTypes.Contains(vType))
                    return Deny(cmd, ReasonUnsupportedType, ("id", existing?.Id), ("verseType", vType));
                verses.Add(new Verse
                {
                    Type     = vType,
                    Text     = v.TryGetProperty("text", out var tEl) && tEl.ValueKind == JsonValueKind.String
                                   ? tEl.GetString() ?? "" : "",
                    Position = pos++
                });
            }
            if (verses.Count == 0 && existing != null)
                return Deny(cmd, ReasonEmptyVerses, ("id", existing.Id));

            // Protokół NIE niesie obrazków (plik leży na dysku komputera, telefon go nie widzi),
            // więc round-trip song_get → song_update wyzerowałby tła zwrotek. Przenosimy je
            // z dotychczasowej treści po POZYCJI; ImagePath dodatkowo tylko przy zgodnym typie,
            // bo zamiana zwrotki-obrazka na tekst to świadoma zmiana rodzaju, nie edycja tła.
            CarryOverVerseImages(existing, verses);
        }
        else
        {
            verses = [.. (existing?.Verses ?? []).OrderBy(v => v.Position)
                        .Select((v, i) => new Verse
                        {
                            Type = v.Type, Text = v.Text, Position = i,
                            ImagePath = v.ImagePath, BackgroundImagePath = v.BackgroundImagePath
                        })];
        }

        // Kolejność odtwarzania: indeksy zwrotek jako JSON (tak trzyma to kolumna PlayOrderJson).
        // Brak pola: gdy zwrotki wymieniono — kolejność naturalna (stare indeksy są nieważne);
        // gdy zwrotek nie ruszaliśmy — zostaje dotychczasowa (inaczej „zmiana tytułu" gubiłaby
        // ustawioną w oknie kolejność refrenów).
        string? playOrderJson = versesGiven ? null : existing?.PlayOrderJson;
        var rawOrder = Str(root, "playOrderJson");
        if (rawOrder.Length > 0)
        {
            List<int>? indices;
            try { indices = JsonSerializer.Deserialize<List<int>>(rawOrder); }
            catch { indices = null; }
            if (indices == null || indices.Count == 0 || indices.Any(i => i < 0 || i >= verses.Count))
                return Deny(cmd, ReasonInvalidPlayOrder, ("id", existing?.Id));
            playOrderJson = JsonSerializer.Serialize(indices);
        }

        var song = new Song
        {
            Id            = existing?.Id ?? 0,
            Title         = title,
            // Brak pola = NIE RUSZAJ (tak samo jak `author`); przysłane 0 czyści numer.
            Number        = TryInt(root, "number", out var num) ? (num > 0 ? num : 0) : existing?.Number ?? 0,
            Author        = root.TryGetProperty("author", out var aEl) && aEl.ValueKind == JsonValueKind.String
                                ? (aEl.GetString() ?? "").Trim() : existing?.Author,
            CategoryId    = categoryId,
            PlayOrderJson = playOrderJson,
            Verses        = verses
        };

        // Kolejność wstawiania (Song → SaveChanges → Verses) pilnuje SaveSongAsync/EF przez
        // nawigację — zwrotki nowej pieśni dostają SongId po nadaniu klucza pieśni.
        await db.SaveSongAsync(song);

        var change = create ? SongChange.Created : SongChange.Updated;
        return new Result(
            PilotStatus.BuildAckJson(cmd, true,
                ("id", song.Id), ("title", song.Title), ("verses", verses.Count)),
            BuildSongChangedJson(song.Id, change),
            change, song.Id);
    }

    /// <summary>
    /// Kasowanie pieśni. Okno Cantio pyta tylko „Usunąć pieśń?" i kasuje razem z pozycjami
    /// w ZAPISANYCH zestawach (<see cref="DatabaseService.DeleteSongAsync"/> usuwa
    /// <c>SetlistItems</c>, bo FK ma RESTRICT). Na tablecie nie ma nikogo, kto by przewidział
    /// skutek, więc protokół najpierw ODMAWIA i podaje liczbę zestawów:
    /// <c>reason:"in_setlists", setlists:N</c>. Dopiero <c>force:true</c> robi to samo,
    /// co przycisk w oknie.
    /// </summary>
    private static async Task<Result> DeleteAsync(DatabaseService db, JsonElement root)
    {
        if (!TryInt(root, "id", out var id) || id <= 0)
            return Deny(DeleteCommand, ReasonNotFound);

        var song = await db.GetSongWithVersesAsync(id);
        if (song == null) return Deny(DeleteCommand, ReasonNotFound, ("id", id));

        int setlists = await db.CountSetlistsWithSongAsync(id);
        if (setlists > 0 && !Flag(root, "force"))
            return Deny(DeleteCommand, ReasonInSetlists,
                        ("id", id), ("title", song.Title), ("setlists", setlists));

        await db.DeleteSongAsync(id);
        return new Result(
            PilotStatus.BuildAckJson(DeleteCommand, true,
                ("id", id), ("title", song.Title), ("setlists", setlists)),
            BuildSongChangedJson(id, SongChange.Deleted),
            SongChange.Deleted, id);
    }

    /// <summary>
    /// Przenosi obrazki zwrotek (<c>ImagePath</c> / <c>BackgroundImagePath</c>) ze starej treści
    /// do nowej, dopasowując PO POZYCJI. Protokół tych pól nie niesie — bez tego zwykła poprawka
    /// literówki z tabletu kasowałaby tła ustawione w oknie Cantio.
    /// <c>ImagePath</c> wędruje tylko przy zgodnym typie zwrotki: zamiana zwrotki-obrazka na
    /// tekstową jest świadomą zmianą rodzaju, a nie edycją tła.
    /// </summary>
    private static void CarryOverVerseImages(Song? existing, List<Verse> verses)
    {
        if (existing == null) return;
        var old = existing.Verses.OrderBy(v => v.Position).ToList();
        for (int i = 0; i < verses.Count && i < old.Count; i++)
        {
            verses[i].BackgroundImagePath = old[i].BackgroundImagePath;
            if (old[i].Type == verses[i].Type) verses[i].ImagePath = old[i].ImagePath;
        }
    }

    // ─── Pomocnicze ──────────────────────────────────────────────────────

    private static Result Deny(string cmd, string reason, params (string Key, object? Value)[] extra) =>
        new(PilotStatus.BuildAckJson(cmd, false, [("reason", reason), .. extra]),
            null, SongChange.None, 0);

    private static string Str(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? "").Trim() : "";

    /// <summary>Flaga boolowska; wyłącznie jawne <c>true</c> jest zgodą (brak pola i <c>false</c> = nie).</summary>
    private static bool Flag(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.True;

    private static bool TryInt(JsonElement root, string prop, out int value)
    {
        value = 0;
        return root.TryGetProperty(prop, out var el) &&
               el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }
}
