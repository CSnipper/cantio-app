using Cantor.Models;
using Cantor.Services;
using Cantor.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Cantor.ViewModels;

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
    }

    public Task InitializeAsync() => LoadCategoriesAsync();

    public void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => HandleKey(e.Key, e.KeyboardDevice.Modifiers);

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

    partial void OnSelectedSongChanged(Song? value)
    {
        if (value != null) _ = LoadVersesAsync(value.Id);
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = SearchSongsAsync(value);
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

    public string ToggleScreenIcon => IsScreenOn ? "■" : "▶";
    public string ToggleScreenText => IsScreenOn ? "WYŁĄCZ EKRAN" : "POKAŻ NA EKRANIE";

    public bool CanGoPrev => CurrentSlideIndex > 0;
    public bool CanGoNext => CurrentSlideIndex < _slides.Count - 1;

    // ── Zestaw ────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<SetlistItem> _setlistItems = [];
    [ObservableProperty] private string _setlistName = string.Empty;
    [ObservableProperty] private ObservableCollection<Setlist> _pinnedSetlists = [];

    // ── Projekcja ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleScreenLabel))]
    [NotifyPropertyChangedFor(nameof(IsScreenOn))]
    [NotifyPropertyChangedFor(nameof(ToggleScreenIcon))]
    [NotifyPropertyChangedFor(nameof(ToggleScreenText))]
    private bool _projectionActive = false;

    [ObservableProperty] private bool _screenBlanked = false;

    [ObservableProperty] private SetlistItem? _selectedSetlistItem;

    partial void OnSelectedSetlistItemChanged(SetlistItem? value)
    {
        if (value != null) LoadSongFromSetlist(value);
    }

    public string ToggleScreenLabel => ProjectionActive ? "■ WYŁĄCZ EKRAN" : "▶ POKAŻ NA EKRANIE";
    public bool IsScreenOn => ProjectionActive;

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
        if (CanGoNext) GoToSlide(CurrentSlideIndex + 1);
    }

    [RelayCommand]
    private void PrevSlide()
    {
        if (CanGoPrev) GoToSlide(CurrentSlideIndex - 1);
    }

    [RelayCommand]
    private void NextSong()
    {
        var idx = SetlistItems.ToList().FindIndex(i => i.SongId == SelectedSong?.Id);
        if (idx >= 0 && idx < SetlistItems.Count - 1)
            LoadSongFromSetlist(SetlistItems[idx + 1]);
    }

    [RelayCommand]
    private void PrevSong()
    {
        var idx = SetlistItems.ToList().FindIndex(i => i.SongId == SelectedSong?.Id);
        if (idx > 0) LoadSongFromSetlist(SetlistItems[idx - 1]);
    }

    [RelayCommand]
    private void AddToSetlist(Song? song)
    {
        if (song == null) return;
        SetlistItems.Add(new SetlistItem
        {
            Song = song,
            SongId = song.Id,
            Position = SetlistItems.Count + 1,
            Type = "song"
        });
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

    [RelayCommand]
    private void ToggleScreen()
    {
        if (!ProjectionActive)
        {
            OpenProjectionWindow();
            ProjectionActive = true;
            ScreenBlanked = false;
        }
        else
        {
            CloseProjectionWindow();
            ProjectionActive = false;
        }
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
                if (!ProjectionActive) break;
                ScreenBlanked = !ScreenBlanked;
                _projectionWindow?.SetBlanked(ScreenBlanked);
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

    private async Task LoadVersesAsync(int songId)
    {
        var song = await _db.GetSongWithVersesAsync(songId);
        if (song == null) return;
        Verses = new ObservableCollection<Verse>(song.Verses);
        RebuildSlides();
        if (_slides.Count > 0) GoToSlide(0);
    }

    public void RebuildSlides()
    {
        int prevIndex = CurrentSlideIndex;
        var settings = BuildLayoutSettings();
        var texts = Verses.Select(v => v.Text).ToList();
        _slides = SlideLayoutService.BuildSlides(texts, settings);
        CurrentSlideIndex = -1;
        OnPropertyChanged(nameof(SlideInfo));
        if (prevIndex >= 0 && _slides.Count > 0)
            GoToSlide(Math.Min(prevIndex, _slides.Count - 1));
    }

    private void GoToSlide(int index)
    {
        if (index < 0 || index >= _slides.Count) return;
        CurrentSlideIndex = index;
        _projection.SetSlide(_slides[index]);
        if (ProjectionActive && !ScreenBlanked)
            _projectionWindow?.Refresh();
    }

    private void LoadSongFromSetlist(SetlistItem item)
    {
        if (item.SongId == 0) return;
        _ = LoadVersesAsync(item.SongId);
        if (item.Song != null) SelectedSong = item.Song;
    }

    private void OpenProjectionWindow()
    {
        if (_projectionWindow != null) return;
        var screenIndexStr = _db.GetSettingAsync("projection_screen").Result;
        int screenIndex = int.TryParse(screenIndexStr, out var idx) ? idx : 1;

        _projectionWindow = new ProjectionWindow(_projection);
        _projectionWindow.Closed += (_, _) => { _projectionWindow = null; ProjectionActive = false; };
        _projectionWindow.MoveToSecondaryScreen(screenIndex);
        _projectionWindow.Show();
        _projection.ApplySettings(_db.GetSettings());
        if (CurrentSlideIndex >= 0 && CurrentSlideIndex < _slides.Count)
            _projection.SetSlide(_slides[CurrentSlideIndex]);
    }

    private void CloseProjectionWindow()
    {
        _projectionWindow?.Close();
        _projectionWindow = null;
    }

    private async Task LoadPinnedSetlistsAsync()
    {
        var list = await _db.GetPinnedSetlistsAsync();
        PinnedSetlists = new ObservableCollection<Setlist>(list);
    }

    private SlideLayoutSettings BuildLayoutSettings()
    {
        var s = _db.GetSettings();
        return new SlideLayoutSettings
        {
            FontFamily = s.FontFamily,
            FontBold = s.FontBold,
            FontSize = s.FontSize,
            LineHeightMultiplier = s.LineHeightMultiplier,
            SlideWidth = 1920,
            SlideHeight = 1080,
            MarginH = s.TextMarginH,
            MarginV = s.TextMarginV
        };
    }
}