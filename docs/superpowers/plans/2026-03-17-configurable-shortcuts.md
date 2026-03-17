# Configurable Keyboard Shortcuts — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Skróty" tab to Cantio's settings where the user can configure keyboard shortcuts for slide navigation, song navigation, blank toggle, tab switching, and search opening; also make Down-arrow in search boxes move focus to the results list.

**Architecture:** A new `ShortcutService` (plain class, shared singleton) holds the active shortcut map loaded from the `settings` table. `DisplayViewModel.HandleKey` and `MainWindow.OnPreviewKeyDown` both ask `ShortcutService.IsMatch()` instead of hard-coding keys. A new `ShortcutsViewModel` provides the UI bindings and saves changes via `DatabaseService`. `KeyCaptureHelper` is extended to record the Ctrl modifier and enforce Ctrl for letter keys.

**Tech Stack:** C# 12 / .NET 10 / WPF / CommunityToolkit.Mvvm / EF Core + SQLite / existing `KeyCaptureHelper` attached property

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Cantio/Services/ShortcutService.cs` | **Create** | Action IDs, defaults, DB load, `IsMatch`, label↔key conversion |
| `Cantio/ViewModels/ShortcutsViewModel.cs` | **Create** | UI bindings per shortcut, Save/Reset commands |
| `Cantio/Helpers/KeyCaptureHelper.cs` | **Modify** | Capture Ctrl modifier; auto-Ctrl for letter keys |
| `Cantio/ViewModels/DisplayViewModel.cs` | **Modify** | Accept `ShortcutService`; rewrite `HandleKey` to use it |
| `Cantio/MainWindow.xaml.cs` | **Modify** | Create shared `ShortcutService`; add tab/search shortcuts; wire Down-arrow in search boxes |
| `Cantio/MainWindow.xaml` | **Modify** | Add `TabShortcuts` button; add `PaneShortcuts` grid; name the two song ListBoxes; add `PreviewKeyDown` on search boxes |

---

## Task 1 — ShortcutService

**Files:**
- Create: `Cantio/Services/ShortcutService.cs`

### Action IDs

```
slide_next   slide_prev   song_next   song_prev   blank
tab_show     tab_songs    tab_sets    tab_template  tab_import
search_open
```

### Default labels

| Action | Default |
|---|---|
| slide_next | Right |
| slide_prev | Left |
| song_next | Down |
| song_prev | Up |
| blank | Escape |
| tab_* | *(empty — unassigned)* |
| search_open | *(empty)* |

- [ ] **Step 1: Create the file**

```csharp
using System.Windows.Input;

namespace Cantio.Services;

/// <summary>
/// Holds the active shortcut map. One shared instance created in MainWindow,
/// passed to DisplayViewModel and ShortcutsViewModel.
/// </summary>
public class ShortcutService
{
    // ── Action ID constants ────────────────────────────────────────────────
    public const string SlideNext   = "slide_next";
    public const string SlidePrev   = "slide_prev";
    public const string SongNext    = "song_next";
    public const string SongPrev    = "song_prev";
    public const string Blank       = "blank";
    public const string TabShow     = "tab_show";
    public const string TabSongs    = "tab_songs";
    public const string TabSets     = "tab_sets";
    public const string TabTemplate = "tab_template";
    public const string TabImport   = "tab_import";
    public const string SearchOpen  = "search_open";

    public static readonly IReadOnlyList<string> AllActions =
    [
        SlideNext, SlidePrev, SongNext, SongPrev, Blank,
        TabShow, TabSongs, TabSets, TabTemplate, TabImport, SearchOpen
    ];

    private static readonly Dictionary<string, string> _defaults = new()
    {
        [SlideNext]   = "Right",
        [SlidePrev]   = "Left",
        [SongNext]    = "Down",
        [SongPrev]    = "Up",
        [Blank]       = "Escape",
        [TabShow]     = string.Empty,
        [TabSongs]    = string.Empty,
        [TabSets]     = string.Empty,
        [TabTemplate] = string.Empty,
        [TabImport]   = string.Empty,
        [SearchOpen]  = string.Empty,
    };

    // label → (Key, ModifierKeys), empty label = unassigned
    private Dictionary<string, (Key key, ModifierKeys mods)> _map = new();

    // ── Loading ────────────────────────────────────────────────────────────

