using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cantio.ViewModels;

/// <summary>Pojedyncza zwrotka w edytorze — wrapper z UI-state.</summary>
public partial class VerseEditorItem : ObservableObject
{
    [ObservableProperty] private string _type = "v";   // v, c, b
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private int _number = 1;      // numer zwrotki tego typu

    public string Label => Type switch
    {
        "c" => "R",
        "b" => "B",
        _ => $"{Number}"
    };

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Label));
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));
}

public partial class SongEditorViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    public SongEditorViewModel(DatabaseService db)
    {
        _db = db;
        _ = LoadAsync();
    }

    // ── Lista pieśni (lewa kolumna) ───────────────────────────────────────

    [ObservableProperty] private ObservableCollection<Song> _songs = [];
    [ObservableProperty] private ObservableCollection<Category> _categories = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<Song> _filteredSongs = [];
    [ObservableProperty] private Song? _selectedSongInList;  // ← dodaj tu

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

    /// <summary>Tekst aktualnie wybranej zwrotki — dwukierunkowy binding z TextBox.</summary>
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

    /// <summary>Kolejność wykonania — każdy element to referencja do VerseEditorItem.</summary>
    [ObservableProperty] private ObservableCollection<VerseEditorItem> _playOrder = [];

    // ── Commands ──────────────────────────────────────────────────────────

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
        AddVerse("v"); // pierwsza zwrotka od razu
    }

    [RelayCommand]
    private void SelectVerse(VerseEditorItem verse) => SelectedVerse = verse;

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
        // Usuń też z kolejności wykonania
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

        // Buduj zwrotki z edytora
        EditingSong.Verses = Verses.Select((v, i) => new Verse
        {
            Position = i,
            Type = v.Type,
            Text = v.Text
        }).ToList();

        await _db.SaveSongAsync(EditingSong);
        IsDirty = false;
        await LoadAsync();

        // Zaznacz zapisaną pieśń
        var saved = Songs.FirstOrDefault(s => s.Title == EditingSong.Title);
        if (saved != null) LoadSong(saved);
    }

    [RelayCommand]
    private async Task DeleteSongAsync()
    {
        if (EditingSong == null || EditingSong.Id == 0) return;
        var r = MessageBox.Show($"Usunąć pieśń \"{EditTitle}\"?",
            "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        await _db.DeleteSongAsync(EditingSong.Id);
        EditingSong = null;
        Verses.Clear();
        PlayOrder.Clear();
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (EditingSong != null && EditingSong.Id > 0)
            LoadSong(EditingSong); // przywróć oryginał
        else
        {
            EditingSong = null;
            Verses.Clear();
            PlayOrder.Clear();
        }
        IsDirty = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void LoadSong(Song song)
    {
        EditingSong = song;
        EditTitle = song.Title;
        EditAuthor = song.Author ?? string.Empty;
        EditNumber = song.Number;
        EditCategory = Categories.FirstOrDefault(c => c.Id == song.CategoryId);

        Verses.Clear();
        PlayOrder.Clear();

        // Załaduj zwrotki z bazy
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
            Number = 1 // renumber below
        }).ToList();

        foreach (var item in items) Verses.Add(item);
        RenumberVerses();

        // Odtwórz kolejność wykonania
        // Domyślnie — kolejność jak zwrotki
        foreach (var v in Verses) PlayOrder.Add(v);


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

    private async Task LoadAsync()
    {
        try
        {
            var cats = await _db.GetCategoriesAsync();
            Categories = new ObservableCollection<Category>(cats);

            var songs = await _db.GetAllSongsAsync();
            Songs = new ObservableCollection<Song>(songs);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd ładowania: {ex.Message}");
        }
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(q)
            ? Songs
            : new ObservableCollection<Song>(Songs.Where(s =>
                s.Title.ToLower().Contains(q) ||
                s.Number.ToString().Contains(q)));
        FilteredSongs = filtered;
    }
}
