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
    /// <summary>
    /// Dopisek przy pozycji, którą widzą teraz wierni. Od etapu 3 brzmi „na ekranie”, a nie
    /// „bieżąca”: odkąd kursory są dwa, „bieżąca” nie odróżniałaby pieśni na rzutniku od tej,
    /// na której stoi operator.
    /// </summary>
    public string CurrentMark { get; init; } = "na ekranie";
    /// <summary>Dopisek przy pozycji, na której stoi KURSOR ZESTAWU (tu zadziała Delete i przenoszenie).</summary>
    public string PointerMark { get; init; } = "tutaj";
    /// <summary>{0} = pozycja od 1, {1} = ile pozycji, {2} = tytuł. Ogłoszenie ruchu kursora zestawu.</summary>
    public string Position { get; init; } = "Pozycja {0} z {1}: {2}";

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

    // ── filtr listy zapisanych zestawów (etap 3) ─────────────────────────────
    /// <summary>{0} = ile zestawów pasuje do wpisanego fragmentu.</summary>
    public string FilterMatchMany { get; init; } = "Pasuje {0} zestawów";
    public string FilterMatchOne { get; init; } = "Pasuje 1 zestaw";
    public string FilterMatchNone { get; init; } = "Żaden zestaw nie pasuje";

    // ── usuwanie ZAPISANEGO zestawu z bazy (etap 3) ──────────────────────────
    /// <summary>{0} = nazwa, {1} = liczba pieśni. PIERWSZE Delete — pyta, nie usuwa.</summary>
    public string DeleteConfirm { get; init; } =
        "Usunąć zapisany zestaw: {0}, pieśni: {1}? Naciśnij Delete ponownie, aby potwierdzić, Escape aby anulować";
    /// <summary>
    /// To samo pytanie dla zestawu WCZYTANEGO na pulpicie — ostrzeżenie jest w NIM, bo skutek
    /// jest inny (pulpit przestanie mieć gdzie się zapisać przez Ctrl+S, Enter).
    /// </summary>
    public string DeleteConfirmLoaded { get; init; } =
        "Uwaga: to zestaw wczytany na pulpicie. Usunąć zapisany zestaw: {0}, pieśni: {1}? "
        + "Pieśni zostaną na pulpicie. Naciśnij Delete ponownie, aby potwierdzić, Escape aby anulować";
    /// <summary>{0} = nazwa, {1} = ile zestawów ZOSTAŁO w bazie.</summary>
    public string Deleted { get; init; } = "Usunięto zestaw: {0}. Pozostało {1} zestawów";
    public string DeletedOne { get; init; } = "Usunięto zestaw: {0}. Pozostał 1 zestaw";
    public string DeletedNone { get; init; } = "Usunięto zestaw: {0}. Nie ma zapisanych zestawów";
    /// <summary>{0} = nazwa. Ten sam komunikat, ale z przypomnieniem, że pieśni zostały na pulpicie.</summary>
    public string DeletedLoaded { get; init; } =
        "Usunięto zestaw: {0}. Pieśni zostały na pulpicie, ale nie są już z niczym powiązane — "
        + "Kontrol z S zapisze je jako nowy zestaw";
    public string DeleteFailed { get; init; } = "Nie udało się usunąć zestawu";
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
    /// <param name="pointerIndex">
    /// Pozycja KURSORA ZESTAWU (-1 = nie pokazuj). Odczyt musi powiedzieć OBIE rzeczy: co widzą
    /// wierni i gdzie stoi operator — inaczej po przesunięciu kursora Altem klawisz L mówiłby
    /// dokładnie to samo co przed nim, a jedyna różnica (gdzie zadziała Delete) byłaby niesłyszalna.
    /// </param>
    public static string DescribeSetlist(IReadOnlyList<string?> titles, int currentIndex, SetlistText t,
                                         int pointerIndex = -1)
    {
        if (titles is null || titles.Count == 0) return t.Empty;

        var header = titles.Count == 1 ? t.ListHeaderOne : string.Format(t.ListHeaderMany, titles.Count);
        var sb = new System.Text.StringBuilder(header);
        for (int i = 0; i < titles.Count; i++)
        {
            var name = Name(titles[i], t);
            sb.Append(' ').Append(i + 1).Append(". ").Append(name);
            if (i == currentIndex) sb.Append(", ").Append(t.CurrentMark);
            if (i == pointerIndex) sb.Append(", ").Append(t.PointerMark);
            sb.Append('.');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Ogłoszenie ruchu kursora ZESTAWU („Pozycja 3 z 7: Barka”). Numer i liczba pozycji są
    /// obowiązkowe: to jedyny sposób, w jaki niewidomy operator wie, jak daleko zajechał —
    /// sam tytuł nie mówi nic o miejscu w kolejce.
    /// </summary>
    public static string DescribePosition(string? title, int index, int total, SetlistText t)
        => string.Format(t.Position, index + 1, total, Name(title, t));

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

    /// <summary>
    /// Pytanie pierwszego Delete w panelu otwierania. Liczba pieśni jest w pytaniu, bo to jedyna
    /// miara tego, ILE CUDZEJ PRACY właśnie znika; <paramref name="isLoaded"/> dokłada ostrzeżenie,
    /// gdy kasowany zestaw jest tym wczytanym na pulpicie.
    /// </summary>
    public static string DescribeDeleteConfirm(string? name, int songCount, bool isLoaded, SetlistText t)
        => string.Format(isLoaded ? t.DeleteConfirmLoaded : t.DeleteConfirm, SetlistName(name, t), songCount);

    /// <summary>
    /// Potwierdzenie usunięcia zestawu z bazy. Liczba POZOSTAŁYCH zestawów jest obowiązkowa
    /// z tego samego powodu co przy usuwaniu pieśni: to jedyny dowód, że zniknął jeden, a nie dwa.
    /// </summary>
    public static string DescribeDeleted(string? name, int remaining, bool wasLoaded, SetlistText t)
        => wasLoaded ? string.Format(t.DeletedLoaded, SetlistName(name, t))
         : remaining <= 0 ? string.Format(t.DeletedNone, SetlistName(name, t))
         : remaining == 1 ? string.Format(t.DeletedOne, SetlistName(name, t))
         : string.Format(t.Deleted, SetlistName(name, t), remaining);

    /// <summary>Ile zestawów pasuje do wpisanego fragmentu (ogłaszane po każdej zmianie filtra).</summary>
    public static string DescribeFilterCount(int count, SetlistText t)
        => count <= 0 ? t.FilterMatchNone
         : count == 1 ? t.FilterMatchOne
         : string.Format(t.FilterMatchMany, count);

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

    private static string SetlistName(string? name, SetlistText t)
    {
        var label = (name ?? string.Empty).Trim();
        return label.Length == 0 ? t.UnnamedSetlist : label;
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
    /// Czy to naciśnięcie klawisza gasi uzbrojone potwierdzenie USUNIĘCIA PIEŚNI. Gasi WSZYSTKO
    /// poza samym Delete (drugi Delete to odpowiedź) i poza gołymi modyfikatorami (Ctrl/Shift/Alt
    /// same z siebie nie są akcją operatora — puszczenie ich zdarza się między naciśnięciami).
    /// </summary>
    public static bool Cancels(DeskKey key, DeskAction action)
        => Cancels(key, action, DeskAction.RemoveSetlistSong);

    /// <summary>
    /// To samo, ale dla DOWOLNEGO pytania — <paramref name="answer"/> to akcja, która jest na nie
    /// odpowiedzią (i jako jedyna go nie gasi).
    ///
    /// Pulpit ma DWA niezależne pytania „na Delete”: usunięcie pieśni z zestawu (fokus na liście
    /// slajdów) i usunięcie ZAPISANEGO zestawu z bazy (fokus w panelu otwierania). Muszą mieć
    /// osobne uzbrojenia, bo wspólne oznaczałoby, że pytanie zadane w jednym miejscu da się
    /// potwierdzić Delete'em w drugim — a niewidomy operator nie ma jak sprawdzić, gdzie stoi
    /// fokus. Ta przeciążka jest miejscem, w którym ta niezależność JEST WYRAŻONA: Delete
    /// w panelu (DeleteSavedSetlist) gasi uzbrojone usunięcie pieśni i odwrotnie.
    /// </summary>
    public static bool Cancels(DeskKey key, DeskAction action, DeskAction answer)
        => key != DeskKey.Modifier && action != answer;
}
