# Display Tab Improvements — Design Spec
**Date:** 2026-03-17
**Project:** Cantio (WPF liturgical song projection app)
**Status:** Approved by user

---

## Overview

Four improvements to the Display tab (PaneShow) of the Cantio application:

1. Setlist header rename + "Otwórz zestaw" search popup
2. Inline verse editor panel (slide-in from right)
3. Preview window mirrors projection screen exactly
4. Verse list: hide formatting tags + auto-scroll to active slide

---

## Feature 1: Setlist Header + "Otwórz zestaw" Popup

### Goal
Allow the organist to quickly load any saved setlist by searching, alongside the existing pinned setlist buttons.

### UI Changes
- Label "Kolejność wykonania" → **"ZESTAW"** in the setlist column header
- New button `[⊞ Otwórz zestaw]` next to the label
- Clicking opens a WPF `Popup` (`StaysOpen=False`, closes on click-outside) containing:
  - `TextBox` for live search (filters by setlist name)
  - `ListBox` of filtered results showing name + creation date
  - Clicking a result loads the setlist and closes the popup

### ViewModel Changes (`DisplayViewModel`)
- `IsSetlistSearchOpen` (bool, `[ObservableProperty]`) — controls `Popup.IsOpen`
- `SetlistSearchText` (string, `[ObservableProperty]`) — filter text; `partial void OnSetlistSearchTextChanged` updates `FilteredSetlists`
- `AllSetlists` (List<Setlist>) — private field, populated each time popup opens
- `FilteredSetlists` (ObservableCollection<Setlist>) — filtered view of `AllSetlists`
- `OpenSetlistSearchCommand` — calls `DatabaseService.GetAllSetlists()`, populates `AllSetlists` and `FilteredSetlists`, then sets `IsSetlistSearchOpen = true`. Loading happens on-demand each time (always fresh, no stale data risk).
- `LoadSetlistFromSearchCommand(Setlist)` — calls the existing `LoadPinnedSetlistAsync(setlist)` internally (no duplicate logic), then sets `IsSetlistSearchOpen = false`

### Notes
- Pinned setlist bar at bottom remains unchanged
- `GetAllSetlists()` already exists in `DatabaseService`

---

## Feature 2: Inline Verse Editor (Slide-in Panel)

### Goal
Allow quick text fixes to song verses directly from the setlist, without switching to the Song Editor tab.

### UI Changes
- Add `✏` icon button to each setlist item row, before the `×` delete button
- `✏` button is disabled (grayed out) when `IsInlineEditorOpen == true` to prevent concurrent edits
- Clicking `✏` slides in an editor panel from the right, covering the setlist column
- Panel content:
  - Header: song title + `×` close button (cancel without saving)
  - `ScrollViewer` with `ItemsControl` — one `TextBox` per verse (`AcceptsReturn=True`, multi-line) with label (e.g., "Zwrotka 1", "Refren")
  - Footer: `[Zapisz]` and `[Anuluj]` buttons

### Animation
- Panel sits in the same `Grid` as the setlist column (overlays it)
- `TranslateTransform` animated: X=350→0 (open), X=0→350 (close) via `DoubleAnimation` in `Storyboard`
- Width matches setlist column width (~350px)

### Data Model (`EditableVerse`)
```csharp
int Id          // Verse.Id — used for targeted DB update
string Type     // "v", "c", "b", etc. — used to build Label
int Position    // Verse.Position — used to build Label
string Text     // editable copy of Verse.Text
string Label    // computed display-only: e.g. "Zwrotka 1", "Refren"
                // computed from Type + counter (same logic as VerseEditorItem.Label
                // in SongEditorViewModel); not persisted
```

### ViewModel Changes (`DisplayViewModel`)
- `IsInlineEditorOpen` (bool, `[ObservableProperty]`)
- `InlineEditorTitle` (string, `[ObservableProperty]`) — song title for panel header
- `EditableVerses` (ObservableCollection<EditableVerse>, `[ObservableProperty]`) — working copy
- `OpenInlineEditorCommand(SetlistItem)` — loads verses for the song, builds `EditableVerses` as a deep copy, sets `InlineEditorTitle`, sets `IsInlineEditorOpen = true`
- `SaveInlineEditCommand` — for each `EditableVerse` whose `Text` differs from DB, calls `DatabaseService.SaveVerseTextAsync(int verseId, string newText)` (see below), then calls `RebuildSlides()`, then closes panel
- `CancelInlineEditCommand` — sets `IsInlineEditorOpen = false`, discards `EditableVerses`