    public async Task LoadAsync(DatabaseService db)
    {
        _map.Clear();
        foreach (var actionId in AllActions)
        {
            var stored = await db.GetSettingAsync($"shortcut_{actionId}");
            var label  = stored ?? _defaults.GetValueOrDefault(actionId, string.Empty);
            if (!string.IsNullOrEmpty(label))
                _map[actionId] = ParseLabel(label);
        }
    }

    public string GetLabel(string actionId)
    {
        var stored = _rawLabels.GetValueOrDefault(actionId);
        return stored ?? _defaults.GetValueOrDefault(actionId, string.Empty);
    }

    // Keep raw labels too (for display in VM)
    private Dictionary<string, string> _rawLabels = new();

    public async Task LoadWithLabelsAsync(DatabaseService db)
    {
        _map.Clear();
        _rawLabels.Clear();
        foreach (var actionId in AllActions)
        {
            var stored = await db.GetSettingAsync($"shortcut_{actionId}");
            var label  = stored ?? _defaults.GetValueOrDefault(actionId, string.Empty);
            _rawLabels[actionId] = label;
            if (!string.IsNullOrEmpty(label))
                _map[actionId] = ParseLabel(label);
        }
    }

    public void SetLabel(string actionId, string label)
    {
        _rawLabels[actionId] = label;
        _map.Remove(actionId);
        if (!string.IsNullOrEmpty(label))
            _map[actionId] = ParseLabel(label);
    }

    public static IReadOnlyDictionary<string, string> Defaults => _defaults;

    // ── Matching ───────────────────────────────────────────────────────────

    public bool IsMatch(Key key, ModifierKeys modifiers, string actionId)
    {
        if (!_map.TryGetValue(actionId, out var expected)) return false;
        return key == expected.key && modifiers == expected.mods;
    }

    // ── Label ↔ Key conversion ─────────────────────────────────────────────

    /// <summary>Converts a captured key+modifiers to a display label like "Ctrl+F" or "Escape".</summary>
    public static string KeyComboToLabel(Key key, ModifierKeys modifiers)
    {
        var label = KeyToLabel(key);
        if (string.IsNullOrEmpty(label)) return string.Empty;
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        // letters always get Ctrl
        bool isLetter = key >= Key.A && key <= Key.Z;
        return (ctrl || isLetter) ? "Ctrl+" + label : label;
    }

    public static string KeyToLabel(Key key) => key switch
    {
        >= Key.A and <= Key.Z         => key.ToString(),
        >= Key.D0 and <= Key.D9       => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"N{(int)(key - Key.NumPad0)}",
        >= Key.F1 and <= Key.F12      => key.ToString(),
        Key.Space  => "Space",
        Key.Escape => "Escape",
        Key.Return => "Return",
        Key.Up     => "Up",
        Key.Down   => "Down",
        Key.Left   => "Left",
        Key.Right  => "Right",
        Key.Prior  => "Prior",
        Key.Next   => "Next",
        Key.Home   => "Home",
        Key.End    => "End",
        Key.OemComma  => ",",
        Key.OemPeriod => ".",
        Key.OemMinus  => "-",
        Key.OemPlus   => "+",
        _ => key.ToString()
    };

    private static (Key key, ModifierKeys mods) ParseLabel(string label)
    {
        if (label.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
            return (LabelToKey(label[5..]), ModifierKeys.Control);
        return (LabelToKey(label), ModifierKeys.None);
    }

    private static Key LabelToKey(string label)
    {
        if (label.Length == 1 && label[0] is >= 'A' and <= 'Z')
            return Key.A + (label[0] - 'A');
        if (label.Length == 1 && label[0] is >= '0' and <= '9')
            return Key.D0 + (label[0] - '0');
        if (label.Length == 2 && label[0] == 'N' && label[1] is >= '0' and <= '9')
            return Key.NumPad0 + (label[1] - '0');
        if (label.StartsWith('F') && int.TryParse(label[1..], out var fn) && fn >= 1 && fn <= 12)
            return Key.F1 + (fn - 1);
        return label switch
        {
            "Space"  => Key.Space,
            "Escape" => Key.Escape,
            "Return" => Key.Return,
            "Up"     => Key.Up,
            "Down"   => Key.Down,
            "Left"   => Key.Left,
            "Right"  => Key.Right,
            "Prior"  => Key.Prior,
            "Next"   => Key.Next,
            "Home"   => Key.Home,
            "End"    => Key.End,
            ","      => Key.OemComma,
            "."      => Key.OemPeriod,
            "-"      => Key.OemMinus,
            "+"      => Key.OemPlus,
            _ => Enum.TryParse<Key>(label, out var k) ? k : Key.None
        };
    }
}
```

- [ ] **Step 2: Build and verify no compiler errors**

```bash
dotnet build Cantio
```
Expected: 0 errors.

---

## Task 2 — Update KeyCaptureHelper to capture Ctrl modifier

**Files:**
- Modify: `Cantio/Helpers/KeyCaptureHelper.cs`

The `OnKeyDown` handler currently stores only the key label. Change it to:
- Always include "Ctrl+" prefix for A-Z keys (even if Ctrl not held)
- Include "Ctrl+" prefix for other keys when Ctrl IS held
- Use `ShortcutService.KeyComboToLabel` for consistent output

- [ ] **Step 1: Update `OnKeyDown` and `KeyToLabel` in `KeyCaptureHelper.cs`**

Replace the `OnKeyDown` method:

```csharp
private static void OnKeyDown(object sender, KeyEventArgs e)
{
    if (sender is not TextBox tb) return;

    // Ignore pure modifiers
    if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
              or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin
              or Key.Tab)
        return;

