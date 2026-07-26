namespace Cantio.Services;

/// <summary>
/// Czysta logika utrzymania pozycji slajdu przy przebudowie listy — bez zależności od WPF, żeby dała się testować.
/// Po edycji treści liczba i podział slajdów mogą się zmienić, więc goły indeks przestaje wskazywać tę samą
/// zwrotkę. Kotwiczymy się na parze (zwrotka, część), a stary indeks jest dopiero ostatnią deską ratunku.
/// </summary>
public static class SlideAnchor
{
    /// <param name="verseIndex">Zwrotka slajdu sprzed przebudowy; -1 = nic nie było wyświetlane.</param>
    /// <param name="partIndex">Część zwrotki (podział długiej zwrotki na slajdy) sprzed przebudowy.</param>
    /// <param name="previousIndex">Numeryczny indeks slajdu sprzed przebudowy; -1 = nic nie było wyświetlane.</param>
    /// <param name="slides">Nowa lista slajdów jako pary (zwrotka, część), w kolejności wyświetlania.</param>
    /// <returns>Indeks w nowej liście albo -1, gdy nie ma czego pokazać.</returns>
    public static int Resolve(
        int verseIndex, int partIndex, int previousIndex,
        IReadOnlyList<(int VerseIndex, int PartIndex)> slides)
    {
        if (slides.Count == 0 || previousIndex < 0) return -1;

        if (verseIndex >= 0)
        {
            // 1. Dokładnie ta sama część tej samej zwrotki — najczęstszy przypadek (poprawiona literówka).
            for (int i = 0; i < slides.Count; i++)
                if (slides[i].VerseIndex == verseIndex && slides[i].PartIndex == partIndex) return i;

            // 2. Zwrotka się skróciła (mniej części niż było) — ostatnia istniejąca część tej zwrotki.
            int lastPartOfVerse = -1;
            for (int i = 0; i < slides.Count; i++)
                if (slides[i].VerseIndex == verseIndex && slides[i].PartIndex < partIndex) lastPartOfVerse = i;
            if (lastPartOfVerse >= 0) return lastPartOfVerse;

            // 3. Zwrotka istnieje, ale numeracja części się rozjechała — jej pierwszy slajd.
            for (int i = 0; i < slides.Count; i++)
                if (slides[i].VerseIndex == verseIndex) return i;
        }

        // 4. Zwrotka zniknęła — zostań jak najbliżej starej pozycji, bez wychodzenia poza zakres
        //    (nigdy nie wracamy bez potrzeby na początek pieśni).
        return Math.Min(previousIndex, slides.Count - 1);
    }
}
