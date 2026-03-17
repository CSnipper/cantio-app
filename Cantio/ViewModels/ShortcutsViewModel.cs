using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cantio.ViewModels;

public partial class ShortcutsViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ShortcutService _shortcuts;

    public ShortcutsViewModel(DatabaseService db, ShortcutService shortcuts)
    {
        _db = db;
        _shortcuts = shortcuts;
        _ = LoadAsync();
    }

    // ── Shortcut labels (bound to KeyCapture TextBoxes) ────────────────────

    [ObservableProperty] private string _slideNext   = string.Empty;
    [ObservableProperty] private string _slidePrev   = string.Empty;
    [ObservableProperty] private string _songNext    = string.Empty;
    [ObservableProperty] private string _songPrev    = string.Empty;
    [ObservableProperty] private string _blank       = string.Empty;
    [ObservableProperty] private string _tabShow     = string.Empty;
    [ObservableProperty] private string _tabSongs    = string.Empty;
    [ObservableProperty] private string _tabSets     = string.Empty;
    [ObservableProperty] private string _tabTemplate = string.Empty;
    [ObservableProperty] private string _tabImport   = string.Empty;
    [ObservableProperty] private string _searchOpen  = string.Empty;

    private async Task LoadAsync()
    {
        await _shortcuts.LoadWithLabelsAsync(_db);
        SlideNext   = _shortcuts.GetLabel(ShortcutService.SlideNext);
        SlidePrev   = _shortcuts.GetLabel(ShortcutService.SlidePrev);
        SongNext    = _shortcuts.GetLabel(ShortcutService.SongNext);
        SongPrev    = _shortcuts.GetLabel(ShortcutService.SongPrev);
        Blank       = _shortcuts.GetLabel(ShortcutService.Blank);
        TabShow     = _shortcuts.GetLabel(ShortcutService.TabShow);
        TabSongs    = _shortcuts.GetLabel(ShortcutService.TabSongs);
        TabSets     = _shortcuts.GetLabel(ShortcutService.TabSets);
        TabTemplate = _shortcuts.GetLabel(ShortcutService.TabTemplate);
        TabImport   = _shortcuts.GetLabel(ShortcutService.TabImport);
        SearchOpen  = _shortcuts.GetLabel(ShortcutService.SearchOpen);
    }

    // ── Save ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        var pairs = new[]
        {
            (ShortcutService.SlideNext,   SlideNext),
            (ShortcutService.SlidePrev,   SlidePrev),
            (ShortcutService.SongNext,    SongNext),
            (ShortcutService.SongPrev,    SongPrev),
            (ShortcutService.Blank,       Blank),
            (ShortcutService.TabShow,     TabShow),
            (ShortcutService.TabSongs,    TabSongs),
            (ShortcutService.TabSets,     TabSets),
            (ShortcutService.TabTemplate, TabTemplate),
            (ShortcutService.TabImport,   TabImport),
            (ShortcutService.SearchOpen,  SearchOpen),
        };
        foreach (var (actionId, label) in pairs)
        {
            await _db.SaveSettingAsync($"shortcut_{actionId}", label);
            _shortcuts.SetLabel(actionId, label);
        }
    }

    // ── Reset to defaults ─────────────────────────────────────────────────

    [RelayCommand]
    private void Reset()
    {
        SlideNext   = ShortcutService.Defaults[ShortcutService.SlideNext];
        SlidePrev   = ShortcutService.Defaults[ShortcutService.SlidePrev];
        SongNext    = ShortcutService.Defaults[ShortcutService.SongNext];
        SongPrev    = ShortcutService.Defaults[ShortcutService.SongPrev];
        Blank       = ShortcutService.Defaults[ShortcutService.Blank];
        TabShow     = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.TabShow, string.Empty);
        TabSongs    = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.TabSongs, string.Empty);
        TabSets     = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.TabSets, string.Empty);
        TabTemplate = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.TabTemplate, string.Empty);
        TabImport   = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.TabImport, string.Empty);
        SearchOpen  = ShortcutService.Defaults.GetValueOrDefault(ShortcutService.SearchOpen, string.Empty);
    }
}
