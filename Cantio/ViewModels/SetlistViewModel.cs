using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cantio.ViewModels;

public partial class SetlistViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    public event Action? PinnedChanged;
    public event Action<Setlist>? LoadForDisplayRequested;

    public SetlistViewModel(DatabaseService db)
    {
        _db = db;
        _ = LoadAsync();
    }

    // ── Grupy i zestawy ───────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> _groups = [];
    [ObservableProperty] private string? _selectedGroup;

    [ObservableProperty] private string _newGroupName = "";


    [ObservableProperty] private ObservableCollection<Setlist> _setlists = [];
    [ObservableProperty] private ObservableCollection<Setlist> _filteredSetlists = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedSetlists))]
    private ObservableCollection<Setlist> _pinnedSetlists = [];

    public bool HasPinnedSetlists => PinnedSetlists.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetlistSelected))]
    private Setlist? _selectedSetlist;

    public bool IsSetlistSelected => SelectedSetlist != null;

    partial void OnSelectedGroupChanged(string? value) => ApplyFilter();

    partial void OnSelectedSetlistChanged(Setlist? value)
    {
        if (value != null) _ = LoadSetlistItemsAsync(value.Id);
    }

    // ── Pozycje zestawu ───────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<SetlistItem> _items = [];

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectSetlist(Setlist setlist)
    {
        SelectedSetlist = setlist;
    }

    [RelayCommand]
    private void LoadForDisplay()
    {
        if (SelectedSetlist != null)
            LoadForDisplayRequested?.Invoke(SelectedSetlist);
    }

    [RelayCommand]
    private void SelectAllGroups()
    {
        SelectedGroup = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectGroup(string group)
    {
        SelectedGroup = group == SelectedGroup ? null : group;
        ApplyFilter();
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName)) return;
        if (!Groups.Contains(NewGroupName))
        {
            Groups.Add(NewGroupName);
            _ = SaveGroupsAsync();
        }
        SelectedGroup = NewGroupName;
        NewGroupName = "";
    }

    [RelayCommand]
    private async Task DeleteSetlistAsync()
    {
        if (SelectedSetlist == null) return;
        var r = MessageBox.Show($"Usunąć zestaw \"{SelectedSetlist.Name}\"?",
            "Cantio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        await _db.DeleteSetlistAsync(SelectedSetlist.Id);
        SelectedSetlist = null;
        Items.Clear();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (SelectedSetlist == null) return;
        var prevId = SelectedSetlist.Id;
        SelectedSetlist.IsPinned = !SelectedSetlist.IsPinned;
        await _db.SaveSetlistAsync(SelectedSetlist);
        PinnedChanged?.Invoke();
        await LoadAsync();
        SelectedSetlist = Setlists.FirstOrDefault(s => s.Id == prevId);
    }

    [RelayCommand]
    private void CloneItem(SetlistItem item)
    {
        var idx = Items.IndexOf(item);
        var clone = new SetlistItem
        {
            SetlistId = item.SetlistId,
            SongId = item.SongId,
            Song = item.Song,
            Position = item.Position,
            Type = item.Type
        };
        Items.Insert(idx + 1, clone);
        RenumberItems();
        IsDirty = true;
    }

    [RelayCommand]
    private void RemoveItem(SetlistItem item)
    {
        Items.Remove(item);
        RenumberItems();
        IsDirty = true;
    }

    [RelayCommand]
    private async Task SaveItemsAsync()
    {
        if (SelectedSetlist == null) return;
        await _db.SaveSetlistItemsAsync(SelectedSetlist.Id, Items.ToList());
        IsDirty = false;
    }

    [RelayCommand]
    private async Task RenameGroupAsync()
    {
        // TODO: prosty dialog
    }

    [ObservableProperty] private bool _isDirty = false;

    // ── Drag & Drop helpers ───────────────────────────────────────────────

    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        var item = Items[fromIndex];
        Items.RemoveAt(fromIndex);
        Items.Insert(toIndex, item);
        RenumberItems();
        IsDirty = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void RenumberItems()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].Position = i + 1;
    }

    private async Task LoadSetlistItemsAsync(int setlistId)
    {
        var items = await _db.GetSetlistItemsAsync(setlistId);
        Items = new ObservableCollection<SetlistItem>(items);
    }

    public async Task LoadAsync()
    {
        try
        {
            await LoadGroupsAsync();

            var setlists = await _db.GetAllSetlistsAsync();
            Setlists = new ObservableCollection<Setlist>(setlists);
            PinnedSetlists = new ObservableCollection<Setlist>(setlists.Where(s => s.IsPinned));

            var groupsFromSetlists = setlists
                .Where(s => !string.IsNullOrEmpty(s.Group))
                .Select(s => s.Group!)
                .Distinct()
                .Where(g => !Groups.Contains(g));
            foreach (var g in groupsFromSetlists)
                Groups.Add(g);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd ładowania zestawów: {ex.Message}");
        }
    }

    private async Task SaveGroupsAsync()
    {
        await _db.SaveSettingAsync("setlist_groups", string.Join(",", Groups));
    }

    private async Task LoadGroupsAsync()
    {
        var setting = await _db.GetSettingAsync("setlist_groups");
        if (!string.IsNullOrWhiteSpace(setting))
            Groups = new ObservableCollection<string>(setting.Split(','));
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrEmpty(SelectedGroup)
            ? Setlists
            : new ObservableCollection<Setlist>(
                Setlists.Where(s => s.Group == SelectedGroup));
        FilteredSetlists = filtered;
    }
}
