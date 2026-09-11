using System.Globalization;
using System.Text;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Nawigacja literami po liście zestawu: wciśnięcie A–Z zaznacza NASTĘPNĄ pozycję, której tytuł
/// zaczyna się na tę literę (cyklicznie, z zawinięciem na początek listy).
///
/// Dopasowanie jest „bez ogonków": litera bazowa łapie też diakrytyk, więc S trafia w „Święty…",
/// L w „Łaska…", Z w „Życzymy…". Organista pamięta tytuł, nie zapis — a na klawiaturze i tak nie
/// da się wpisać Ś bez AltGr (który w WPF jest Ctrl+Alt i świadomie NIE uruchamia nawigacji).
///
/// Klasa jest czysta (bez WPF) — okno tylko podaje tytuły i indeks zaznaczenia.
/// </summary>
public static class SetlistLetterJump
{
    /// <summary>
    /// Indeks następnej pozycji pasującej do litery albo <c>null</c>, gdy nic nie pasuje.
    ///
    /// Szukanie zaczyna się ZA <paramref name="currentIndex"/> i zawija na początek, a bieżąca
    /// pozycja sprawdzana jest jako OSTATNIA — dzięki temu kolejne naciśnięcia tej samej litery
    /// obchodzą wszystkie trafienia po kolei, a przy jedynym trafieniu zaznaczenie po prostu
    /// zostaje na miejscu. <paramref name="currentIndex"/> spoza zakresu (np. -1 = brak
    /// zaznaczenia) znaczy „szukaj od początku listy".
    /// </summary>
    public static int? FindNext(IReadOnlyList<string?> titles, int currentIndex, char letter)
    {
        if (titles is null || titles.Count == 0) return null;

        var needle = NormalizeLetter(letter);
        if (needle is null) return null;

        int n = titles.Count;
        int start = currentIndex >= 0 && currentIndex < n ? currentIndex : -1;

        for (int step = 1; step <= n; step++)
        {
            int idx = (int)(((long)start + step) % n);
            if (StartsWithLetter(titles[idx], needle.Value)) return idx;
        }
        return null;
    }

    /// <summary>Czy tytuł zaczyna się na daną (już znormalizowaną) literę bazową.</summary>
    public static bool StartsWithLetter(string? title, char normalizedLetter)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        var first = NormalizeLetter(title.TrimStart()[0]);
        return first is not null && first.Value == normalizedLetter;
    }

    /// <summary>
    /// Znak → wielka litera bazowa A–Z (bez diakrytyków) albo <c>null</c>, gdy to nie litera
    /// łacińska. Rozkład Unicode FormD zdejmuje ogonki i kreski, ale Ł/ł się NIE rozkłada
    /// (to osobny znak, nie L + znak łączący) — stąd jawny przypadek.
    /// </summary>
    public static char? NormalizeLetter(char c)
    {
        if (c is 'Ł' or 'ł') return 'L';

        foreach (var ch in c.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var upper = char.ToUpperInvariant(ch);
            return upper is >= 'A' and <= 'Z' ? upper : null;
        }
        return null;
    }

    /// <summary>
    /// Tytuł pozycji zestawu widziany przez operatora — dokładnie to, co pokazuje wiersz listy
    /// (MainWindow.xaml): pieśń → <see cref="Song.Title"/>, tekst jednorazowy →
    /// <see cref="SetlistItem.CustomTitle"/>, obrazek → nazwa pliku.
    /// </summary>
    public static string? TitleOf(SetlistItem? item)
    {
        if (item is null) return null;
        if (item.IsImageItem) return Path.GetFileName(item.ImagePath);
        if (item.IsTextItem) return item.CustomTitle;
        return item.Song?.Title;
    }

    /// <summary>Tytuły całej listy w kolejności — wejście dla <see cref="FindNext"/>.</summary>
    public static IReadOnlyList<string?> TitlesOf(IEnumerable<SetlistItem> items)
        => items.Select(TitleOf).ToList();
}
