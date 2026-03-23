using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Xml.Linq;

namespace Cantio.ViewModels;

// ── Główny ViewModel edytora pieśni ──────────────────────────────────────────

public partial class SongEditorViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    public SongEditorViewModel(DatabaseService db)
    {
        _db = db;
        _ = LoadAsync();
    }

    // ── Kategorie ─────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Category> _categories = [];
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
        await ReloadCategoriesAsync();
    }

    [RelayCommand]
    private void StartEditCategory(CategoryEditorItem item)
    {
        // zamknij inne otwarte edycje
        foreach (var c in CategoryItems)
            if (c != item) c.IsEditing = false;
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
        await ReloadCategoriesAsync();
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(CategoryEditorItem item)
    {
        var result = MessageBox.Show(
            $"Usunąć kategorię \"{item.Name}\"?\nPieśni w tej kategorii pozostaną bez kategorii.",
            "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        await _db.DeleteCategoryAsync(item.Id);
        await ReloadCategoriesAsync();
    }

    [RelayCommand]
    private void CancelEditCategory(CategoryEditorItem item)
        => item.IsEditing = false;

    public async Task ReloadCategoriesAsync()
    {
        var cats = await _db.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(cats);
        CategoryItems = new ObservableCollection<CategoryEditorItem>(
            cats.Select(c => new CategoryEditorItem { Id = c.Id, Number = c.Number, Name = c.Name }));
    }

    // ── Lista pieśni ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Song> _songs = [];
    [ObservableProperty] private ObservableCollection<Song> _filteredSongs = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Song? _selectedSongInList;

    partial void OnSelectedSongInListChanged(Song? value)
    {
        if (value != null) LoadSong(value);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    // ── Edytowana pieśń ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Song? _editingSong;

    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editAuthor = string.Empty;
    [ObservableProperty] private int _editNumber = 0;
    [ObservableProperty] private Category? _editCategory;
    [ObservableProperty] private bool _isDirty = false;

    public bool IsEditing => EditingSong != null;

    // ── Zwrotki ───────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<VerseEditorItem> _verses = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVerseText))]
    private VerseEditorItem? _selectedVerse;

    public string SelectedVerseText
    {
        get => SelectedVerse?.Text ?? string.Empty;
        set
        {
            if (SelectedVerse != null)
                SelectedVerse.Text = value;
        }
    }

    partial void OnSelectedVerseChanged(VerseEditorItem? value)
        => OnPropertyChanged(nameof(SelectedVerseText));

    // ── Kolejność wykonania ───────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<VerseEditorItem> _playOrder = [];

    // ── Komendy pieśni ────────────────────────────────────────────────────

    [RelayCommand]
    private void NewSong()
    {
        EditingSong = new Song { Id = 0 };
        EditTitle = string.Empty;
        EditAuthor = string.Empty;
        EditNumber = 0;
        EditCategory = Categories.FirstOrDefault();
        Verses.Clear();
        PlayOrder.Clear();
        IsDirty = false;
        AddVerse("v");
    }

    [RelayCommand]
    private void SelectVerse(VerseEditorItem verse) => SelectedVerse = verse;

    [RelayCommand]
    private void OpenPasteTextDialog()
    {
        var currentText = string.Join("\n\n", Verses.Select(v => v.Text));
        var dlg = new PasteTextWindow(currentText) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultText))
            PasteText(dlg.ResultText);
    }

    [RelayCommand]
    private void PasteText(string rawText)
    {
        var blocks = rawText.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        Verses.Clear();
        PlayOrder.Clear();
        int n = 1;
        foreach (var block in blocks)
        {
            var item = new VerseEditorItem { Type = "v", Text = block.Trim(), Number = n++ };
            Verses.Add(item);
            PlayOrder.Add(item);
        }
        SelectedVerse = Verses.FirstOrDefault();
        IsDirty = true;
    }

    [RelayCommand]
    private void AddVerse(string type)
    {
        int number = Verses.Count(v => v.Type == type) + 1;
        var item = new VerseEditorItem { Type = type, Number = number };
        Verses.Add(item);
        SelectedVerse = item;
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveVerse(VerseEditorItem verse)
    {
        var toRemove = PlayOrder.Where(p => p == verse).ToList();
        foreach (var p in toRemove) PlayOrder.Remove(p);
        Verses.Remove(verse);
        RenumberVerses();
        IsDirty = true;
    }

    [RelayCommand]
    private void AddToPlayOrder(VerseEditorItem verse)
    {
        PlayOrder.Add(verse);
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveFromPlayOrder(VerseEditorItem verse)
    {
        PlayOrder.Remove(verse);
        IsDirty = true;
    }

    [RelayCommand]
    private void ChangeVerseType(VerseEditorItem verse)
    {
        verse.Type = verse.Type switch
        {
            "v" => "c",
            "c" => "b",
            _ => "v"
        };
        RenumberVerses();
        IsDirty = true;
    }

    [RelayCommand]
    private async Task SaveSongAsync()
    {
        if (EditingSong == null) return;

        EditingSong.Title = EditTitle.Trim();
        EditingSong.Author = EditAuthor.Trim();
        EditingSong.Number = EditNumber;
        EditingSong.CategoryId = EditCategory?.Id ?? 0;

        int pos = 0;
        EditingSong.Verses = Verses.Select(v => new Verse
        {
            Type = v.Type,
            Text = v.Text,
            Position = pos++,
            SongId = EditingSong.Id
        }).ToList();

        // Zapisz kolejność wykonania jako indeksy do tablicy Verses
        var playOrderIndices = PlayOrder.Select(p => Verses.IndexOf(p)).Where(i => i >= 0).ToList();
        EditingSong.PlayOrderJson = playOrderIndices.Count > 0
            ? JsonSerializer.Serialize(playOrderIndices)
            : null;

        await _db.SaveSongAsync(EditingSong);

        await LoadAsync();
        IsDirty = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingSong = null;
        Verses.Clear();
        PlayOrder.Clear();
        IsDirty = false;
    }

    [RelayCommand]
    private async Task DeleteSongAsync()
    {
        if (EditingSong == null) return;
        var r = MessageBox.Show($"Usunąć pieśń \"{EditingSong.Title}\"?",
            "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        await _db.DeleteSongAsync(EditingSong.Id);
        EditingSong = null;
        Verses.Clear();
        PlayOrder.Clear();
        await LoadAsync();
    }

    // ── Ładowanie ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        try
        {
            var cats = await _db.GetCategoriesAsync();
            Categories = new ObservableCollection<Category>(cats);
            CategoryItems = new ObservableCollection<CategoryEditorItem>(
                cats.Select(c => new CategoryEditorItem { Id = c.Id, Number = c.Number, Name = c.Name }));

            var songs = await _db.GetAllSongsAsync();
            Songs = new ObservableCollection<Song>(songs);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd ładowania: {ex.Message}");
        }
    }

    private void LoadSong(Song song)
    {
        EditingSong = song;
        EditTitle = song.Title;
        EditAuthor = song.Author ?? string.Empty;
        EditNumber = song.Number;
        EditCategory = Categories.FirstOrDefault(c => c.Id == song.CategoryId);
        Verses.Clear();
        PlayOrder.Clear();
        _ = LoadSongVersesAsync(song.Id);
        IsDirty = false;
    }

    private async Task LoadSongVersesAsync(int songId)
    {
        var full = await _db.GetSongWithVersesAsync(songId);
        if (full == null) return;

        Verses.Clear();
        PlayOrder.Clear();

        var items = full.Verses.OrderBy(v => v.Position).Select(v => new VerseEditorItem
        {
            Type = v.Type,
            Text = v.Text,
            Number = 1
        }).ToList();

        foreach (var item in items) Verses.Add(item);
        RenumberVerses();

        // Wczytaj kolejność wykonania
        List<int> playOrderIndices = [];
        if (!string.IsNullOrEmpty(full.PlayOrderJson))
        {
            try { playOrderIndices = JsonSerializer.Deserialize<List<int>>(full.PlayOrderJson) ?? []; }
            catch { }
        }

        if (playOrderIndices.Count > 0)
        {
            foreach (var idx in playOrderIndices)
                if (idx >= 0 && idx < Verses.Count) PlayOrder.Add(Verses[idx]);
        }
        else
        {
            foreach (var v in Verses) PlayOrder.Add(v);
        }

        SelectedVerse = Verses.FirstOrDefault();
    }

    private void RenumberVerses()
    {
        var counters = new Dictionary<string, int> { ["v"] = 0, ["c"] = 0, ["b"] = 0 };
        foreach (var v in Verses)
        {
            if (!counters.ContainsKey(v.Type)) counters[v.Type] = 0;
            counters[v.Type]++;
            v.Number = counters[v.Type];
        }
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLower();
        FilteredSongs = string.IsNullOrEmpty(q)
            ? new ObservableCollection<Song>(Songs)
            : new ObservableCollection<Song>(Songs.Where(s =>
                s.Title.ToLower().Contains(q) ||
                s.Number.ToString().Contains(q)));
    }
}