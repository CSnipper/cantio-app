using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cantio.ViewModels;

public partial class ShortcutsViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ShortcutService _shortcuts;

    public ObservableCollection<ShortcutItem> Items { get; } = [];
    public ICollectionView FilteredView { get; }

    [ObservableProperty] private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        var q = value.Trim().ToLowerInvariant();
        foreach (var item in Items)
            item.IsVisible = string.IsNullOrEmpty(q)
                || item.Name.ToLowerInvariant().Contains(q);
        FilteredView.Refresh();
    }

    public ShortcutsViewModel(DatabaseService db, ShortcutService shortcuts)
    {
        _db = db;
        _shortcuts = shortcuts;

        var cvs = new CollectionViewSource { Source = Items };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutItem.Category)));
        cvs.Filter += (_, e) => { e.Accepted = ((ShortcutItem)e.Item).IsVisible; };
        FilteredView = cvs.View;

        BuildItems();
        _ = LoadAsync();
    }

    private static string Loc(string key)
        => Application.Current.TryFindResource(key) is string s ? s : key;

    private void BuildItems()
    {
        var nav    = Loc("Shortcuts.Navigation");
        var tabs   = Loc("Shortcuts.Tabs");
        var search = Loc("Shortcuts.Search");

        var defs = new (string actionId, string nameKey, string category)[]
        {
            (ShortcutService.SlideNext,   "Shortcuts.SlideNext",   nav),
            (ShortcutService.SlidePrev,   "Shortcuts.SlidePrev",   nav),
            (ShortcutService.SongNext,    "Shortcuts.SongNext",    nav),
            (ShortcutService.SongPrev,    "Shortcuts.SongPrev",    nav),
            (ShortcutService.Blank,       "Shortcuts.Blank",       nav),
            (ShortcutService.TabShow,     "Shortcuts.TabShow",     tabs),
            (ShortcutService.TabTemplate, "Shortcuts.TabTemplate", tabs),
            (ShortcutService.TabImport,   "Shortcuts.TabImport",   tabs),
            (ShortcutService.SearchOpen,  "Shortcuts.SearchOpen",  search),
            (ShortcutService.SongSearch,  "Shortcuts.SongSearch",  search),
        };

        Items.Clear();
        foreach (var (actionId, nameKey, category) in defs)
            Items.Add(new ShortcutItem { ActionId = actionId, Name = Loc(nameKey), Category = category });
    }

    private async Task LoadAsync()
    {
        await _shortcuts.LoadWithLabelsAsync(_db);
        ReloadFromService();
    }

    /// <summary>Przywraca wartości z ostatnio zapisanego stanu (bez DB).</summary>
    public void ReloadFromService()
    {
        foreach (var item in Items)
            item.Value = _shortcuts.GetLabel(item.ActionId);
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        foreach (var item in Items)
        {
            await _db.SaveSettingAsync($"shortcut_{item.ActionId}", item.Value);
            _shortcuts.SetLabel(item.ActionId, item.Value);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        foreach (var item in Items)
            item.Value = ShortcutService.Defaults.GetValueOrDefault(item.ActionId, string.Empty);
    }
}
