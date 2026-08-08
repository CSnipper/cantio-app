using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// „Przypnij tydzień" — JEDNO miejsce logiki dla obu ścieżek: przycisku w oknie Cantio
/// (<c>DisplayViewModel.PinNextWeek</c>) i komendy <c>pin_next_week</c> z Pilota.
///
/// Operacja jest IDEMPOTENTNA: zestaw o nazwie efektywnej dnia jest szukany po
/// <see cref="DatabaseService.GetSetlistForPinAsync"/> (nazwa + klucz okresu), a zakładany tylko
/// wtedy, gdy go nie ma. Powtórne wywołanie tego samego dnia niczego nie tworzy i zwraca
/// <c>pinned = 0</c> — to jedyny sensowny kontrakt, bo operator klika ten przycisk odruchowo.
///
/// Nazwa zestawu NIGDY nie niesie obchodu — obchód wraca osobno jako <c>celebration</c>
/// (zob. <see cref="PinnedCelebrations"/>), żeby ten sam zestaw dało się reużyć w kolejnym roku.
///
/// Zmiana flagi przypięcia idzie przez <see cref="DatabaseService.SetSetlistPinnedAsync"/>,
/// więc NIE rusza <c>UpdatedAt</c> (tabela w Cantio/Services/CLAUDE.md).
/// </summary>
public static class PilotPinWeek
{
    public const string Command = "pin_next_week";
    /// <summary>Broadcast z podpisami obchodów dla listy PRZYPIĘTE.</summary>
    public const string CelebrationsMessage = "pinned_celebrations";

    /// <summary>Ile dni przypina jedno kliknięcie / jedna komenda.</summary>
    public const int Days = 7;

    /// <param name="Date">data dnia</param>
    /// <param name="Name">efektywna nazwa zestawu (temporalna albo wypierający obchód)</param>
    /// <param name="Celebration">podpis obchodu ("" = brak)</param>
    /// <param name="SetlistId">zestaw, który obsługuje ten dzień</param>
    /// <param name="Created">czy zestaw powstał w tym wywołaniu</param>
    public readonly record struct DayInfo(
        DateOnly Date, string Name, string Celebration, int SetlistId, bool Created);

    /// <param name="Days">zawsze <see cref="PilotPinWeek.Days"/> pozycji, od dnia startowego</param>
    /// <param name="ChangedIds">zestawy, które realnie zmieniły stan (nowe albo dopiero przypięte)</param>
    public readonly record struct Result(IReadOnlyList<DayInfo> Days, IReadOnlyList<int> ChangedIds)
    {
        /// <summary>Ile zestawów realnie przypięto/utworzono (0 = wszystko już wisiało).</summary>
        public int Pinned => ChangedIds.Count;
    }

    public static bool IsCommand(string? type) => type == Command;

    /// <summary>Diecezja z ustawień — jedno źródło dla okna i dla komend z Pilota.</summary>
    public static async Task<string> DioceseAsync(DatabaseService db)
        => await db.GetSettingAsync("diocese") ?? "";

    /// <summary>
    /// Przypina <see cref="Days"/> kolejnych dni począwszy od <paramref name="from"/>.
    /// Nie dotyka UI ani protokołu — wynik opisuje, co się stało.
    /// </summary>
    public static async Task<Result> RunAsync(DatabaseService db, DateOnly from, string diocese)
    {
        var days = new List<DayInfo>(Days);
        var changed = new List<int>();

        for (int i = 0; i < Days; i++)
        {
            var date = from.AddDays(i);
            var day = LiturgicalCalendarService.GetDay(date);
            // Kalendarz diecezji: uroczystość/święto/wspomnienie obowiązkowe (lub ręczny wybór
            // formularza) wypiera nazwę dnia — zob. DiocesanCalendarService.PinSetlistName.
            var pinName = DiocesanCalendarService.PinSetlistName(date, day, diocese);
            var name = pinName.Name;
            var group = await db.ResolveGroupNameAsync(day.Group);
            await db.EnsureGroupAsync(group);

            var existing = await db.GetSetlistForPinAsync(name, day.Group);
            // Zestaw obchodu jest przywiązany do STAŁEJ daty, więc szukamy go w całej bibliotece,
            // nie tylko w okresie — a użytkownik nazywa go po swojemu („Dominik” zamiast
            // „Św. Dominika, prezbitera”). Bez tego kroku powstałby duplikat.
            existing ??= await FindCelebrationSetlistAsync(db, pinName);
            int id;
            bool created = false;
            if (existing == null)
            {
                var fresh = new Setlist { Name = name, Group = group, SeasonKey = day.Group, IsPinned = true };
                await db.SaveSetlistAsync(fresh);
                id = fresh.Id;
                created = true;
                changed.Add(id);
            }
            else
            {
                id = existing.Id;
                if (!existing.IsPinned)
                {
                    await db.SetSetlistPinnedAsync(id, true);
                    changed.Add(id);
                }
            }

            days.Add(new DayInfo(date, name, PinnedCelebrations.CaptionFor(date, day, name, diocese), id, created));
        }

        return new Result(days, changed);
    }

