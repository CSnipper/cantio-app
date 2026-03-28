# Cantio/ViewModels — lokalny kontekst

## Mapa ViewModels

| ViewModel | Pane | Odpowiada za |
|---|---|---|
| `DisplayViewModel` | `PaneShow` | Wyświetlanie na projektorze, nawigacja slajdami, zestaw |
| `SongEditorViewModel` | `PaneSongs` | Edycja pieśni, zarządzanie kategoriami |
| `SetlistViewModel` | `PaneSets` | Zestawy, grupy zestawów |
| `SzablonViewModel` | `PaneTemplate` | Szablon projekcji (czcionka, kolory, tło) |
| `ImportViewModel` | `PaneImport` | Import z OpenLP/OpenSong/OSZ |
| `ProjectionViewModel` | — | Stan okna projekcji (tekst, blank, styl) |

## Właściwości wrapperów

### VerseEditorItem (w SongEditorViewModel)
Wrapper zwrotki z UI-state. `Label` wyliczany z `Type` i `Number`.

### CategoryEditorItem (w SongEditorViewModel)
Wrapper kategorii z `IsEditing`/`EditName` dla inline edit.

## Kluczowe zależności

```
DisplayViewModel
  └── ProjectionViewModel (ref)
  └── DatabaseService

SongEditorViewModel
  └── DatabaseService

SetlistViewModel
  └── DatabaseService

SzablonViewModel
  └── DatabaseService
  └── ProjectionViewModel (ref) — do podglądu na żywo
```

## Komunikacja między VM

- `SzablonViewModel.Saved` event → `DisplayViewModel.RebuildSlides()`
- `ImportViewModel.SetlistsImported` event → `SetlistViewModel.LoadAsync()`
- Brak innych bezpośrednich zależności między VM

## Psalm mode (DisplayViewModel)

- `IsPsalmMode` — auto-włączane w `LoadVersesAsync` gdy `song.CategoryId == PsalmCategoryId`
- `OnCurrentSlideIndexChanged`: gdy `IsPsalmMode && slide.VerseType != "c"` → `_projection.SetOperatorSlide(slide)` (podgląd operatora); w pozostałych przypadkach → `_projection.ClearOperatorSlide()` + `SetSlide()`
- `ProjectedSlide` — slajd aktualnie wyświetlany na projektorze; używany do złotego paska w liście slajdów
- `ParseAndApplyText(string, bool isPsalm)` — gdy `isPsalm=true`, pomija budowanie PlayOrder z auto-wstawianiem refrenu; kolejność zostaje taka jak w wklejonym tekście

## ProjectionViewModel — operator override

- `IsOperatorOverride` — `true` gdy podgląd pokazuje inny tekst niż projektor (tryb psalm)
- `SetOperatorSlide(slide)` — ustawia `OperatorSlideText`, `OperatorFontSize`, `IsOperatorOverride=true`
- `ClearOperatorSlide()` — `IsOperatorOverride=false`
- `ProjectionView.xaml.cs` subskrybuje `PropertyChanged` na `IsOperatorOverride` i `IsBlank` → `SyncOperatorVisibility()`

## Pułapki

- `CurrentSlideIndex` zmiana wywołuje `_projection.SetSlide()` — nie rób tego ręcznie
- `SetBlanked(true)` zapisuje pending slide, `SetBlanked(false)` go aplikuje
- ESC w `OnPreviewKeyDown` zachowuje stan blanku
- `HandleKey` w DisplayViewModel obsługuje ←→ (slajdy) i ↑↓ (pieśni w zestawie)
- `HandleKey` musi być wywoływany zanim sprawdzimy fokus list — blokuje go tylko aktywny TextBox/RichTextBox
- `RelativeSource=AncestorType` w `MultiDataTrigger.Conditions` jest zawodne w WPF — używaj code-behind z DP i subskrypcją PropertyChanged
