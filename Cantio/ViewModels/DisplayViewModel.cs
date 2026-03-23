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

    // Dialog confirmation — set from code-behind to avoid MessageBox in VM
    public Func<string, bool>? ConfirmRequested { get; set; }
    private bool Confirm(string message) => ConfirmRequested?.Invoke(message) ?? false;

    public DisplayViewModel(DatabaseService db, ProjectionViewModel projection, ShortcutService shortcuts)
    {
        _db = db;
        _projection = projection;
        _shortcuts = shortcuts;
        _ = LoadCategoriesAsync();
        _ = LoadPinnedSetlistsAsync();
        _ = LoadSetlistGroupsAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        await OpenProjectionWindowAsync();

        var loadLast = await _db.GetSettingAsync("load_last_setlist");
        if (loadLast == "1")
        {
            var lastId = await _db.GetSettingAsync("last_setlist_id");
            if (int.TryParse(lastId, out var id))
            {
                var setlist = await _db.GetSetlistWithItemsAsync(id);
                if (setlist != null) await LoadPinnedSetlistAsync(setlist);
            }
        }
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
        if (!_loadingVerses && value != null) _ = LoadVersesAsync(value.Id);
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

    private int _loadedSetlistId;
    private string _loadedSetlistName = string.Empty;

    [ObservableProperty] private ObservableCollection<SetlistItem> _setlistItems = [];
    [ObservableProperty] private string _setlistName = string.Empty;
    [ObservableProperty] private string _setlistGroup = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _setlistGroups = [];
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
    private void MoveEditableVerseUp(EditableVerse verse)
    {
        var idx = EditableVerses.IndexOf(verse);
        if (idx <= 0) return;
        EditableVerses.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveEditableVerseDown(EditableVerse verse)
    {
        var idx = EditableVerses.IndexOf(verse);
        if (idx < 0 || idx >= EditableVerses.Count - 1) return;
        EditableVerses.Move(idx, idx + 1);
    }

    [RelayCommand]
    private async Task SaveInlineEditAsync()
    {
        foreach (var ev in EditableVerses)
            await _db.SaveVerseTextAsync(ev.Id, ev.Text);

        var order = EditableVerses.Select((ev, i) => (ev.Id, i));
        await _db.SaveVerseOrderAsync(order);

        // Refresh the song in the setlist item so changes are visible immediately
        if (SelectedSetlistItem?.Song != null)
        {
            var refreshed = await _db.GetSongWithVersesAsync(SelectedSetlistItem.Song.Id);
            if (refreshed != null) SelectedSetlistItem.Song = refreshed;
        }

        IsInlineEditorOpen = false;
        RebuildSlides();
    }

    [RelayCommand]
    private void CancelInlineEdit()
    {
        IsInlineEditorOpen = false;
        EditableVerses = [];
    }

    // ── Tryb edycji pieśni ────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    private bool _isEditMode = false;

    public bool IsNormalMode => !IsEditMode;

    [ObservableProperty] private bool _isEditDirty = false;

    // Edytowana pieśń
    private Song? _editingSong;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editNumber = string.Empty;
    [ObservableProperty] private Category? _editCategory;
    [ObservableProperty] private ObservableCollection<VerseEditorItem> _editingVerses = [];

    partial void OnEditTitleChanged(string v) => IsEditDirty = true;
    partial void OnEditNumberChanged(string v) => IsEditDirty = true;
    partial void OnEditCategoryChanged(Category? v) => IsEditDirty = true;

    // Kategorie z inline edit (shared z song edit mode i display)
    [ObservableProperty] private ObservableCollection<CategoryEditorItem> _categoryItems = [];
    [ObservableProperty] private string _newCategoryName = string.Empty;

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var name = NewCategoryName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var cat = new Category
        {
            Name = name,
            Number = Categories.Count > 0 ? Categories.Max(c => c.Number) + 1 : 1
        };
        await _db.SaveCategoryAsync(cat);
        NewCategoryName = string.Empty;
        await ReloadCategoriesForEditorAsync();
    }

    [RelayCommand]
    private void StartEditCategory(CategoryEditorItem item)
    {
        foreach (var c in CategoryItems) if (c != item) c.IsEditing = false;
        item.IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveCategoryAsync(CategoryEditorItem item)
    {
        var name = item.EditName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        await _db.SaveCategoryAsync(new Category
        {
            Id = item.Id,
            Name = name,
            Number = item.EditNumber > 0 ? item.EditNumber : item.Number
        });
        item.Name = name;
        item.Number = item.EditNumber > 0 ? item.EditNumber : item.Number;
        item.IsEditing = false;
        await ReloadCategoriesForEditorAsync();
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(CategoryEditorItem item)
    {
        if (!Confirm($"Usunąć kategorię \"{item.Name}\"?\nPieśni pozostaną bez kategorii.")) return;
        await _db.DeleteCategoryAsync(item.Id);
        await ReloadCategoriesForEditorAsync();
    }

    [RelayCommand]
    private void CancelEditCategory(CategoryEditorItem item) => item.IsEditing = false;

    // Delegates to LoadCategoriesAsync (no logic duplication)
    private async Task ReloadCategoriesForEditorAsync() => await LoadCategoriesAsync();

    // ── Projekcja ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _screenBlanked = true;
    [ObservableProperty] private double _projectionScreenWidth = 1920;
    [ObservableProperty] private double _projectionScreenHeight = 1080;

    [ObservableProperty] private SetlistItem? _selectedSetlistItem;

    private bool _loadingFromSetlist;
    private bool _loadingVerses;

    partial void OnSelectedSetlistItemChanged(SetlistItem? value)
    {
        if (!_loadingFromSetlist && value != null) LoadSongFromSetlist(value);
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
        LoadSongFromSetlist(newItem);
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
        var group = string.IsNullOrEmpty(SetlistGroup.Trim()) ? null : SetlistGroup.Trim();

        if (_loadedSetlistId != 0)
        {
            var dlg = new ConfirmOverwriteWindow(_loadedSetlistName);
            dlg.Owner = Application.Current.MainWindow;
            dlg.ShowDialog();
            switch (dlg.Result)
            {
                case OverwriteChoice.Overwrite:
                    var existing = await _db.GetSetlistAsync(_loadedSetlistId);
                    if (existing == null) goto case OverwriteChoice.AddNew;
                    existing.Name = name;
                    existing.Group = group;
                    await _db.SaveSetlistAsync(existing);
                    await _db.SaveSetlistItemsAsync(existing.Id, BuildItems());
                    _loadedSetlistName = name;
                    break;
                case OverwriteChoice.AddNew:
                    await SaveAsNewAsync(name, group);
                    break;
                case OverwriteChoice.Cancel:
                    return;
            }
        }
        else
        {
            await SaveAsNewAsync(name, group);
        }

        await LoadPinnedSetlistsAsync();
    }

    private async Task SaveAsNewAsync(string name, string? group)
    {
        var setlist = new Setlist { Name = name, Group = group, CreatedAt = DateTime.UtcNow };
        await _db.SaveSetlistAsync(setlist);
        await _db.SaveSetlistItemsAsync(setlist.Id, BuildItems());
        _loadedSetlistId = setlist.Id;
        _loadedSetlistName = setlist.Name;
        SetlistName = string.Empty;
        SetlistGroup = string.Empty;
    }

    // Tworzymy nowe obiekty bez nav property Song — inaczej EF próbuje wstawić istniejące piosenki
    private List<SetlistItem> BuildItems() =>
        SetlistItems.Select((item, i) => new SetlistItem
        {
            SongId = item.SongId,
            Position = i + 1,
            Type = item.Type,
            SelectedVerses = item.SelectedVerses
        }).ToList();

    [RelayCommand]
    private void ClearSetlist()
    {
        SetlistItems.Clear();
        SetlistName = string.Empty;
        SetlistGroup = string.Empty;
        _loadedSetlistId = 0;
        _loadedSetlistName = string.Empty;
    }

    [RelayCommand]
    public async Task LoadPinnedSetlistAsync(Setlist setlist)
    {
        var full = await _db.GetSetlistWithItemsAsync(setlist.Id);
        if (full == null) return;
        await _db.SaveSettingAsync("last_setlist_id", setlist.Id.ToString());
        _loadedSetlistId = full.Id;
        _loadedSetlistName = full.Name;
        SetlistItems = new ObservableCollection<SetlistItem>(full.Items);
        SetlistName = full.Name;
        SetlistGroup = full.Group ?? string.Empty;
        await LoadSetlistGroupsAsync();
        if (SetlistItems.Count > 0) LoadSongFromSetlist(SetlistItems[0]);
    }

    private async Task LoadSetlistGroupsAsync()
    {
        var csv = await _db.GetSettingAsync("setlist_groups") ?? string.Empty;
        var groups = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(g => g.Trim())
                        .Where(g => !string.IsNullOrEmpty(g))
                        .Distinct()
                        .OrderBy(g => g)
                        .ToList();
        SetlistGroups = new ObservableCollection<string>(groups);
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
        CategoryItems = new ObservableCollection<CategoryEditorItem>(
            list.Select(c => new CategoryEditorItem { Id = c.Id, Number = c.Number, Name = c.Name }));
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
        _loadingVerses = true;
        var song = await _db.GetSongWithVersesAsync(songId);
        if (song == null) { _loadingVerses = false; return; }
        SelectedSong = song;
        _loadingVerses = false;

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

        var typeCounts = new Dictionary<string, int>();
        var verseLabels = Verses.Select(v =>
        {
            typeCounts[v.Type] = typeCounts.GetValueOrDefault(v.Type) + 1;
            return v.Type switch
            {
                "c" => typeCounts[v.Type] == 1 ? "R" : $"R{typeCounts[v.Type]}",
                "b" => typeCounts[v.Type] == 1 ? "B" : $"B{typeCounts[v.Type]}",
                _ => typeCounts[v.Type].ToString()
            };
        }).ToArray();
        foreach (var slide in _slides)
            if (slide.VerseIndex >= 0 && slide.VerseIndex < verseLabels.Length)
                slide.Label = verseLabels[slide.VerseIndex];

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
        _loadingFromSetlist = true;
        SelectedSetlistItem = item;
        _loadingFromSetlist = false;
        _ = LoadVersesAsync(item.SongId, goToLast);
    }

    private async Task OpenProjectionWindowAsync()
    {
        if (_projectionWindow != null) return;
        var screenIndexStr = await _db.GetSettingAsync("projection_screen");
        int screenIndex = int.TryParse(screenIndexStr, out var idx) ? idx : 1;

        var screens = WpfScreenHelper.Screen.AllScreens.ToList();
        var target = screenIndex < screens.Count ? screens[screenIndex] : screens.Last();

        // Wstępna wartość na wypadek gdyby PresentationSource nie był dostępny
        ProjectionScreenWidth = target.WpfBounds.Width;
        ProjectionScreenHeight = target.WpfBounds.Height;

        _projectionWindow = new ProjectionWindow(_projection);
        _projectionWindow.Owner = Application.Current.MainWindow;
        _projectionWindow.Closed += (_, _) => _projectionWindow = null;
        _projectionWindow.MoveToSecondaryScreen(screenIndex);
        _projectionWindow.Show();

        // Czekaj na Background — niższy priorytet niż Loaded (6), więc wykona się po wszystkich Loaded callbackach
        // Dzięki temu odczytujemy DPI już po tym, jak okno zostało ustabilizowane na docelowym monitorze
        await _projectionWindow.Dispatcher.InvokeAsync(
            () => { }, System.Windows.Threading.DispatcherPriority.Background);

        // Pobierz faktyczny DPI okna projekcji (nie okna głównego) i przelicz wymiary
        var ps = System.Windows.PresentationSource.FromVisual(_projectionWindow);
        if (ps?.CompositionTarget != null)
        {
            var scale = ps.CompositionTarget.TransformFromDevice;
            ProjectionScreenWidth  = target.Bounds.Width  * scale.M11;
            ProjectionScreenHeight = target.Bounds.Height * scale.M22;
            _projectionWindow.Width  = ProjectionScreenWidth;
            _projectionWindow.Height = ProjectionScreenHeight;
        }

        // Przebuduj slajdy z poprawnymi wymiarami ekranu (AutoFit używa ProjectionScreenWidth/Height)
        RebuildSlides();

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
            MarginV = s.TextMarginV,
            AutoFit = s.FontAutoFit
        };
    }
}