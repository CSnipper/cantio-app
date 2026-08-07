namespace Cantio.Services;

/// <summary>
/// Szablony komunikatów wyszukiwarki pulpitu niewidomego. Tak jak <see cref="AnnouncementText"/>:
/// rdzeń nie zna WPF ani <c>LocalizationManager</c>, więc teksty wstrzykuje warstwa okienkowa
/// (klucze <c>Acc.*</c>), a wartości domyślne są polskie — brak klucza w zasobach nie może
/// skończyć się przeczytaniem na głos „Acc.SearchFoundMany”.
/// </summary>
public sealed record SearchText
{
    public string Opened { get; init; } = "Wyszukiwanie pieśni. Wpisz numer albo tytuł i naciśnij Enter.";
    public string Closed { get; init; } = "Wyszukiwanie zamknięte";
    /// <summary>Liczba mnoga po polsku ma tu tylko dwie formy: „1 pieśń” i „N pieśni”.</summary>
    public string FoundOne { get; init; } = "Znaleziono 1 pieśń";
    /// <summary>{0} = liczba wyników.</summary>
    public string FoundMany { get; init; } = "Znaleziono {0} pieśni";
    public string NoResults { get; init; } = "Brak wyników";
    public string EmptyQuery { get; init; } = "Wpisz numer albo tytuł pieśni";
    /// <summary>{0} = tytuł, {1} = pozycja w zestawie (od 1).</summary>
    public string Added { get; init; } = "Dodano: {0}, pozycja {1}";
    /// <summary>{0} = tytuł, {1} = pozycja w zestawie (od 1).</summary>
    public string AddedAndShown { get; init; } = "Dodano i wyświetlono: {0}, pozycja {1}";
    public string NoSelection { get; init; } = "Nie wybrano pieśni";
    public string Untitled { get; init; } = "pieśń bez tytułu";
}

/// <summary>
/// Czyste budowanie komunikatów wyszukiwarki. Samo SZUKANIE jest i zostaje w
/// <see cref="SongSearch"/> — pulpit niewidomego nie ma własnego wyszukiwania, bo dwie różne
/// odpowiedzi na to samo zapytanie w jednym programie byłyby pułapką nie do wyśledzenia
/// (widzący pomocnik widziałby co innego niż słyszy niewidomy operator).
///
/// Tu żyje wyłącznie to, czego <see cref="SongSearch"/> nie umie i umieć nie powinien: jak
/// wynik i skutek dodania BRZMI w uchu czytnika ekranu.
/// </summary>
public static class AccessibleSearch
{
    /// <summary>
    /// Jedna pozycja wyników: „123, Kiedy ranne wstają zorze”. Numer idzie PIERWSZY, bo to on
    /// jest kluczem wyszukiwania (organista szuka po numerze ze śpiewnika, a przy przewijaniu
    /// listy słyszy go od razu, bez czekania na koniec tytułu).
    /// </summary>
    public static string DescribeResult(int number, string? title, SearchText t)
    {
        var name = (title ?? string.Empty).Trim();
        if (name.Length == 0) name = t.Untitled;
        // Number == 0 to konwencja bazy „pieśń bez numeru” — nie ma czego mówić.
        return number > 0 ? $"{number}, {name}" : name;
    }

    /// <summary>Ile znaleziono. Zero ma własny komunikat — „Znaleziono 0 pieśni” brzmi jak usterka.</summary>
    public static string DescribeCount(int count, SearchText t)
        => count <= 0 ? t.NoResults
         : count == 1 ? t.FoundOne
         : string.Format(t.FoundMany, count);

    /// <summary>
    /// Potwierdzenie dodania. POZYCJA JEST OBOWIĄZKOWA: bez niej niewidomy operator nie ma jak
    /// sprawdzić, czy pieśń wylądowała tam, gdzie chciał — a to jedyny jego dowód, że zestaw
    /// rośnie zgodnie z planem mszy.
    /// </summary>
    public static string DescribeAdded(string? title, int position, bool shown, SearchText t)
    {
        var name = (title ?? string.Empty).Trim();
        if (name.Length == 0) name = t.Untitled;
        return string.Format(shown ? t.AddedAndShown : t.Added, name, position);
    }
}
