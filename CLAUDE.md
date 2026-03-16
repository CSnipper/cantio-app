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

### UI / Styl
- Kolory: tło `#0f1117`, panel `#161b25`, akcent `#c9a84c`, tekst `#e8eaf0`
- Czcionka nagłówkowa: `HeaderFont` (aktualnie Playfair Display) — `FontFamily="{StaticResource HeaderFont}"`
- Czcionka UI: `BodyFont` (aktualnie Lato) — `FontFamily="{StaticResource BodyFont}"` (domyślna dla całego okna)
- Klucze celowo generyczne — łatwa zamiana czcionki przez zmianę tylko App.xaml
- Przyciski dotykowe: min. wysokość 58-70px
- Każdy kontrolek ma własny `ControlTemplate` — nie używaj domyślnych stylów WPF

### Zasoby zewnętrzne (czcionki, obrazy, itp.)
- Jeśli zadanie wymaga pobrania pliku (czcionka, ikona, itp.) — **przerwij i każ użytkownikowi pobrać plik samodzielnie**, podając dokładną nazwę i lokalizację docelową. Nie próbuj pobierać zasobów automatycznie.

### Czego unikać
- `UseWindowsForms=true` — konflikty namespace
- `WindowState.Maximized` po ustawieniu `Left`/`Top` — użyj `Normal` + jawne wymiary
- `MessageBox.Show` w kodzie produkcyjnym — tylko debug
- Blazor / MAUI / WinForms — projekt jest WPF
