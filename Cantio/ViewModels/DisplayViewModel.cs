using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
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
    private CancellationTokenSource _searchCts = new();

    public ProjectionViewModel Projection => _projection;

    // Dialog confirmation — set from code-behind to avoid MessageBox in VM
    public Func<string, bool>? ConfirmRequested { get; set; }
    private bool Confirm(string message) => ConfirmRequested?.Invoke(message) ?? false;

    public DisplayViewModel(DatabaseService db, ProjectionViewModel projection, ShortcutService shortcuts)
    {
        _db = db;
        _projection = projection;
        _shortcuts = shortcuts;
        _ = LoadCategoriesAsync().ContinueWith(
            t => System.Diagnostics.Debug.WriteLine($"[DisplayViewModel] LoadCategoriesAsync: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
        _ = LoadPinnedSetlistsAsync().ContinueWith(
            t => System.Diagnostics.Debug.WriteLine($"[DisplayViewModel] LoadPinnedSetlistsAsync: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
        _ = LoadSetlistGroupsAsync().ContinueWith(
            t => System.Diagnostics.Debug.WriteLine($"[DisplayViewModel] LoadSetlistGroupsAsync: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
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

    // Kategorie

    [ObservableProperty] private ObservableCollection<Category> _categories = [];
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private CategoryEditorItem? _selectedCategoryItem;

    partial void OnSelectedCategoryChanged(Category? value)
    {
        if (value != null) _ = LoadSongsAsync(value.Id);
    }

    partial void OnSelectedCategoryItemChanged(CategoryEditorItem? value)
    {
        if (value == null || value.IsEditing || value.Id == 0) return;
        var cat = Categories.FirstOrDefault(c => c.Id == value.Id);
        if (cat != null) SelectedCategory = cat;
    }

    // Pieśni

    [ObservableProperty] private ObservableCollection<Song> _songs = [];
    [ObservableProperty] private Song? _selectedSong;
    [ObservableProperty] private string _searchText = string.Empty;


    [ObservableProperty] private Verse? _selectedVerse;

    [ObservableProperty] private bool _isPsalmMode = false;
    [ObservableProperty] private Slide? _projectedSlide;

    partial void OnCurrentSlideIndexChanged(int value)
    {
        if (value < 0 || value >= _slides.Count) return;
        var slide = _slides[value];
        if (slide.IsImageSlide)
        {
            _projection.ClearOperatorSlide();
            ProjectedSlide = slide;
            _projection.SetImageSlide(slide.ImagePath!);
            return;
        }
        if ((IsPsalmMode && slide.VerseType != "c") || slide.VerseType == "p")
        {
            // Psalm verse / prywatna zwrotka: pokaż w podglądzie operatora, projektor trzyma poprzedni slajd
            _projection.SetOperatorSlide(slide);
        }
        else
        {
            _projection.ClearOperatorSlide();
            ProjectedSlide = slide;
            _projection.SetSlide(slide);
        }
    }

    partial void OnSelectedSongChanged(Song? value)
    {
        // Ładowanie tylko na jawne polecenie (dwuklik / ikona oka), nie na samo zaznaczenie
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = RunDebouncedSearchAsync(value);
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

    // Zwrotki / Slajdy

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

    // Zestaw

    private int _loadedSetlistId;
    private string _loadedSetlistName = string.Empty;

    [ObservableProperty] private ObservableCollection<SetlistItem> _setlistItems = [];
    [ObservableProperty] private string _setlistName = string.Empty;
    [ObservableProperty] private string _setlistGroup = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _setlistGroups = [];
    [ObservableProperty] private ObservableCollection<Setlist> _pinnedSetlists = [];
    [ObservableProperty] private bool _isCurrentSetlistPinned;

    // Wyszukiwarka zestawów

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

    [RelayCommand]
    private async Task DeleteSetlistFromSearchAsync(Setlist setlist)
    {
        if (MessageBox.Show(
                $"Usunąć zestaw \"{setlist.Name}\"?",
                "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;
        await _db.DeleteSetlistAsync(setlist.Id);
        FilteredSetlists.Remove(setlist);
    }

    // Edytor zwrotek inline

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenInlineEditor))]
    private bool _isInlineEditorOpen;

    public bool CanOpenInlineEditor => !IsInlineEditorOpen;

    [ObservableProperty] private string _inlineEditorTitle = string.Empty;
    [ObservableProperty] private ObservableCollection<EditableVerse> _editableVerses = [];

    [RelayCommand]
    private async Task OpenInlineEditorAsync(SetlistItem item)
    {
        if (!item.SongId.HasValue) return;
        var song = await _db.GetSongWithVersesAsync(item.SongId.Value);
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
                    "img" => "Obrazek",
                    _ => $"Zwrotka {counts[v.Type]}"
                };
                return new EditableVerse { Id = v.Id, Type = v.Type, Label = label, Text = v.Text, ImagePath = v.ImagePath };
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
        await _db.SaveVerseTextsAsync(EditableVerses.Select(ev => (ev.Id, ev.Text, ev.ImagePath)));

        var order = EditableVerses.Select((ev, i) => (ev.Id, i));
        await _db.SaveVerseOrderAsync(order);

        var editedSongId = SelectedSetlistItem?.SongId;

        // Refresh the song in the setlist item so changes are visible immediately
        if (SelectedSetlistItem?.Song != null)
        {
            var refreshed = await _db.GetSongWithVersesAsync(SelectedSetlistItem.Song.Id);
            if (refreshed != null) SelectedSetlistItem.Song = refreshed;
        }

        IsInlineEditorOpen = false;

        // Jeśli edytowana pieśń jest aktualnie wyświetlana — przeładuj z DB zachowując pozycję slajdu
        if (editedSongId.HasValue && SelectedSong?.Id == editedSongId.Value)
            await LoadVersesAsync(editedSongId.Value, restoreSlide: CurrentSlideIndex);
        else
            RebuildSlides();
    }

    [RelayCommand]
    private void CancelInlineEdit()
    {
        IsInlineEditorOpen = false;
        EditableVerses = [];
    }

    // Tryb edycji pieśni

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
    [ObservableProperty] private ObservableCollection<PlayOrderEntry> _editPlayOrder = [];
    [ObservableProperty] private VerseEditorItem? _selectedPlayOrderAddVerse;

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
    private void AddNewCategoryInline()
    {
        foreach (var c in CategoryItems) c.IsEditing = false;
        var item = new CategoryEditorItem
        {
            Id = 0,
            Number = 0,
            IsEditing = true
        };
        CategoryItems.Insert(0, item);
        SelectedCategoryItem = item;
    }

    [RelayCommand]
    private void CancelEditCategory(CategoryEditorItem item)
    {
        if (item.Id == 0)
            CategoryItems.Remove(item);
        else
            item.IsEditing = false;
    }

    public async Task SaveCategoryOrderAsync()
    {
        for (int i = 0; i < CategoryItems.Count; i++)
        {
            var item = CategoryItems[i];
            item.Number = i + 1;
            await _db.SaveCategoryAsync(new Category { Id = item.Id, Name = item.Name, Number = i + 1 });
        }
        Categories = new ObservableCollection<Category>(await _db.GetCategoriesAsync());
    }

    private async Task ReloadCategoriesForEditorAsync()
    {
        var prevId = SelectedCategoryItem?.Id ?? 0;
        await LoadCategoriesAsync();
        if (prevId > 0)
        {
            var restored = CategoryItems.FirstOrDefault(c => c.Id == prevId);
            if (restored != null) SelectedCategoryItem = restored;
        }
    }

    [RelayCommand]
    private void NewSong()
    {
        _editingSong = new Song { Id = 0 };
        EditTitle = string.Empty;
        EditNumber = string.Empty;
        EditCategory = Categories.FirstOrDefault();
        EditingVerses.Clear();
        EditPlayOrder.Clear();
        AddVerseToEditor("v");
        IsEditDirty = false;
        IsEditMode = true;
    }

    [RelayCommand]
    private async Task EditSongAsync(Song song)
    {
        _editingSong = song;
        EditTitle = song.Title;
        EditNumber = song.Number > 0 ? song.Number.ToString() : string.Empty;
        EditCategory = Categories.FirstOrDefault(c => c.Id == song.CategoryId);
        EditingVerses.Clear();
        EditPlayOrder.Clear();
        var full = await _db.GetSongWithVersesAsync(song.Id);
        if (full != null)
        {
            var items = full.Verses.OrderBy(v => v.Position).ToList();
            var counters = new Dictionary<string, int>();
            foreach (var v in items)
            {
                counters[v.Type] = counters.GetValueOrDefault(v.Type) + 1;
                EditingVerses.Add(new VerseEditorItem { Type = v.Type, Text = v.Text, Number = counters[v.Type], ImagePath = v.ImagePath });
            }

            if (!string.IsNullOrEmpty(full.PlayOrderJson))
            {
                try
                {
                    var indices = JsonSerializer.Deserialize<List<int>>(full.PlayOrderJson) ?? [];
                    foreach (var idx in indices.Where(i => i >= 0 && i < EditingVerses.Count))
                        EditPlayOrder.Add(new PlayOrderEntry { Verse = EditingVerses[idx] });
                }
                catch { /* ignore malformed JSON */ }
            }
            else
            {
                // Brak PlayOrderJson — inicjalizuj naturalną kolejnością
                foreach (var v in EditingVerses)
                    EditPlayOrder.Add(new PlayOrderEntry { Verse = v });
            }
        }
        SelectedPlayOrderAddVerse = EditingVerses.FirstOrDefault();
        IsEditDirty = false;
        IsEditMode = true;
    }

    [RelayCommand]
    private void BackFromEdit()
    {
        if (IsEditDirty && !Confirm("Masz niezapisane zmiany. Wyjść bez zapisywania?"))
            return;
        IsEditMode = false;
        IsEditDirty = false;
        _editingSong = null;
    }

    [RelayCommand]
    private async Task SaveEditedSongAsync()
    {
        if (_editingSong == null) return;
        _editingSong.Title = EditTitle.Trim();
        _editingSong.Number = int.TryParse(EditNumber, out var n) ? n : 0;
        _editingSong.CategoryId = EditCategory?.Id ?? 0;
        int pos = 0;
        _editingSong.Verses = EditingVerses.Select(v => new Verse
        {
            Type = v.Type,
            Text = v.Text,
            ImagePath = v.ImagePath,
            Position = pos++,
            SongId = _editingSong.Id
        }).ToList();

        // Zapisz kolejność odtwarzania
        var verseList = EditingVerses.ToList();
        var playIndices = EditPlayOrder.Select(e => verseList.IndexOf(e.Verse)).Where(i => i >= 0).ToList();
        _editingSong.PlayOrderJson = playIndices.Count > 0
            ? JsonSerializer.Serialize(playIndices)
            : null;

        await _db.SaveSongAsync(_editingSong);
        await LoadCategoriesAsync();
        if (SelectedCategory != null)
            await LoadSongsAsync(SelectedCategory.Id);
        IsEditMode = false;
        IsEditDirty = false;
        _editingSong = null;
    }

    [RelayCommand]
    private async Task DeleteEditedSongAsync()
    {
        if (_editingSong == null) return;
        if (!Confirm($"Usunąć pieśń \"{_editingSong.Title}\"?")) return;
        await _db.DeleteSongAsync(_editingSong.Id);
        if (SelectedCategory != null) await LoadSongsAsync(SelectedCategory.Id);
        IsEditMode = false;
        _editingSong = null;
    }

    [RelayCommand]
    private void AddVerseToEditor(string type)
    {
        int number = EditingVerses.Count(v => v.Type == type) + 1;
        var item = new VerseEditorItem { Type = type, Number = number };
        EditingVerses.Add(item);
        EditPlayOrder.Add(new PlayOrderEntry { Verse = item });
        IsEditDirty = true;
    }

    [RelayCommand]
    private void RemoveVerseFromEditor(VerseEditorItem verse)
    {
        if (verse.Type != "img" && !string.IsNullOrWhiteSpace(verse.Text) && !Confirm("Usunąć tę zwrotkę?"))
            return;
        EditingVerses.Remove(verse);
        RenumberEditorVerses();
        IsEditDirty = true;
    }

    [RelayCommand]
    private void MoveEditorVerseUp(VerseEditorItem verse)
    {
        var idx = EditingVerses.IndexOf(verse);
        if (idx <= 0) return;
        EditingVerses.Move(idx, idx - 1);
        IsEditDirty = true;
    }

    [RelayCommand]
    private void MoveEditorVerseDown(VerseEditorItem verse)
    {
        var idx = EditingVerses.IndexOf(verse);
        if (idx < 0 || idx >= EditingVerses.Count - 1) return;
        EditingVerses.Move(idx, idx + 1);
        IsEditDirty = true;
    }

    private void RenumberEditorVerses()
    {
        var counters = new Dictionary<string, int>();
        foreach (var v in EditingVerses)
        {
            counters[v.Type] = counters.GetValueOrDefault(v.Type) + 1;
            v.Number = counters[v.Type];
        }
    }

    // Kolejność odtwarzania (PlayOrder)

    [RelayCommand]
    private void MovePlayOrderUp(PlayOrderEntry entry)
    {
        var idx = EditPlayOrder.IndexOf(entry);
        if (idx <= 0) return;
        EditPlayOrder.Move(idx, idx - 1);
        IsEditDirty = true;
    }

    [RelayCommand]
    private void MovePlayOrderDown(PlayOrderEntry entry)
    {
        var idx = EditPlayOrder.IndexOf(entry);
        if (idx < 0 || idx >= EditPlayOrder.Count - 1) return;
        EditPlayOrder.Move(idx, idx + 1);
        IsEditDirty = true;
    }

    [RelayCommand]
    private void RemoveFromPlayOrder(PlayOrderEntry entry)
    {
        EditPlayOrder.Remove(entry);
        IsEditDirty = true;
    }

    [RelayCommand]
    private void AddVerseToPlayOrder()
    {
        if (SelectedPlayOrderAddVerse == null) return;
        EditPlayOrder.Add(new PlayOrderEntry { Verse = SelectedPlayOrderAddVerse });
        IsEditDirty = true;
    }

    [RelayCommand]
    private void RebuildPlayOrderAuto()
    {
        EditPlayOrder.Clear();
        var verses = EditingVerses.ToList();
        var chorus = verses.FirstOrDefault(v => v.Type == "c");
        if (chorus == null)
        {
            foreach (var v in verses) EditPlayOrder.Add(new PlayOrderEntry { Verse = v });
            IsEditDirty = true;
            return;
        }
        if (verses.IndexOf(chorus) == 0) EditPlayOrder.Add(new PlayOrderEntry { Verse = chorus });
        foreach (var v in verses)
        {
            if (v == chorus) continue;
            EditPlayOrder.Add(new PlayOrderEntry { Verse = v });
            EditPlayOrder.Add(new PlayOrderEntry { Verse = chorus });
        }
        IsEditDirty = true;
    }

    [RelayCommand]
    private void ClearPlayOrder()
    {
        EditPlayOrder.Clear();
        foreach (var v in EditingVerses) EditPlayOrder.Add(new PlayOrderEntry { Verse = v });
        IsEditDirty = true;
    }

    [RelayCommand]
    private void OpenPasteTextEditor()
    {
        var currentText = string.Join("\n\n", EditingVerses.Select(v => v.Text));
        var psalmCatId = _db.GetSettings().PsalmCategoryId;
        bool currentIsPsalm = psalmCatId > 0 && (EditCategory?.Id ?? 0) == psalmCatId;
        var dlg = new PasteTextWindow(currentText, currentIsPsalm) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            ParseAndApplyText(dlg.ResultText, dlg.IsPsalm);
    }

    private void ParseAndApplyText(string rawText, bool isPsalm = false)
    {
        var blocks = rawText.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries)
                            .Select(b => b.Trim())
                            .Where(b => !string.IsNullOrEmpty(b))
                            .ToList();
        EditingVerses.Clear();

        // Detect chorus and determine its index
        int chorusBlockIndex = -1;
        var parsed = blocks.Select((block, i) =>
        {
            bool isChorus = block.StartsWith("Refren:", StringComparison.OrdinalIgnoreCase)
                         || block.StartsWith("Aklamacja:", StringComparison.OrdinalIgnoreCase);
            if (isChorus && chorusBlockIndex < 0) chorusBlockIndex = i;
            int prefixLen = block.StartsWith("Aklamacja:", StringComparison.OrdinalIgnoreCase)
                ? "Aklamacja:".Length : "Refren:".Length;
            var text = isChorus ? block[prefixLen..].TrimStart('\n', '\r', ' ') : block;
            return (type: isChorus ? "c" : "v", text);
        }).ToList();

        // Add all verses to EditingVerses
        var counters = new Dictionary<string, int>();
        var verseItems = new List<VerseEditorItem>();
        foreach (var (type, text) in parsed)
        {
            counters[type] = counters.GetValueOrDefault(type) + 1;
            verseItems.Add(new VerseEditorItem { Type = type, Text = text, Number = counters[type] });
        }
        foreach (var item in verseItems) EditingVerses.Add(item);

        // Build play order with auto-inserted chorus — pomiń dla psalmów (refren już w tekście)
        if (!isPsalm && chorusBlockIndex >= 0)
        {
            var chorusItem = verseItems[chorusBlockIndex];
            var playOrder = new List<VerseEditorItem>();

            // If chorus is first: [R, Z1, R, Z2, R, ...]
            if (chorusBlockIndex == 0)
                playOrder.Add(chorusItem);

            // For each non-chorus verse, add it followed by chorus
            foreach (var item in verseItems)
            {
                if (item == chorusItem) continue;
                playOrder.Add(item);
                playOrder.Add(chorusItem);
            }

            EditPlayOrder.Clear();
            foreach (var item in playOrder)
                EditPlayOrder.Add(new PlayOrderEntry { Verse = item });
        }
        else
        {
            // Psalm lub brak refrenu — kolejność = naturalna
            EditPlayOrder.Clear();
            foreach (var item in verseItems)
                EditPlayOrder.Add(new PlayOrderEntry { Verse = item });
        }

        SelectedPlayOrderAddVerse = EditingVerses.FirstOrDefault();
        IsEditDirty = true;
    }

    // Projekcja

    [ObservableProperty] private bool _screenBlanked = true;
    [ObservableProperty] private double _projectionScreenWidth = 1920;
    [ObservableProperty] private double _projectionScreenHeight = 1080;

    [ObservableProperty] private SetlistItem? _selectedSetlistItem;

    private bool _loadingFromSetlist;
    private bool _loadingVerses;

    partial void OnSelectedSetlistItemChanged(SetlistItem? value)
    {
        // Ładowanie tylko na jawne polecenie (dwuklik / ikona oka), nie na samo zaznaczenie
    }

    // Commands

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
    private void DisplaySetlistItem(SetlistItem item) => LoadSongFromSetlist(item);

    [RelayCommand]
    private async Task DisplaySongAsync(Song song) => await LoadVersesAsync(song.Id, restoreSlide: CurrentSlideIndex);

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
        IsCurrentSetlistPinned = false;
        TogglePinSetlistCommand.NotifyCanExecuteChanged();
        SetlistName = string.Empty;
        SetlistGroup = string.Empty;
    }

    // Tworzymy nowe obiekty bez nav property Song — inaczej EF próbuje wstawić istniejące piosenki
    private List<SetlistItem> BuildItems() =>
        SetlistItems.Select((item, i) => new SetlistItem
        {
            SongId = item.SongId,
            ImagePath = item.ImagePath,
            Position = i + 1,
            Type = item.Type,
            SelectedVerses = item.SelectedVerses
        }).ToList();

    [RelayCommand]
    private void AddImageToSetlist()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Wybierz obrazek",
            Filter = "Obrazki (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Wszystkie pliki (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var newItem = new SetlistItem { ImagePath = ImageStorage.Import(dlg.FileName), Type = "image" };
        var idx = SelectedSetlistItem != null ? SetlistItems.IndexOf(SelectedSetlistItem) : -1;
        if (idx >= 0) SetlistItems.Insert(idx + 1, newItem);
        else SetlistItems.Add(newItem);
        for (int i = 0; i < SetlistItems.Count; i++) SetlistItems[i].Position = i + 1;
        LoadSongFromSetlist(newItem);
    }

    [RelayCommand]
    private void ClearSetlist()
    {
        SetlistItems.Clear();
        SetlistName = string.Empty;
        SetlistGroup = string.Empty;
        _loadedSetlistId = 0;
        _loadedSetlistName = string.Empty;
        IsCurrentSetlistPinned = false;
    }

    [RelayCommand(CanExecute = nameof(CanTogglePin))]
    private async Task TogglePinSetlist()
    {
        var setlist = await _db.GetSetlistAsync(_loadedSetlistId);
        if (setlist == null) return;
        setlist.IsPinned = !setlist.IsPinned;
        await _db.SaveSetlistAsync(setlist);
        IsCurrentSetlistPinned = setlist.IsPinned;
        await LoadPinnedSetlistsAsync();
    }

    private bool CanTogglePin() => _loadedSetlistId > 0;

    [RelayCommand]
    private async Task LoadPinnedSetlistAsync(Setlist setlist)
    {
        var full = await _db.GetSetlistWithItemsAsync(setlist.Id);
        if (full == null) return;
        await _db.SaveSettingAsync("last_setlist_id", setlist.Id.ToString());
        _loadedSetlistId = full.Id;
        _loadedSetlistName = full.Name;
        SetlistItems = new ObservableCollection<SetlistItem>(full.Items);
        SetlistName = full.Name;
        SetlistGroup = full.Group ?? string.Empty;
        IsCurrentSetlistPinned = full.IsPinned;
        TogglePinSetlistCommand.NotifyCanExecuteChanged();
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
        GroupItems = new ObservableCollection<GroupEditorItem>(
            groups.Select(g => new GroupEditorItem { OriginalName = g }));
    }

    // Zarządzanie grupami zestawów

    [ObservableProperty] private ObservableCollection<GroupEditorItem> _groupItems = [];
    [ObservableProperty] private bool _isGroupPopupOpen = false;

    [RelayCommand]
    private void OpenGroupPopup() => IsGroupPopupOpen = true;

    [RelayCommand]
    private void AddNewGroupInline()
    {
        foreach (var g in GroupItems) g.IsEditing = false;
        GroupItems.Insert(0, new GroupEditorItem { OriginalName = string.Empty, IsEditing = true });
    }

    [RelayCommand]
    private void StartEditGroup(GroupEditorItem item)
    {
        foreach (var g in GroupItems) if (g != item) g.IsEditing = false;
        item.IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveGroupAsync(GroupEditorItem item)
    {
        var name = item.EditName.Trim();
        if (string.IsNullOrEmpty(name)) { GroupItems.Remove(item); return; }
        item.OriginalName = name;
        item.IsEditing = false;
        await PersistGroupsAsync();
        await LoadSetlistGroupsAsync();
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(GroupEditorItem item)
    {
        GroupItems.Remove(item);
        await PersistGroupsAsync();
        await LoadSetlistGroupsAsync();
    }

    [RelayCommand]
    private void CancelEditGroup(GroupEditorItem item)
    {
        if (string.IsNullOrEmpty(item.OriginalName))
            GroupItems.Remove(item);
        else
            item.IsEditing = false;
    }

    private async Task PersistGroupsAsync()
    {
        var csv = string.Join(",", GroupItems
            .Where(g => !string.IsNullOrEmpty(g.OriginalName))
            .Select(g => g.OriginalName));
        await _db.SaveSettingAsync("setlist_groups", csv);
    }

    // Klawiatura

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

    // Helpers

    private async Task LoadCategoriesAsync()
    {
        var list = await _db.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(list);
        CategoryItems = new ObservableCollection<CategoryEditorItem>(
            list.Select(c => new CategoryEditorItem { Id = c.Id, Number = c.Number, Name = c.Name }));
    }

    private async Task LoadSongsAsync(int categoryId)
    {
        var list = await _db.GetSongsByCategoryAsync(categoryId);
        Songs = new ObservableCollection<Song>(list);
    }

    private async Task RunDebouncedSearchAsync(string query)
    {
        _searchCts.Cancel();
        _searchCts.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        try
        {
            await Task.Delay(300, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            if (SelectedCategory != null) await LoadSongsAsync(SelectedCategory.Id);
            return;
        }
        var list = await _db.SearchSongsAsync(query, ct);
        Songs = new ObservableCollection<Song>(list);
    }

    private async Task LoadVersesAsync(int songId, bool goToLast = false, int restoreSlide = -1)
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

        var psalmCategoryId = _db.GetSettings().PsalmCategoryId;
        IsPsalmMode = psalmCategoryId > 0 && song.CategoryId == psalmCategoryId;
        ProjectedSlide = null;

        Verses = new ObservableCollection<Verse>(ordered);
        RebuildSlides();
        if (_slides.Count > 0)
        {
            if (restoreSlide >= 0)
                GoToSlide(Math.Min(restoreSlide, _slides.Count - 1));
            else
                GoToSlide(goToLast ? _slides.Count - 1 : 0);
        }
    }

    [ObservableProperty] private ObservableCollection<Slide> _slideList = [];

    public void RebuildSlides()
    {
        int prevIndex = CurrentSlideIndex;
        var settings = BuildLayoutSettings();
        if (IsPsalmMode)
        {
            // Zwrotki psalmu: ForceSingleSlide + auto-fit (cały tekst na jednym slajdzie)
            // Refren psalmu: stały rozmiar czcionki z ustawień (AutoFit=false, ForceSingleSlide=false)
            var verseSettings = new SlideLayoutSettings
            {
                FontFamily = settings.FontFamily, FontBold = settings.FontBold,
                FontSize = settings.FontSize, LineHeightMultiplier = settings.LineHeightMultiplier,
                SlideWidth = settings.SlideWidth, SlideHeight = settings.SlideHeight,
                MarginH = settings.MarginH, MarginV = settings.MarginV,
                ForceSingleSlide = true, AutoFit = settings.AutoFit
            };
            var chorusSettings = new SlideLayoutSettings
            {
                FontFamily = settings.FontFamily, FontBold = settings.FontBold,
                FontSize = settings.FontSize, LineHeightMultiplier = settings.LineHeightMultiplier,
                SlideWidth = settings.SlideWidth, SlideHeight = settings.SlideHeight,
                MarginH = settings.MarginH, MarginV = settings.MarginV,
                ForceSingleSlide = false, AutoFit = false
            };
            var psalmSlides = new List<Slide>();
            for (int vi = 0; vi < Verses.Count; vi++)
            {
                if (Verses[vi].Type == "img")
                {
                    psalmSlides.Add(new Slide { VerseIndex = vi, PartIndex = 0, ImagePath = Verses[vi].ImagePath });
                    continue;
                }
                var s = Verses[vi].Type == "c" ? chorusSettings : verseSettings;
                var parts = SlideLayoutService.SplitVerse(Verses[vi].Text, s);
                for (int pi = 0; pi < parts.Count; pi++)
                    psalmSlides.Add(new Slide
                    {
                        Text = parts[pi],
                        FontSize = SlideLayoutService.ComputeFitFontSize(parts[pi], s),
                        VerseIndex = vi,
                        PartIndex = pi
                    });
            }
            // Wyrównaj czcionkę zwrotek psalmu (refreny mają AutoFit=false → settings.FontSize)
            var verseFonts = psalmSlides
                .Where(s => !s.IsImageSlide && Verses[s.VerseIndex].Type != "c")
                .Select(s => s.FontSize)
                .ToList();
            if (verseFonts.Count > 1)
            {
                double unifiedVerseFont = verseFonts.Min();
                foreach (var slide in psalmSlides.Where(s => Verses[s.VerseIndex].Type != "c"))
                    slide.FontSize = unifiedVerseFont;
            }

            _slides = psalmSlides;
        }
        else
        {
            // Buduj per-verse: typ "p" (prywatna) dostaje ForceSingleSlide=true
            var privateSettings = new SlideLayoutSettings
            {
                FontFamily = settings.FontFamily, FontBold = settings.FontBold,
                FontSize = settings.FontSize, LineHeightMultiplier = settings.LineHeightMultiplier,
                SlideWidth = settings.SlideWidth, SlideHeight = settings.SlideHeight,
                MarginH = settings.MarginH, MarginV = settings.MarginV,
                ForceSingleSlide = true, AutoFit = settings.AutoFit
            };
            var allSlides = new List<Slide>();
            var normalSlides = new List<Slide>();
            var privateSlides = new List<Slide>();
            for (int vi = 0; vi < Verses.Count; vi++)
            {
                if (Verses[vi].Type == "img")
                {
                    allSlides.Add(new Slide { VerseIndex = vi, PartIndex = 0, ImagePath = Verses[vi].ImagePath });
                    continue;
                }
                var isPrivate = Verses[vi].Type == "p";
                var s = isPrivate ? privateSettings : settings;
                var parts = SlideLayoutService.SplitVerse(Verses[vi].Text, s);
                for (int pi = 0; pi < parts.Count; pi++)
                {
                    var slide = new Slide
                    {
                        Text = parts[pi],
                        FontSize = SlideLayoutService.ComputeFitFontSize(parts[pi], s),
                        VerseIndex = vi,
                        PartIndex = pi
                    };
                    allSlides.Add(slide);
                    if (isPrivate) privateSlides.Add(slide);
                    else normalSlides.Add(slide);
                }
            }
            // Normalizuj czcionkę w każdej grupie osobno (pomijaj slajdy-obrazki)
            if (normalSlides.Count(s => !s.IsImageSlide) > 1)
            {
                double u = normalSlides.Where(s => !s.IsImageSlide).Min(s => s.FontSize);
                foreach (var sl in normalSlides.Where(s => !s.IsImageSlide)) sl.FontSize = u;
            }
            if (privateSlides.Count > 1)
            {
                double u = privateSlides.Min(s => s.FontSize);
                foreach (var sl in privateSlides) sl.FontSize = u;
            }
            _slides = allSlides;
        }

        var typeCounts = new Dictionary<string, int>();
        var verseLabels = Verses.Select(v =>
        {
            typeCounts[v.Type] = typeCounts.GetValueOrDefault(v.Type) + 1;
            return v.Type switch
            {
                "c" => typeCounts[v.Type] == 1 ? "R" : $"R{typeCounts[v.Type]}",
                "b" => typeCounts[v.Type] == 1 ? "B" : $"B{typeCounts[v.Type]}",
                "p" => typeCounts[v.Type] == 1 ? "P" : $"P{typeCounts[v.Type]}",
                "img" => "🖼",
                _ => typeCounts[v.Type].ToString()
            };
        }).ToArray();
        foreach (var slide in _slides)
        {
            if (slide.VerseIndex >= 0 && slide.VerseIndex < verseLabels.Length)
                slide.Label = verseLabels[slide.VerseIndex];
            if (slide.VerseIndex >= 0 && slide.VerseIndex < Verses.Count)
                slide.VerseType = Verses[slide.VerseIndex].Type;
        }

        SlideList = new ObservableCollection<Slide>(_slides);
        CurrentSlideIndex = -1;
        OnPropertyChanged(nameof(SlideInfo));
        if (prevIndex >= 0 && _slides.Count > 0)
            GoToSlide(Math.Min(prevIndex, _slides.Count - 1));
    }

    private void GoToSlide(int index)
    {
        if (index < 0 || index >= _slides.Count) return;
        CurrentSlideIndex = index; // OnCurrentSlideIndexChanged obsługuje projekcję
    }

    private void LoadSongFromSetlist(SetlistItem item, bool goToLast = false)
    {
        if (item.IsImageItem)
        {
            SelectedSetlistItem = item;
            LoadImageFromSetlist(item);
            return;
        }
        if (!item.SongId.HasValue) return;
        bool sameSong = SelectedSong?.Id == item.SongId.Value;
        _loadingFromSetlist = true;
        SelectedSetlistItem = item;
        _loadingFromSetlist = false;
        _ = LoadVersesAsync(item.SongId.Value, goToLast, restoreSlide: sameSong ? CurrentSlideIndex : -1);
    }

    private void LoadImageFromSetlist(SetlistItem item)
    {
        Verses.Clear();
        _slides.Clear();
        SlideList = new ObservableCollection<Slide>();
        _projection.ClearOperatorSlide();
        _projection.SetImageSlide(item.ImagePath!);
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
        _projectionWindow.ShowInTaskbar = false;
        _projectionWindow.Closed += (_, _) => _projectionWindow = null;
        _projectionWindow.MoveToSecondaryScreen(screenIndex);
        _projectionWindow.Show();

        // Jeden monitor — zminimalizuj okno projekcji żeby nie przykrywało aplikacji.
        // Użytkownik może je przywrócić z paska zadań lub podłączyć projektor i zmienić ustawienie ekranu.
        if (screens.Count == 1)
            _projectionWindow.WindowState = System.Windows.WindowState.Minimized;

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

        Application.Current.MainWindow.Activate();
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

public class PlayOrderEntry
{
    public VerseEditorItem Verse { get; set; } = null!;
    public string Label => Verse.Label;
}