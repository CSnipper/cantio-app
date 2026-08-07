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
///   ↑ / ↓          kursor CZYTANIA (prywatny) — czytnik ekranu czyta slajd, rzutnik bez zmian
///   Home / End     kursor czytania na pierwszy / ostatni slajd pieśni
///   R              powtórz odczyt czytanego slajdu (przez live region)
///   PageDown / PageUp        kursor PROJEKCJI — to widzą wierni
///   Ctrl+PageDown / Ctrl+PageUp   następna / poprzednia pieśń zestawu (też widoczne dla wiernych)
///   Enter          wyświetl wiernym slajd spod kursora czytania
///   B              wygaś / przywróć ekran
///   S              powiedz, co widzą wierni
///   F2             sprawdź ekrany (powielanie obrazu)
///   F1             pomoc — cała mapa klawiszy
///   Ctrl+F / F3    wyszukiwarka pieśni (etap 2)
///
/// W trybie wyszukiwania:
///   pole tekstowe  wpisz numer albo tytuł, Enter szuka i przenosi na wyniki
///   ↑ / ↓          po WYNIKACH (czytnik czyta „numer, tytuł")
///   Enter          dodaj na koniec zestawu (wyszukiwarka zostaje otwarta)
///   Ctrl+Enter     dodaj i od razu wyświetl wiernym
///   Escape         zamknij wyszukiwarkę, fokus wraca na listę slajdów
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
            "Slajdy bieżącej pieśni. Strzałki góra i dół czytają slajd, Page Down wyświetla następny wiernym."));
        AutomationProperties.SetName(SearchBox, AccessibleShellViewModel.Text("Acc.SearchBoxName",
            "Szukaj pieśni. Wpisz numer albo tytuł i naciśnij Enter."));
        AutomationProperties.SetName(SearchList, AccessibleShellViewModel.Text("Acc.SearchListName",
            "Wyniki wyszukiwania. Strzałki góra i dół wybierają pieśń, Enter dodaje ją na koniec zestawu."));

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

    /// <summary>Czy fokus stoi na liście WYNIKÓW (a nie na liście slajdów).</summary>
    private bool IsOnSearchResults()
        => _vm.IsSearchOpen && !IsTypingInSearchBox()
           && (ReferenceEquals(Keyboard.FocusedElement, SearchList)
               || (Keyboard.FocusedElement is DependencyObject d && IsInside(d, SearchList)));

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

    private void FocusSearchItem()
    {
        if (SearchList.SelectedIndex < 0) return;
        SearchList.ScrollIntoView(SearchList.SelectedItem);
        SearchList.UpdateLayout();
        if (SearchList.ItemContainerGenerator.ContainerFromIndex(SearchList.SelectedIndex)
            is ListBoxItem item)
            item.Focus();
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
        var focus = CurrentFocus();

        // O tym, CO robi klawisz, decyduje rdzeń (AccessibleKeys) — tu zostaje samo wykonanie.
        var action = AccessibleKeys.Route(
            ToDeskKey(e.Key), ctrl, focus, _vm.IsSearchOpen, hasResults: SearchList.Items.Count > 0);

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
        }

        bool handled = action != DeskAction.None;
        e.Handled = handled;
        if (!handled) base.OnPreviewKeyDown(e);
    }

    private DeskFocus CurrentFocus()
        => IsTypingInSearchBox() ? DeskFocus.SearchBox
         : IsOnSearchResults() ? DeskFocus.SearchResults
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
        Key.F => DeskKey.F,
        Key.R => DeskKey.R,
        Key.B => DeskKey.B,
        Key.S => DeskKey.S,
        _ => DeskKey.Other,
    };
}