### Guard: concurrent edit / item removal
- When `IsInlineEditorOpen == true`: the `✏` button on all other items is disabled via `IsEnabled="{Binding DataContext.IsInlineEditorOpen, RelativeSource=..., Converter={StaticResource InverseBoolConverter}}"` (existing `InverseBoolConverter` in project)
- If the edited setlist item is deleted while panel is open: `CancelInlineEditCommand` is called automatically in `RemoveFromSetlistCommand` if `IsInlineEditorOpen == true` (defensive close)

### New DatabaseService method
```csharp
public async Task SaveVerseTextAsync(int verseId, string newText)
{
    // Targeted UPDATE: UPDATE Verses SET Text = @newText WHERE Id = @verseId
    await using var ctx = new CantioDbContext();
    var verse = await ctx.Verses.FindAsync(verseId);
    if (verse != null) { verse.Text = newText; await ctx.SaveChangesAsync(); }
}
```
No migration needed (no schema change).

### Scope
- Edits only `Verse.Text` — no changes to title, author, category, play order, `PlayOrderJson`
- Does not affect setlist structure

---

## Feature 3: Preview = Projection Screen

### Goal
The preview thumbnail in PaneShow shows exactly what the projection window shows — same background, gradient, image, shadow, text formatting.

### Approach
Extract a `ProjectionView` UserControl from the existing `ProjectionWindow.xaml` content. Both the projection window and the preview use the same control bound to the same `ProjectionViewModel`.

### New File: `Views/ProjectionView.xaml`
- UserControl containing all existing projection XAML (background, image, text, shadow, blank overlay)
- Designed at 1920×1080 natural size

### Changes to `ProjectionWindow.xaml`
- Replace inline content with `<local:ProjectionView DataContext="{Binding}"/>`

### Changes to `MainWindow.xaml` (PaneShow preview)
```xml
<Viewbox Stretch="Uniform">
    <local:ProjectionView DataContext="{Binding Projection}"
                          Width="1920" Height="1080"/>
</Viewbox>
```
- `Projection` property already exists on `DisplayViewModel`: `public ProjectionViewModel Projection => _projection;` — no new code needed
- `Viewbox` scales everything uniformly: fonts, shadows, background all scale proportionally — this is intentional and correct
- `DropShadowEffect` at small preview size is scaled by Viewbox and renders correctly; no suppression needed

### Notes
- No changes to `ProjectionViewModel`
- Only reorganization of XAML into a reusable UserControl

---

## Feature 4: Verse List — Hide Tags + Auto-Scroll

### Goal
- Formatting tags like `{wk}`, `{/wk}`, `{big}`, `{/big}` must not be visible in the verse list
- When the active slide changes, the list scrolls to show it automatically

### Hide Tags: `StripTagsConverter`
- New `Helpers/StripTagsConverter.cs` (IValueConverter)
- Regex pattern: `\{/?\\w+\}` — matches `{tagname}` and `{/tagname}` where tagname is `\w+`
- Consistent with the pattern `TextBlockHelper.ParseInlines` already recognizes
- Applied in XAML: `Text="{Binding Text, Converter={StaticResource StripTagsConverter}}"`
- Original `Slide.Text` unchanged — tags still used for projection rendering

### Auto-Scroll: `ListBoxAutoScrollBehavior`
- New `Helpers/ListBoxAutoScrollBehavior.cs` (attached property, standard WPF pattern)
- Attaches to `SelectionChanged` event on the verse `ListBox`
- Calls `listBox.ScrollIntoView(listBox.SelectedItem)` on each selection change
- Applied in XAML: `helpers:ListBoxAutoScrollBehavior.IsEnabled="True"`
- Triggers on: keyboard navigation, song selection, setlist load — all paths that change `CurrentSlideIndex`

---

## Files Changed / Created

| File | Change |
|------|--------|
| `Views/ProjectionView.xaml` + `.xaml.cs` | **New** — extracted UserControl |
| `Views/ProjectionWindow.xaml` | Modified — uses ProjectionView |
| `Helpers/StripTagsConverter.cs` | **New** |
| `Helpers/ListBoxAutoScrollBehavior.cs` | **New** |
| `ViewModels/DisplayViewModel.cs` | Modified — Features 1 & 2 |
| `Services/DatabaseService.cs` | Modified — add `SaveVerseTextAsync` |
| `MainWindow.xaml` | Modified — all 4 features |

No EF migrations needed.

---

## Out of Scope
- Full song editor (title, author, category, play order) — separate tab
- Changes to pinned setlist bar
- Changes to SlideLayoutService or ProjectionViewModel
