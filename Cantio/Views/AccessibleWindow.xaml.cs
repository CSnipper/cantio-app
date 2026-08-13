using Cantio.Services;
using Cantio.ViewModels;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cantio.Views;

/// <summary>
/// Pulpit organisty NIEWIDOMEGO. Osobna powłoka nad tym samym rdzeniem co pulpit widzącego —
/// ta sama baza, ten sam <see cref="DisplayViewModel"/>, to samo okno projekcji. <c>MainWindow</c>
/// nie jest w to w ogóle zaangażowane (i nie został zmieniony).
///
/// Cała obsługa idzie z klawiatury i jest przechwytywana tutaj, w <see cref="OnPreviewKeyDown"/>:
/// lista slajdów sama zjadłaby strzałki, Page Up/Down, Home i End, więc decyzja o każdym z tych
/// klawiszy musi zapaść w JEDNYM miejscu — inaczej „strzałka nie rusza projekcji" byłoby prawdą
/// tylko dopóki fokus stoi tam, gdzie autor zakładał.
///
/// Mapa klawiszy:
///   ↑ / ↓          kursor CZYTANIA po LINIJKACH (frazach) slajdu — w obrębie slajdu rzutnik
///                  bez zmian; na krańcu slajdu przeskok o jeden slajd RAZEM z rzutnikiem
///   Home / End     kursor czytania na pierwszą / ostatnią linijkę BIEŻĄCEGO slajdu
///   R              powtórz odczyt czytanej linijki (przez live region)
///   PageDown / PageUp        kursor PROJEKCJI — to widzą wierni (czytanie staje na 1. linijce)
///   Ctrl+PageDown / Ctrl+PageUp   następna / poprzednia pieśń zestawu (też widoczne dla wiernych)
///   Enter          wyświetl wiernym slajd spod kursora czytania
///   B              wygaś / przywróć ekran
///   S              powiedz, co widzą wierni
///   F2             sprawdź ekrany (powielanie obrazu)
///   F1             pomoc — cała mapa klawiszy
///   Ctrl+F / F3    wyszukiwarka pieśni (etap 2)
///   L              przeczytaj cały zestaw po kolei
///   Alt+↑ / Alt+↓  kursor ZESTAWU — po pieśniach zestawu, BEZ ruszania rzutnika i kursora czytania
///   Alt+Home / Alt+End   kursor zestawu na pierwszą / ostatnią pieśń zestawu
///   Alt+Enter      uczyń pieśń spod kursora zestawu bieżącą i wyświetl ją wiernym
///   Delete         usuń pieśń SPOD KURSORA ZESTAWU (dwa razy — pierwszy raz pyta)
///   Ctrl+Shift+PageUp / PageDown   przenieś pieśń spod kursora zestawu
///   Ctrl+S         zapisz zestaw (pole nazwy)
///   Ctrl+O         otwórz zapisany zestaw (pole filtra + lista)
///   F9             klucz licencyjny (wklej Ctrl+V, Enter zatwierdza)
///
/// W trybie wyszukiwania:
///   pole tekstowe  wpisz numer albo tytuł, Enter szuka i przenosi na wyniki
///   ↑ / ↓          po WYNIKACH (czytnik czyta „numer, tytuł")
///   Enter          dodaj na koniec zestawu (wyszukiwarka zostaje otwarta)
///   Ctrl+Enter     dodaj i od razu wyświetl wiernym
///   Escape         zamknij wyszukiwarkę, fokus wraca na listę slajdów
///
/// W panelu zapisu / otwierania zestawu:
///   Enter          zapisz pod wpisaną nazwą / wczytaj wybrany zestaw
///   pole filtra    wpisz fragment nazwy — lista przycina się na bieżąco („Pasuje 5 zestawów")
///   ↓ / Enter      z pola filtra na listę zestawów
///   ↑ / ↓          po liście zapisanych zestawów (czytnik czyta „nazwa, pieśni: N")
///   Delete         usuń ZAPISANY zestaw z bazy (dwa razy — pierwszy raz pyta)
///   Escape         zamknij panel, fokus wraca na listę slajdów
/// </summary>
public partial class AccessibleWindow : Window
{
    private readonly AccessibleShellViewModel _vm;

