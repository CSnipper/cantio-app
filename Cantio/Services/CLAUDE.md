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
- `Slide.VerseType` — "v", "c", "b", "p", "img"; `Slide.IsChorusSlide` = `VerseType == "c"`, `IsPrivateSlide` = `"p"`
- Na protokół WS tłumaczy to WYŁĄCZNIE `SlideKind.FromSlide` (zob. „Typ zwrotki przy slajdzie") — nie powielać mapowania
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

### OpenSong — wybór folderu (PUŁAPKA)
- `CommonOpenFileDialog` z `Microsoft.WindowsAPICodePack` crashuje aplikację w release (działa w debug)
- **Fix (v1.43):** używaj `Microsoft.Win32.OpenFolderDialog` — dostępne natywnie od .NET 8, bez zewnętrznych pakietów
  ```csharp
  var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "..." };
  if (dlg.ShowDialog() == true) path = dlg.FolderName;
  ```

## RemoteControlServer — pułapki

- `HttpListener` z `http://*:port/` wymaga admin lub rejestracji URL ACL — dla LAN serwera bez uprawnień używaj `TcpListener(IPAddress.Any, port)`
- `WebSocket.CreateFromStream(stream, isServer: true, ...)` dostępne w .NET 10 bez dodatkowych paczek

### Protokół WS desktop ↔ Pilot (jedno źródło prawdy)

Serwer: `RemoteControlServer` (TcpListener + ręczny handshake WS). Discovery: UDP broadcast na `Port+1`, żądanie `{"type":"discover"}` → odpowiedź `{"type":"cantio","port":N}`.
Każdy `type` w `ReceiveLoopAsync` → `event` → przepięcie w `RemoteControlViewModel` → handler w `MainWindow.xaml.cs`. Odczyt zawsze `TryGetProperty` (brak pola = ignoruj).

#### Parowanie PIN-em (v1.56+) — OBOWIĄZKOWE przed jakąkolwiek komendą

Handshake po nawiązaniu WS (gdy `RequirePin`, domyślnie tak):

| kierunek | komunikat | opis |
|---|---|---|
| D→P | `{"type":"auth_required"}` | wysyłany natychmiast po połączeniu; do czasu auth serwer **ignoruje wszystkie komendy**, nie wywołuje `ClientConnected` i nie dodaje klienta do listy broadcastu |
| P→D | `{"type":"auth","pin":"1234"}` | parowanie PIN-em (4 cyfry, ustawienie `pilot_pin`) |
| P→D | `{"type":"auth","token":"<zapamiętany>"}` | logowanie zapamiętanym tokenem (bez PIN-u) |
| D→P | `{"type":"auth_ok","token":"<token>"}` | sukces; klient ZAPISUJE token trwale. Zaraz potem lecą `categories_data`, `slide`, `setlist`, `devices` (jak dotąd na `ClientConnected`) |
| D→P | `{"type":"auth_failed","retryAfter":N}` | błąd; `retryAfter` > 0 = adres IP zablokowany na N sekund (serwer zaraz zamknie połączenie) |

Zasady serwera:
- 5 nieudanych prób w jednym połączeniu (`MaxAttemptsPerConnection`) → zamknięcie WS
- 10 nieudanych prób z jednego IP (`MaxIpFailures`) → blokada IP na 5 min (`IpLockout`); licznik kasuje się po udanym auth lub po okresie bez prób
- brak uwierzytelnienia w 30 s (`AuthTimeout`) → zamknięcie + `ClientRejected` (log `[Pilot] Odrzucono urządzenie IP: …` + komunikat w panelu pilota) — tak wygląda stary, niezaktualizowany klient
- tokeny: 256 bitów z `RandomNumberGenerator`, max 20 sztuk, ustawienie `pilot_tokens` (JSON array); „nowy PIN" (`NewPinCommand`) czyści je wszystkie
- QR w panelu pilota koduje `http://IP:PORT/?pin=1234` → skan paruje bez przepisywania PIN-u; ręcznie wpisany adres pyta o PIN
- `pilot_require_pin=0` → serwer działa jak przed v1.56 (bez auth); wysłany mimo to `{"type":"auth"}` dostaje `auth_ok` z tokenem (żeby późniejsze włączenie PIN-u nie odparowało urządzenia)

Ustawienia: `pilot_pin`, `pilot_tokens`, `pilot_require_pin` (+ istniejące `pilot_remember`, `pilot_was_running`, `pilot_port`).

**Pilot → Desktop (komendy):** wszystkie wymagają wcześniejszego `auth_ok`.

| type | pola | akcja |
|---|---|---|
| `next` / `prev` / `blank` | — | nawigacja slajdami / wygaszenie |
| `goto` | `index` | slajd nr index |
| `goto_song` | `index` | pieśń nr index w zestawie |
| `setlist_add` | `songId` | dodaj pieśń do zestawu |
| `setlist_remove` | `index` | usuń pozycję |
| `setlist_move` | `from`, `to` | przenieś pozycję |
| `setlist_clear` | — | wyczyść zestaw |
| `setlist_restore` | `songs[]` (`{id}`), **`activeIndex?`** | odtwórz zestaw z listy ID; `activeIndex` = pozycja podświetlona w Pilocie, przycinana do `0..count-1`. Zgodność wsteczna: brak pola (starszy Pilot) → aktywna PIERWSZA pozycja, nigdy ostatnia (`Services/SetlistRestore.ResolveActiveIndex`) |
| `get_songs` | `offset`, `limit` | → `songs_data` |
| `get_setlists` | — | → `setlists_data` |
| `open_setlist` | `id` | otwórz zestaw z bazy |
| `get_setlist_detail` | `id` | → `setlist_detail` |
| `sync_push` | raw JSON | sync pieśni (→ `sync_push_ack`) |
| `setlist_sync_push` | `desktopId?`, `name`, `updatedAt`, `songs[]` (`{id}`), **`baseUpdatedAt?`**, **`force?`** | sync zestawu (→ `setlist_sync_ack` albo `setlist_sync_conflict`) |
| `setlist_delete` | `desktopId` | usuń zestaw z bazy desktopu (→ `setlist_delete_ack`) |
| `devices_power_all` | `on` (bool) | włącz/wyłącz wszystkie urządzenia projekcyjne |

##### Zestawy: wykrywanie konfliktu (v1.61+)

Pilot edytuje zestawy offline, więc ten sam zestaw może się zmienić po obu stronach. Reguła:
**zmiana po jednej stronie → zastosuj po cichu; pytamy TYLKO przy realnym konflikcie** (pytanie pokazuje Pilot, desktop go tylko wykrywa).

- `baseUpdatedAt` (long, ms) = `UpdatedAt` zestawu z chwili, gdy Pilot ostatnio go zsynchronizował.
- Konflikt = `baseUpdatedAt` podane **i** `desktopId` wskazuje istniejący zestaw **i** jego `UpdatedAt` w bazie jest **większy** niż `baseUpdatedAt`.
  Wtedy desktop **nie zapisuje niczego** i odsyła `setlist_sync_conflict` z pełną wersją desktopową.
- `force: true` → pomija sprawdzenie i nadpisuje (Pilot wysyła po wyborze „wersja z telefonu").
- **Zgodność wsteczna:** brak `baseUpdatedAt` = zachowanie sprzed zmiany (bezwarunkowe nadpisanie + `setlist_sync_ack`) — tak działa Pilot już zainstalowany u użytkownika. `force` bez `baseUpdatedAt` niczego nie zmienia.
- Rozstrzygnięcie „wersja z komputera" po stronie Pilota nie wymaga żadnej komendy — Pilot bierze dane z `setlist_sync_conflict` (albo `get_setlist_detail`) i nadpisuje siebie.
- Logika: `Services/PilotSetlistSync.HandlePushAsync` (parse + odpowiedź) → `DatabaseService.SyncSetlistFromPilotAsync` (wykrycie konfliktu) → `CreateOrUpdateSetlistFromPilotAsync` (zapis). Handlery w `MainWindow.xaml.cs` nie zawierają logiki.
- `setlist_delete` idzie przez `DatabaseService.DeleteSetlistAsync` (kasuje też `SetlistItems`, zwraca `false` gdy zestawu nie było). Gdy usunięty zestaw był wczytany w Cantio, `DisplayViewModel.OnSetlistDeletedExternallyAsync` zeruje powiązanie z rekordem (kolejny ZAPISZ = nowy zestaw) i odświeża PRZYPIĘTE; treść na ekranie zostaje — tak samo jak przy usuwaniu z popupu wyszukiwarki zestawów.
- Obie komendy przechodzą normalną bramą auth (`if (!authed) continue;`) — przed `auth_ok` są ignorowane bez odpowiedzi.

**Desktop → Pilot (broadcast/odpowiedzi):**

| type | pola | znaczenie |
|---|---|---|
| `auth_required` / `auth_ok` / `auth_failed` | `token` / `retryAfter` | parowanie (zob. wyżej) |
| `slide` | `text, songTitle, index, total, isBlank, slides[]`, **`kind`**, **`slideKinds[]`** | bieżący slajd (+ typ zwrotki, v1.63) |
| `setlist` | `activeIndex, songs[]` (`{id,title}`) | stan zestawu |
| `categories_data` | `categories[]` | kategorie (na `ClientConnected`) |
| `songs_data` / `setlists_data` / `setlist_detail` / `sync_push_ack` | — | dane sync |
| `setlist_sync_ack` | `desktopId`, `name`, `updatedAt` | zestaw zapisany; `desktopId` = ID nadane przez desktop, `updatedAt` = wartość przysłana przez Pilota (nowa baza do `baseUpdatedAt`) |
| `setlist_sync_conflict` | `desktopId`, `name`, `updatedAt`, `songs[]` (`{id,title}`) | zestaw zmieniono po obu stronach — NIC nie zapisano; pola niosą wersję **desktopową** do pokazania użytkownikowi |
| `setlist_delete_ack` | `desktopId`, `existed` (bool) | zestaw usunięty; `existed=false` = już go nie było |
| `devices` | `state` (`on`/`off`/`mixed`), `count` | zbiorczy stan urządzeń |

Na `ClientConnected` (czyli po `auth_ok`, a przy wyłączonym PIN-ie od razu po połączeniu) desktop wysyła świeżemu klientowi: `categories_data`, `slide`, `setlist`, `devices`.

##### Typ zwrotki przy slajdzie (v1.63+)

Pilot ma pokazywać etykiety 1/2/3/R bez parsowania tekstu (dawniej jedyną poszlaką był prefiks `Refren:`/`Aklamacja:`, który w zwykłych pieśniach w ogóle nie występuje). Komunikat `slide` niesie więc typ **gotowy**:

- `slideKinds[]` — tablica **równoległa do `slides[]`** (ta sama długość i kolejność), jedna wartość na slajd;
- `kind` — typ slajdu wskazanego przez `index` (skrót, żeby Pilot nie musiał indeksować); gdy `index` jest poza zakresem → `"verse"`.

Wartości: `verse` · `chorus` · `bridge` · `private` · `image`. Mapowanie z typu w bazie (`Verse.Type`: `v`/`c`/`b`/`p`/`img`) robi **jedna czysta funkcja** `Services/SlideKind.FromVerseType(verseType, hasImage)` (+ `FromSlide(slide)`), używana przez oba miejsca budujące komunikat. Nieznany/pusty typ → `verse`; pole **nigdy nie jest null**.

Skąd biorą się poszczególne wartości:
- **psalm**: bloki `Refren:` i `Aklamacja:` dostają w `DisplayViewModel` `Type = "c"`, więc wychodzą jako `chorus` bez osobnej reguły — Pilot nie musi patrzeć na prefiks;
- **tekst jednorazowy** z zestawu (`SetlistItem.CustomText`, v1.6): `SplitTextToVerses` nadaje wszystkim blokom `Type = "v"` → `verse`;
- **obrazek jako zwrotka pieśni** (`Type = "img"` / `Slide.ImagePath`) → `image`; obrazek wygrywa nad typem tekstowym;
- **element-obrazek zestawu** (`SetlistItem.Type = "image"`) nie tworzy slajdów w ogóle (`LoadImageFromSetlist` czyści listę) → `slides` i `slideKinds` puste, `kind = "verse"`.

**Zgodność wsteczna (obowiązkowa — u użytkownika jest już zainstalowany stary Pilot):** oba pola są wyłącznie **dopisane** na końcu obiektu. `slides[]` pozostaje tablicą **stringów** (nie obiektów), a `text/songTitle/index/total/isBlank` nie zmieniają ani kształtu, ani znaczenia. Stary Pilot po prostu ignoruje nieznane pola i działa jak dotąd. Odwrotnie też jest bezpiecznie: `BroadcastAsync` wywołane bez nowych argumentów wysyła `slideKinds: []` i `kind: "verse"`.

Komunikat składa **jedna** metoda `RemoteControlServer.BuildSlideJson` (broadcast i wysyłka do świeżego klienta na `ClientConnected`) — inaczej łatwo o rozjazd dwóch niezależnych list pól (ten sam błąd co przy notatkach pozycji zestawu w v1.6).

### `UpdatedAt` — kto podbija, a kto NIE (kluczowe dla wykrywania konfliktów)

Cała detekcja konfliktów opiera się na tym znaczniku (Pilot porównuje `desktop.updatedAt != lastSyncedUpdatedAt`), więc reguła jest sztywna:

| metoda | podbija `UpdatedAt`? | dlaczego |
|---|---|---|
| `SaveSetlistAsync` | **TAK**, zawsze | zapis zestawu = zmiana treści |
| `SaveSetlistItemsAsync` | **TAK** (zestaw nadrzędny, w tej samej transakcji) | dodanie/usunięcie/przeniesienie pieśni |
| `CreateOrUpdateSetlistFromPilotAsync` | **NIE** — zapisuje znacznik **przysłany przez Pilota** | ta sama wartość wraca w `setlist_sync_ack` i staje się nową bazą `baseUpdatedAt`; własny czas desktopu = fałszywy konflikt przy każdej synchronizacji |
| `SetSetlistPinnedAsync` | **NIE** | przypięcie to flaga UI, nie zmiana treści; inaczej kliknięcie pinezki generowałoby konflikt |
| `SaveSetlistItemNotesAsync` | **NIE** | Pilot nie przenosi notatek; przy pełnym „ZAPISZ ZESTAW" i tak idzie `SaveSetlistItemsAsync` |

**BUG, który to wymusił (naprawiony 2026-07-28):** `SaveSetlistAsync` nie dotykało znacznika, więc zwykły zapis zestawu w Cantio był dla Pilota niewidoczny i telefon **cicho nadpisywał pracę operatora**. Harness dawał 12 czerwonych asercji przed poprawką.

### Korelacja `setlist_sync_push` ↔ `setlist_sync_ack` (zależność, o której trzeba pamiętać)

Pilot NIE szuka rekordu po nazwie (tak było i przy duplikatach nazw ack przypinał `desktopId` do losowego zestawu). Koreluje przez `PendingPushRegistry` — kolejkę wysłanych żądań dopasowywaną po parze **`(name, updatedAt)`**, bo desktop echo'uje obie wartości bez zmian (`PilotSetlistSyncResult`).

**Jeśli kiedykolwiek zmienisz desktop tak, żeby nadawał własny `updatedAt` przy zapisie z Pilota — korelacja przestanie działać dla NOWYCH zestawów** (brak `desktopId` do fallbacku). Wtedy trzeba dołożyć do protokołu własny identyfikator żądania (`clientRef`) echo'owany w acku.

## Style WPF — zasoby w UserControl

- Style `GoldBtn`, `OutlineBtn`, `DarkTextBox`, `TabBtn` są w `MainWindow.xaml` (nie `App.xaml`)
- `StaticResource` w UserControl nie widzi zasobów z `MainWindow.Resources` podczas `InitializeComponent()` — kopiuj potrzebne style do `<UserControl.Resources>` nowego UserControl
- `BoolToVis` i `HeaderFont` SĄ w `App.xaml` — dostępne wszędzie

## ImportPsalmySeedAsync — zasady bulk insert

- Psalmy responsoryjne importowane z wbudowanego zasobu `Assets/Data/psalmy.json.gz`
- Tytuł = `dzien` gdy `cykl` jest pusty (uroczystości), `"{dzien} {cykl}"` gdy cykl niepusty
- **Kolejność insert:** wszystkie `Song` najpierw → `SaveChangesAsync()` → potem wszystkie `Verse` → `SaveChangesAsync()`
- Bez tego EF Core nie ma ID dla wierszy i FK insert się sypie
- Deduplikacja po `(Title, CategoryId)` — pomiń jeśli już istnieje
