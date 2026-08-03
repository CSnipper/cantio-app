namespace Cantio.Services;

/// <summary>
/// Czysta logika pętli slajdów („tryb przed mszą”) — bez zależności od WPF, żeby dała się testować.
/// Pętla krąży TYLKO w obrębie aktualnie wyświetlanego elementu (pieśń / tekst jednorazowy),
/// nigdy nie przechodzi do następnej pozycji zestawu.
/// </summary>
public static class SlideLoop
{
    public const int MinIntervalSeconds = 3;
    public const int MaxIntervalSeconds = 120;
    public const int DefaultIntervalSeconds = 10;

    /// <summary>
    /// Następny indeks slajdu z zawijaniem po ostatnim na pierwszy.
    /// Zwraca -1, gdy nie ma czego pokazywać (brak slajdów).
    /// Indeks spoza zakresu (np. -1 tuż po przebudowie) → start od pierwszego slajdu.
    /// </summary>
    public static int NextIndex(int current, int slideCount)
    {
        if (slideCount <= 0) return -1;
        if (current < 0 || current >= slideCount) return 0;
        return (current + 1) % slideCount;
    }

    /// <summary>
    /// Czy pętla jest w ogóle dostępna (przycisk aktywny).
    /// Wymaga co najmniej dwóch slajdów i wyklucza tryb psalm — tam projektor pokazuje refren,
    /// a zwrotki idą wyłącznie do podglądu operatora, więc automatyczne przewijanie nic nie zmieni
    /// na ekranie, a operatorowi ucieknie podgląd.
    /// </summary>
    public static bool CanLoop(bool isPsalmMode, int slideCount) => !isPsalmMode && slideCount > 1;

    /// <summary>Czy pętlę można teraz uruchomić — na wygaszonym ekranie nie ma czego zapętlać.</summary>
    public static bool CanStart(bool isPsalmMode, int slideCount, bool screenBlanked) =>
        CanLoop(isPsalmMode, slideCount) && !screenBlanked;

    /// <summary>Interwał docięty do zakresu akceptowanego przez UI.</summary>
    public static int ClampInterval(int seconds) =>
        seconds < MinIntervalSeconds ? MinIntervalSeconds
        : seconds > MaxIntervalSeconds ? MaxIntervalSeconds
        : seconds;

    /// <summary>Odczyt interwału z ustawień (klucz `loop_interval`); śmieci → wartość domyślna.</summary>
    public static int ParseInterval(string? raw) =>
        int.TryParse(raw, out var seconds) ? ClampInterval(seconds) : DefaultIntervalSeconds;
}
