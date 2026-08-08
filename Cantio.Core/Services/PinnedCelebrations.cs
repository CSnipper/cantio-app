using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Podpisy obchodów pod nazwami zestawów na liście PRZYPIĘTE.
///
/// Po co: „Przypnij tydzień" nazywa zestaw dniem temporalnym (np. „18 Pon”), a nazwa zmienia się
/// tylko wtedy, gdy obchód REALNIE ją wypiera. Podpis zostaje dla JEDNEGO przypadku, w którym
/// obchód jest obowiązujący, a nazwy nie zabiera: UROCZYSTOŚCI w niedzielę uprzywilejowaną
/// (8 XII w niedzielę Adwentu, 3 V w niedzielę Wielkanocy) — aplikacja nie przenosi jej na
/// poniedziałek, więc przypomnienie ma sens.
///
/// <para>WSPOMNIENIA podpisu NIE dostają (decyzja użytkownika 2026-08-08): obowiązkowe mają od
/// tej pory WŁASNY zestaw pod nazwą obchodu (<see cref="DiocesanCalendarService.PinSetlistName"/>),
/// więc podpis byłby powtórzeniem; dowolne nie wypływają nigdzie, bo organista często ich nie
/// obchodzi i podpowiedź myliłaby.</para>
///
/// Podpis jest liczony PRZY WYŚWIETLANIU i nigdy nie trafia do <see cref="Setlist.Name"/>:
/// zestawy dni temporalnych są reużywane między latami (ten sam „18 Pon” wraca co roku z inną datą).
///
/// Klasa jest CZYSTA (bez bazy i bez WPF) — wejście to lista przypiętych zestawów, zakres dat
/// i diecezja, wyjście to mapa `id zestawu → podpis`.
/// </summary>
public static class PinnedCelebrations
{
    /// <summary>Przypięty zestaw widziany przez tę klasę — wyłącznie identyfikator i nazwa.</summary>
    public readonly record struct PinnedRef(int Id, string Name);

    /// <summary>
    /// Podpis dla konkretnej daty albo "" (brak).
    /// <para>Reguły: liczy się tylko NAJWYŻSZY obchód rangi uroczystość; jeśli to on wyparł nazwę
    /// dnia (<paramref name="effectiveName"/>), podpis jest zbędny — nazwa już nim jest.</para>
    /// </summary>
    public static string CaptionFor(DateOnly date, string effectiveName, string diocese)
        => CaptionFor(date, LiturgicalCalendarService.GetDay(date), effectiveName, diocese);

    /// <summary>Jak wyżej, gdy dzień liturgiczny jest już policzony (unika drugiego liczenia).</summary>
    public static string CaptionFor(DateOnly date, LiturgicalDay day, string effectiveName, string diocese)
    {
        var top = DiocesanCalendarService.ForDate(date, diocese)
            .FirstOrDefault(c => c.Ranga >= Ranga.Uroczystosc);
        if (top == null) return "";
        if (DatabaseService.NameEquals(top.Tytul, effectiveName)) return "";   // wyparł nazwę
        // Dzień z własną rangą (obchód ruchomy) pochłania obchody nie wyższe od siebie —
        // w Boże Ciało nie obchodzi się wspomnienia, więc podpowiadanie go byłoby fałszem.
        if (top.Ranga <= day.Rank) return "";
        return top.Tytul;
    }

    /// <summary>
    /// Mapa `id przypiętego zestawu → podpis` dla najbliższych <paramref name="days"/> dni.
    /// Zestaw jest kojarzony z datą po nazwie (porównanie pl-PL przez
    /// <see cref="DatabaseService.NameEquals"/> — dokładnie to, po czym „Przypnij tydzień"
    /// rozpoznaje istniejący zestaw). Wpisy bez podpisu w mapie NIE występują.
    /// </summary>
    public static Dictionary<int, string> Build(
        IEnumerable<PinnedRef> pinned, DateOnly from, int days, string diocese)
    {
        var result = new Dictionary<int, string>();
        var list = pinned as IList<PinnedRef> ?? pinned.ToList();
        if (list.Count == 0) return result;

        for (int i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            var day = LiturgicalCalendarService.GetDay(date);
            // Ta sama nazwa, pod którą „Przypnij tydzień" założył zestaw — inaczej podpis
            // przykleiłby się do zestawu, który tego dnia nie obsługuje.
            var name = DiocesanCalendarService.PinSetlistName(date, day, diocese).Name;
            var caption = CaptionFor(date, day, name, diocese);
            if (caption.Length == 0) continue;
            foreach (var p in list)
                if (!result.ContainsKey(p.Id) && DatabaseService.NameEquals(p.Name, name))
                    result[p.Id] = caption;
        }
        return result;
    }
}