    public AccessibleWindow(DatabaseService db)
    {
        InitializeComponent();

        var display = new DisplayViewModel(db, new ProjectionViewModel(), new ShortcutService());
        _vm = new AccessibleShellViewModel(db, display);
        _vm.Announced += Announce;
        DataContext = _vm;

        // Tytuł okna i nazwa listy NIE mogą zostać puste — czytnik ekranu czyta tytuł przy każdym
        // przełączeniu okna, a pusty tytuł zostawia niewidomego bez informacji, gdzie jest.
        // DynamicResource sam z siebie daje pustkę, gdy klucza zabraknie w słowniku języka.
        Title = AccessibleShellViewModel.Text("Acc.WindowTitle", "Cantio — pulpit organisty niewidomego");
        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetName(ReadingList, AccessibleShellViewModel.Text("Acc.ListName",
            "Linijki bieżącej pieśni. Strzałki góra i dół czytają po jednej linijce; na końcu slajdu "
            + "przechodzą do następnego i wyświetlają go wiernym. Page Down przerzuca cały slajd."));
        AutomationProperties.SetName(SearchBox, AccessibleShellViewModel.Text("Acc.SearchBoxName",
            "Szukaj pieśni. Wpisz numer albo tytuł i naciśnij Enter."));
        AutomationProperties.SetName(SearchList, AccessibleShellViewModel.Text("Acc.SearchListName",
            "Wyniki wyszukiwania. Strzałki góra i dół wybierają pieśń, Enter dodaje ją na koniec zestawu."));
        AutomationProperties.SetName(SaveBox, AccessibleShellViewModel.Text("Acc.SaveBoxName",
            "Nazwa zestawu. Naciśnij Enter, aby zapisać, Escape aby anulować."));
        AutomationProperties.SetName(PickerList, AccessibleShellViewModel.Text("Acc.PickerListName",
            "Zapisane zestawy. Strzałki góra i dół wybierają zestaw, Enter go wczytuje, Delete usuwa."));
        AutomationProperties.SetName(FilterBox, AccessibleShellViewModel.Text("Acc.FilterBoxName",
            "Filtr zestawów. Wpisz fragment nazwy, strzałką w dół przejdź na listę."));
        AutomationProperties.SetName(LicenseBox, AccessibleShellViewModel.Text("Acc.LicenseBoxName",
            "Klucz licencyjny. Wklej klucz skrótem Ctrl plus V i naciśnij Enter."));

        Loaded += (_, _) => FocusReadingList();
    }

    /// <summary>Uruchomienie po pokazaniu okna (projekcja + ostatni zestaw + ogłoszenia startowe).</summary>
    public Task StartAsync() => _vm.InitializeAsync();

