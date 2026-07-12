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

## Psalm mode — rozpoznawanie refrenu

Prefiksy traktowane jako refren (`type = "c"`):
- `Refren:` — zwykły refren psalmu
- `Aklamacja:` — aklamacja (dodano v1.42); dotyczy też aklamacji postnych
- `Albo:` w kolejnym bloku po refrenie → scalany w jeden slajd (oddzielony `\n\nAlbo:\n`)

Kod w `DisplayViewModel` (~linia 695):
```csharp
bool isChorus = block.StartsWith("Refren:", ...) || block.StartsWith("Aklamacja:", ...);
```

## ImportViewModel — pułapki

- **Wybór folderu OpenSong:** NIE używaj `CommonOpenFileDialog` (WindowsAPICodePack) — crashuje w release
  Używaj `Microsoft.Win32.OpenFolderDialog` (v1.43+)
- `BrowseXmlOrFolder` używa zwykłego `OpenFileDialog` (pliki, nie folder) — OK

## DisplayViewModel — pułapki

- `CurrentSlideIndex` zmiana wywołuje `_projection.SetSlide()` — nie rób tego ręcznie
- `SetBlanked(true)` zapisuje pending slide, `SetBlanked(false)` go aplikuje
- ESC w `OnPreviewKeyDown` zachowuje stan blanku
- `HandleKey` w DisplayViewModel obsługuje ←→ (slajdy) i ↑↓ (pieśni w zestawie)
- `PrevSong` (↑) ładuje poprzednią pieśń od PIERWSZEGO slajdu; `PrevSongLastSlide()` (tylko fallback z `PrevSlide` ←) — od ostatniego slajdu. Nie scalać ich z powrotem.
- `HandleKey` musi być wywoływany zanim sprawdzimy fokus list — blokuje go tylko aktywny TextBox/RichTextBox
- `RelativeSource=AncestorType` w `MultiDataTrigger.Conditions` jest zawodne w WPF — używaj code-behind z DP i subskrypcją PropertyChanged

## Popup focus
Żeby TextBox w Popup dostał focus przy otwarciu: dodaj `Opened="Handler"` do Popup, w handler wywołaj `textBox.Focus()`.

## Preview-only tag (v1.45+)
`TextFormatTag.PreviewOnly=true` → treść `{tag}...{/tag}` wystrippowana z tekstu projektora, widoczna tylko w podglądzie operatora.
- `SlideLayoutService.StripPreviewOnlyTags(text, names)` — stripuje bloki preview-only
- `Slide.OperatorText` / `Slide.OperatorFontSize` / `HasPreviewOnlyContent` — ustawiane w `RebuildSlides`
- `OnCurrentSlideIndexChanged`: gdy `HasPreviewOnlyContent` → `SetSlide` (stripped) + `SetOperatorOverride` (full); projektor i operator działają równocześnie (inaczej niż psalm mode gdzie projektor trzyma poprzedni slajd)

## TextFormatTag XAML
Dodając pole do `TextFormatTag` — zaktualizuj DWIE gridy w MainWindow.xaml: nagłówek (tuż przed `ItemsControl`) i `DataTemplate` w `ItemsControl` — mają różne definicje kolumn.

## SzablonViewModel — komendy DB

Dodane w v1.4+:
- `BackupDatabaseCommand` — `SaveFileDialog` → `File.Copy`
- `RestoreDatabaseCommand` → `File.Copy` + `RestartApp()`
- `ClearDatabaseAsync` → `db.ClearAllDataAsync()` + restart
- `ExportZipCommand` / `ImportZipCommand` — `System.IO.Compression.ZipFile`
- `RunOnStartup` — rejestr `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- `ImportPsalmyCommand` — wywołuje `DatabaseService.ImportPsalmySeedAsync()`; blokuje się przez `IsImportingPsalmy`