    if (e.Key is Key.Escape or Key.Back or Key.Delete)
    {
        tb.Text = string.Empty;
        e.Handled = true;
        return;
    }

    tb.Text = Services.ShortcutService.KeyComboToLabel(e.Key, e.KeyboardDevice.Modifiers);
    e.Handled = true;
}
```

> NOTE: `KeyToLabel` in `KeyCaptureHelper` is still used by the text-tag shortcut system in `VerseTextBox_PreviewKeyDown`. Do NOT remove it — just leave it as-is. The new `ShortcutService.KeyToLabel` is a separate copy used by the shortcut system.

- [ ] **Step 2: Build**

```bash
dotnet build Cantio
```
Expected: 0 errors.

---

## Task 3 — ShortcutsViewModel

**Files:**
- Create: `Cantio/ViewModels/ShortcutsViewModel.cs`

One string property per shortcut (bound to `KeyCaptureHelper` TextBoxes in the UI). `SaveCommand` persists to DB and updates the live `ShortcutService`. `ResetCommand` loads defaults without saving.

- [ ] **Step 1: Create the file**

```csharp
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

    [ObservableProperty] private string _slideNext    = string.Empty;
    [ObservableProperty] private string _slidePrev    = string.Empty;
    [ObservableProperty] private string _songNext     = string.Empty;
    [ObservableProperty] private string _songPrev     = string.Empty;
    [ObservableProperty] private string _blank        = string.Empty;
    [ObservableProperty] private string _tabShow      = string.Empty;
    [ObservableProperty] private string _tabSongs     = string.Empty;
    [ObservableProperty] private string _tabSets      = string.Empty;
    [ObservableProperty] private string _tabTemplate  = string.Empty;
    [ObservableProperty] private string _tabImport    = string.Empty;
    [ObservableProperty] private string _searchOpen   = string.Empty;

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
```

- [ ] **Step 2: Build**

```bash
dotnet build Cantio
```
Expected: 0 errors.

---

## Task 4 — Update DisplayViewModel to use ShortcutService

**Files:**
- Modify: `Cantio/ViewModels/DisplayViewModel.cs`

`DisplayViewModel` needs a `ShortcutService` reference. `HandleKey` replaces the hardcoded `switch` with `IsMatch` calls. `Space` is kept as a hardcoded secondary alias for `slide_next` (many remotes use it).

- [ ] **Step 1: Add `ShortcutService` field**

Add to the field declarations at the top of `DisplayViewModel`:

```csharp
private readonly ShortcutService _shortcuts;
```

- [ ] **Step 2: Update the constructor signature**

Change:
```csharp
public DisplayViewModel(DatabaseService db, ProjectionViewModel projection)
{
    _db = db;
    _projection = projection;
```
To:
```csharp
public DisplayViewModel(DatabaseService db, ProjectionViewModel projection, ShortcutService shortcuts)
{
    _db = db;
    _projection = projection;
    _shortcuts = shortcuts;
```

- [ ] **Step 3: Replace HandleKey**

Replace the entire `HandleKey` method (lines ~358–387):

```csharp
public void HandleKey(Key key, ModifierKeys modifiers)
{
    // Space is kept as a secondary hardcoded alias for slide_next
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
```

- [ ] **Step 4: Build**

```bash
dotnet build Cantio
```
Expected: compiler error in `MainWindow.xaml.cs` — `DisplayViewModel` constructor now requires 3 args. Fix in next task.

---

## Task 5 — MainWindow.xaml.cs wiring

**Files:**
- Modify: `Cantio/MainWindow.xaml.cs`

Create the shared `ShortcutService`, pass to `DisplayViewModel` and `ShortcutsViewModel`. Add shortcut handling for tabs and search_open in `OnPreviewKeyDown`. Add Down-arrow handlers for `SearchBoxShow` and `SearchBoxSongs`.

- [ ] **Step 1: Add fields**

```csharp
private readonly ShortcutService _shortcutService;
private readonly ShortcutsViewModel _shortcutsVm;
```

- [ ] **Step 2: Update constructor**

```csharp
public MainWindow(DatabaseService db)
{
    InitializeComponent();

    _db = db;
    _shortcutService = new ShortcutService();

    _vm = new DisplayViewModel(db, new ProjectionViewModel(), _shortcutService);
    DataContext = _vm;

    _importVm = new ImportViewModel(db);
    PaneImport.DataContext = _importVm;

    _songEditorVm = new SongEditorViewModel(db);
    PaneSongs.DataContext = _songEditorVm;

    _setlistVm = new SetlistViewModel(db);
    PaneSets.DataContext = _setlistVm;

    _szablonVm = new SzablonViewModel(db, _vm.Projection);
    _szablonVm.Saved += () => _vm.RebuildSlides();
    PaneTemplate.DataContext = _szablonVm;

    _shortcutsVm = new ShortcutsViewModel(db, _shortcutService);
    PaneShortcuts.DataContext = _shortcutsVm;

    _importVm.SetlistsImported += async () => await _setlistVm.LoadAsync();
    _setlistVm.PinnedChanged += async () => await _vm.LoadPinnedSetlistsAsync();
    _setlistVm.LoadForDisplayRequested += setlist =>
    {
        _ = _vm.LoadPinnedSetlistAsync(setlist);
        ShowPane(PaneShow, TabShow);
    };

    Loaded += async (_, _) => await _vm.InitializeAsync();
    KeyDown += _vm.OnKeyDown;
}
```

- [ ] **Step 3: Add tab/search shortcut handling at start of `OnPreviewKeyDown`**

Add BEFORE the existing Ctrl+S block:

```csharp
// Configured tab shortcuts
if (e.OriginalSource is not TextBox && e.OriginalSource is not RichTextBox)
{
    var mods = e.KeyboardDevice.Modifiers;
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabShow))
    { ShowPane(PaneShow, TabShow); e.Handled = true; return; }
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabSongs))
    { ShowPane(PaneSongs, TabSongs); e.Handled = true; return; }
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabSets))
    { ShowPane(PaneSets, TabSets); e.Handled = true; return; }
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabTemplate))
    { ShowPane(PaneTemplate, TabTemplate); e.Handled = true; return; }
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.TabImport))
    { ShowPane(PaneImport, TabImport); e.Handled = true; return; }
    if (_shortcutService.IsMatch(e.Key, mods, ShortcutService.SearchOpen))
    { _vm.OpenSetlistSearchCommand.Execute(null); e.Handled = true; return; }
}
```

- [ ] **Step 4: Add `ShowPane` entries for `PaneShortcuts`**

In `ShowPane`, add hiding + tracking:

```csharp
PaneShortcuts.Visibility = Visibility.Collapsed;
```
(in the "Hide all panes" block)

And in `_activeTab` assignment:
```csharp
: pane == PaneShortcuts ? "shortcuts"
```

- [ ] **Step 5: Add tab-click handler**

```csharp
private void TabShortcuts_Click(object sender, RoutedEventArgs e) => ShowPane(PaneShortcuts, TabShortcuts);
```

- [ ] **Step 6: Add Down-arrow handlers for search boxes**

```csharp
private void SearchBoxShow_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key != Key.Down) return;
    SongListShow.Focus();
    if (SongListShow.Items.Count > 0 && SongListShow.SelectedIndex < 0)
        SongListShow.SelectedIndex = 0;
    e.Handled = true;
}

