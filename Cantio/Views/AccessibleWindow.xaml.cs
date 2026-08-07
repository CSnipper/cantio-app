using Cantio.Services;
using Cantio.ViewModels;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt+F4 zostaje systemowy — droga wyjścia musi być zawsze.
        if (e.Key == Key.System && e.SystemKey == Key.F4)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        bool ctrl = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control);
        bool handled = true;

        switch (e.Key)
        {
            // ── kursor CZYTANIA — nic nie zmienia wiernym ───────────────────
            case Key.Up:    _vm.ReadUp();    FocusSelectedItem(); break;
            case Key.Down:  _vm.ReadDown();  FocusSelectedItem(); break;
            case Key.Home:  _vm.ReadStart(); FocusSelectedItem(); break;
            case Key.End:   _vm.ReadEnd();   FocusSelectedItem(); break;
            case Key.R:     _vm.RepeatReading(); break;

            // ── kursor PROJEKCJI — to widzą wierni ──────────────────────────
            case Key.Next:  if (ctrl) _vm.NextSong(); else _vm.ProjectNextSlide(); break;
            case Key.Prior: if (ctrl) _vm.PrevSong(); else _vm.ProjectPrevSlide(); break;
            case Key.Enter: _vm.ProjectReadingSlide(); FocusSelectedItem(); break;
            case Key.B:     _vm.ToggleBlank(); break;

            // ── informacja na żądanie ───────────────────────────────────────
            case Key.S:     _vm.AnnounceStatus(); break;
            case Key.F2:    _vm.AnnounceScreens(); break;
            case Key.F1:    _vm.AnnounceHelp(); break;

            default: handled = false; break;
        }

        e.Handled = handled;
        if (!handled) base.OnPreviewKeyDown(e);
    }
}
