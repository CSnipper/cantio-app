using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace Cantio.ViewModels;

public partial class DisplayViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ProjectionViewModel _projection;
    private readonly ShortcutService _shortcuts;
    private ProjectionWindow? _projectionWindow;

    public ProjectionViewModel Projection => _projection;

    public DisplayViewModel(DatabaseService db, ProjectionViewModel projection, ShortcutService shortcuts)
    {
        _db = db;
        _projection = projection;
        _shortcuts = shortcuts;
        _ = LoadCategoriesAsync();
        _ = LoadPinnedSetlistsAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        await OpenProjectionWindowAsync();
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

    // ── Wyszukiwarka zestawów ─────────────────────────────────────────────

    [ObservableProperty] private bool _isSetlistSearchOpen;
    [ObservableProperty] private string _setlistSearchText = string.Empty;
    [ObservableProperty] private ObservableCollection<Setlist> _filteredSetlists = [];

    private List<Setlist> _allSetlists = [];

    partial void OnSetlistSearchTextChanged(string value)
    {
        var q = value.Trim().ToLowerInvariant();
        FilteredSetlists = new ObservableCollection<Setlist>(
            string.IsNullOrEmpty(q)
                ? _allSetlists
                : _allSetlists.Where(s => s.Name.ToLowerInvariant().Contains(q)));
    }

    [RelayCommand]
    private async Task OpenSetlistSearchAsync()
    {
        if (IsSetlistSearchOpen) { IsSetlistSearchOpen = false; return; }
        _allSetlists = await _db.GetAllSetlistsAsync();
        SetlistSearchText = string.Empty;
        FilteredSetlists = new ObservableCollection<Setlist>(_allSetlists);
        IsSetlistSearchOpen = true;
    }

    [RelayCommand]
    private async Task LoadSetlistFromSearchAsync(Setlist setlist)
    {
        IsSetlistSearchOpen = false;
        await LoadPinnedSetlistAsync(setlist);
    }

    [RelayCommand]
    private async Task AppendSetlistFromSearchAsync(Setlist setlist)
    {
        IsSetlistSearchOpen = false;
        var full = await _db.GetSetlistWithItemsAsync(setlist.Id);
        if (full == null) return;
        foreach (var item in full.Items)
            SetlistItems.Add(item);
        for (int i = 0; i < SetlistItems.Count; i++) SetlistItems[i].Position = i + 1;
    }

    // ── Edytor zwrotek inline ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenInlineEditor))]
    private bool _isInlineEditorOpen;

    public bool CanOpenInlineEditor => !IsInlineEditorOpen;

    [ObservableProperty] private string _inlineEditorTitle = string.Empty;
    [ObservableProperty] private ObservableCollection<EditableVerse> _editableVerses = [];

    [RelayCommand]
    private async Task OpenInlineEditorAsync(SetlistItem item)
    {
        var song = await _db.GetSongWithVersesAsync(item.SongId);
        if (song == null) return;
        InlineEditorTitle = song.Title;
        var verses = song.Verses.OrderBy(v => v.Position).ToList();
        var counts = new Dictionary<string, int>();
        EditableVerses = new ObservableCollection<EditableVerse>(
            verses.Select(v =>
            {
                counts[v.Type] = counts.GetValueOrDefault(v.Type) + 1;
                var label = v.Type switch
                {
                    "c" => counts[v.Type] == 1 ? "Refren" : $"Refren {counts[v.Type]}",
                    "b" => counts[v.Type] == 1 ? "Bridge" : $"Bridge {counts[v.Type]}",
                    _ => $"Zwrotka {counts[v.Type]}"
                };
                return new EditableVerse { Id = v.Id, Type = v.Type, Label = label, Text = v.Text };
            }));
        IsInlineEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveInlineEditAsync()
    {
        foreach (var ev in EditableVerses)
            await _db.SaveVerseTextAsync(ev.Id, ev.Text);
        IsInlineEditorOpen = false;
        RebuildSlides();
    }

    [RelayCommand]
    private void CancelInlineEdit()
    {
        IsInlineEditorOpen = false;
        EditableVerses = [];
    }

    // ── Projekcja ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _screenBlanked = true;
    [ObservableProperty] private double _projectionScreenWidth = 1920;
    [ObservableProperty] private double _projectionScreenHeight = 1080;

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
    private void RemoveFromSetlist(SetlistItem item)
    {
        if (IsInlineEditorOpen) CancelInlineEdit();
        SetlistItems.Remove(item);
    }

    [RelayCommand]
    private async Task SaveSetlistAsync()
    {
        var name = string.IsNullOrEmpty(SetlistName.Trim())
            ? $"Zestaw {DateTime.Now:dd.MM HH:mm}" : SetlistName.Trim();

        var setlist = new Setlist { Name = name, CreatedAt = DateTime.UtcNow };
        // Tworzymy nowe obiekty bez nav property Song — inaczej EF próbuje wstawić istniejące piosenki
        setlist.Items = SetlistItems.Select((item, i) => new SetlistItem
        {
            SongId = item.SongId,
            Position = i + 1,
            Type = item.Type
        }).ToList();

        await _db.SaveSetlistAsync(setlist);
        SetlistName = string.Empty;
        await LoadPinnedSetlistsAsync();
    }

    [RelayCommand]
    private void ClearSetlist()
    {
        SetlistItems.Clear();
        SetlistName = string.Empty;
    }

    [RelayCommand]
    public async Task LoadPinnedSetlistAsync(Setlist setlist)
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
        // Space is kept as a secondary hardcoded alias for slide_next (remote controls)
        if (_shortcuts.IsMatch(key, modifiers, ShortcutService.SlideNext)
            || (key == Key.Space && modifiers == ModifierKeys.None))
        { NextSlide(); return; }

        if (_shortcuts.IsMatch(key, modifiers, ShortcutService.SlidePrev))
        { PrevSlide(); return; }

        if (_shortcuts.IsMatch(key, modifiers, ShortcutService.SongNext))
        { NextSong(); return; }

        if (_shortcuts.IsMatch(key, modifiers, ShortcutService.SongPrev))
        { PrevSong(); return; }

        if (_shortcuts.IsMatch(key, modifiers, ShortcutService.Blank))
        { ToggleBlank(); return; }

        // Home always goes to first slide (not configurable)
        if (key == Key.Home && _slides.Count > 0)
        { GoToSlide(0); return; }
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

        var baseVerses = song.Verses.OrderBy(v => v.Position).ToList();
        List<Verse> ordered = baseVerses;

        if (!string.IsNullOrEmpty(song.PlayOrderJson))
        {
            try
            {
                var indices = JsonSerializer.Deserialize<List<int>>(song.PlayOrderJson) ?? [];
                var expanded = indices.Where(i => i >= 0 && i < baseVerses.Count).Select(i => baseVerses[i]).ToList();
                if (expanded.Count > 0) ordered = expanded;
            }
            catch { }
        }

        Verses = new ObservableCollection<Verse>(ordered);
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

    private async Task OpenProjectionWindowAsync()
    {
        if (_projectionWindow != null) return;
        var screenIndexStr = await _db.GetSettingAsync("projection_screen");
        int screenIndex = int.TryParse(screenIndexStr, out var idx) ? idx : 1;

        var screens = WpfScreenHelper.Screen.AllScreens.ToList();
        var target = screenIndex < screens.Count ? screens[screenIndex] : screens.Last();
        ProjectionScreenWidth = target.WpfBounds.Width;
        ProjectionScreenHeight = target.WpfBounds.Height;

        _projectionWindow = new ProjectionWindow(_projection);
        _projectionWindow.Owner = Application.Current.MainWindow;
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
        return new SlideLayoutSettings
        {
            FontFamily = s.FontFamily,
            FontBold = s.FontBold,
            FontSize = s.FontSize,
            LineHeightMultiplier = s.LineHeightMultiplier,
            SlideWidth = ProjectionScreenWidth,
            SlideHeight = ProjectionScreenHeight,
            MarginH = s.TextMarginH,
            MarginV = s.TextMarginV
        };
    }
}