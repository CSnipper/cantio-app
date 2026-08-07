namespace Cantio.Services;

/// <summary>
/// Szablony komunikatów ZARZĄDZANIA ZESTAWEM na pulpicie niewidomego. Jak
/// <see cref="AnnouncementText"/> i <see cref="SearchText"/>: rdzeń nie zna WPF ani
/// <c>LocalizationManager</c>, więc teksty wstrzykuje warstwa okienkowa (klucze <c>Acc.*</c>),
/// a wartości domyślne są polskie — brak klucza w zasobach nie może skończyć się
/// przeczytaniem na głos „Acc.SetlistRemoved”.
/// </summary>
public sealed record SetlistText
{
    /// <summary>{0} = liczba pieśni. Nagłówek odczytu całego zestawu.</summary>
    public string ListHeaderMany { get; init; } = "Zestaw, {0} pieśni:";
    public string ListHeaderOne { get; init; } = "Zestaw, 1 pieśń:";
    public string Empty { get; init; } = "Zestaw jest pusty";
    /// <summary>Dopisek przy pozycji, która jest teraz bieżąca (jedyny sygnał orientacyjny w odczycie).</summary>
    public string CurrentMark { get; init; } = "bieżąca";

    /// <summary>{0} = tytuł. PIERWSZE naciśnięcie Delete — pyta, nie usuwa.</summary>
    public string ConfirmRemove { get; init; } =
        "Usunąć: {0}? Naciśnij Delete ponownie, aby potwierdzić, Escape aby anulować";
    /// <summary>{0} = tytuł, {1} = ile pieśni ZOSTAŁO.</summary>
    public string Removed { get; init; } = "Usunięto: {0}. W zestawie pozostało {1} pieśni";
    /// <summary>{0} = tytuł. Forma dla jednej pozostałej pieśni.</summary>
    public string RemovedOne { get; init; } = "Usunięto: {0}. W zestawie pozostała 1 pieśń";
    /// <summary>{0} = tytuł. Ostatnia pieśń zestawu — mówimy wprost, że ekran gaśnie.</summary>
    public string RemovedLast { get; init; } = "Usunięto: {0}. Zestaw jest pusty, ekran wygaszony";
    public string RemoveCancelled { get; init; } = "Anulowano usuwanie";
    public string NoCurrent { get; init; } = "Nie ma bieżącej pieśni";

    /// <summary>{0} = tytuł, {1} = nowa pozycja (od 1), {2} = ile pieśni w zestawie.</summary>
    public string Moved { get; init; } = "Przeniesiono: {0}, pozycja {1} z {2}";
    public string AtFirst { get; init; } = "Pierwsza pozycja";
    public string AtLast { get; init; } = "Ostatnia pozycja";

    public string SaveOpened { get; init; } = "Zapisz zestaw. Wpisz nazwę i naciśnij Enter.";
    public string SaveClosed { get; init; } = "Zapisywanie anulowane";
    /// <summary>{0} = nazwa. Nadpisanie wczytanego zestawu.</summary>
    public string Saved { get; init; } = "Zapisano zestaw: {0}";
    /// <summary>{0} = nazwa. Nowy rekord w bazie — inny komunikat, bo to inny skutek.</summary>
    public string SavedNew { get; init; } = "Zapisano nowy zestaw: {0}";
    public string SaveEmptyName { get; init; } = "Wpisz nazwę zestawu";
    public string SaveNothing { get; init; } = "Zestaw jest pusty, nie ma czego zapisać";

    public string OpenOpened { get; init; } =
        "Otwórz zestaw. Strzałkami wybierz zestaw i naciśnij Enter.";
    public string OpenClosed { get; init; } = "Otwieranie anulowane";
    /// <summary>{0} = nazwa zestawu, {1} = liczba pieśni.</summary>
    public string Entry { get; init; } = "{0}, pieśni: {1}";
    /// <summary>{0} = nazwa zestawu. Zestaw bez pieśni na liście do otwarcia.</summary>
    public string EntryEmpty { get; init; } = "{0}, pusty";
    /// <summary>{0} = nazwa, {1} = liczba pieśni.</summary>
    public string Loaded { get; init; } = "Wczytano zestaw: {0}, pieśni: {1}";
    public string NoSetlists { get; init; } = "Nie ma zapisanych zestawów";
    public string NoSelection { get; init; } = "Nie wybrano zestawu";
    public string Untitled { get; init; } = "pieśń bez tytułu";
    public string UnnamedSetlist { get; init; } = "zestaw bez nazwy";
}

/// <summary>Co zrobić z zapisem zestawu pod nazwą wpisaną przez operatora.</summary>
public enum SaveDecision
{
    /// <summary>Pusta nazwa — nic nie zapisujemy, prosimy o nazwę.</summary>
    Refuse,
    /// <summary>Nadpisz zestaw wczytany z bazy (ta sama nazwa, ten sam rekord).</summary>
    Overwrite,
    /// <summary>Załóż nowy rekord.</summary>
    CreateNew,
}

