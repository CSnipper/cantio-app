using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Cantio.ViewModels;

/// <summary>Jedna pozycja listy czytania (slajd bieżącej pieśni).</summary>
public sealed class AccessibleSlideItem
{
    /// <summary>Tekst, który ma przeczytać czytnik ekranu (nagłówek + treść slajdu).</summary>
    public string Spoken { get; init; } = string.Empty;
    /// <summary>To samo dla widzącego pomocnika na ekranie.</summary>
    public string Display => Spoken;
}

/// <summary>Jedna pozycja wyników wyszukiwania („123, Kiedy ranne wstają zorze”).</summary>
public sealed class AccessibleSearchItem
{
    public required Song Song { get; init; }
    public string Spoken { get; init; } = string.Empty;
    public string Display => Spoken;
}

/// <summary>Jedna pozycja listy zapisanych zestawów („Niedziela 3 zwykła, pieśni: 5”).</summary>
public sealed class AccessibleSetlistEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SongCount { get; init; }
    public string Spoken { get; init; } = string.Empty;
    public string Display => Spoken;
}

/// <summary>
/// Pulpit organisty NIEWIDOMEGO — cienka warstwa nad <see cref="DisplayViewModel"/>.
/// Cały rdzeń (baza, slajdy, projekcja) jest wspólny z pulpitem widzącego; tutaj dochodzi
/// wyłącznie to, czego wymaga praca z czytnikiem ekranu:
///
/// • DRUGI KURSOR — <see cref="AccessibleCursor"/> (kursor CZYTANIA). Strzałki góra/dół chodzą po
///   slajdach bieżącej pieśni i pozwalają je przeczytać, NIE RUSZAJĄC obrazu widzianego przez
///   wiernych. Kursorem projekcji zostaje <c>DisplayViewModel.CurrentSlideIndex</c>, ruszany
///   wyłącznie klawiszami Page Up / Page Down (i Enter — świadome „wyświetl to, co czytam").
/// • OGŁOSZENIA — po każdej zmianie tego, co widzą wierni, oraz na żądanie (klawisz stanu).
///   Nie ma tu żadnej syntezy mowy: teksty idą do live region w oknie, a mówi je NVDA
///   ustawieniami użytkownika.
///
/// Ta klasa NIE dotyka <c>MainWindow</c> ani jego ViewModeli — pulpit widzącego zostaje bez zmian.
/// </summary>
public partial class AccessibleShellViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly DisplayViewModel _display;
    private readonly AccessibleCursor _cursor = new();
    private readonly AccessibleCursor _searchCursor = new();
    private readonly AnnouncementText _texts;
    private readonly SearchText _searchTexts;
    private readonly SetlistText _setlistTexts;
    private readonly LicenseText _licenseTexts;
    private readonly AccessibleCursor _pickerCursor = new();

    /// <summary>
    /// TRZECI kursor — pozycja w ZESTAWIE (Alt + strzałki). Niezależny od kursora czytania
    /// i od projekcji; to na nim działają Delete i przenoszenie. Zob. <see cref="SetlistCursorDown"/>.
    /// </summary>
    private readonly AccessibleCursor _setlistCursor = new();

    /// <summary>Komunikat do live region — okno wypycha go do czytnika ekranu.</summary>
    public event Action<string>? Announced;

    public DisplayViewModel Display => _display;

    public ObservableCollection<AccessibleSlideItem> ReadingSlides { get; } = [];

    /// <summary>Wyniki ostatniego wyszukiwania (pusta lista, dopóki nikt nie szukał).</summary>
    public ObservableCollection<AccessibleSearchItem> SearchResults { get; } = [];

    /// <summary>Pozycja kursora CZYTANIA (dwukierunkowo z listą w oknie). Nie rusza projekcji.</summary>
    [ObservableProperty] private int _readingIndex = -1;

    /// <summary>Podpisy dla widzącego pomocnika (i dla zrzutu ekranu w zgłoszeniu).</summary>
    [ObservableProperty] private string _setlistCaption = string.Empty;
    [ObservableProperty] private string _songCaption = string.Empty;
    [ObservableProperty] private string _projectionCaption = string.Empty;
    [ObservableProperty] private string _lastAnnouncement = string.Empty;

    /// <summary>Tryb wyszukiwania. Okno używa tego do pokazania panelu I do rozstrzygnięcia klawiszy.</summary>
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private string _searchQuery = string.Empty;
    /// <summary>Pozycja kursora WYNIKÓW (dwukierunkowo z listą w oknie).</summary>
    [ObservableProperty] private int _searchIndex = -1;
    [ObservableProperty] private string _searchCaption = string.Empty;

    /// <summary>Panel zapisu zestawu (Ctrl+S). Okno pokazuje pole nazwy i przenosi do niego fokus.</summary>
    [ObservableProperty] private bool _isSavePanelOpen;
    [ObservableProperty] private string _setlistNameInput = string.Empty;

    /// <summary>Panel otwierania zestawu (Ctrl+O).</summary>
    [ObservableProperty] private bool _isPickerOpen;
    [ObservableProperty] private int _pickerIndex = -1;
    [ObservableProperty] private string _pickerCaption = string.Empty;

    /// <summary>
    /// Filtr listy zapisanych zestawów. Powód istnienia: organista ma ich kilkaset, a strzałkami
    /// przejść się przez taką listę ze słuchu nie da. Filtruje NA BIEŻĄCO (nie po Enterze jak
    /// wyszukiwarka pieśni), bo tutaj po każdym znaku ogłaszana jest tylko LICZBA trafień —
    /// krótka i nie zagłusza pisania.
    /// </summary>
    [ObservableProperty] private string _pickerFilter = string.Empty;

    /// <summary>Zapisane zestawy PO filtrze — to jest lista, po której chodzi kursor panelu.</summary>
    public ObservableCollection<AccessibleSetlistEntry> SavedSetlists { get; } = [];

    /// <summary>Wszystkie zapisane zestawy (bez filtra) — źródło dla <see cref="SavedSetlists"/>.</summary>
    private readonly List<AccessibleSetlistEntry> _allSetlists = [];

    /// <summary>Pozycja kursora ZESTAWU (podpis dla widzącego pomocnika; -1 = pusty zestaw).</summary>
    [ObservableProperty] private string _setlistCursorCaption = string.Empty;

    /// <summary>Czy jakikolwiek panel zestawu zasłania pulpit (Escape zamyka JEGO).</summary>
    public bool IsSetlistPanelOpen => IsSavePanelOpen || IsPickerOpen;

    // ── Licencja (F9) ────────────────────────────────────────────────────────

    /// <summary>Panel wpisania klucza licencyjnego (F9). Okno pokazuje pole i przenosi do niego fokus.</summary>
    [ObservableProperty] private bool _isLicensePanelOpen;

    /// <summary>Treść pola klucza — operator wkleja tu ciąg z maila (Ctrl+V).</summary>
    [ObservableProperty] private string _licenseKeyInput = string.Empty;

    /// <summary>
    /// „Licencja: Jan Kowalski" albo „Wersja niezarejestrowana”. Widoczne w oknie i ogłaszane
    /// przy starcie oraz w pomocy F1 — nazwisko właściciela JEST tu zabezpieczeniem, więc nie
    /// wolno go schować za żadnym dodatkowym krokiem.
    /// </summary>
    [ObservableProperty] private string _licenseCaption = string.Empty;

    /// <summary>Czy zapisany klucz przechodzi weryfikację podpisem (wejście dla blokady autora).</summary>
    public bool IsLicensed { get; private set; }

    /// <summary>Czy JAKIKOLWIEK panel zasłania pulpit — to trafia do mapy klawiszy jako PanelOpen.</summary>
    public bool IsAnyPanelOpen => IsSetlistPanelOpen || IsLicensePanelOpen;

    /// <summary>
    /// Zegar dla wygasania potwierdzenia usunięcia. Wystawiony, bo inaczej „potwierdzenie
    /// wygasa po 5 sekundach" dałoby się sprawdzić wyłącznie testem, który 5 sekund śpi —
    /// a taki test nikt nie uruchamia dość często, żeby cokolwiek złapał.
    /// </summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    // Uzbrojone potwierdzenie usunięcia PIEŚNI: KTÓREJ pozycji dotyczy i KIEDY zadaliśmy pytanie.
    private int? _pendingRemoveIndex;
    private DateTime? _pendingRemoveAt;

    // Uzbrojone potwierdzenie usunięcia ZAPISANEGO ZESTAWU — CELOWO osobne pola, nie te same.
    // Wspólne uzbrojenie znaczyłoby, że pytanie „usunąć pieśń?” da się potwierdzić Delete'em
    // w panelu otwierania (i odwrotnie), a niewidomy operator nie ma jak sprawdzić, gdzie stoi
    // fokus — skasowałby cudzą pracę, będąc pewnym, że odpowiada na zupełnie inne pytanie.
    private int? _pendingDeleteSetlistId;
    private DateTime? _pendingDeleteSetlistAt;

    public AccessibleShellViewModel(DatabaseService db, DisplayViewModel display)
    {
        _db = db;
        _display = display;
        _texts = BuildTexts();
        _searchTexts = BuildSearchTexts();
        _setlistTexts = BuildSetlistTexts();
        _licenseTexts = BuildLicenseTexts();
        _display.PropertyChanged += OnDisplayPropertyChanged;
    }

    /// <summary>
    /// Szablony komunikatów z zasobów lokalizacji (pl/en/es) — rdzeń ich nie zna. Brak klucza
    /// w zasobach NIE MOŻE skończyć się przeczytaniem na głos „Acc.ViewersSee": wtedy wchodzi
    /// polski tekst domyślny z <see cref="AnnouncementText"/>.
    /// </summary>
    private static AnnouncementText BuildTexts()
    {
        var d = new AnnouncementText();
        return new AnnouncementText
        {
            ViewersSee   = L("Acc.ViewersSee",   d.ViewersSee),
            SlideOfCount = L("Acc.SlideOfCount", d.SlideOfCount),
            Blanked      = L("Acc.Blanked",      d.Blanked),
            Restored     = L("Acc.Restored",     d.Restored),
            Nothing      = L("Acc.Nothing",      d.Nothing),
            Verse        = L("Acc.KindVerse",    d.Verse),
            Chorus       = L("Acc.KindChorus",   d.Chorus),
            Bridge       = L("Acc.KindBridge",   d.Bridge),
            Private      = L("Acc.KindPrivate",  d.Private),
            Image        = L("Acc.KindImage",    d.Image),
        };
    }

    /// <summary>To samo dla licencji.</summary>
    private static LicenseText BuildLicenseTexts()
    {
        var d = new LicenseText();
        return new LicenseText
        {
            Registered   = L("Acc.LicenseRegistered",   d.Registered),
            Unregistered = L("Acc.LicenseUnregistered", d.Unregistered),
            PanelOpened  = L("Acc.LicensePanelOpened",  d.PanelOpened),
            PanelClosed  = L("Acc.LicensePanelClosed",  d.PanelClosed),
            Accepted     = L("Acc.LicenseAccepted",     d.Accepted),
            Invalid      = L("Acc.LicenseInvalid",      d.Invalid),
            Empty        = L("Acc.LicenseEmpty",        d.Empty),
            WrongProduct = L("Acc.LicenseWrongProduct", d.WrongProduct),
        };
    }

    /// <summary>To samo dla wyszukiwarki — rdzeń zna wyłącznie polskie teksty domyślne.</summary>
    private static SearchText BuildSearchTexts()
    {
        var d = new SearchText();
        return new SearchText
        {
            Opened        = L("Acc.SearchOpened",   d.Opened),
            Closed        = L("Acc.SearchClosed",   d.Closed),
            FoundOne      = L("Acc.SearchFoundOne", d.FoundOne),
            FoundMany     = L("Acc.SearchFoundMany", d.FoundMany),
            NoResults     = L("Acc.SearchNoResults", d.NoResults),
            EmptyQuery    = L("Acc.SearchEmptyQuery", d.EmptyQuery),
            Added         = L("Acc.SearchAdded",    d.Added),
            AddedAndShown = L("Acc.SearchAddedShown", d.AddedAndShown),
            NoSelection   = L("Acc.SearchNoSelection", d.NoSelection),
            Untitled      = L("Acc.SearchUntitled", d.Untitled),
        };
    }

    /// <summary>To samo dla zarządzania zestawem (odczyt, usuwanie, przenoszenie, zapis, otwieranie).</summary>
    private static SetlistText BuildSetlistTexts()
    {
        var d = new SetlistText();
        return new SetlistText
        {
            ListHeaderMany  = L("Acc.SetlistHeaderMany",  d.ListHeaderMany),
            ListHeaderOne   = L("Acc.SetlistHeaderOne",   d.ListHeaderOne),
            Empty           = L("Acc.SetlistEmpty",       d.Empty),
            CurrentMark     = L("Acc.SetlistCurrentMark", d.CurrentMark),
            PointerMark     = L("Acc.SetlistPointerMark", d.PointerMark),
            Position        = L("Acc.SetlistPosition",    d.Position),
            ConfirmRemove   = L("Acc.SetlistConfirmRemove", d.ConfirmRemove),
            Removed         = L("Acc.SetlistRemoved",     d.Removed),
            RemovedOne      = L("Acc.SetlistRemovedOne",  d.RemovedOne),
            RemovedLast     = L("Acc.SetlistRemovedLast", d.RemovedLast),
            RemoveCancelled = L("Acc.SetlistRemoveCancelled", d.RemoveCancelled),
            NoCurrent       = L("Acc.SetlistNoCurrent",   d.NoCurrent),
            Moved           = L("Acc.SetlistMoved",       d.Moved),
            AtFirst         = L("Acc.SetlistAtFirst",     d.AtFirst),
            AtLast          = L("Acc.SetlistAtLast",      d.AtLast),
            SaveOpened      = L("Acc.SetlistSaveOpened",  d.SaveOpened),
            SaveClosed      = L("Acc.SetlistSaveClosed",  d.SaveClosed),
            Saved           = L("Acc.SetlistSaved",       d.Saved),
            SavedNew        = L("Acc.SetlistSavedNew",    d.SavedNew),
            SaveEmptyName   = L("Acc.SetlistSaveEmptyName", d.SaveEmptyName),
            SaveNothing     = L("Acc.SetlistSaveNothing", d.SaveNothing),
            OpenOpened      = L("Acc.SetlistOpenOpened",  d.OpenOpened),
            OpenClosed      = L("Acc.SetlistOpenClosed",  d.OpenClosed),
            Entry           = L("Acc.SetlistEntry",       d.Entry),
            EntryEmpty      = L("Acc.SetlistEntryEmpty",  d.EntryEmpty),
            // Ten sam klucz co podpis wczytanego zestawu — komunikat i podpis mówią to samo,
            // więc dwa klucze rozjechałyby się przy pierwszym tłumaczeniu.
            Loaded          = L("Acc.SetlistLoaded",      d.Loaded),
            NoSetlists      = L("Acc.SetlistNoneSaved",   d.NoSetlists),
            NoSelection     = L("Acc.SetlistNoSelection", d.NoSelection),
            Untitled        = L("Acc.SearchUntitled",     d.Untitled),
            UnnamedSetlist  = L("Acc.SetlistUnnamed",     d.UnnamedSetlist),
            FilterMatchMany = L("Acc.SetlistFilterMany",  d.FilterMatchMany),
            FilterMatchOne  = L("Acc.SetlistFilterOne",   d.FilterMatchOne),
            FilterMatchNone = L("Acc.SetlistFilterNone",  d.FilterMatchNone),
            DeleteConfirm       = L("Acc.SetlistDeleteConfirm",       d.DeleteConfirm),
            DeleteConfirmLoaded = L("Acc.SetlistDeleteConfirmLoaded", d.DeleteConfirmLoaded),
            Deleted             = L("Acc.SetlistDeleted",             d.Deleted),
            DeletedOne          = L("Acc.SetlistDeletedOne",          d.DeletedOne),
            DeletedNone         = L("Acc.SetlistDeletedNone",         d.DeletedNone),
            DeletedLoaded       = L("Acc.SetlistDeletedLoaded",       d.DeletedLoaded),
            DeleteFailed        = L("Acc.SetlistDeleteFailed",        d.DeleteFailed),
        };
    }

    /// <summary>
    /// Tekst z zasobów albo zapasowy — dla okna, które nie może pokazać (ani przeczytać
    /// czytnikiem) pustki, gdy klucza zabraknie.
    /// </summary>
    public static string Text(string key, string fallback) => L(key, fallback);

    /// <summary>Tekst z zasobów albo zapasowy, gdy <see cref="LocalizationManager"/> oddał sam klucz.</summary>
    private static string L(string key, string fallback)
    {
        var value = LocalizationManager.Get(key);
        return value == key ? fallback : value;
    }

    // ── Start ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Otwiera projekcję, wczytuje ostatni zestaw i ogłasza stan. Kolejność ogłoszeń jest celowa:
    /// najpierw OSTRZEŻENIE o powielaniu ekranu (to jedyna rzecz, której niewidomy nie zauważy,
    /// a która kompromituje go przed całym kościołem), potem wczytany zestaw.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _display.InitializeAsync();

        // DisplayViewModel wczytuje ostatni zestaw tylko gdy operator włączył takie ustawienie.
        // Na tym pulpicie zestaw to jedyna droga do pieśni (wyszukiwarki nie ma w etapie 1),
        // więc wczytujemy go zawsze — bez tego pulpit startowałby pusty i bezużyteczny.
        if (_display.SetlistItems.Count == 0)
        {
            var lastId = await _db.GetSettingAsync("last_setlist_id");
            if (int.TryParse(lastId, out var id))
            {
                var setlist = await _db.GetSetlistWithItemsAsync(id);
                if (setlist != null)
                    await _display.LoadPinnedSetlistCommand.ExecuteAsync(setlist);
            }
        }

        await RefreshLicenseAsync();

        AnnounceScreens();
        AnnounceSetlist();
        // Licencja NA KOŃCU, a nie na początku: ostrzeżenie o ekranach i wczytany zestaw są tym,
        // po co operator siada do pulpitu, a nazwisko właściciela ma zostać w uchu jako ostatnie.
        Announce(LicenseCaption);
    }

    // ── Licencja (F9) ────────────────────────────────────────────────────────

    /// <summary>Czyta klucz z bazy, ustawia podpis w oknie i flagę <see cref="IsLicensed"/>.</summary>
    public async Task RefreshLicenseAsync()
    {
        var stored = await _db.GetSettingAsync(LicenseKey.SettingKey);
        IsLicensed = AccessibleLicense.IsRegistered(stored);
        LicenseCaption = AccessibleLicense.DescribeStored(stored, _licenseTexts);
    }

    /// <summary>
    /// F9 — panel klucza. Pole startuje PUSTE (nie pokazujemy dotychczasowego klucza): jedyne,
    /// co się tu robi, to wklejenie nowego, a wcześniejsza zawartość musiałaby zostać skasowana
    /// w ciemno przez osobę, która jej nie widzi.
    /// </summary>
    public void OpenLicensePanel()
    {
        LicenseKeyInput = string.Empty;
        IsLicensePanelOpen = true;
        Announce(_licenseTexts.PanelOpened);
    }

    public void CloseLicensePanel()
    {
        if (!IsLicensePanelOpen) return;
        IsLicensePanelOpen = false;
        LicenseKeyInput = string.Empty;
        Announce(_licenseTexts.PanelClosed);
    }

    /// <summary>
    /// Enter w polu klucza. Zwraca true, gdy panel można zamknąć (klucz przyjęty).
    /// Klucz odrzucony ZOSTAWIA panel otwarty i treść w polu — inaczej operator, który przekręcił
    /// jeden znak, musiałby wklejać całość od nowa, nie wiedząc nawet, czy coś tam w ogóle było.
    /// </summary>
    public async Task<bool> ConfirmLicenseAsync()
    {
        var result = AccessibleLicense.Validate(LicenseKeyInput, out var info);
        if (result != AccessibleLicense.Result.Ok)
        {
            Announce(AccessibleLicense.DescribeResult(result, info, _licenseTexts));
            return false;
        }

        // Do bazy idzie tekst PO oczyszczeniu z białych znaków — mail potrafi go złamać na kilka
        // wierszy, a zapisany z nimi klucz przeszedłby dziś (odczyt jest tolerancyjny), lecz
        // wyglądałby na uszkodzony w każdym późniejszym zgłoszeniu.
        var normalized = new string(LicenseKeyInput.Where(c => !char.IsWhiteSpace(c)).ToArray());
        await _db.SaveSettingAsync(LicenseKey.SettingKey, normalized);

        IsLicensePanelOpen = false;
        LicenseKeyInput = string.Empty;
        await RefreshLicenseAsync();
        Announce(AccessibleLicense.DescribeResult(result, info, _licenseTexts));
        return true;
    }

    // ── Kursor CZYTANIA (prywatny — projekcja się nie zmienia) ───────────────

    public void ReadUp()    => AfterReadingMove(_cursor.MoveUp(), atEdge: "Acc.AtFirstSlide");
    public void ReadDown()  => AfterReadingMove(_cursor.MoveDown(), atEdge: "Acc.AtLastSlide");
    public void ReadStart() => AfterReadingMove(_cursor.MoveToStart(), atEdge: "Acc.AtFirstSlide");
    public void ReadEnd()   => AfterReadingMove(_cursor.MoveToEnd(), atEdge: "Acc.AtLastSlide");

    private void AfterReadingMove(bool moved, string atEdge)
    {
        if (moved)
        {
            // Sam ruch NIE jest ogłaszany live regionem: fokus przechodzi na pozycję listy,
            // a czytnik ekranu czyta ją sam. Podwójne czytanie byłoby gorsze niż brak.
            ReadingIndex = _cursor.Index;
            return;
        }
        Announce(LocalizationManager.Get(atEdge));
    }

    /// <summary>Klawisz „przeczytaj ponownie" — awaryjna droga, gdy czytnik przegapi zmianę fokusu.</summary>
    public void RepeatReading()
    {
        if (!_cursor.HasPosition) { Announce(_texts.Nothing); return; }
        Announce(ReadingSlides[_cursor.Index].Spoken);
    }

    /// <summary>Synchronizacja z listą w oknie (klik myszą pomocnika, przewinięcie).</summary>
    partial void OnReadingIndexChanged(int value)
    {
        if (value != _cursor.Index) _cursor.MoveTo(value);
    }

    // ── Kursor PROJEKCJI (to, co widzą wierni) ───────────────────────────────

    public void ProjectNextSlide() => _display.NextSlideCommand.Execute(null);
    public void ProjectPrevSlide() => _display.PrevSlideCommand.Execute(null);
    public void NextSong()         => _display.NextSongCommand.Execute(null);
    public void PrevSong()         => _display.PrevSongCommand.Execute(null);

    /// <summary>
    /// „Wyświetl to, co właśnie czytam" — jedyny most między kursorami, zawsze na jawne polecenie
    /// (Enter). Ustawiamy indeks projekcji wprost: <c>OnCurrentSlideIndexChanged</c> w
    /// <see cref="DisplayViewModel"/> sam wypycha slajd na rzutnik.
    /// </summary>
    public void ProjectReadingSlide()
    {
        if (!_cursor.HasPosition) { Announce(_texts.Nothing); return; }
        if (_cursor.Index == _display.CurrentSlideIndex) { AnnounceStatus(); return; }
        _display.CurrentSlideIndex = _cursor.Index;
    }

    public void ToggleBlank()
    {
        _display.ToggleBlankCommand.Execute(null);
        // Ogłoszenie leci z obserwatora ScreenBlanked — jedno miejsce, żeby zmiana zrobiona
        // skądkolwiek indziej (pilot, mysz pomocnika) też była słyszalna.
    }

    // ── Wyszukiwarka pieśni (etap 2) ─────────────────────────────────────────

    /// <summary>
    /// Otwiera tryb wyszukiwania. Nie czyści poprzednich wyników — organista, który dodał pieśń
    /// i wrócił do wyszukiwarki, zwykle chce dołożyć KOLEJNĄ z tej samej listy.
    /// </summary>
    public void OpenSearch()
    {
        IsSearchOpen = true;
        Announce(_searchTexts.Opened);
    }

    /// <summary>Zamyka tryb wyszukiwania; okno oddaje fokus liście slajdów.</summary>
    public void CloseSearch()
    {
        if (!IsSearchOpen) return;
        IsSearchOpen = false;
        Announce(_searchTexts.Closed);
    }

    /// <summary>
    /// Szuka przez WSPÓLNY <see cref="SongSearch"/> (te same wyniki co u widzącego operatora
    /// i w Pilocie). Zwraca true, gdy coś znaleziono — okno przenosi wtedy fokus na wyniki.
    /// </summary>
    public async Task<bool> RunSearchAsync()
    {
        var query = (SearchQuery ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            SetSearchResults([]);
            Announce(_searchTexts.EmptyQuery);
            return false;
        }

        var hits = await _db.SearchSongsAsync(query);
        SetSearchResults(hits);
        Announce(AccessibleSearch.DescribeCount(SearchResults.Count, _searchTexts));
        return SearchResults.Count > 0;
    }

    private void SetSearchResults(IReadOnlyList<Song> hits)
    {
        SearchResults.Clear();
        foreach (var s in hits)
            SearchResults.Add(new AccessibleSearchItem
            {
                Song = s,
                Spoken = AccessibleSearch.DescribeResult(s.Number, s.Title, _searchTexts),
            });

        _searchCursor.Reset(SearchResults.Count);
        SearchIndex = _searchCursor.Index;
        SearchCaption = AccessibleSearch.DescribeCount(SearchResults.Count, _searchTexts);
    }

    public void SearchUp()   => AfterSearchMove(_searchCursor.MoveUp());
    public void SearchDown() => AfterSearchMove(_searchCursor.MoveDown());

    private void AfterSearchMove(bool moved)
    {
        // Jak przy kursorze czytania: ruch czyta czytnik ekranu przez fokus pozycji listy,
        // live region milczy, żeby nie mówić wszystkiego dwa razy.
        if (moved) SearchIndex = _searchCursor.Index;
    }

    partial void OnSearchIndexChanged(int value)
    {
        if (value != _searchCursor.Index) _searchCursor.MoveTo(value);
    }

    /// <summary>Powtórzenie odczytu bieżącego wyniku (klawisz R w trybie wyszukiwania).</summary>
    public void RepeatSearchResult()
    {
        if (!_searchCursor.HasPosition) { Announce(_searchTexts.NoSelection); return; }
        Announce(SearchResults[_searchCursor.Index].Spoken);
    }

    /// <summary>
    /// Dokłada zaznaczony wynik na koniec zestawu. Wyszukiwarka ZOSTAJE otwarta — organista
    /// przygotowuje całą mszę jednym ciągiem, a zamykanie panelu po każdej pieśni kazałoby mu
    /// od nowa szukać drogi do pola.
    /// </summary>
    /// <param name="alsoShow">Ctrl+Enter: dodaj i od razu wyświetl wiernym.</param>
    public void AddSelectedToSetlist(bool alsoShow = false)
    {
        if (!_searchCursor.HasPosition) { Announce(_searchTexts.NoSelection); return; }
        var song = SearchResults[_searchCursor.Index].Song;

        var item = _display.AppendSongToSetlist(song);
        // Pusty zestaw: pierwsza pieśń jest ładowana już przez AppendSongToSetlist — drugie
        // wczytanie tylko przewinęłoby slajdy na początek i skasowało kursor czytania.
        if (alsoShow && !ReferenceEquals(_display.SelectedSetlistItem, item))
            _display.DisplaySetlistItemCommand.Execute(item);

        // Pozycja liczona z ZESTAWU, nie z licznika dodań: gdy zestaw był pusty, dołożona pieśń
        // od razu staje się bieżąca i to jedyny sygnał, po którym niewidomy to pozna.
        int position = _display.SetlistItems.IndexOf(item) + 1;
        UpdateSetlistCaption();
        Announce(AccessibleSearch.DescribeAdded(song.Title, position, alsoShow, _searchTexts));
    }

    // ── Zarządzanie zestawem (etapy 2-3) ─────────────────────────────────────
    //
    // BIEŻĄCA pieśń zestawu to ta, którą widzą wierni (`SelectedSetlistItem`) — ta sama, po której
    // chodzi Ctrl+Page Up/Down. Od etapu 3 usuwanie i przenoszenie NIE działają na niej, tylko na
    // KURSORZE ZESTAWU (Alt + strzałki): dojście do pieśni bieżącej zmieniało obraz na rzutniku,
    // więc wyrzucenie z zestawu czwartej pieśni kosztowało przewinięcie projekcji na oczach wiernych.

    /// <summary>Indeks bieżącej pozycji zestawu (-1 = żadna).</summary>
    private int CurrentSetlistIndex =>
        _display.SelectedSetlistItem is { } item ? _display.SetlistItems.IndexOf(item) : -1;

    // ── TRZECI kursor: pozycja w zestawie (Alt + strzałki) ───────────────────

    /// <summary>Pozycja kursora ZESTAWU (-1 = pusty zestaw). Wystawione dla testów i podpisu.</summary>
    public int SetlistCursorIndex
    {
        get { SyncCursorCount(); return _setlistCursor.Index; }
    }

    /// <summary>
    /// Liczba pozycji zmienia się poza tym kursorem (dodanie z wyszukiwarki, wczytanie zestawu,
    /// synchronizacja z Pilotem), więc przed każdym użyciem sprawdzamy ją na nowo. Bez tego kursor
    /// chodziłby po nieaktualnej długości i cicho odmawiałby ruchu na końcu zestawu.
    /// </summary>
    private void SyncCursorCount()
    {
        int count = _display.SetlistItems.Count;
        if (_setlistCursor.Count != count) _setlistCursor.Reset(count, _setlistCursor.Index);
    }

    /// <summary>Ustawia kursor zestawu na konkretnej pozycji (bez ogłaszania).</summary>
    private void SetSetlistCursor(int index)
    {
        _setlistCursor.Reset(_display.SetlistItems.Count, index < 0 ? 0 : index);
        UpdateSetlistCursorCaption();
    }

    public void SetlistCursorUp()    => AfterSetlistCursorMove(MoveCursor(-1), _setlistTexts.AtFirst);
    public void SetlistCursorDown()  => AfterSetlistCursorMove(MoveCursor(+1), _setlistTexts.AtLast);
    public void SetlistCursorFirst() => AfterSetlistCursorMove(MoveCursorTo(0), _setlistTexts.AtFirst);
    public void SetlistCursorLast()
        => AfterSetlistCursorMove(MoveCursorTo(_display.SetlistItems.Count - 1), _setlistTexts.AtLast);

    private bool MoveCursor(int delta)
    {
        SyncCursorCount();
        return delta < 0 ? _setlistCursor.MoveUp() : _setlistCursor.MoveDown();
    }

    private bool MoveCursorTo(int index)
    {
        SyncCursorCount();
        return _setlistCursor.MoveTo(index);
    }

    /// <summary>
    /// Ogłoszenie po ruchu kursora zestawu. W przeciwieństwie do kursora CZYTANIA mówi tu live
    /// region, a nie fokus listy: kursor zestawu nie ma własnej listy w oknie (fokus zostaje na
    /// slajdach bieżącej pieśni), więc bez ogłoszenia ruch byłby zupełnie niesłyszalny.
    /// </summary>
    private void AfterSetlistCursorMove(bool moved, string atEdge)
    {
        SyncCursorCount();
        UpdateSetlistCursorCaption();
        if (!_setlistCursor.HasPosition) { Announce(_setlistTexts.Empty); return; }
        if (!moved) { Announce(atEdge); return; }
        AnnounceSetlistCursor();
    }

    private void AnnounceSetlistCursor()
    {
        int i = _setlistCursor.Index;
        Announce(AccessibleSetlist.DescribePosition(
            ItemTitle(_display.SetlistItems[i]), i, _display.SetlistItems.Count, _setlistTexts));
    }

    /// <summary>
    /// Alt+Enter — JEDYNY most z kursora zestawu do rzutnika. Wszystko inne, co robi ten kursor,
    /// jest dla wiernych niewidoczne, więc wyświetlenie musi być jawnym poleceniem.
    /// </summary>
    public void ShowSetlistCursorSong()
    {
        SyncCursorCount();
        if (!_setlistCursor.HasPosition) { Announce(_setlistTexts.Empty); return; }

        var item = _display.SetlistItems[_setlistCursor.Index];
        int before = _announceCount;
        _display.DisplaySetlistItemCommand.Execute(item);
        // Gdy pieśń spod kursora JUŻ była na ekranie, nic się nie zmienia i nic samo nie zabrzmi —
        // a operator musi dostać odpowiedź na każde naciśnięcie klawisza.
        if (_announceCount == before) AnnounceStatus();
    }

    private void UpdateSetlistCursorCaption()
    {
        int i = _setlistCursor.Index;
        SetlistCursorCaption = i < 0 || i >= _display.SetlistItems.Count
            ? string.Empty
            : AccessibleSetlist.DescribePosition(
                ItemTitle(_display.SetlistItems[i]), i, _display.SetlistItems.Count, _setlistTexts);
    }

    /// <summary>Tytuł pozycji zestawu dla czytnika: pieśń, tekst jednorazowy albo obrazek.</summary>
    private string ItemTitle(SetlistItem item)
        => item.IsImageItem ? _texts.Image
         : item.IsTextItem ? (item.CustomTitle ?? _texts.Image)
         : (item.Song?.Title ?? string.Empty);

    /// <summary>
    /// Klawisz L: przeczytaj cały zestaw po kolei. Dla niewidomego to jedyny sposób, żeby wiedzieć,
    /// co w ogóle jest w zestawie i w jakiej kolejności — reszta pulpitu mówi tylko o jednej pieśni.
    /// </summary>
    public void ReadSetlist()
    {
        var titles = _display.SetlistItems.Select(i => (string?)ItemTitle(i)).ToList();
        Announce(AccessibleSetlist.DescribeSetlist(
            titles, CurrentSetlistIndex, _setlistTexts, SetlistCursorIndex));
    }

    /// <summary>
    /// Delete: pierwszy raz PYTA, drugi usuwa. Niewidomy operator nie zobaczy okna dialogowego,
    /// więc pytanie zadaje live region — ale uzbrojone potwierdzenie wygasa (czas albo dowolny
    /// inny klawisz, zob. <see cref="DeleteConfirmation"/>), żeby Delete naciśnięty przypadkiem
    /// pół mszy później nie skasował zupełnie innej pieśni.
    /// </summary>
    public void RemoveCurrentFromSetlist()
    {
        // Usuwamy pozycję spod KURSORA ZESTAWU, nie bieżącą — dojście do bieżącej wymagało
        // przewinięcia projekcji przed wiernymi (zob. nagłówek sekcji).
        int index = SetlistCursorIndex;
        if (index < 0) { ClearPendingRemove(); Announce(_setlistTexts.NoCurrent); return; }

        if (!DeleteConfirmation.Confirms(_pendingRemoveIndex, _pendingRemoveAt, index, Clock()))
        {
            _pendingRemoveIndex = index;
            _pendingRemoveAt = Clock();
            Announce(AccessibleSetlist.DescribeConfirmRemove(
                ItemTitle(_display.SetlistItems[index]), _setlistTexts));
            return;
        }

        ClearPendingRemove();
        var item = _display.SetlistItems[index];
        var title = ItemTitle(item);
        int currentBefore = CurrentSetlistIndex;

        _display.SetlistItems.RemoveAt(index);
        Renumber();
        int countAfter = _display.SetlistItems.Count;

        // TA SAMA reguła dla obu kursorów — bieżącej pozycji i kursora zestawu. Różnią się tylko
        // tym, od czego startują; wspólna funkcja gwarantuje, że żaden z nich nie zostanie na
        // pieśni, której już nie ma (ani nie przeskoczy na cudzą).
        int nextCurrent = AccessibleSetlist.CurrentAfterRemoval(index, currentBefore, countAfter);
        int nextCursor  = AccessibleSetlist.CurrentAfterRemoval(index, index, countAfter);

        // Obraz na rzutniku rusza się WYŁĄCZNIE wtedy, gdy zniknęła pieśń, którą wierni widzieli.
        // Ekran gaśnie WYŁĄCZNIE przy pustym zestawie — warunkiem jest `countAfter`, a nie
        // „nie ma bieżącej pozycji”: bieżąca bywa chwilowo nieustawiona, a zgaszony rzutnik
        // w środku śpiewu jest dla niewidomego operatora najkosztowniejszym z możliwych błędów.
        if (countAfter == 0)
            _display.ClearCurrentSong();
        else if (currentBefore == index)
            _display.DisplaySetlistItemCommand.Execute(_display.SetlistItems[nextCurrent]);

        // PO ewentualnej zmianie bieżącej pieśni: ta zmiana sama przestawia kursor zestawu
        // (kursor podąża za bieżącą), a my chcemy zostać tam, gdzie operator usuwał.
        SetSetlistCursor(nextCursor);

        UpdateSetlistCaption();
        Announce(AccessibleSetlist.DescribeRemoved(title, countAfter, _setlistTexts));
    }

    /// <summary>
    /// Escape (i każdy inny klawisz): gasi uzbrojone potwierdzenie — OBOJĘTNIE które z dwóch
    /// (usunięcie pieśni albo usunięcie zapisanego zestawu). Bez uzbrojenia MILCZY.
    /// </summary>
    public void CancelPendingRemove()
    {
        if (_pendingRemoveIndex == null && _pendingDeleteSetlistId == null) return;
        ClearPendingRemove();
        ClearPendingDeleteSetlist();
        Announce(_setlistTexts.RemoveCancelled);
    }

    /// <summary>
    /// Wygaszenie potwierdzenia po KAŻDYM innym klawiszu. Okno woła to dla każdego naciśnięcia —
    /// jedno miejsce, więc nie da się dodać nowej akcji, która „zapomni” rozbroić Delete.
    /// </summary>
    public void NotifyKeyHandled(DeskKey key, DeskAction action)
    {
        if (action == DeskAction.CancelRemove) return; // Escape ma własny komunikat

        // Każde z dwóch pytań ma WŁASNĄ odpowiedź, więc i własne wygaszanie. Skutek uboczny jest
        // celowy: Delete w panelu otwierania gasi uzbrojone usunięcie pieśni (i odwrotnie) —
        // dwa pytania nigdy nie są uzbrojone naraz.
        if (_pendingRemoveIndex != null
            && DeleteConfirmation.Cancels(key, action, DeskAction.RemoveSetlistSong))
            ClearPendingRemove();

        if (_pendingDeleteSetlistId != null
            && DeleteConfirmation.Cancels(key, action, DeskAction.DeleteSavedSetlist))
            ClearPendingDeleteSetlist();
    }

    private void ClearPendingRemove()
    {
        _pendingRemoveIndex = null;
        _pendingRemoveAt = null;
    }

    /// <summary>
    /// Ctrl+Shift+PageUp / PageDown — pieśń spod KURSORA ZESTAWU o jedno miejsce. Bez zawijania.
    /// </summary>
    public void MoveCurrentUp() => MoveCurrent(-1);
    public void MoveCurrentDown() => MoveCurrent(+1);

    private void MoveCurrent(int delta)
    {
        int index = SetlistCursorIndex;
        if (index < 0) { Announce(_setlistTexts.NoCurrent); return; }

        int target = index + delta;
        if (target < 0) { Announce(_setlistTexts.AtFirst); return; }
        if (target >= _display.SetlistItems.Count) { Announce(_setlistTexts.AtLast); return; }

        var item = _display.SetlistItems[index];
        _display.SetlistItems.Move(index, target);
        Renumber();
        // Kursor jedzie RAZEM z pieśnią — operator, który przenosi pieśń o trzy miejsca, robi to
        // trzema naciśnięciami pod rząd, a kursor zostawiony na starym miejscu przenosiłby
        // za drugim razem zupełnie inną pieśń.
        SetSetlistCursor(target);
        // Przeniesienie NIE zmienia obrazu wiernym: bieżąca pozycja to wciąż ten sam obiekt,
        // slajdy nie są przebudowywane. Zmienia się wyłącznie miejsce w kolejce.
        Announce(AccessibleSetlist.DescribeMoved(
            ItemTitle(item), target, _display.SetlistItems.Count, _setlistTexts));
        UpdateSetlistCaption();
    }

    private void Renumber()
    {
        for (int i = 0; i < _display.SetlistItems.Count; i++)
            _display.SetlistItems[i].Position = i + 1;
    }

    // ── Zapis zestawu (Ctrl+S) ───────────────────────────────────────────────

    /// <summary>
    /// Otwiera panel zapisu z nazwą WCZYTANEGO zestawu w polu — Enter zapisuje wtedy w miejscu,
    /// a wpisanie innej nazwy robi kopię. Bez tego każdy zapis zakładałby nowy rekord i baza
    /// zarastałaby kopiami tego samego zestawu, czego niewidomy operator nie miałby jak zauważyć.
    /// </summary>
    public void OpenSavePanel()
    {
        SetlistNameInput = _display.LoadedSetlistName is { Length: > 0 } loaded
            ? loaded
            : _display.SetlistName ?? string.Empty;
        IsSavePanelOpen = true;
        Announce(_setlistTexts.SaveOpened);
    }

    public void CloseSavePanel()
    {
        if (!IsSavePanelOpen) return;
        IsSavePanelOpen = false;
        Announce(_setlistTexts.SaveClosed);
    }

    /// <summary>Enter w polu nazwy. Zwraca true, gdy panel można zamknąć (zapis się udał).</summary>
    public async Task<bool> ConfirmSaveAsync()
    {
        if (_display.SetlistItems.Count == 0) { Announce(_setlistTexts.SaveNothing); return false; }

        var decision = AccessibleSetlist.DecideSave(
            _display.LoadedSetlistId, _display.LoadedSetlistName, SetlistNameInput);
        if (decision == SaveDecision.Refuse) { Announce(_setlistTexts.SaveEmptyName); return false; }

        var name = SetlistNameInput.Trim();
        bool ok = await _display.SaveSetlistNoPromptAsync(name, decision == SaveDecision.Overwrite);
        if (!ok) { Announce(_setlistTexts.SaveEmptyName); return false; }

        IsSavePanelOpen = false;
        UpdateSetlistCaption();
        Announce(AccessibleSetlist.DescribeSaved(name, decision == SaveDecision.CreateNew, _setlistTexts));
        return true;
    }

    // ── Otwieranie zestawu (Ctrl+O) ──────────────────────────────────────────

    /// <summary>Wypełnia listę zapisanych zestawów (najświeższe pierwsze) i otwiera panel.</summary>
    public async Task OpenPickerAsync()
    {
        var summaries = await _db.GetSetlistSummariesAsync();
        _allSetlists.Clear();
        foreach (var s in summaries)
            _allSetlists.Add(new AccessibleSetlistEntry
            {
                Id = s.Id,
                Name = s.Name,
                SongCount = s.SongCount,
                Spoken = AccessibleSetlist.DescribeEntry(s.Name, s.SongCount, _setlistTexts),
            });

        // Filtr startuje pusty przy KAŻDYM otwarciu panelu — filtr pamiętany z poprzedniego razu
        // ukrywałby zestawy, a niewidomy operator nie miałby jak zauważyć, że lista jest przycięta.
        PickerFilter = string.Empty;
        ApplyPickerFilter(announce: false);

        PickerCaption = _allSetlists.Count == 0
            ? _setlistTexts.NoSetlists
            : _setlistTexts.OpenOpened;
        ClearPendingDeleteSetlist();
        IsPickerOpen = true;
        Announce(PickerCaption);
    }

    /// <summary>
    /// Przebudowa listy po zmianie filtra. Porównanie idzie <see cref="DatabaseService.NameContains"/>
    /// — po stronie klienta i z polskimi znakami złożonymi do ASCII, więc „spiew” trafia w „Śpiew”.
    /// (Filtrowanie w zapytaniu SQLite by tu skłamało: `lower()` zna wyłącznie ASCII.)
    /// </summary>
    private void ApplyPickerFilter(bool announce = true)
    {
        var query = PickerFilter ?? string.Empty;
        SavedSetlists.Clear();
        foreach (var e in _allSetlists)
            if (DatabaseService.NameContains(e.Name, query))
                SavedSetlists.Add(e);

        _pickerCursor.Reset(SavedSetlists.Count);
        PickerIndex = _pickerCursor.Index;
        // Zmiana listy unieważnia pytanie o usunięcie: dotyczyło konkretnego zestawu, a po
        // przefiltrowaniu może go już na liście nie być.
        ClearPendingDeleteSetlist();
        if (announce) Announce(AccessibleSetlist.DescribeFilterCount(SavedSetlists.Count, _setlistTexts));
    }

    partial void OnPickerFilterChanged(string value)
    {
        if (IsPickerOpen) ApplyPickerFilter();
    }

    /// <summary>
    /// Delete na liście zapisanych zestawów: pierwszy raz PYTA, drugi usuwa zestaw Z BAZY.
    /// To operacja niszcząca CUDZĄ PRACĘ (przygotowaną mszę), więc pytanie wymienia nazwę,
    /// liczbę pieśni i ostrzega, gdy kasowany zestaw jest tym wczytanym na pulpicie.
    /// Uzbrojenie jest WŁASNE — zob. <see cref="_pendingDeleteSetlistId"/>.
    /// </summary>
    public async Task DeleteSelectedSetlistAsync()
    {
        if (!_pickerCursor.HasPosition) { ClearPendingDeleteSetlist(); Announce(_setlistTexts.NoSelection); return; }
        var entry = SavedSetlists[_pickerCursor.Index];
        bool isLoaded = _display.LoadedSetlistId == entry.Id;

        if (!DeleteConfirmation.Confirms(_pendingDeleteSetlistId, _pendingDeleteSetlistAt, entry.Id, Clock()))
        {
            _pendingDeleteSetlistId = entry.Id;
            _pendingDeleteSetlistAt = Clock();
            Announce(AccessibleSetlist.DescribeDeleteConfirm(entry.Name, entry.SongCount, isLoaded, _setlistTexts));
            return;
        }

        ClearPendingDeleteSetlist();
        // TA SAMA droga co przycisk usuwania w popupie wyszukiwarki u widzącego operatora.
        bool ok = await _db.DeleteSetlistAsync(entry.Id);
        if (!ok) { Announce(_setlistTexts.DeleteFailed); return; }

        // Zestaw wczytany na pulpicie: pieśni ZOSTAJĄ (w środku mszy wyczyszczenie ich byłoby
        // nieporównanie gorsze), znika samo powiązanie z rekordem, więc kolejny Ctrl+S założy nowy.
        if (isLoaded) _display.DetachLoadedSetlist();

        _allSetlists.RemoveAll(e => e.Id == entry.Id);
        ApplyPickerFilter(announce: false);
        if (_allSetlists.Count == 0) PickerCaption = _setlistTexts.NoSetlists;
        Announce(AccessibleSetlist.DescribeDeleted(entry.Name, _allSetlists.Count, isLoaded, _setlistTexts));
    }

    private void ClearPendingDeleteSetlist()
    {
        _pendingDeleteSetlistId = null;
        _pendingDeleteSetlistAt = null;
    }

    partial void OnIsSavePanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSetlistPanelOpen));
        OnPropertyChanged(nameof(IsAnyPanelOpen));
    }

    partial void OnIsPickerOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSetlistPanelOpen));
        OnPropertyChanged(nameof(IsAnyPanelOpen));
    }

    partial void OnIsLicensePanelOpenChanged(bool value) => OnPropertyChanged(nameof(IsAnyPanelOpen));

    public void ClosePicker()
    {
        if (!IsPickerOpen) return;
        IsPickerOpen = false;
        Announce(_setlistTexts.OpenClosed);
    }

    public void PickerUp() { if (_pickerCursor.MoveUp()) PickerIndex = _pickerCursor.Index; }
    public void PickerDown() { if (_pickerCursor.MoveDown()) PickerIndex = _pickerCursor.Index; }

    partial void OnPickerIndexChanged(int value)
    {
        if (value != _pickerCursor.Index) _pickerCursor.MoveTo(value);
    }

    /// <summary>Powtórzenie odczytu wybranego zestawu (klawisz R w panelu otwierania).</summary>
    public void RepeatPickerEntry()
    {
        if (!_pickerCursor.HasPosition) { Announce(_setlistTexts.NoSelection); return; }
        Announce(SavedSetlists[_pickerCursor.Index].Spoken);
    }

    /// <summary>
    /// Enter na liście: wczytuje zestaw TĄ SAMĄ drogą co przycisk „Otwórz" u widzącego operatora
    /// (<c>LoadPinnedSetlistCommand</c>) — z zapisem `last_setlist_id` i ustawieniem pierwszej
    /// pieśni jako bieżącej. Zwraca true, gdy panel można zamknąć.
    /// </summary>
    public async Task<bool> LoadPickedSetlistAsync()
    {
        if (!_pickerCursor.HasPosition) { Announce(_setlistTexts.NoSelection); return false; }
        var entry = SavedSetlists[_pickerCursor.Index];
        var setlist = await _db.GetSetlistAsync(entry.Id);
        if (setlist == null) { Announce(_setlistTexts.NoSelection); return false; }

        ClearPendingRemove();
        ClearPendingDeleteSetlist();
        await _display.LoadPinnedSetlistCommand.ExecuteAsync(setlist);
        SetSetlistCursor(CurrentSetlistIndex);
        IsPickerOpen = false;
        UpdateSetlistCaption();
        Announce(AccessibleSetlist.DescribeLoaded(
            setlist.Name, _display.SetlistItems.Count, _setlistTexts));
        return true;
    }

    private void UpdateSetlistCaption() => SetlistCaption = LocalizationManager.Format(
        "Acc.SetlistLoaded", _display.SetlistName, _display.SetlistItems.Count);

    // ── Ogłoszenia na żądanie ────────────────────────────────────────────────

    /// <summary>Klawisz stanu: co dokładnie widzą teraz wierni.</summary>
    public void AnnounceStatus() => Announce(DescribeProjection());

    public void AnnounceSetlist()
    {
        var name = _display.SetlistName;
        if (string.IsNullOrWhiteSpace(name) && _display.SetlistItems.Count == 0)
        {
            Announce(LocalizationManager.Get("Acc.NoSetlist"));
            return;
        }
        UpdateSetlistCaption();
        Announce(SetlistCaption);
    }

    /// <summary>
    /// Powielanie ekranu. Windows w trybie „Duplikuj" pokazuje zwykle JEDEN monitor, więc
    /// jeden ekran = ostrzeżenie (albo rzutnik powiela pulpit, albo w ogóle go nie ma —
    /// obie sytuacje wymagają reakcji, a niewidomy nie ma jak ich zobaczyć).
    /// </summary>
    public void AnnounceScreens()
    {
        var setup = ScreenTopology.Evaluate(CurrentScreens());
        var key = setup switch
        {
            ScreenSetup.Extended => "Acc.ScreensExtended",
            ScreenSetup.Mirrored => "Acc.ScreensMirrored",
            _ => "Acc.ScreensSingle",
        };
        Announce(LocalizationManager.Get(key));
    }

    private static IReadOnlyList<ScreenBox> CurrentScreens()
        => WpfScreenHelper.Screen.AllScreens
            .Select(s => new ScreenBox(s.WpfBounds.X, s.WpfBounds.Y, s.WpfBounds.Width, s.WpfBounds.Height))
            .ToList();

    /// <summary>
    /// F1 — cała mapa klawiszy, a na końcu stan licencji. Doklejenie nazwiska TUTAJ, a nie do
    /// klawisza stanu (S), jest świadome: stan operator naciska w trakcie mszy kilkadziesiąt razy
    /// i słucha go w pośpiechu, a pomoc otwiera się wtedy, gdy szuka informacji o programie.
    /// </summary>
    public void AnnounceHelp() => Announce(LocalizationManager.Get("Acc.Help") + " " + LicenseCaption);

    // ── Reakcja na zmiany wspólnego rdzenia ──────────────────────────────────

    private void OnDisplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DisplayViewModel.SlideList):
                RebuildReadingList();
                break;
            case nameof(DisplayViewModel.CurrentSlideIndex):
                AnnounceProjectionChange();
                break;
            case nameof(DisplayViewModel.ScreenBlanked):
                Announce(_display.ScreenBlanked ? _texts.Blanked : _texts.Restored);
                UpdateProjectionCaption();
                break;
            case nameof(DisplayViewModel.SelectedSetlistItem):
                // Kursor zestawu PODĄŻA za bieżącą pieśnią. Wybór jest świadomy: zmiana pieśni
                // (Ctrl+Page Up/Down, wczytanie zestawu, dołożenie pieśni z wyszukiwarki) to
                // moment, w którym operator „jest” gdzie indziej niż przed chwilą — kursor
                // zostawiony w tyle znaczyłby, że Delete kasuje pieśń sprzed dwóch zwrotek.
                // Ruch samym Altem kursora NIE zabiera nigdzie indziej: zostaje tam, gdzie go
                // postawiono, aż do następnej zmiany bieżącej pieśni.
                SetSetlistCursor(CurrentSetlistIndex);
                break;
        }
    }

    /// <summary>
    /// Nowa pieśń / przebudowa slajdów: lista czytania idzie za treścią, a kursor czytania wraca
    /// tam, gdzie stoi projekcja (operator zaczyna czytać od tego, co widzą wierni).
    /// </summary>
    private void RebuildReadingList()
    {
        var slides = _display.SlideList;
        ReadingSlides.Clear();
        for (int i = 0; i < slides.Count; i++)
        {
            var s = slides[i];
            ReadingSlides.Add(new AccessibleSlideItem
            {
                Spoken = ProjectionAnnouncement.DescribeReading(
                    i, slides.Count, SlideKind.FromSlide(s), s.Label,
                    s.HasPreviewOnlyContent ? s.OperatorText : s.Text, _texts),
            });
        }

        int start = _display.CurrentSlideIndex >= 0 ? _display.CurrentSlideIndex : 0;
        _cursor.Reset(ReadingSlides.Count, start);
        ReadingIndex = _cursor.Index;
        SongCaption = _display.SelectedSong?.Title ?? string.Empty;
    }

    private void AnnounceProjectionChange()
    {
        if (_display.CurrentSlideIndex < 0) return; // stan przejściowy przy przebudowie slajdów
        UpdateProjectionCaption();
        Announce(DescribeProjection());
    }

    /// <summary>
    /// Prawdziwy stan rzutnika. Nie wystarczy <c>CurrentSlideIndex</c>: w trybie psalm, przy
    /// zwrotce prywatnej i przy slajdzie „tylko podgląd" projektor TRZYMA poprzedni slajd, a nowy
    /// widzi wyłącznie operator. Ogłoszenie „wierni widzą" musi mówić o tym, co naprawdę wisi
    /// na ekranie — inaczej kłamałoby dokładnie w tych trzech miejscach, gdzie to najgroźniejsze.
    /// </summary>
    private string DescribeProjection()
    {
        var slides = _display.SlideList;
        var projected = _display.ProjectedSlide;
        int shownIndex = projected != null ? slides.IndexOf(projected) : -1;

        string state = ProjectionAnnouncement.DescribeProjection(
            shownIndex, slides.Count,
            projected != null ? SlideKind.FromSlide(projected) : null,
            projected?.Label, _display.SelectedSong?.Title,
            _display.ScreenBlanked, _texts);

        // Wygaszony ekran: operator i tak chce wiedzieć, co pójdzie po przywróceniu.
        if (_display.ScreenBlanked && _display.CurrentSlideIndex >= 0 && _display.CurrentSlideIndex < slides.Count)
        {
            var pending = slides[_display.CurrentSlideIndex];
            var head = ProjectionAnnouncement.SlideHeader(
                _display.CurrentSlideIndex, slides.Count, SlideKind.FromSlide(pending), pending.Label, _texts);
            var title = _display.SelectedSong?.Title;
            if (!string.IsNullOrWhiteSpace(title)) head = $"{head}, {title.Trim()}";
            return $"{state}. {LocalizationManager.Format("Acc.Prepared", head)}";
        }

        // Slajd, który widzi tylko operator (psalm / zwrotka prywatna / „tylko podgląd").
        if (!_display.ScreenBlanked && _display.CurrentSlideIndex >= 0
            && _display.CurrentSlideIndex < slides.Count && _display.CurrentSlideIndex != shownIndex)
        {
            var op = slides[_display.CurrentSlideIndex];
            var head = ProjectionAnnouncement.SlideHeader(
                _display.CurrentSlideIndex, slides.Count, SlideKind.FromSlide(op), op.Label, _texts);
            return $"{state}. {LocalizationManager.Format("Acc.OperatorOnly", head)}";
        }

        return state;
    }

    private void UpdateProjectionCaption() => ProjectionCaption = DescribeProjection();

    /// <summary>
    /// Ile razy cokolwiek ogłoszono. Służy do jednego: rozstrzygnięcia „czy ta akcja SAMA coś
    /// powiedziała” (zob. <see cref="ShowSetlistCursorSong"/>) — bez tego pulpit albo milczałby
    /// po naciśnięciu klawisza, albo mówił dwa razy.
    /// </summary>
    private int _announceCount;

    private void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _announceCount++;
        LastAnnouncement = text;
        Announced?.Invoke(text);
    }
}
