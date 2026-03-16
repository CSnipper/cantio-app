using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Cantio.ViewModels;

public partial class DisplayViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ProjectionViewModel _projection;
    private ProjectionWindow? _projectionWindow;

    public ProjectionViewModel Projection => _projection;

    public DisplayViewModel(DatabaseService db, ProjectionViewModel projection)
    {
        _db = db;
        _projection = projection;
        _ = LoadCategoriesAsync();
        _ = LoadPinnedSetlistsAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        OpenProjectionWindow();
    }

    public void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => HandleKey(e.Key, e.KeyboardDevice.Modifiers);

    [RelayCommand]
    private void ToggleBlank()
    {
        ScreenBlanked = !ScreenBlanked;
        _projection.SetBlanked(ScreenBlanked);
    }

    // ── Kategorie ─────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Category> _categories = [];
    [ObservableProperty] private Category? _selectedCategory;


    partial void OnSelectedCategoryChanged(Category? value)
    {
        if (value != null) _ = LoadSongsAsync(value.Id);
    }

    // ── Pieśni ────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Song> _songs = [];
    [ObservableProperty] private Song? _selectedSong;
    [ObservableProperty] private string _searchText = string.Empty;


    [ObservableProperty] private Verse? _selectedVerse;

    partial void OnCurrentSlideIndexChanged(int value)
    {
        if (value >= 0 && value < _slides.Count)
            _projection.SetSlide(_slides[value]);
    }

    partial void OnSelectedSongChanged(Song? value)
    {
        if (value != null) _ = LoadVersesAsync(value.Id);
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchSongsAsync(value);
    }

    [RelayCommand]
    private void MoveToTop()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx <= 0) return;
        SetlistItems.Move(idx, 0);
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx <= 0) return;
        SetlistItems.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx < 0 || idx >= SetlistItems.Count - 1) return;
        SetlistItems.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void MoveToBottom()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx < 0 || idx >= SetlistItems.Count - 1) return;
        SetlistItems.Move(idx, SetlistItems.Count - 1);
    }

    // ── Zwrotki / Slajdy ──────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Verse> _verses = [];

    private List<Slide> _slides = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSlideText))]
    [NotifyPropertyChangedFor(nameof(SlideInfo))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _currentSlideIndex = -1;

    public string CurrentSlideText => CurrentSlideIndex >= 0 && CurrentSlideIndex < _slides.Count
        ? _slides[CurrentSlideIndex].Text : string.Empty;

    public string SlideInfo => _slides.Count > 0 && CurrentSlideIndex >= 0
        ? $"{CurrentSlideIndex + 1} / {_slides.Count}" : string.Empty;

    public bool CanGoPrev => CurrentSlideIndex > 0;
    public bool CanGoNext => CurrentSlideIndex < _slides.Count - 1;

    // ── Zestaw ────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<SetlistItem> _setlistItems = [];
    [ObservableProperty] private string _setlistName = string.Empty;
    [ObservableProperty] private ObservableCollection<Setlist> _pinnedSetlists = [];

    // ── Projekcja ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _screenBlanked = true;

    [ObservableProperty] private SetlistItem? _selectedSetlistItem;

    partial void OnSelectedSetlistItemChanged(SetlistItem? value)
    {
        if (value != null) LoadSongFromSetlist(value);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowVerseSlide(Verse verse)
    {
        int verseIdx = Verses.IndexOf(verse);
        var idx = _slides.FindIndex(s => s.VerseIndex == verseIdx && s.PartIndex == 0);
        if (idx >= 0) GoToSlide(idx);
    }

    [RelayCommand]
    private void NextSlide()
    {
        if (CanGoNext)
            GoToSlide(CurrentSlideIndex + 1);
        else
            NextSong(); // ostatni slajd → następna pieśń
    }

    [RelayCommand]
    private void PrevSlide()
    {
        if (CanGoPrev)
            GoToSlide(CurrentSlideIndex - 1);
        else
            PrevSong(); // pierwszy slajd → poprzednia pieśń (ostatni slajd)
    }

    [RelayCommand]
    private void NextSong()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx >= 0 && idx < SetlistItems.Count - 1)
            LoadSongFromSetlist(SetlistItems[idx + 1]);
    }

    [RelayCommand]
    private void PrevSong()
    {
        if (SelectedSetlistItem == null) return;
        var idx = SetlistItems.IndexOf(SelectedSetlistItem);
        if (idx > 0)
            LoadSongFromSetlist(SetlistItems[idx - 1], goToLast: true);
    }

    [RelayCommand]
    private void AddToSetlist(Song? song)
    {
        if (song == null) return;
        var newItem = new SetlistItem { Song = song, SongId = song.Id, Type = "song" };
        var idx = SelectedSetlistItem != null ? SetlistItems.IndexOf(SelectedSetlistItem) : -1;
        if (idx >= 0)
            SetlistItems.Insert(idx + 1, newItem);
        else
            SetlistItems.Add(newItem);
        for (int i = 0; i < SetlistItems.Count; i++) SetlistItems[i].Position = i + 1;
    }

    [RelayCommand]
    private void RemoveFromSetlist(SetlistItem item) => SetlistItems.Remove(item);

    [RelayCommand]
    private async Task SaveSetlistAsync()
    {
        var name = string.IsNullOrEmpty(SetlistName.Trim())
            ? $"Zestaw {DateTime.Now:dd.MM HH:mm}" : SetlistName.Trim();

        var setlist = new Setlist { Name = name, CreatedAt = DateTime.UtcNow };
        setlist.Items = SetlistItems.Select((item, i) => { item.Position = i + 1; return item; }).ToList();

        await _db.SaveSetlistAsync(setlist);
        await LoadPinnedSetlistsAsync();
    }

    [RelayCommand]
    private void ClearSetlist()
    {
        SetlistItems.Clear();
        SetlistName = string.Empty;
    }

    [RelayCommand]
    private async Task LoadPinnedSetlistAsync(Setlist setlist)
    {
        var full = await _db.GetSetlistWithItemsAsync(setlist.Id);
        if (full == null) return;
        SetlistItems = new ObservableCollection<SetlistItem>(full.Items);
        SetlistName = full.Name;
        if (SetlistItems.Count > 0) LoadSongFromSetlist(SetlistItems[0]);
    }

    // ── Klawiatura ────────────────────────────────────────────────────────

    public void HandleKey(Key key, ModifierKeys modifiers)
    {
        switch (key)
        {
            case Key.Right:
            case Key.Space:
            case Key.Next:
                if (modifiers == ModifierKeys.Control) NextSong();
                else NextSlide();
                break;
            case Key.Left:
            case Key.Prior:
                if (modifiers == ModifierKeys.Control) PrevSong();
                else PrevSlide();
                break;
            case Key.Home:
                if (_slides.Count > 0) GoToSlide(0);
                break;
            case Key.Escape:
                ScreenBlanked = !ScreenBlanked;
                _projection.SetBlanked(ScreenBlanked);
                break;
            case Key.Up:
                PrevSong();
                break;
            case Key.Down:
                NextSong();
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task LoadCategoriesAsync()
    {
        var list = await _db.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(list);
        await LoadPinnedSetlistsAsync();
    }

    private async Task LoadSongsAsync(int categoryId)
    {
        var list = await _db.GetSongsByCategoryAsync(categoryId);
        Songs = new ObservableCollection<Song>(list);
    }

    private async Task SearchSongsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SelectedCategory != null) await LoadSongsAsync(SelectedCategory.Id);
            return;
        }
        var list = await _db.SearchSongsAsync(query);
        Songs = new ObservableCollection<Song>(list);
    }

    private async Task LoadVersesAsync(int songId, bool goToLast = false)
    {
        var song = await _db.GetSongWithVersesAsync(songId);
        if (song == null) return;
        Verses = new ObservableCollection<Verse>(song.Verses);
        RebuildSlides();
        if (_slides.Count > 0)
            GoToSlide(goToLast ? _slides.Count - 1 : 0);
    }

    [ObservableProperty] private ObservableCollection<Slide> _slideList = [];

    public void RebuildSlides()
    {
        int prevIndex = CurrentSlideIndex;
        var settings = BuildLayoutSettings();
        var texts = Verses.Select(v => v.Text).ToList();
        _slides = SlideLayoutService.BuildSlides(texts, settings);
        SlideList = new ObservableCollection<Slide>(_slides);
        CurrentSlideIndex = -1;
        OnPropertyChanged(nameof(SlideInfo));
        if (prevIndex >= 0 && _slides.Count > 0)
            GoToSlide(Math.Min(prevIndex, _slides.Count - 1));
    }

    private void GoToSlide(int index)
    {
        if (index < 0 || index >= _slides.Count) return;
        CurrentSlideIndex = index;
        if (!ScreenBlanked)
            _projection.SetSlide(_slides[index]);
    }

    private void LoadSongFromSetlist(SetlistItem item, bool goToLast = false)
    {
        if (item.SongId == 0) return;
        _ = LoadVersesAsync(item.SongId, goToLast);
        SelectedSetlistItem = item;
    }

    private void OpenProjectionWindow()
    {
        if (_projectionWindow != null) return;
        var screenIndexStr = _db.GetSettingAsync("projection_screen").Result;
        int screenIndex = int.TryParse(screenIndexStr, out var idx) ? idx : 1;

        _projectionWindow = new ProjectionWindow(_projection);
        _projectionWindow.Closed += (_, _) => _projectionWindow = null;
        _projectionWindow.MoveToSecondaryScreen(screenIndex);
        _projectionWindow.Show();
        Application.Current.MainWindow.Focus();
        _projection.ApplySettings(_db.GetSettings());
        _projection.SetBlanked(ScreenBlanked);
        if (CurrentSlideIndex >= 0 && CurrentSlideIndex < _slides.Count)
            _projection.SetSlide(_slides[CurrentSlideIndex]);
    }

    private void CloseProjectionWindow()
    {
        _projectionWindow?.Close();
        _projectionWindow = null;
    }

    public async Task LoadPinnedSetlistsAsync()
    {
        var list = await _db.GetPinnedSetlistsAsync();
        PinnedSetlists = new ObservableCollection<Setlist>(list);
    }

    private SlideLayoutSettings BuildLayoutSettings()
    {
        var s = _db.GetSettings();

        // Pobierz wymiary ekranu projekcji
        var screens = WpfScreenHelper.Screen.AllScreens.ToList();
        var screenIndexStr = _db.GetSettingAsync("projection_screen").Result;
        int screenIndex = int.TryParse(screenIndexStr, out var idx) ? idx : 1;
        var target = screenIndex < screens.Count ? screens[screenIndex] : screens.Last();

        return new SlideLayoutSettings
        {
            FontFamily = s.FontFamily,
            FontBold = s.FontBold,
            FontSize = s.FontSize,
            LineHeightMultiplier = s.LineHeightMultiplier,
            SlideWidth = target.WpfBounds.Width,
            SlideHeight = target.WpfBounds.Height,
            MarginH = s.TextMarginH,
            MarginV = s.TextMarginV
        };
    }
}