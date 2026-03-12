# Instrukcje dla GitHub Copilot – Cantor

## Stack technologiczny
- C# 12, .NET 10, WPF (Windows Presentation Foundation)
- MVVM przez CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableProperty)
- SQLite przez Entity Framework Core + Microsoft.EntityFrameworkCore.Sqlite
- WpfScreenHelper do obsługi wielu monitorów
- Microsoft.WindowsAPICodePack-Shell do okna wyboru folderu

## Architektura projektu
```
Cantor/
├── Models/        – encje bazy danych (Category, Song, Verse, Setlist, SetlistItem, AppSettings)
├── ViewModels/    – MVVM ViewModels (DisplayViewModel, SzablonViewModel, SongEditorViewModel, SetlistViewModel, ImportViewModel, ProjectionViewModel)
├── Views/         – dodatkowe okna WPF (ProjectionWindow)
├── Services/      – CantorDbContext, DatabaseService, SlideLayoutService
│   └── Import/    – ILyricsImporter, OpenLpImporter, OpenSongImporter, OszImporter
├── Helpers/       – konwertery WPF, attached behaviors (BoolToVisConverter, EnumToBoolConverter, IsEqualConverter, TextBlockHelper, NumericBox)
└── Assets/Fonts/  – Cinzel (czcionka nagłówkowa)
```

## Zasady kodowania

- Używaj `partial class` + `[ObservableProperty]` i `[RelayCommand]` z CommunityToolkit.Mvvm
- Asynchroniczne metody z `[RelayCommand]` są `private async Task`, nie `public`
- CommunityToolkit generuje `XxxAsyncCommand` dla metod async, `XxxCommand` dla sync
- Pola prywatne backing field: `_camelCase`, properties: `PascalCase`
- Nie używaj `var` gdy typ nie jest oczywisty
- Nullable reference types włączone – unikaj nulli, używaj `?.` i `??`
- `[ObservableProperty] private string _foo = string.Empty;` – zawsze inicjalizuj stringi

## Konwencje WPF / XAML

- Główne okno: `MainWindow.xaml` – jeden plik, zakładki przełączane przez `Visibility`
- DataContext ustawiany w `MainWindow.xaml.cs`, nie przez XAML
- `Value="{Binding}"` w `DataTrigger` jest NIEDOZWOLONE – używaj `MultiBinding` z `IsEqualConverter`
- `LineHeight` w WPF musi być ≥ `FontSize` – nigdy nie ustawiaj mniejszej wartości
- Dla list z `ItemTemplate` nie ustawiaj jednocześnie `DisplayMemberPath`
- Scrollowanie poziome wyłączaj przez `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`

## Baza danych

- `CantorDbContext` dziedziczy po `DbContext` z EF Core
- Migracje przez `MigrateAsync()` w `App.xaml.cs` przy starcie
- `DatabaseService` to jedyna warstwa dostępu do danych – ViewModels nie używają DbContext bezpośrednio
- Ustawienia aplikacji przechowywane w tabeli `settings` jako klucz-wartość
- Grupy zestawów zapisywane jako `settings["setlist_groups"]` = CSV

## Projekcja na drugi ekran

- `ProjectionWindow` wyświetla `SlideText` z `ProjectionViewModel`
- `SlideLayoutService` dzieli tekst na slajdy – mierzy wysokość przez `FormattedText`
- Rozmiar czcionki z ustawień to MINIMUM – jeśli tekst się nie mieści, dziel na slajdy
- Po zmianie ustawień szablonu wywołaj `DisplayViewModel.RebuildSlides()`
- `ProjectionViewModel.DisplayLineHeight` = `FontSize * LineHeightMultiplier` (nie używaj `LineHeight` jako nazwy – konflikt z WPF)

## Import

- Obsługiwane formaty: OpenLP SQLite, OpenLP XML, OpenSong XML/folder, OSZ (zip z service_data.osj)
- OSZ = ZIP → `service_data.osj` → JSON array → `header.xml_version` = OpenLyrics XML
- Importer szuka pieśni po tytule w DB, jeśli nie znajdzie – importuje z XML

## Styl UI

- Paleta kolorów: tło `#0f1117`, panel `#161b25`, akcent `#c9a84c` (złoto), tekst `#e8eaf0`
- Czcionka nagłówkowa: Cinzel (zasoby: `/Assets/Fonts/#Cinzel`)
- Czcionka UI: Segoe UI
- Przyciski dotykowe: minimalna wysokość 58-70px
- Nie używaj domyślnych stylów WPF – każdy kontrolek ma własny `ControlTemplate`

## Czego unikać

- Nie używaj `UseWindowsForms=true` – konflikty namespace; używaj `WpfScreenHelper`
- Nie używaj `WindowState.Maximized` po ustawieniu `Left`/`Top` – użyj `Normal` + jawne wymiary
- Nie dodawaj właściwości do modelu `Song` bez aktualizacji migracji EF
- Nie używaj `MessageBox.Show` w kodzie produkcyjnym – tylko debug
- Nie używaj Blazor/MAUI/WinForms – projekt jest WPF
