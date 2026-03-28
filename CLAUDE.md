# Cantio — Claude Code Context

## Cel projektu
Natywna aplikacja WPF do wyświetlania pieśni liturgicznych na projektorze w kościele.
Obsługa dotykowa, import z OpenLP/OpenSong, zestawy na nabożeństwo.

## Stack
- C# 12 / .NET 10 / WPF
- CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableProperty)
- Entity Framework Core + SQLite
- WpfScreenHelper (wielomonitorowość)

## Mapa repo
```
Cantio/
├── Models/          – encje EF: Category, Song, Verse, Setlist, SetlistItem
├── ViewModels/      – MVVM: DisplayViewModel, SongEditorViewModel, SetlistViewModel,
│                           SzablonViewModel, ImportViewModel, ProjectionViewModel
├── Views/           – dodatkowe okna: ProjectionWindow
├── Services/        – CantioDbContext, DatabaseService, SlideLayoutService
│   └── Import/      – OpenLpImporter, OpenSongImporter, OszImporter
├── Helpers/         – konwertery WPF, BoolToVis, InverseBoolToVis, TextBlockHelper
└── Assets/Fonts/    – Playfair Display (nagłówki), Lato (reszta UI), Cinzel (legacy)
```

## Komendy
```bash
# Build
dotnet build

# Run
dotnet run --project Cantio

# Installer (Inno Setup 6 musi być zainstalowany)
cd Installer && powershell -ExecutionPolicy Bypass -File build-installer.ps1
# Wynik: Installer/Output/CantioSetup-{version}.exe

# Migracje EF (uruchamiane automatycznie przy starcie przez App.xaml.cs)
dotnet ef migrations add <NazwaMigracji> --project Cantio
dotnet ef database update --project Cantio
```

## Kluczowe zasady — przeczytaj zanim cokolwiek zmienisz

### MVVM
- `partial class` + `[ObservableProperty]` i `[RelayCommand]` — zawsze
- Async RelayCommand: `private async Task`, NIE `public`
- CommunityToolkit generuje `XxxCommand` (sync) i `XxxAsyncCommand` (async) — w XAML używaj bez sufiksu `Async`
- Stringi inicjalizuj: `string _foo = string.Empty`

### WPF / XAML
- Jeden plik `MainWindow.xaml` — zakładki przełączane przez `Visibility`
- DataContext ustawiany w `MainWindow.xaml.cs`, NIE przez XAML
- `Value="{Binding}"` w `DataTrigger` — NIEDOZWOLONE, używaj `MultiBinding` z `IsEqualConverter`
- `LineHeight` musi być ≥ `FontSize` — nigdy mniejsze
- `FontSize` w customowym `ControlTemplate` wymaga `TextElement.FontSize="{TemplateBinding FontSize}"` na `ScrollViewer`
- Nie ustawiaj jednocześnie `ItemTemplate` i `DisplayMemberPath` na ListBox
- Scrollowanie poziome: `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`
- **`RelativeSource=AncestorType` w `MultiDataTrigger.Conditions` jest zawodne** — WPF może cicho nie zbindować; zamiast tego użyj Dependency Property + code-behind z subskrypcją `PropertyChanged`

### Baza danych
- `DatabaseService` to JEDYNA warstwa dostępu — ViewModels nie używają DbContext bezpośrednio
- Ustawienia: tabela `settings` jako klucz-wartość
- Grupy zestawów: `settings["setlist_groups"]` = CSV
- Nowa właściwość w modelu = nowa migracja EF — nie zapomnij

### Projekcja
- `ProjectionViewModel.DisplayLineHeight` = `FontSize * LineHeightMultiplier`
- NIE używaj `LineHeight` jako nazwy właściwości — konflikt z WPF
- Po zmianie ustawień szablonu wywołaj `DisplayViewModel.RebuildSlides()`
- Wymiary ekranu przez `WpfScreenHelper`, NIE hardkoduj 1920x1080
- `ProjectionView` używany w dwóch miejscach: okno projektora i podgląd w MainWindow — zawsze ustaw `IsPreviewMode="True"` na instancjach podglądu
- W trybie podglądu (`IsPreviewMode=True`) overlay wygaszenia (`BlankOverlay`) jest zawsze `Collapsed` — operator widzi tekst cały czas
- Psalm mode: `IsPsalmMode` w `DisplayViewModel`; gdy zwrotka (VerseType != "c") → `_projection.SetOperatorSlide(slide)` (podgląd pokazuje zwrotkę); gdy refren → `_projection.SetSlide(slide)` (projektor aktualizuje)
- `ProjectionViewModel.IsOperatorOverride` przełączany przez `SetOperatorSlide()`/`ClearOperatorSlide()` — `ProjectionView.xaml.cs` subskrybuje PropertyChanged i wywołuje `SyncOperatorVisibility()`

