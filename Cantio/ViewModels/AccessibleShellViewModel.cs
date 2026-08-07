using Cantio.Helpers;
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
    private readonly AnnouncementText _texts;

    /// <summary>Komunikat do live region — okno wypycha go do czytnika ekranu.</summary>
    public event Action<string>? Announced;

    public DisplayViewModel Display => _display;

    public ObservableCollection<AccessibleSlideItem> ReadingSlides { get; } = [];

    /// <summary>Pozycja kursora CZYTANIA (dwukierunkowo z listą w oknie). Nie rusza projekcji.</summary>
    [ObservableProperty] private int _readingIndex = -1;

    /// <summary>Podpisy dla widzącego pomocnika (i dla zrzutu ekranu w zgłoszeniu).</summary>
    [ObservableProperty] private string _setlistCaption = string.Empty;
    [ObservableProperty] private string _songCaption = string.Empty;
    [ObservableProperty] private string _projectionCaption = string.Empty;
    [ObservableProperty] private string _lastAnnouncement = string.Empty;

    public AccessibleShellViewModel(DatabaseService db, DisplayViewModel display)
    {
        _db = db;
        _display = display;
        _texts = BuildTexts();
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

        AnnounceScreens();
        AnnounceSetlist();
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
        SetlistCaption = LocalizationManager.Format("Acc.SetlistLoaded", name, _display.SetlistItems.Count);
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

    public void AnnounceHelp() => Announce(LocalizationManager.Get("Acc.Help"));

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

    private void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        LastAnnouncement = text;
        Announced?.Invoke(text);
    }
}