private void SearchBoxSongs_PreviewKeyDown(object sender, KeyEventArgs e)
{
    if (e.Key != Key.Down) return;
    SongListSongs.Focus();
    if (SongListSongs.Items.Count > 0 && SongListSongs.SelectedIndex < 0)
        SongListSongs.SelectedIndex = 0;
    e.Handled = true;
}
```

- [ ] **Step 7: Build**

```bash
dotnet build Cantio
```
Expected: errors about missing `PaneShortcuts`, `TabShortcuts`, `SongListShow`, `SongListSongs` — fix in Task 6.

---

## Task 6 — MainWindow.xaml changes

**Files:**
- Modify: `Cantio/MainWindow.xaml`

Four changes: (A) add TabShortcuts button, (B) name the two song ListBoxes, (C) wire PreviewKeyDown on search boxes, (D) add PaneShortcuts grid.

### A — Add TabShortcuts button

- [ ] **Step 1: Add button to TabBar**

After the `TabImport` button (line ~612), add:

```xml
<Button x:Name="TabShortcuts" Content="Skróty"
        Style="{StaticResource TabBtn}" Height="56"
        Click="TabShortcuts_Click"/>
```

### B — Name the song ListBoxes

- [ ] **Step 2: Add `x:Name="SongListShow"` to the Songs ListBox in PaneShow** (currently at line ~740)

Find:
```xml
<ListBox ItemsSource="{Binding Songs}"
         SelectedItem="{Binding SelectedSong}"