    /// <summary>
    /// Istniejący zestaw obchodu nazwany po swojemu („Dominik” ↔ „Św. Dominika, prezbitera”) —
    /// dopasowanie po rdzeniach słów (<see cref="CelebrationStems"/>) w CAŁEJ bibliotece zestawów.
    ///
    /// <para>To krok DODATKOWY po <see cref="DatabaseService.GetSetlistForPinAsync"/>, nie zamiennik:
    /// identyfikacja po (nazwa, SeasonKey) zostaje pierwsza i rozstrzyga dni temporalne.</para>
    ///
    /// <para>Przy NIEJEDNOZNACZNOŚCI (dwa pasujące zestawy) świadomie zwracamy <c>null</c> —
    /// wołający założy nowy zestaw, a wpis w logu mówi organiście, co się stało. Podstawienie
    /// cudzego zestawu pod obchód wygląda poprawnie i jest niewykrywalne, więc duplikat, który
    /// widać na liście, jest mniejszym złem.</para>
    /// </summary>
    private static async Task<Setlist?> FindCelebrationSetlistAsync(
        DatabaseService db, DiocesanCalendarService.SetlistNameForDay pinName)
    {
        if (!pinName.FromCelebration) return null;

        var all = await db.GetAllSetlistsAsync();
        var found = CelebrationStems.Find(
            pinName.Name, all.Select(s => new CelebrationStems.Candidate(s.Id, s.Name)));

        if (found.Match is { } m)
        {
            if (!DatabaseService.NameEquals(m.Name, pinName.Name))
                AppLog.Write("PinWeek", $"obchód „{pinName.Name}” → istniejący zestaw „{m.Name}” (id={m.Id})");
            return all.FirstOrDefault(s => s.Id == m.Id);
        }

        if (found.Candidates.Count > 1)
            AppLog.Write("PinWeek",
                $"obchód „{pinName.Name}”: NIEJEDNOZNACZNE dopasowanie (" +
                string.Join(", ", found.Candidates.Select(c => $"„{c.Name}” id={c.Id}")) +
                ") — zakładam nowy zestaw");
        return null;
    }

    /// <summary>
    /// Ack komendy <c>pin_next_week</c> — JEDYNE miejsce jego składania.
    /// <c>days</c> ma zawsze <see cref="Days"/> elementów; <c>celebration</c> pomijamy, gdy puste.
    /// </summary>
    public static string BuildAckJson(Result result)
    {
        var days = result.Days.Select(d =>
        {
            var o = new Dictionary<string, object?>
            {
                ["date"] = d.Date.ToString("yyyy-MM-dd"),
                ["name"] = d.Name
            };
            if (d.Celebration.Length > 0) o["celebration"] = d.Celebration;
            return o;
        }).ToList();

        return PilotStatus.BuildAckJson(Command, true,
            ("pinned", result.Pinned), ("days", days));
    }

    /// <summary>
    /// Broadcast <c>pinned_celebrations</c> — JEDYNE miejsce jego składania. Zawiera wyłącznie
    /// zestawy z NIEPUSTYM podpisem; pusta lista też jest wysyłana (kasuje podpisy w Pilocie).
    /// </summary>
    public static string BuildCelebrationsJson(IEnumerable<KeyValuePair<int, string>> captions) =>
        JsonSerializer.Serialize(new
        {
            type  = CelebrationsMessage,
            items = captions.Select(kv => new { desktopId = kv.Key, celebration = kv.Value })
        });

    /// <summary>Podpisy dla AKTUALNIE przypiętych zestawów (baza + ustawienie diecezji).</summary>
    public static async Task<Dictionary<int, string>> CaptionsAsync(DatabaseService db, DateOnly from)
    {
        var pinned = await db.GetPinnedSetlistsAsync();
        var diocese = await DioceseAsync(db);
        return PinnedCelebrations.Build(
            pinned.Select(s => new PinnedCelebrations.PinnedRef(s.Id, s.Name)), from, Days, diocese);
    }

    /// <summary>Gotowy JSON <c>pinned_celebrations</c> dla bieżącego stanu bazy.</summary>
    public static async Task<string> BuildCelebrationsJsonAsync(DatabaseService db, DateOnly from)
        => BuildCelebrationsJson(await CaptionsAsync(db, from));
}