/// <summary>
/// Czyste reguły i komunikaty zarządzania zestawem z klawiatury (pulpit niewidomego).
///
/// Powód wydzielenia do rdzenia: NIC z tego nie da się sprawdzić okiem. Widzący operator
/// widzi listę zestawu i skutek każdej akcji od razu; niewidomy zna wyłącznie to, co zostanie
/// powiedziane, więc treść komunikatu i reguła „co jest bieżące po usunięciu” SĄ funkcją,
/// a nie ozdobą — i muszą dać się przetestować bez WPF.
/// </summary>
public static class AccessibleSetlist
{
    /// <summary>
    /// Odczyt całego zestawu: „Zestaw, 3 pieśni: 1. Barka. 2. Kiedy ranne wstają zorze, bieżąca. 3. …”.
    /// Numery są OBOWIĄZKOWE — to jedyny układ współrzędnych, w którym niewidomy operator może
    /// powiedzieć „przenieś tę na trzecią pozycję”. Bieżąca pieśń jest oznaczona, bo bez tego
    /// odczyt mówi, CO jest w zestawie, ale nie mówi, GDZIE operator stoi.
    /// </summary>
    public static string DescribeSetlist(IReadOnlyList<string?> titles, int currentIndex, SetlistText t)
    {
        if (titles is null || titles.Count == 0) return t.Empty;

        var header = titles.Count == 1 ? t.ListHeaderOne : string.Format(t.ListHeaderMany, titles.Count);
        var sb = new System.Text.StringBuilder(header);
        for (int i = 0; i < titles.Count; i++)
        {
            var name = Name(titles[i], t);
            sb.Append(' ').Append(i + 1).Append(". ").Append(name);
            if (i == currentIndex) sb.Append(", ").Append(t.CurrentMark);
            sb.Append('.');
        }
        return sb.ToString();
    }

    /// <summary>Pytanie pierwszego Delete. Tytuł jest w NIM, żeby operator wiedział, co potwierdza.</summary>
    public static string DescribeConfirmRemove(string? title, SetlistText t)
        => string.Format(t.ConfirmRemove, Name(title, t));

    /// <summary>
    /// Potwierdzenie usunięcia. Liczba pozostałych pieśni jest obowiązkowa — to jedyny dowód,
    /// że zniknęła JEDNA pozycja, a nie dwie (podwójne naciśnięcie Delete zdarza się każdemu).
    /// </summary>
    public static string DescribeRemoved(string? title, int remaining, SetlistText t)
        => remaining <= 0 ? string.Format(t.RemovedLast, Name(title, t))
         : remaining == 1 ? string.Format(t.RemovedOne, Name(title, t))
         : string.Format(t.Removed, Name(title, t), remaining);

    /// <summary>{0} = tytuł, {1} = pozycja od 1, {2} = ile pozycji w zestawie.</summary>
    public static string DescribeMoved(string? title, int newIndex, int total, SetlistText t)
        => string.Format(t.Moved, Name(title, t), newIndex + 1, total);

    /// <summary>Jedna pozycja listy zapisanych zestawów („Niedziela 3 zwykła, pieśni: 5”).</summary>
    public static string DescribeEntry(string? name, int songCount, SetlistText t)
    {
        var label = string.IsNullOrWhiteSpace(name) ? t.UnnamedSetlist : name.Trim();
        return songCount <= 0 ? string.Format(t.EntryEmpty, label)
                              : string.Format(t.Entry, label, songCount);
    }

    /// <summary>Potwierdzenie wczytania zestawu.</summary>
    public static string DescribeLoaded(string? name, int songCount, SetlistText t)
        => string.Format(t.Loaded, string.IsNullOrWhiteSpace(name) ? t.UnnamedSetlist : name.Trim(), songCount);

    /// <summary>Potwierdzenie zapisu; <paramref name="asNew"/> rozróżnia nadpisanie od nowego rekordu.</summary>
    public static string DescribeSaved(string name, bool asNew, SetlistText t)
        => string.Format(asNew ? t.SavedNew : t.Saved, name.Trim());

    /// <summary>
    /// Nadpisać wczytany zestaw czy założyć nowy? Rozstrzyga NAZWA: ta sama co wczytana
    /// (porównanie kulturą pl-PL, bo SQLite `lower()` nie zna polskich znaków) = nadpisanie,
    /// każda inna = nowy rekord. Dzięki temu „Ctrl+S, Enter” na wczytanym zestawie zapisuje
    /// w miejscu, a „Ctrl+S, nowa nazwa, Enter” robi kopię — bez okna dialogowego z wyborem,
    /// którego niewidomy i tak by nie zobaczył.
    /// </summary>
    public static SaveDecision DecideSave(int loadedId, string? loadedName, string? typedName)
    {
        var name = (typedName ?? string.Empty).Trim();
        if (name.Length == 0) return SaveDecision.Refuse;
        if (loadedId <= 0) return SaveDecision.CreateNew;
        return NameEquals(name, loadedName) ? SaveDecision.Overwrite : SaveDecision.CreateNew;
    }

