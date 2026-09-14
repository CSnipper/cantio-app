namespace Cantio.Services;

/// <summary>
/// Zakres wyrównywania rozmiaru czcionki po auto-dopasowaniu (ustawienie <c>font_fit_scope</c>).
/// Działa TYLKO przy włączonym auto-dopasowaniu (<c>font_auto_fit</c>); przy stałej wielkości
/// nie ma czego wyrównywać, bo każdy slajd dostaje rozmiar prosto z ustawień.
/// </summary>
public enum FontFitScope
{
    /// <summary>Jeden rozmiar dla CAŁEJ pieśni — minimum ze wszystkich slajdów (domyślne, zachowanie od v1.0).</summary>
    Song,
    /// <summary>Rozmiar wspólny w obrębie ZWROTKI — zwrotka podzielona na trzy slajdy ma na nich tę samą czcionkę.</summary>
    Verse
}

/// <summary>
/// Wyrównywanie rozmiaru czcionki policzonego per slajd (<c>SlideLayoutService.ComputeFitFontSize</c>).
///
/// Po co: per-slajd auto-fit daje drastyczne różnice (krótki tekst = gigantyczna czcionka, długi =
/// drobna). Wspólny rozmiar to zawsze MINIMUM z grupy — tekst wtedy na pewno się mieści.
/// Różnicą jest tylko to, co uznajemy za grupę: cała pieśń (spójnie, ale krótkie zwrotki drobne)
/// albo pojedyncza zwrotka (najlepsze dopasowanie, różnice między zwrotkami).
///
/// Czysta arytmetyka — bez WPF i bez bazy, żeby dało się ją dowieść testem na literałach.
/// </summary>
public static class SlideFontFit
{
    public const string SettingKey = "font_fit_scope";
    public const string ValueSong  = "song";
    public const string ValueVerse = "verse";

    /// <summary>Wartość z tabeli <c>settings</c> → tryb. Cokolwiek innego niż „verse" = „song" (domyślne).</summary>
    public static FontFitScope Parse(string? value) =>
        string.Equals(value?.Trim(), ValueVerse, StringComparison.OrdinalIgnoreCase)
            ? FontFitScope.Verse
            : FontFitScope.Song;

    /// <summary>Tryb → wartość do tabeli <c>settings</c> i na łącze WS.</summary>
    public static string ToSetting(FontFitScope scope) =>
        scope == FontFitScope.Verse ? ValueVerse : ValueSong;

    /// <summary>
    /// Wyrównuje rozmiar czcionki w przekazanej grupie slajdów. Skład grupy ustala wołający
    /// (osobno slajdy zwykłe, osobno prywatne, w psalmie bez refrenów, wszędzie bez obrazków) —
    /// ta metoda go NIE filtruje.
    /// </summary>
    public static void Unify(IReadOnlyList<Slide> group, FontFitScope scope)
    {
        if (group == null || group.Count < 2) return;

        if (scope == FontFitScope.Song)
        {
            Apply(group);
            return;
        }

        // Verse: każda zwrotka osobno. Slajdy jednej zwrotki leżą obok siebie, ale nie zakładamy
        // tego — grupujemy po VerseIndex, żeby reguła działała też dla list poskładanych z filtrów.
        foreach (var verse in group.GroupBy(s => s.VerseIndex))
            Apply(verse.ToList());
    }

    private static void Apply(IReadOnlyList<Slide> slides)
    {
        if (slides.Count < 2) return;
        double unified = slides.Min(s => s.FontSize);
        foreach (var slide in slides)
            slide.FontSize = unified;
    }
}
