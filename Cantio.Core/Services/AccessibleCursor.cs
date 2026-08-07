namespace Cantio.Services;

/// <summary>
/// Kursor CZYTANIA pulpitu niewidomego operatora — druga, PRYWATNA pozycja na liście slajdów,
/// całkowicie niezależna od <c>DisplayViewModel.CurrentSlideIndex</c> (kursora PROJEKCJI).
///
/// Sedno funkcji: strzałki góra/dół przesuwają ten kursor, żeby czytnik ekranu przeczytał treść
/// slajdu — a obraz widziany przez wiernych ma się przy tym NIE ZMIENIĆ. Dlatego ta klasa nie zna
/// ani projekcji, ani WPF: nie ma jak przypadkiem czegokolwiek wypchnąć na rzutnik.
///
/// Konwencja indeksu: -1 = brak (pusta lista). Ruch poza zakres jest bezpiecznym no-op
/// (zwraca false), a nie zawinięciem — niewidomy operator musi po dźwięku poznać, że jest
/// na krańcu, zamiast cicho wylądować na drugim końcu pieśni.
/// </summary>
public sealed class AccessibleCursor
{
    /// <summary>Liczba pozycji, po których chodzi kursor.</summary>
    public int Count { get; private set; }

    /// <summary>Bieżąca pozycja kursora czytania; -1 gdy nie ma czego czytać.</summary>
    public int Index { get; private set; } = -1;

    public bool HasPosition => Index >= 0 && Index < Count;

    /// <summary>
    /// Nowa lista slajdów (zmiana pieśni / przebudowa slajdów). Kursor jest ustawiany na
    /// <paramref name="index"/> przyciętym do zakresu; pusta lista → -1.
    /// </summary>
    public void Reset(int count, int index = 0)
    {
        Count = count < 0 ? 0 : count;
        Index = Count == 0 ? -1 : Clamp(index);
    }

    /// <summary>Skok na konkretną pozycję (np. klik myszą albo synchronizacja z listą w oknie).</summary>
    public bool MoveTo(int index)
    {
        if (Count == 0) { Index = -1; return false; }
        var target = Clamp(index);
        if (target == Index) return false;
        Index = target;
        return true;
    }

    public bool MoveUp()
    {
        if (Count == 0 || Index <= 0) return false;
        Index--;
        return true;
    }

    public bool MoveDown()
    {
        if (Count == 0 || Index >= Count - 1) return false;
        Index++;
        return true;
    }

    public bool MoveToStart() => MoveTo(0);

    public bool MoveToEnd() => MoveTo(Count - 1);

    private int Clamp(int index) => index < 0 ? 0 : index > Count - 1 ? Count - 1 : index;
}
