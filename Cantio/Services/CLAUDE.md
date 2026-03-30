# Cantio/Services — lokalny kontekst

## DatabaseService — zasady

`DatabaseService` to **jedyna** warstwa dostępu do danych.
ViewModels NIE używają `CantioDbContext` bezpośrednio.

### Pattern każdej metody

```csharp
public async Task<List<Song>> GetAllSongsAsync()
{
    await using var db = new CantioDbContext();
    return await db.Songs
        .AsNoTracking()          // zawsze dla odczytu
        .Include(s => s.Category) // dołącz relacje jeśli potrzebne
        .OrderBy(s => s.Title)
        .ToListAsync();
}
```

### Save (insert lub update)

```csharp
public async Task SaveSongAsync(Song song)
{
    await using var db = new CantioDbContext();
    if (song.Id == 0)
        db.Songs.Add(song);
    else
        db.Songs.Update(song);
    await db.SaveChangesAsync();
}
```

### Ustawienia aplikacji

```csharp
await _db.GetSettingAsync("klucz");           // odczyt
await _db.SetSettingAsync("klucz", "wartość"); // zapis
```

## SlideLayoutService

Dzieli tekst pieśni na slajdy które mieszczą się na ekranie projekcji.
- Mierzy wysokość tekstu przez `FormattedText`
- Rozmiar czcionki z ustawień to MINIMUM — jeśli tekst nie mieści się w jednym slajdzie, dziel dalej
- Po zmianie ustawień szablonu: wywołaj `DisplayViewModel.RebuildSlides()`
- `SlideLayoutSettings.ForceSingleSlide = true` — wyłącza dzielenie; cała zwrotka to jeden slajd, min. czcionka = 1px (używane w psalm mode)
- `Slide.VerseType` — "v", "c", "b"; `Slide.IsChorusSlide` = `VerseType == "c"`
- `PsalmCategoryId` w `DisplaySettings` (klucz `psalm_category_id` w tabeli settings); 0 = wyłączone

## Import — ILyricsImporter

Każdy importer implementuje `ILyricsImporter`:
- `GetPreviewAsync()` — podgląd bez importu
- `ImportAsync(db, options, progress)` — właściwy import

Obsługiwane formaty:
- `OpenLpImporter` — SQLite baza OpenLP
- `OpenSongImporter` — XML lub folder z plikami XML
- `OszImporter` — ZIP z `.osj` (JSON) → OpenLyrics XML

OSZ flow: `.osz` → `ZipFile` → `.osj` (szukaj pierwszego, nie tylko `service_data.osj`) → JSON → `header.xml_version` = OpenLyrics XML

### OpenLP SQLite — schemat (pułapki)
- Tabele: `songs`, `song_books`, `songs_songbooks` — NIE `songs_song`, `songs_book`, `songs_song_books`
- Relacja: `songs_songbooks.songbook_id` (nie `book_id`), `entry` to VARCHAR (nie int)
- `song_books` nie ma kolumny `book_number`
- Sprawdzaj istnienie tabel przez `sqlite_master` przed zapytaniem (`TableExistsAsync`)

### OpenSong — pliki bez rozszerzenia
- OpenSong zapisuje pliki XML **bez rozszerzenia** — filtr w OpenFileDialog musi uwzględniać `*.*` lub `*` obok `*.xml`
