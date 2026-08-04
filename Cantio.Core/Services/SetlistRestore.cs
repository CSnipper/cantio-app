namespace Cantio.Services;

/// <summary>
/// Reguła aktywnej pozycji przy odtwarzaniu zestawu z Pilota („Pokaż w Cantio", <c>setlist_restore</c>).
/// </summary>
public static class SetlistRestore
{
    /// <summary>
    /// Indeks pozycji, która ma zostać aktywna po odbudowaniu zestawu.
    /// <paramref name="requested"/> == -1 oznacza starszego Pilota, który nie wysyła
    /// <c>activeIndex</c> — wtedy PIERWSZA pozycja (nigdy ostatnia, to był zgłoszony błąd).
    /// Zwraca -1 dla pustego zestawu.
    /// </summary>
    public static int ResolveActiveIndex(int requested, int count)
    {
        if (count <= 0) return -1;
        if (requested < 0) return 0;
        return requested >= count ? count - 1 : requested;
    }
}