    /// <summary>
    /// KTÓRA pozycja jest bieżąca po usunięciu. Reguła:
    /// • usunięto pozycję PRZED bieżącą → bieżąca ta sama pieśń, indeks o 1 mniejszy;
    /// • usunięto pozycję PO bieżącej → bez zmian (obraz dla wiernych nietknięty);
    /// • usunięto BIEŻĄCĄ → wchodzi pieśń, która zajęła jej miejsce (następna), a gdy usunięto
    ///   ostatnią — nowa ostatnia. Pulpit nigdy nie wskazuje pieśni, której już nie ma w zestawie;
    /// • zestaw opustoszał → -1 (brak bieżącej).
    /// </summary>
    /// <param name="countAfter">Liczba pozycji PO usunięciu.</param>
    public static int CurrentAfterRemoval(int removedIndex, int currentIndex, int countAfter)
    {
        if (countAfter <= 0) return -1;
        if (currentIndex < 0) return -1;
        if (currentIndex < removedIndex) return currentIndex;
        if (currentIndex > removedIndex) return currentIndex - 1;
        return Math.Min(removedIndex, countAfter - 1);
    }

    /// <summary>Porównanie nazw zestawów kulturą pl-PL (Ś/Ż/Ó), bo tak porównuje reszta programu.</summary>
    private static bool NameEquals(string a, string? b)
        => System.Globalization.CultureInfo.GetCultureInfo("pl-PL").CompareInfo
            .Compare((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
                     System.Globalization.CompareOptions.IgnoreCase) == 0;

    private static string Name(string? title, SetlistText t)
    {
        var name = (title ?? string.Empty).Trim();
        return name.Length == 0 ? t.Untitled : name;
    }
}

/// <summary>
/// Reguła DWUKROTNEGO potwierdzenia usunięcia. Niewidomy operator nie zobaczy okna dialogowego,
/// więc pytanie zadaje live region, a odpowiedzią jest drugie naciśnięcie Delete — ale takie
/// „uzbrojenie” MUSI wygasać. Bez wygasania Delete naciśnięty przez pomyłkę pół mszy później
/// skasowałby zupełnie inną pieśń, a operator nie miałby jak tego zauważyć.
///
/// Wygasanie ma dwa niezależne bezpieczniki i oba są potrzebne:
/// • CZAS (<see cref="Timeout"/>) — chroni przed „zapomnianym” pytaniem, na które nikt nie odpowiedział;
/// • KAŻDY INNY KLAWISZ (<see cref="Cancels"/>) — chroni przed sytuacją, w której operator w międzyczasie
///   zmienił pieśń albo cokolwiek zrobił, więc pytanie dotyczy już czegoś innego niż myśli.
/// </summary>
public static class DeleteConfirmation
{
    /// <summary>Ile żyje uzbrojone potwierdzenie. 5 s = tyle, ile trwa wysłuchanie pytania i reakcja.</summary>
    // 12 s, nie 5: NVDA czyta samo pytanie ("Usunąć: <tytuł>? Naciśnij Delete ponownie…")
    // około 4 sekund, a niewidomy operator musi je USŁYSZEĆ DO KOŃCA, zanim zdecyduje.
    // Przy 5 s okno na potwierdzenie było praktycznie nie do trafienia. Drugim bezpiecznikiem
    // jest i tak dowolny inny klawisz (NotifyKeyHandled), więc dłuższy czas niczym nie grozi.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Czy naciśnięcie Delete na pozycji <paramref name="index"/> jest POTWIERDZENIEM wcześniejszego
    /// pytania. Musi się zgadzać pozycja (inna pieśń = inne pytanie) i mieścić czas.
    /// </summary>
    public static bool Confirms(int? armedIndex, DateTime? armedAt, int index, DateTime now)
        => armedIndex.HasValue && armedAt.HasValue
           && armedIndex.Value == index
           && index >= 0
           && now - armedAt.Value <= Timeout;

    /// <summary>
    /// Czy to naciśnięcie klawisza gasi uzbrojone potwierdzenie. Gasi WSZYSTKO poza samym
    /// Delete (drugi Delete to odpowiedź) i poza gołymi modyfikatorami (Ctrl/Shift/Alt same
    /// z siebie nie są akcją operatora — puszczenie ich zdarza się między dwoma naciśnięciami).
    /// </summary>
    public static bool Cancels(DeskKey key, DeskAction action)
        => key != DeskKey.Modifier && action != DeskAction.RemoveSetlistSong;
}
