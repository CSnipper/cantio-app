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

## Pułapki

- `CurrentSlideIndex` zmiana wywołuje `_projection.SetSlide()` — nie rób tego ręcznie
- `SetBlanked(true)` zapisuje pending slide, `SetBlanked(false)` go aplikuje
- ESC w `OnPreviewKeyDown` zachowuje stan blanku
- `HandleKey` w DisplayViewModel obsługuje ←→ (slajdy) i ↑↓ (pieśni w zestawie)
