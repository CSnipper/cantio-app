using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Filtr zwrotek wg wydania lekcjonarza. Psalm-pieśń niesie zwrotki OBU wydań
/// (każda oznaczona <see cref="Verse.Lekcjonarz"/> = <c>"N"</c>/<c>"S"</c>); przy projekcji
/// pokazujemy tylko zwrotki bieżącego wydania oraz zwrotki wspólne (<c>Lekcjonarz == null</c>,
/// czyli wszystkie zwykłe pieśni).
/// </summary>
public static class LectionaryFilter
{
    /// <summary>Domyślne wydanie (nowy lekcjonarz), gdy ustawienie nie jest zapisane.</summary>
    public const string Default = "N";

    /// <summary>Normalizuje wartość ustawienia do <c>"N"</c>/<c>"S"</c> (fallback: <see cref="Default"/>).</summary>
    public static string Normalize(string? edition)
    {
        var e = edition?.Trim().ToUpperInvariant();
        return e is "N" or "S" ? e : Default;
    }

    /// <summary>Czy zwrotka o danym znaczniku ma być widoczna dla wybranego wydania.</summary>
    public static bool IsVisible(string? verseLekcjonarz, string edition)
        => verseLekcjonarz is null || string.Equals(verseLekcjonarz, edition, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Zostawia zwrotki wspólne (null) oraz zgodne z <paramref name="edition"/>.
    /// Dla zwykłych pieśni (wszystkie null) zwraca listę bez zmian.
    /// </summary>
    public static List<Verse> Apply(IEnumerable<Verse> verses, string? edition)
    {
        var ed = Normalize(edition);
        return verses.Where(v => IsVisible(v.Lekcjonarz, ed)).ToList();
    }
}
