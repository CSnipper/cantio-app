namespace Cantio.Services;

/// <summary>
/// Podział treści slajdu na FRAZY (linijki logiczne) dla pulpitu organisty niewidomego.
///
/// Fraza = tekst od początku wiersza do Entera w treści zwrotki, a NIE linia po zawinięciu na
/// wyświetlaczu. To rozróżnienie jest całą funkcją: „Bądźże pozdrowiona Hostio żywa" na rzutniku
/// bywa łamane na dwie–trzy linie zależnie od czcionki i skali ekranu, ale dla organisty jest
/// JEDNĄ frazą, którą się śpiewa na jednym oddechu. Gdyby podział szedł za szerokością, ta sama
/// pieśń czytałaby się inaczej po zmianie kroju albo monitora — a niewidomy nie ma jak zauważyć,
/// że zmieniło się coś w wyglądzie.
///
/// Puste wiersze są POMIJANE (podwójny Enter między blokami, wiersz z samymi spacjami): dla
/// czytnika ekranu pusta fraza to naciśnięcie strzałki, po którym nic nie zabrzmiało — nieodróżnialne
/// od zawieszonego programu.
/// </summary>
public static class SlidePhrases
{
    /// <summary>
    /// Frazy slajdu w kolejności występowania. Pusty tekst (obrazek, slajd bez treści) → pusta
    /// lista; warstwa wyżej daje wtedy jedną pozycję z samym nagłówkiem, żeby każdy slajd był
    /// osiągalny strzałkami.
    /// </summary>
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var result = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result;
    }
}