```
Add `x:Name="SongListShow"`:
```xml
<ListBox x:Name="SongListShow"
         ItemsSource="{Binding Songs}"
         SelectedItem="{Binding SelectedSong}"
```

- [ ] **Step 3: Add `x:Name="SongListSongs"` to FilteredSongs ListBox in PaneSongs** (currently at line ~1804)

Find:
```xml
<ListBox ItemsSource="{Binding FilteredSongs}"
         SelectedItem="{Binding SelectedSongInList}"
```
Add:
```xml
<ListBox x:Name="SongListSongs"
         ItemsSource="{Binding FilteredSongs}"
         SelectedItem="{Binding SelectedSongInList}"
```

### C — Wire PreviewKeyDown on search boxes

- [ ] **Step 4: Add handler to SearchBoxShow** (line ~717)

Add attribute:
```xml
PreviewKeyDown="SearchBoxShow_PreviewKeyDown"
```

- [ ] **Step 5: Add handler to SearchBoxSongs** (line ~1779)

Add attribute:
```xml
PreviewKeyDown="SearchBoxSongs_PreviewKeyDown"
```

### D — Add PaneShortcuts grid

- [ ] **Step 6: Add PaneShortcuts after PaneTemplate** (just before `<!-- IMPORT -->`)

Find where PaneImport starts and insert before it:

```xml
<!-- SKRÓTY -->
<Grid x:Name="PaneShortcuts" Visibility="Collapsed" Background="#0f1117">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="40,32,40,32" MaxWidth="560">

            <TextBlock Text="Skróty klawiszowe"
                       FontFamily="{StaticResource HeaderFont}"
                       FontSize="28" Foreground="#c9a84c" Margin="0,0,0,24"/>

            <!-- helper style inline — row label+capture -->
            <StackPanel.Resources>
                <Style x:Key="ShortcutRow" TargetType="Grid">
                    <Setter Property="Margin" Value="0,0,0,12"/>
                </Style>
                <Style x:Key="ShortcutLabel" TargetType="TextBlock">
                    <Setter Property="Foreground" Value="#e8eaf0"/>
                    <Setter Property="FontSize" Value="16"/>
                    <Setter Property="VerticalAlignment" Value="Center"/>
                </Style>
                <Style x:Key="ShortcutCapture" TargetType="TextBox"
                       BasedOn="{StaticResource DarkTextBox}">
                    <Setter Property="Width" Value="160"/>
                    <Setter Property="HorizontalAlignment" Value="Right"/>
                    <Setter Property="TextAlignment" Value="Center"/>
                    <Setter Property="FontSize" Value="15"/>
                    <Setter Property="Padding" Value="8,6"/>
                    <Setter Property="helpers:KeyCaptureHelper.IsEnabled" Value="True"/>
                </Style>
            </StackPanel.Resources>

            <!-- Navigation group -->
            <TextBlock Text="Nawigacja" Foreground="#959fb9" FontSize="13"
                       Margin="0,0,0,10" FontFamily="{StaticResource BodyFont}"/>

            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Następny slajd" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding SlideNext, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Poprzedni slajd" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding SlidePrev, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Następna pieśń" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding SongNext, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Poprzednia pieśń" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding SongPrev, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Pokaż / Zaciemnij ekran" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding Blank, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>

            <!-- Tabs group -->
            <TextBlock Text="Przełączanie zakładek" Foreground="#959fb9" FontSize="13"
                       Margin="0,16,0,10" FontFamily="{StaticResource BodyFont}"/>

            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Tab: Wyświetlanie" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding TabShow, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Tab: Pieśni" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding TabSongs, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Tab: Zestawy" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding TabSets, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Tab: Szablon" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding TabTemplate, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>
            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Tab: Import" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding TabImport, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>

            <!-- Search group -->
            <TextBlock Text="Wyszukiwarka" Foreground="#959fb9" FontSize="13"
                       Margin="0,16,0,10" FontFamily="{StaticResource BodyFont}"/>

            <Grid Style="{StaticResource ShortcutRow}">
                <TextBlock Text="Otwórz wyszukiwarkę zestawu" Style="{StaticResource ShortcutLabel}"/>
                <TextBox Text="{Binding SearchOpen, UpdateSourceTrigger=PropertyChanged}"
                         Style="{StaticResource ShortcutCapture}"/>
            </Grid>

            <!-- Buttons -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,28,0,0">
                <Button Content="Przywróć domyślne"
                        Command="{Binding ResetCommand}"
                        Style="{StaticResource DarkBtn}"
                        Height="48" Padding="20,0" Margin="0,0,12,0"/>
                <Button Content="Zapisz"
                        Command="{Binding SaveCommand}"
                        Style="{StaticResource AccentBtn}"
                        Height="48" Padding="24,0"/>
            </StackPanel>

        </StackPanel>
    </ScrollViewer>