    /// <summary>
    /// Wypchnięcie komunikatu do czytnika ekranu. NIE MA tu żadnej syntezy mowy (System.Speech
    /// ani własnego głosu): tekst trafia do live region, a mówi go NVDA — głosem, tempem
    /// i językiem, które użytkownik sam sobie ustawił.
    /// </summary>
    private void Announce(string text)
    {
        // Tekst ustawia binding (LastAnnouncement), ale zdarzenie i tak podnosimy sami:
        // przy POWTÓRZONYM komunikacie (np. dwa razy klawisz stanu) właściwość się nie zmienia,
        // a operator musi usłyszeć odpowiedź na każde naciśnięcie.
        LiveRegion.Text = text;
        var peer = UIElementAutomationPeer.FromElement(LiveRegion)
                   ?? UIElementAutomationPeer.CreatePeerForElement(LiveRegion);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void FocusReadingList()
    {
        ReadingList.Focus();
        FocusSelectedItem();
    }

    /// <summary>
    /// Przenosi fokus na zaznaczoną pozycję listy — to ONO każe czytnikowi przeczytać slajd
    /// (zdarzenie zmiany fokusu UIA). Samo przestawienie <c>SelectedIndex</c> bywa nieme.
    /// </summary>
    private void FocusSelectedItem()
    {
        if (ReadingList.SelectedIndex < 0) return;
        ReadingList.ScrollIntoView(ReadingList.SelectedItem);
        ReadingList.UpdateLayout();
        if (ReadingList.ItemContainerGenerator.ContainerFromIndex(ReadingList.SelectedIndex)
            is ListBoxItem item)
            item.Focus();
    }

    // ── Wyszukiwarka ─────────────────────────────────────────────────────────

    /// <summary>
    /// Czy klawisze pisze się teraz W POLU TEKSTOWYM. To JEDYNY warunek rozstrzygający kolizję
    /// gołych liter (R, B, S) z pisaniem: dopóki fokus jest w polu, litery należą do pola.
    /// Sprawdzamy realny fokus klawiatury, a nie flagę trybu wyszukiwania — panel bywa otwarty,
    /// gdy operator stoi już na liście wyników, i tam skróty mają znowu działać.
    /// </summary>
    private bool IsTypingInSearchBox() => Keyboard.FocusedElement is TextBox;

    /// <summary>Czy fokus stoi w polu NAZWY zapisywanego zestawu (drugie pole tekstowe pulpitu).</summary>
    private bool IsTypingInSaveBox() => ReferenceEquals(Keyboard.FocusedElement, SaveBox);

    /// <summary>Czy fokus stoi w polu FILTRA zapisanych zestawów (trzecie pole tekstowe pulpitu).</summary>
    private bool IsTypingInFilterBox() => ReferenceEquals(Keyboard.FocusedElement, FilterBox);

    /// <summary>Czy fokus stoi w polu KLUCZA LICENCYJNEGO (czwarte pole tekstowe pulpitu).</summary>
    private bool IsTypingInLicenseBox() => ReferenceEquals(Keyboard.FocusedElement, LicenseBox);

    /// <summary>Czy fokus stoi na liście WYNIKÓW (a nie na liście slajdów).</summary>
    private bool IsOnSearchResults()
        => _vm.IsSearchOpen && !IsTypingInSearchBox() && IsFocusIn(SearchList);

    /// <summary>Czy fokus stoi na liście zapisanych zestawów (panel Ctrl+O).</summary>
    private bool IsOnPicker()
        => _vm.IsPickerOpen && !IsTypingInSearchBox() && IsFocusIn(PickerList);

    private static bool IsFocusIn(DependencyObject root)
        => ReferenceEquals(Keyboard.FocusedElement, root)
           || (Keyboard.FocusedElement is DependencyObject d && IsInside(d, root));

    private static bool IsInside(DependencyObject node, DependencyObject root)
    {
        for (var p = node; p != null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    private void OpenSearch()
    {
        _vm.OpenSearch();
        // Fokus musi wylądować W POLU — inaczej pierwsza wpisana litera trafiłaby w skrót.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseSearch()
    {
        _vm.CloseSearch();
        FocusReadingList();
    }

    private async void RunSearchAsync()
    {
        if (await _vm.RunSearchAsync()) FocusSearchItem();
        // Brak wyników: fokus zostaje w polu, żeby dało się poprawić zapytanie bez szukania drogi.
    }

    private void FocusSearchItem() => FocusListItem(SearchList);

    private static void FocusListItem(ListBox list)
    {
        if (list.SelectedIndex < 0) return;
        list.ScrollIntoView(list.SelectedItem);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) is ListBoxItem item)
            item.Focus();
    }

    // ── Panele zestawu (Ctrl+S zapis, Ctrl+O otwieranie) ─────────────────────

    private void OpenSavePanel()
    {
        _vm.OpenSavePanel();
        // Jak przy wyszukiwarce: fokus MUSI wylądować w polu, inaczej pierwsza litera nazwy
        // poleciałaby w skrót (L przeczytałoby zestaw, B wygasiło ekran).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SaveBox.Focus();
            SaveBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void ConfirmSaveAsync()
    {
        if (await _vm.ConfirmSaveAsync()) FocusReadingList();
        // Nieudany zapis (pusta nazwa, pusty zestaw): fokus zostaje w polu, żeby dało się poprawić.
    }

    private async void OpenPickerAsync()
    {
        await _vm.OpenPickerAsync();
        // Fokus ląduje w POLU FILTRA, nie na liście: przy kilkuset zestawach filtr jest jedyną
        // realną drogą do zestawu, a lista jest o jedną strzałkę w dół stamtąd.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void DeleteSelectedSetlistAsync()
    {
        await _vm.DeleteSelectedSetlistAsync();
        FocusListItem(PickerList);
    }

    private async void LoadPickedSetlistAsync()
    {
        if (await _vm.LoadPickedSetlistAsync()) FocusReadingList();
    }

    /// <summary>Escape / udany wybór: zamyka otwarty panel i oddaje fokus liście slajdów.</summary>
    private void ClosePanel()
    {
        if (_vm.IsSavePanelOpen) _vm.CloseSavePanel();
        else if (_vm.IsPickerOpen) _vm.ClosePicker();
        else if (_vm.IsLicensePanelOpen) _vm.CloseLicensePanel();
        FocusReadingList();
    }

    // ── Licencja (F9) ────────────────────────────────────────────────────────

    private void OpenLicensePanel()
    {
        _vm.OpenLicensePanel();
        // Jak przy pozostałych polach: fokus MUSI wylądować w polu, inaczej Ctrl+V poszłoby
        // w próżnię, a operator nie miałby jak zauważyć, że nic się nie wkleiło.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LicenseBox.Focus();
            LicenseBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void ConfirmLicenseAsync()
    {
        if (await _vm.ConfirmLicenseAsync()) FocusReadingList();
        // Klucz odrzucony: fokus zostaje w polu, żeby dało się wkleić poprawny bez szukania drogi.
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt+F4 zostaje systemowy — droga wyjścia musi być zawsze.
        if (e.Key == Key.System && e.SystemKey == Key.F4)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        bool ctrl = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Alt);

        // PUŁAPKA WPF: z wciśniętym Altem `e.Key` to Key.System, a prawdziwy klawisz siedzi
        // w `e.SystemKey`. Bez tego przełożenia Alt+↓ byłby nie do odróżnienia od samego Alt
        // i cały kursor zestawu byłby martwy.
        var rawKey = e.Key == Key.System ? e.SystemKey : e.Key;
        var key = ToDeskKey(rawKey);

        // O tym, CO robi klawisz, decyduje rdzeń (AccessibleKeys) — tu zostaje samo wykonanie.
        var action = AccessibleKeys.Route(key, ctrl, shift, alt, new AccessibleKeys.DeskState(
            CurrentFocus(), _vm.IsSearchOpen,
            HasResults: SearchList.Items.Count > 0,
            PanelOpen: _vm.IsAnyPanelOpen));

        // Uzbrojone potwierdzenie usunięcia gaśnie po KAŻDYM innym klawiszu — jedno wywołanie
        // dla wszystkich akcji, żeby nowa akcja nie mogła o tym „zapomnieć".
        _vm.NotifyKeyHandled(key, action);

        switch (action)
        {
            case DeskAction.OpenSearch:  OpenSearch(); break;
            case DeskAction.CloseSearch: CloseSearch(); break;
            case DeskAction.RunSearch:   RunSearchAsync(); break;
            case DeskAction.FocusResults: FocusSearchItem(); break;

            case DeskAction.AddToSetlist: _vm.AddSelectedToSetlist(); FocusSearchItem(); break;
            case DeskAction.AddAndShow:   _vm.AddSelectedToSetlist(alsoShow: true); FocusSearchItem(); break;
            case DeskAction.SearchUp:     _vm.SearchUp();   FocusSearchItem(); break;
            case DeskAction.SearchDown:   _vm.SearchDown(); FocusSearchItem(); break;
            case DeskAction.RepeatSearchResult: _vm.RepeatSearchResult(); break;

            case DeskAction.ReadUp:    _vm.ReadUp();    FocusSelectedItem(); break;
            case DeskAction.ReadDown:  _vm.ReadDown();  FocusSelectedItem(); break;
            case DeskAction.ReadStart: _vm.ReadStart(); FocusSelectedItem(); break;
            case DeskAction.ReadEnd:   _vm.ReadEnd();   FocusSelectedItem(); break;
            case DeskAction.RepeatReading: _vm.RepeatReading(); break;
            case DeskAction.ProjectReadingSlide: _vm.ProjectReadingSlide(); FocusSelectedItem(); break;

            case DeskAction.ProjectNextSlide: _vm.ProjectNextSlide(); break;
            case DeskAction.ProjectPrevSlide: _vm.ProjectPrevSlide(); break;
            case DeskAction.NextSong: _vm.NextSong(); break;
            case DeskAction.PrevSong: _vm.PrevSong(); break;

            case DeskAction.ToggleBlank:     _vm.ToggleBlank(); break;
            case DeskAction.AnnounceStatus:  _vm.AnnounceStatus(); break;
            case DeskAction.AnnounceScreens: _vm.AnnounceScreens(); break;
            case DeskAction.AnnounceHelp:    _vm.AnnounceHelp(); break;

            // ── zarządzanie zestawem ─────────────────────────────────────────
            case DeskAction.ReadSetlist:       _vm.ReadSetlist(); break;
            case DeskAction.RemoveSetlistSong: _vm.RemoveCurrentFromSetlist(); FocusSelectedItem(); break;
            case DeskAction.CancelRemove:      _vm.CancelPendingRemove(); break;
            case DeskAction.MoveSongUp:        _vm.MoveCurrentUp(); break;
            case DeskAction.MoveSongDown:      _vm.MoveCurrentDown(); break;

            // ── kursor ZESTAWU (Alt) — fokus NIE rusza się nigdzie, mówi live region ──
            case DeskAction.SetlistCursorUp:    _vm.SetlistCursorUp(); break;
            case DeskAction.SetlistCursorDown:  _vm.SetlistCursorDown(); break;
            case DeskAction.SetlistCursorFirst: _vm.SetlistCursorFirst(); break;
            case DeskAction.SetlistCursorLast:  _vm.SetlistCursorLast(); break;
            case DeskAction.ShowSetlistCursorSong: _vm.ShowSetlistCursorSong(); FocusSelectedItem(); break;

            case DeskAction.OpenSavePanel:     OpenSavePanel(); break;
            case DeskAction.ConfirmSave:       ConfirmSaveAsync(); break;
            case DeskAction.OpenSetlistPicker: OpenPickerAsync(); break;
            case DeskAction.PickerUp:          _vm.PickerUp();   FocusListItem(PickerList); break;
            case DeskAction.PickerDown:        _vm.PickerDown(); FocusListItem(PickerList); break;
            case DeskAction.RepeatPickerEntry: _vm.RepeatPickerEntry(); break;
            case DeskAction.LoadPickedSetlist: LoadPickedSetlistAsync(); break;
            case DeskAction.FocusPicker:       FocusListItem(PickerList); break;
            case DeskAction.DeleteSavedSetlist: DeleteSelectedSetlistAsync(); break;
            case DeskAction.ClosePanel:        ClosePanel(); break;

            case DeskAction.OpenLicensePanel:  OpenLicensePanel(); break;
            case DeskAction.ConfirmLicense:    ConfirmLicenseAsync(); break;
        }

        bool handled = action != DeskAction.None;
        e.Handled = handled;
        if (!handled) base.OnPreviewKeyDown(e);
    }

    private DeskFocus CurrentFocus()
        => IsTypingInSaveBox() ? DeskFocus.SaveBox
         : IsTypingInFilterBox() ? DeskFocus.SetlistFilterBox
         // MUSI być sprawdzone PRZED SearchBox: IsTypingInSearchBox() odpowiada „tak" na każde
         // pole tekstowe, więc bez tej linii klucz licencyjny liczyłby się jako zapytanie
         // wyszukiwarki i Enter szukałby pieśni zamiast zarejestrować licencję.
         : IsTypingInLicenseBox() ? DeskFocus.LicenseBox
         : IsTypingInSearchBox() ? DeskFocus.SearchBox
         : IsOnSearchResults() ? DeskFocus.SearchResults
         : IsOnPicker() ? DeskFocus.SetlistPicker
         : DeskFocus.Slides;

    private static DeskKey ToDeskKey(Key key) => key switch
    {
        Key.Up => DeskKey.Up,
        Key.Down => DeskKey.Down,
        Key.Home => DeskKey.Home,
        Key.End => DeskKey.End,
        Key.Enter => DeskKey.Enter,
        Key.Next => DeskKey.PageDown,
        Key.Prior => DeskKey.PageUp,
        Key.Escape => DeskKey.Escape,
        Key.F1 => DeskKey.F1,
        Key.F2 => DeskKey.F2,
        Key.F3 => DeskKey.F3,
        Key.F9 => DeskKey.F9,
        Key.F => DeskKey.F,
        Key.R => DeskKey.R,
        Key.B => DeskKey.B,
        Key.S => DeskKey.S,
        Key.L => DeskKey.L,
        Key.O => DeskKey.O,
        Key.Delete => DeskKey.Delete,
        // Gołe modyfikatory: NIE są akcją operatora. Bez tego samo przytrzymanie Ctrl albo Shift
        // gasiłoby uzbrojone potwierdzenie usunięcia między pierwszym a drugim Delete.
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System => DeskKey.Modifier,
        _ => DeskKey.Other,
    };
}