### UI / Styl
- Kolory: tło `#0f1117`, panel `#161b25`, akcent `#c9a84c`, tekst `#e8eaf0`
- Czcionka nagłówkowa: `HeaderFont` (aktualnie Playfair Display) — `FontFamily="{StaticResource HeaderFont}"`
- Czcionka UI: `BodyFont` (aktualnie Lato) — `FontFamily="{StaticResource BodyFont}"` (domyślna dla całego okna)
- Klucze celowo generyczne — łatwa zamiana czcionki przez zmianę tylko App.xaml
- Przyciski dotykowe: min. wysokość 58-70px
- Każdy kontrolek ma własny `ControlTemplate` — nie używaj domyślnych stylów WPF

### Zasoby zewnętrzne (czcionki, obrazy, itp.)
- Jeśli zadanie wymaga pobrania pliku (czcionka, ikona, itp.) — **przerwij i każ użytkownikowi pobrać plik samodzielnie**, podając dokładną nazwę i lokalizację docelową. Nie próbuj pobierać zasobów automatycznie.

### Klawiatura — skróty projekcji
- Skróty nawigacji projekcji (`HandleKey` w `DisplayViewModel`) muszą działać **niezależnie od fokusu** — blokuje je tylko aktywny `TextBox`/`RichTextBox`
- NIE dodawaj `SongListShow.IsKeyboardFocusWithin` (ani żadnej innej listy) do warunków blokujących `HandleKey` — spowoduje to, że skróty przestają działać po kliknięciu listy myszką
- Spacja jest twardym aliasem dla `SlideNext` (piloty zdalne) — zdefiniowana w `HandleKey`, nie w ShortcutService

### Czego unikać
- `UseWindowsForms=true` — konflikty namespace
- `WindowState.Maximized` po ustawieniu `Left`/`Top` — użyj `Normal` + jawne wymiary
- `MessageBox.Show` w kodzie produkcyjnym — tylko debug
- Blazor / MAUI / WinForms — projekt jest WPF
- Edycji plików `Strings.*.xaml` przez PowerShell — wstawia garbled encoding dla polskich znaków i łamie build błędem MC3000; używaj wyłącznie Edit tool

### Wzorce UI — kategorie i grupy
- Kategorie edytowane inline w lewej kolumnie (nie w edytorze pieśni); `CategoryEditorItem` w `CategoryItems`; drag & drop przez `CategoryItem_MouseMove`/`CategoryList_Drop` w code-behind; nowa kategoria pojawia się na początku listy z `IsEditing=true`
- Grupy zestawów: edytowane przez popup `IsGroupPopupOpen`; `GroupEditorItem` to ObservableObject z `OriginalName`/`EditName`/`IsEditing`; grupy zapisywane jako CSV w `settings["setlist_groups"]`
- W ItemTemplate popupu wyszukiwania zestawów: wiersz podrzędny to `Binding Group` (nazwa grupy), nie `CreatedAt`

## Utrzymanie plików CLAUDE.md

### Zasada lokalnych plików CLAUDE.md
W każdym podfolderze zawierającym logicznie wyodrębniony kod istnieje własny `CLAUDE.md` z kontekstem specyficznym dla tego folderu:
- `Cantio/ViewModels/CLAUDE.md` — mapa ViewModels, zależności, pułapki
- `Cantio/Services/CLAUDE.md` — zasady DatabaseService, SlideLayoutService, importerzy

**Gdy modyfikujesz pliki w danym folderze — zaktualizuj tamtejszy `CLAUDE.md`** o wszelkie nieoczywiste decyzje, nowe właściwości/metody, zmienione wzorce lub pułapki odkryte podczas pracy. Dotyczy to zarówno nowych funkcji, jak i poprawek błędów, które ujawniły coś nieoczywistego w kodzie.

Aktualizuj lokalny CLAUDE.md natychmiast po zakończeniu zmian w plikach z danego folderu — nie odkładaj na później.

## Installer

### Narzędzia (zainstalowane na maszynie)
- **Inno Setup 6** — `C:\Program Files (x86)\Inno Setup 6\iscc.exe`

### Pliki
- `Installer/build-installer.ps1` — główny skrypt budujący (uruchamiaj zawsze stąd)
- `Installer/cantio.iss` — skrypt Inno Setup
- `Installer/publish/` — output `dotnet publish` (generowany przez skrypt, nie commituj)
- `Installer/Output/CantioSetup-{version}.exe` — gotowy installer

### Seed baza danych
- Plik `Cantio.db` musi istnieć w **katalogu głównym repo** (`/Cantio.db`)
- Podczas instalacji jest kopiowany do folderu aplikacji
- Przy pierwszym uruchomieniu app kopiuje go do `%LOCALAPPDATA%\Cantio\cantio.db`
- Reinstalacja/upgrade: jeśli `%LOCALAPPDATA%\Cantio\cantio.db` już istnieje → seed pomijany

### Wersja
- Czytana automatycznie z `<Version>` w `Cantio/Cantio.csproj`
- Aktualna: `1.0.0`

### Jak zbudować
```bash
cd Installer
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```
Skrypt: publish self-contained (win-x64) → kompilacja .iss → `Output/CantioSetup-{version}.exe`
