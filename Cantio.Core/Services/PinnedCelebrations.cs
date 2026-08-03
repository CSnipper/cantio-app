using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Podpisy obchodów pod nazwami zestawów na liście PRZYPIĘTE („wsp. św. Dominika, prezbitera").
///
/// Po co: „Przypnij tydzień" nazywa zestaw dniem temporalnym (np. „18 Pon”), a nazwa zmienia się
/// tylko wtedy, gdy obchód REALNIE ją wypiera (uroczystość/święto). Wspomnienie obowiązkowe nigdzie
/// nie wypływało — organista dowiadywał się o nim dopiero przy ołtarzu. Podpis jest liczony
/// PRZY WYŚWIETLANIU i nigdy nie trafia do <see cref="Setlist.Name"/>: zestawy są reużywane między
/// latami (ten sam „18 Pon” wraca co roku z innym wspomnieniem), więc doklejenie obchodu do nazwy
/// zerwałoby dopasowanie przy kolejnym przypinaniu.
///
/// Klasa jest CZYSTA (bez bazy i bez WPF) — wejście to lista przypiętych zestawów, zakres dat
/// i diecezja, wyjście to mapa `id zestawu → podpis`.
/// </summary>
public static class PinnedCelebrations
{
    /// <summary>Przypięty zestaw widziany przez tę klasę — wyłącznie identyfikator i nazwa.</summary>
    public readonly record struct PinnedRef(int Id, string Name);

    /// <summary>Prefiks rangi w podpisie; święto/uroczystość mówią same za siebie.</summary>
    private static string Prefix(Ranga r) => r == Ranga.WspObowiazkowe ? "wsp. " : "";

    /// <summary>
    /// Podpis dla konkretnej daty albo "" (brak).
    /// <para>Reguły: liczy się tylko NAJWYŻSZY obchód rangi ≥ wspomnienie obowiązkowe; jeśli to on
    /// wyparł nazwę dnia (<paramref name="effectiveName"/>), podpis jest zbędny — nazwa już nim jest.
    /// W niedzielę pomijamy wspomnienia i święta: liturgicznie się ich nie obchodzi, więc podpis
    /// byłby fałszywą podpowiedzią (uroczystość zostaje — na niedzielach uprzywilejowanych aplikacja
    /// nie przenosi jej na poniedziałek, więc przypomnienie ma sens).</para>
    /// </summary>
    public static string CaptionFor(DateOnly date, string effectiveName, string diocese)
    {
        var top = DiocesanCalendarService.ForDate(date, diocese)
            .FirstOrDefault(c => c.Ranga >= Ranga.WspObowiazkowe);
        if (top == null) return "";
        if (DatabaseService.NameEquals(top.Tytul, effectiveName)) return "";   // wyparł nazwę
        if (date.DayOfWeek == DayOfWeek.Sunday && top.Ranga < Ranga.Uroczystosc) return "";
        return Prefix(top.Ranga) + top.Tytul;
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
            var name = DiocesanCalendarService.EffectiveSetlistName(date, day, diocese);
            var caption = CaptionFor(date, name, diocese);
            if (caption.Length == 0) continue;
            foreach (var p in list)
                if (!result.ContainsKey(p.Id) && DatabaseService.NameEquals(p.Name, name))
                    result[p.Id] = caption;
        }
        return result;
    }
}