</Grid>
```

> **Note on xmlns:** `helpers:KeyCaptureHelper` needs the helpers namespace. Check the top of MainWindow.xaml — if `xmlns:helpers="clr-namespace:Cantio.Helpers"` is already declared, no change needed. If not, add it to the `<Window>` element.

- [ ] **Step 7: Build and fix any XAML errors**

```bash
dotnet build Cantio
```
Expected: 0 errors.

---

## Task 7 — Final wiring and verification

- [ ] **Step 1: Run the app**

```bash
dotnet run --project Cantio
```

- [ ] **Step 2: Verify — navigation shortcuts**
  - Press Right → next slide
  - Press Left → prev slide
  - Press Down → next song
  - Press Up → prev song
  - Press Escape → toggle blank/show

- [ ] **Step 3: Verify — Skróty tab**
  - Click "Skróty" tab → `PaneShortcuts` appears with all rows
  - Each TextBox shows the default label (Right, Left, Down, Up, Escape, empty for tabs)

- [ ] **Step 4: Verify — KeyCapture**
  - Click a capture TextBox → focus shows selected text
  - Press letter "B" → box shows "Ctrl+B"
  - Press "F5" → box shows "F5"
  - Press Ctrl+F → box shows "Ctrl+F"
  - Press Escape → box clears

- [ ] **Step 5: Verify — save and reload**
  - Assign "F5" to "Następny slajd", click Zapisz
  - Close and reopen app
  - Press F5 → next slide works
  - Press Right → no action (old default gone — configured key replaced it)

- [ ] **Step 6: Verify — tab shortcuts**
  - Assign "F1" to "Tab: Wyświetlanie", save
  - Press F1 from any pane → switches to Show tab

- [ ] **Step 7: Verify — Down-arrow in search**
  - Go to Show tab, type in search box
  - Press Down → focus moves to song list, first item selected, can continue navigating with arrows

- [ ] **Step 8: Verify — Down-arrow in Songs editor search**
  - Go to Songs tab, type in search box
  - Press Down → focus moves to song list

- [ ] **Step 9: Commit**

```bash
git add Cantio/Services/ShortcutService.cs \
        Cantio/ViewModels/ShortcutsViewModel.cs \
        Cantio/Helpers/KeyCaptureHelper.cs \
        Cantio/ViewModels/DisplayViewModel.cs \
        Cantio/MainWindow.xaml.cs \
        Cantio/MainWindow.xaml
git commit -m "feat: configurable keyboard shortcuts + search box Down-arrow navigation"
```

---

## Notes

- **Ctrl+S conflict:** Ctrl+S is reserved for save (handled before shortcut routing in `OnPreviewKeyDown`). Users should avoid assigning Ctrl+S to a shortcut.
- **Space fallback:** Space always triggers next-slide (hardcoded) regardless of configuration. This preserves compatibility with presentation remotes.
- **Home key:** Always goes to first slide (hardcoded, not configurable).
- **Blind accessibility:** This is the first iteration. Future work: assign shortcuts to every interactive element (song list navigation, verse selection, set management) and document the full keyboard map.
