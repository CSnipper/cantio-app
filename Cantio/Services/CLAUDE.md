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
| `setlist_add_text` | `title?`, `text` | dodaj TEKST JEDNORAZOWY do bieżącego zestawu (ścieżka przycisku 📝 w oknie) → `ack` + broadcast `setlist`; pusta treść → `ok:false, reason:"empty_text"` |
| `setlist_update_text` | `index`, `title?`, `text` | zmień treść pozycji tekstowej pod `index` → `ack` + **JAWNY** broadcast `setlist`; `reason`: `bad_index` (poza zakresem/brak pola) · `not_text` (pozycja to pieśń albo obrazek) · `empty_text` |
| `show_song` | `songId` | pokaż pieśń NA EKRANIE bez dodawania jej do zestawu (odpowiednik 👁 w oknie Cantio) |
| `setlist_remove` | `index` | usuń pozycję |
| `setlist_move` | `from`, `to` | przenieś pozycję |
| `setlist_clear` | — | wyczyść zestaw |
| `setlist_restore` | `songs[]` (pozycje, zob. „Kontrakt pozycji zestawu"), **`activeIndex?`** | odtwórz zestaw z listy pozycji: pieśń po `id`, tekst jednorazowy z `customTitle`/`customText`, obrazek po **`imageRef`** (v1.69 — pomijany tylko, gdy pliku nie ma na tym PC albo gdy pola brak); `activeIndex` = pozycja podświetlona w Pilocie, przycinana do `0..count-1`. Zgodność wsteczna: same `{id}` (starszy Pilot) = zachowanie jak dotąd, brak `activeIndex` → aktywna PIERWSZA pozycja, nigdy ostatnia (`Services/SetlistRestore.ResolveActiveIndex`) |
| `get_songs` | `offset`, `limit` | → `songs_data` |
| `get_setlists` | — | → `setlists_data` |
| `open_setlist` | `id` | otwórz zestaw z bazy |
| `get_setlist_detail` | `id` | → `setlist_detail` |
| `sync_push` | raw JSON | sync pieśni (→ `sync_push_ack`) |
| `setlist_sync_push` | `desktopId?`, `name`, `updatedAt`, `songs[]` (pozycje, zob. „Kontrakt pozycji zestawu"), **`baseUpdatedAt?`**, **`force?`** | sync zestawu (→ `setlist_sync_ack` albo `setlist_sync_conflict`) |
| `setlist_delete` | `desktopId` | usuń zestaw z bazy desktopu (→ `setlist_delete_ack`) |
| `setlist_pin` | `desktopId`, `pinned` (bool) | przypnij/odepnij zestaw w panelu PRZYPIĘTE (→ `ack` + broadcast `setlist_pinned`) |
| `pin_next_week` | — | „Przypnij tydzień”: przypina 7 kolejnych dni od dziś (→ `ack {pinned, days[]}` + broadcasty `setlist_pinned` i `pinned_celebrations`) |
| `get_categories` | — | → `categories_data` **do nadawcy** |
| `category_add` | `name` | nowa kategoria na końcu kolejności |
| `category_rename` | `id`, `name` | zmiana nazwy |
| `category_delete` | `id`, **`withSongs?`**, **`keepSongs?`**, **`force?`** | usunięcie; niepusta kategoria WYMAGA `withSongs:true` (kasuje pieśni) albo `keepSongs:true` (pieśni zostają bez kategorii); `withSongs` przy pieśniach w ZAPISANYCH zestawach wymaga dodatkowo `force:true` |
| `category_move` | `id`, `direction` (`up`/`down`) | przesunięcie w kolejności (1:1 ze strzałkami ▲▼) |
| `get_setlist_groups` | — | → `setlist_groups_data` **do nadawcy** |
| `setlist_group_add` | `name` | nowa grupa zestawów |
| `setlist_group_rename` | `name`, `newName` | zmiana nazwy grupy |
| `setlist_group_delete` | `name` | usunięcie grupy |
| `song_get` | `id` | → `song_data` **do nadawcy** (pełna treść pieśni do edycji) |
| `song_create` | `title`, `number?`, `categoryId?`, `author?`, `verses[]` (`{type,text}`), `playOrderJson?` | nowa pieśń (→ `ack` z nadanym `id` + broadcast `song_changed`) |
| `song_update` | `id` + te same pola co `song_create` (wszystkie OPCJONALNE poza `title`) + **`baseUpdatedAt?`**, **`force?`** | aktualizacja CZĘŚCIOWA: **pole nieprzysłane = nie ruszaj**, przysłane `verses` zastępuje treść w całości (→ `ack` + broadcast `song_changed`); `baseUpdatedAt` niezgodny z bazą → `song_update_conflict` i **nic nie zapisano** |
| `song_delete` | `id`, **`force?`** | usunięcie pieśni; pieśń w zapisanych zestawach wymaga `force:true` |
| `get_display_settings` | — | → `display_settings_data` **do nadawcy** |
| `set_display_settings` | `settings` (obiekt klucz→wartość) | częściowa zmiana wyglądu projekcji (→ `ack` + broadcast `display_settings_data`) |
| `get_system_settings` | — | → `system_settings_data` **do nadawcy** (tryb pracy, ekran projekcji, język + lista monitorów) |
| `set_system_settings` | `settings` (obiekt klucz→wartość) | zmiana ustawień SYSTEMOWYCH (→ `ack {keys, trial, restartRequired}` + broadcast `system_settings_data`); `reason`: `unknown_key` · `invalid_value` (+ `key`) · `empty_payload` |
| `system_settings_confirm` | — | potwierdzenie zmiany „na próbę" — ekranu projekcji i/lub portu pilota (→ `ack {confirmed}`); bez niego ekran wraca sam po 20 s, a port po 60 s |
| `pilot_forget_devices` | — | odpowiednik „nowy PIN": losuje PIN, KASUJE wszystkie tokeny, rozłącza klientów (→ `ack {pin, pairedDevices:0}`); `reason`: `unavailable` (brak wstrzykniętego serwera) |
| `get_exchange_files` | — | → `exchange_files_data` **do nadawcy**: zawartość FOLDERU WYMIANY + jego pełna ścieżka (v1.70) |
| `maintenance_run` | `op` (`backup_db`/`export_zip`/`import_psalms`) | długa operacja na plikach → `ack {op, taskId}` **natychmiast**, wynik osobnym broadcastem `maintenance_progress`; `reason`: `busy` (+ `taskId` TRWAJĄCEGO zadania) · `unknown_op` (v1.70) |
| `image_get` | `ref`, **`maxDim?`** | podgląd obrazka → `image_data` **do nadawcy**; brak pliku → `ack ok:false, reason:"not_found"` (v1.69) |
| `image_put_begin` | `name`, `totalChunks` | początek wysyłki obrazka z telefonu → `ack {uploadId}`; `reason`: `too_large` (>512 kawałków) · `bad_total` (0/brak) · `busy` (>2 uploady z klienta) |
| `image_put_chunk` | `uploadId`, `seq`, `data` (base64 ≤64 kB) | kolejny kawałek → `ack {uploadId, seq}`; `reason`: `bad_seq` · `unknown_upload` · `bad_data` · `too_large` |
| `image_put_end` | `uploadId` | sklejenie + `ImageStorage.Import` → `ack {uploadId, ref}`; `reason`: `unknown_upload` · `incomplete` · `read_failed` |
| `setlist_add_image` | `ref` | dodaj pozycję-obrazek do BIEŻĄCEGO zestawu (ścieżka przycisku 🖼 w oknie) → `ack {ref}` + broadcast `setlist`; brak pliku → `reason:"not_found"` |
| `devices_power_all` | `on` (bool) | włącz/wyłącz wszystkie urządzenia projekcyjne |
| `status` | — | → `status_data` (diagnostyka zdalna) |
| `restart_app` | — | restart procesu Cantio (→ `ack`) |
| `open_projection` | — | otwórz okno projekcji (→ `ack`) |
| `close_projection` | — | zamknij okno projekcji (→ `ack`) |

##### `show_song` — pokaż pieśń bez dodawania do zestawu (v1.63+)

Gest w lewo na wierszu listy PIEŚNI w układzie tabletowym Pilota. Handler w `MainWindow.xaml.cs` woła
`DisplayViewModel.DisplaySongCommand` — dokładnie tę samą komendę co przycisk 👁 przy pieśni w oknie Cantio:
`LoadVersesAsync(id, restoreSlide: CurrentSlideIndex)`.

- **Zestaw NIE jest ruszany** — ani zawartość, ani `SelectedSetlistItem` (podświetlenie aktywnej pozycji
  zostaje, tak samo jak w Windows). Do dodania pieśni służy osobna komenda `setlist_add`.
- Nieistniejące `songId` → `GetSongWithVersesAsync` zwraca `null` i handler nic nie robi; brak `songId` w JSON →
  komenda ignorowana (`TryGetProperty`).
- **Zgodność wsteczna:** to wyłącznie DOPISANY typ. Stary desktop nieznanej komendy nie rozpozna i ją po cichu
  pominie (łańcuch `else if` nie ma gałęzi domyślnej, połączenie zostaje otwarte), stary Pilot jej nie zna, więc
  jej nie wyśle. Dlatego gest po stronie Pilota jest aktywny tylko przy połączeniu — offline nie ma odpowiednika.

##### Kontrakt pozycji zestawu i tekst jednorazowy z Pilota (v1.68+)

Tekst jednorazowy (`SetlistItem.CustomTitle`/`CustomText`, v1.6) był dla Pilota niewidzialny, a co
gorsza — GINĄŁ. Pozycja leciała w broadcastcie jako `{id:0,title:""}`, `setlist_restore` filtrował
`id > 0` (więc każde „Wczytaj w Cantio" z telefonu kasowało teksty i obrazki z bieżącego zestawu),
`setlist_sync_push` niósł same `id`, a `GetSetlistDetailAsync` odsiewał pozycje bez `SongId`.

**Jeden kształt pozycji we WSZYSTKICH komunikatach z listą** (`setlist`, `setlist_detail`,
`setlist_sync_conflict`, `setlist_restore`, `setlist_sync_push`):

```
{id, title, type, customTitle?, customText?, imageRef?}
```

- `type`: `song` · `text` · `image`; **brak pola = `song`** (tak wygląda komunikat starego Pilota);
- `text`: `id = 0`, `title` = tytuł EFEKTYWNY (`SetlistTextItem.ResolveTitle` — ta sama reguła, co
  w oknie: tytuł ręczny, a gdy pusty pierwsza linia treści do 40 znaków), `customText` = pełna treść;
- `image`: `id = 0`, `title` = nazwa pliku, `imageRef` = **względna ścieżka pliku** (v1.69, to samo
  co `SetlistItem.ImagePath`); zawartości obrazka pozycja nie niesie — telefon dociąga podgląd
  osobno przez `image_get`.

Kierunek P→D: pozycja `image` **wraca do zestawu**, o ile niesie `imageRef` i desktop MA ten plik
(`PilotImages.RefExists`); bez pola (stary Pilot) albo bez pliku jest pomijana bez błędu, z wpisem
w `AppLog`. Tekst bez treści pomijany jak dotąd.
`songCount` w `setlists_data` liczy dalej SAME PIEŚNI — zgodność wsteczna, strażnik w harnessie.

- Komunikat i jego odczyt składa WYŁĄCZNIE `Services/PilotSetlistItems` (`From` / `ToJsonArray` /
  `BuildSetlistJson` / `Parse`). Do v1.67 ten sam JSON budowały DWA niezależne miejsca w
  `MainWindow` (`BroadcastSetlistState` i `BroadcastSetlistStateToAsync`) — dokładnie ten układ
  dwóch list pól zgubił notatki pozycji zestawu w v1.6.
- Tym samym builderem (i tym samym snapshotem `SetlistSnapshotForPilot`) idzie TOŻSAMOŚĆ listy —
  `setlistId` + `name` z `DisplayViewModel.LoadedSetlistId/LoadedSetlistName`. Tożsamość zmienia się
  także BEZ zmiany kolekcji, więc VM zgłasza ją przez `PropertyChanged`, a `MainWindow` rozgłasza
  `setlist` jawnie; przy zwykłym wczytaniu zestawu leci przy okazji broadcast nadmiarowy
  (ten sam komunikat, telefon go łyka idempotentnie).
- Komendy `setlist_add_text` / `setlist_update_text` obsługuje `Services/PilotTextItem`
  (`IsCommand` → routing w `RemoteControlServer`, `Parse` → `Request`, `ValidateTarget` → odmowa).
  **Ta klasa nie dotyka bazy** — mutacja idzie na kolekcji w pamięci `DisplayViewModel`
  (bieżący zestaw trafia do bazy dopiero przy „ZAPISZ ZESTAW"), więc `MainWindow` wykonuje ją na
  Dispatcherze przez `DisplayViewModel.ApplyTextItem` — tę samą ścieżkę, co przycisk 📝 w oknie.
- **Dodanie rozgłasza się samo** (`CollectionChanged`), ale **edycja W MIEJSCU nie odpala żadnego
  zdarzenia kolekcji** — po `setlist_update_text` broadcast `setlist` woła handler JAWNIE.
  Bez tego drugi tablet zostaje ze starą treścią (asercja w harnessie).
- `reason` przy `ok:false`: `empty_text` (pusta/biała treść — rozstrzygane przy parsowaniu),
  `bad_index`, `not_text`. Przy odmowie **nic nie jest zmieniane** — w szczególności edycja
  wycelowana w pozycję-pieśń NIE nadpisuje jej (dowiedzione sabotażem: zdjęcie `ValidateTarget`
  daje 4 FAIL, w tym „pieśń nietknięta").
- Obie komendy przechodzą normalną bramą auth (`if (!authed) continue;`).
- **Zgodność wsteczna:** `id` i `title` zostają na miejscu i w znaczeniu, reszta jest DOPISANA;
  stary Pilot ignoruje nadmiarowe pola i nie wysyła nowych komend, stary desktop nowych komend
  nie rozpozna i po cichu je pominie (offline w Pilocie działa zawsze).

##### Obrazki: podgląd, wysyłka z telefonu, pozycja zestawu (v1.69+)

Parafie w trybie serwerowym mają tablet jako JEDYNY pulpit, a obrazek dawało się dodać wyłącznie
przy komputerze — i co gorsza ginął przy każdym „Wczytaj w Cantio" z telefonu. Ta runda zamyka
temat w trzech miejscach: pozycja niesie `imageRef`, telefon umie pobrać podgląd i umie wysłać
własny plik.

- **Cała logika: `Services/PilotImages.cs`** (`IsCommand` → routing w `RemoteControlServer`,
  `Handle` → `Result(Response, AddedImageRef)`). Odpowiedzi składa wyłącznie ta klasa
  (`BuildDataJson` + `PilotStatus.BuildAckJson`).
- **Rdzeń NIE skaluje obrazów.** `Cantio.Core` to czysty `net10.0` (Linux/Android): nie ma tam
  `System.Drawing`, a `SkiaSharp` świadomie nie jest dokładany dla jednej funkcji. Skalowanie wpina
  gospodarz delegatem `PilotImages.Scaler` — implementacja WPF to `Cantio/Helpers/PilotImageScaler.cs`
  (`BitmapImage` + `DecodePixelWidth/Height`, `JpegBitmapEncoder` QualityLevel 80). Brak delegata
  (albo niedekodowalny plik) → `ack ok:false, reason:"read_failed"` — **desktop nigdy nie milczy**,
  bo Pilot czeka na odpowiedź.
- **Podgląd idzie w JEDNYM komunikacie** (`image_data`): po przeskalowaniu do 1280 px to ~0,3–0,7 MB
  na LAN. Telefon cache'uje po parze `ref` + `maxDim`.
- **Upload jest dzielony na kawałki**, bo serwer składa całą ramkę WS w pamięci: `image_put_begin`
  (`totalChunks` ≤ 512) → `image_put_chunk` (base64 ≤64 kB, **`seq` MUSI iść po kolei** — luka
  znaczyłaby dziurę w pliku, a JPEG z dziurą wygląda na poprawny i wysypuje się dopiero na
  projektorze) → `image_put_end` (sklejenie w pliku tymczasowym + `ImageStorage.Import`, czyli ten
  sam magazyn i to samo rozstrzyganie kolizji nazw, co przycisk 🖼). Telefon skaluje PRZED wysyłką
  (dłuższy bok ≤1920, JPEG ~85) — nikt nie śle 12 MB zdjęcia po WS.
- **Stan uploadów** żyje w `PilotImages.UploadStore` (jedna instancja w `MainWindow`): timeout 60 s
  bez kawałka, max 2 równoległe uploady **per klient** (`busy`), sprzątanie po rozłączeniu przez
  `RemoteControlServer.ClientDisconnected` — inaczej porzucony upload zjadałby limit do timeoutu.
- **`setlist_add_image` NIE dotyka bazy** — dokładnie jak `setlist_add_text`. Bieżący zestaw żyje
  w pamięci `DisplayViewModel`, więc `PilotImages` zwraca `AddedImageRef`, a `MainWindow` wykonuje
  mutację na Dispatcherze przez **`DisplayViewModel.ApplyImageItem`** — wspólną ścieżkę przycisku
  🖼, komendy z Pilota i odtwarzania zestawu. Broadcast `setlist` leci sam z `CollectionChanged`.
  Parametr `insertAfterSelected` rozdziela dwa zachowania: `true` (przycisk i komenda — pozycja tuż
  za aktywną, od razu na ekran, bo po to się ją dodaje) i `false` (restore — dopisanie na KONIEC,
  inaczej wstawianie „za aktywną" pomieszałoby kolejność przysłaną z telefonu).
- **`imageRef` w `slide`** pojawia się tylko wtedy, gdy bieżący slajd JEST obrazkiem —
  `DisplayViewModel.CurrentImageRef` obsługuje oba przypadki: zwrotkę typu `img` (ścieżka w slajdzie)
  i pozycję-obrazek zestawu (nie tworzy slajdów w ogóle, więc ścieżkę daje sama pozycja).
- **Bezpieczeństwo ścieżek:** `PilotImages.IsSafeRef` odrzuca ref-y z segmentem `..` — sparowany
  telefon nie ma czytać całego dysku przez `image_get`. Ścieżki absolutne przechodzą, bo tak
  wyglądają obrazki dodane przed wprowadzeniem magazynu (legacy w bazach parafii).
- **Zgodność wsteczna:** `imageRef` w pozycji i w `slide` to pola DOPISANE i pojawiają się WYŁĄCZNIE
  przy obrazku — slajd tekstowy ma tę samą 9-polową listę co przed zmianą (strażnik w harnessie).
  Stary Pilot ignoruje nadmiarowe pola i nowych komend nie wysyła; stary desktop nowych typów nie
  rozpozna i pominie je po cichu, więc Pilot musi obsłużyć BRAK odpowiedzi (wzorzec
  `RemoteQueryMachine` → „funkcja wymaga nowszego Cantio").
- Wszystkie pięć komend przechodzi normalną bramą auth (`if (!authed) continue;`).
- Harness: `ImageTests.cs` (im1–im5).

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
| `slide` | `text, songTitle, index, total, isBlank, slides[]`, **`kind`**, **`slideKinds[]`**, **`imageRef?`** | bieżący slajd (+ typ zwrotki, v1.63; `imageRef` v1.69 — **tylko** gdy bieżący slajd jest obrazkiem) |
| `image_data` | `ref, w, h, format:"jpeg", data` (base64) | podgląd obrazka na żądanie `image_get` — **do nadawcy**, jeden komunikat, dłuższy bok ≤ `maxDim` (domyślnie 1280), JPEG ~80 (v1.69) |
| `setlist` | `activeIndex, songs[]` (pozycje: `{id,title,type,customTitle?,customText?,imageRef?}`), **`setlistId?`**, **`name?`** | stan zestawu; `type`/pola tekstu DOPISANE w v1.68 (zob. „Kontrakt pozycji zestawu"). **`setlistId`/`name` (v1.68) = TOŻSAMOŚĆ bieżącej listy** — rekord bazy desktopu, z którego ją wczytano albo pod którym ostatnio zapisano; Pilot proponuje go do nadpisania przy „Zapisz". Lista zebrana ręcznie / po `ClearSetlist` / po skasowaniu rekordu **nie niesie tych pól w ogóle** (brak = nie ma czego nadpisywać). Rozgłaszane także wtedy, gdy zmienia się SAMA tożsamość bez zmiany pozycji — „zapisz jako", nadpisanie pod nową nazwą, odczepienie skasowanego zestawu (`DisplayViewModel` zgłasza `LoadedSetlistId`/`LoadedSetlistName`, `MainWindow` woła broadcast jawnie; `CollectionChanged` wtedy NIE leci) |
| `categories_data` | `categories[]` (`{id,name,number}`) | kategorie — na `ClientConnected`, na `get_categories` (do nadawcy) i **broadcastem po każdej mutacji** (v1.63) |
| `setlist_groups_data` | `groups[]` (stringi, kolejność z CSV) | grupy zestawów — na `get_setlist_groups` i broadcastem po mutacji (v1.63) |
| `songs_data` | `offset`, `total`, `items[]` (`{id,title,number,author,categoryId,parts[],`**`updatedAt`**`,`**`playOrderJson`**`}`) | strona biblioteki pieśni na żądanie `get_songs`; dwa ostatnie pola DOPISANE w v1.65 (baza porównania dla edycji offline + kolejność odtwarzania) |
| `setlist_detail` / `sync_push_ack` | — | dane sync |
| `setlists_data` | `setlists[]` (`{id,name,group,songCount,updatedAt,`**`pinned`**`}`) | biblioteka zestawów — TYLKO na żądanie `get_setlists` (bywa duża); `pinned` dopisane w v1.63 |
| `setlist_pinned` | `desktopId`, `pinned` | zmieniono przypięcie zestawu — broadcast do WSZYSTKICH (v1.63) |
| `pinned_celebrations` | `items[]` (`{desktopId, celebration}`) | podpisy obchodów pod przypiętymi zestawami (np. „wsp. Św. Dominika, prezbitera”) — broadcast po KAŻDEJ zmianie pinów i na `ClientConnected`; wyłącznie wpisy z NIEPUSTYM podpisem, pusta lista = skasuj podpisy (v1.63) |
| `setlist_sync_ack` | `desktopId`, `name`, `updatedAt` | zestaw zapisany; `desktopId` = ID nadane przez desktop, `updatedAt` = wartość przysłana przez Pilota (nowa baza do `baseUpdatedAt`) |
| `setlist_sync_conflict` | `desktopId`, `name`, `updatedAt`, `songs[]` (pozycje jak w `setlist`) | zestaw zmieniono po obu stronach — NIC nie zapisano; pola niosą wersję **desktopową** do pokazania użytkownikowi |
| `setlist_delete_ack` | `desktopId`, `existed` (bool) | zestaw usunięty; `existed=false` = już go nie było |
| `song_data` | `id, title, number, categoryId, author, verses[]` (`{type,text}`), `playOrderJson`, **`updatedAt`** | pełna treść pieśni — TYLKO na żądanie `song_get` (v1.63; `updatedAt` v1.65) |
| `song_update_conflict` | dokładnie te same pola co `song_data` (tylko inny `type`) | pieśń zmieniono po OBU stronach — NIC nie zapisano; pola niosą wersję **desktopową** (v1.65) |
| `song_changed` | `id`, `action` (`created`/`updated`/`deleted`), **`updatedAt`** | pieśń dodano/zmieniono/usunięto — broadcast do WSZYSTKICH, w tym do nadawcy (v1.63); `updatedAt` = znacznik PO zapisie, 0 przy `deleted` (v1.65) |
| `display_settings_data` | `settings` (24 klucze wyglądu), `fonts[]` (wbudowane), `systemFonts[]` (zainstalowane w Windows) | ustawienia projekcji — na `get_display_settings` (do nadawcy) i broadcastem po każdej zmianie: z tabletu ORAZ po „ZAPISZ USTAWIENIA" w oknie Cantio (v1.63) |
| `system_settings_data` | `settings` (`app_mode`, `projection_screen`, `language`, … , `pilot_pin`, `pilot_require_pin`, `pilot_port`), `screens[]` (`{index,label,width,height,primary}`), `languages[]`, `dioceses[]`, `restartRequired`, `trialSeconds`, **`pilotRunning`**, **`pairedDevices`** (dwa ostatnie TYLKO DO ODCZYTU) | ustawienia systemowe — na `get_system_settings` (do nadawcy), broadcastem po zmianie z tabletu i po samoczynnym cofnięciu ekranu |
| `exchange_files_data` | `path` (pełna ścieżka katalogu), `files[]` (`{name,size,modified,kind}`) | zawartość folderu wymiany — TYLKO na żądanie `get_exchange_files`, do nadawcy; `kind`: `db`·`zip`·`osz`·`sqlite`·`xml`·`image`·`other` (po rozszerzeniu); pusty katalog = pusta lista (v1.70) |
| `maintenance_progress` | `taskId`, `op`, `state` (`running`/`done`/`failed`), `percent`, `message?`, `result?` | postęp i wynik długiej operacji — broadcast do WSZYSTKICH; `result` przy `done`: `{file}` (backup/eksport) albo `{count}` (psalmy); `message` przy `failed` (v1.70) |
| `devices` | `state` (`on`/`off`/`mixed`), `count` | zbiorczy stan urządzeń |
| `status_data` | `version`, `mode`, `projectionOpen`, `projectionScreen`, `screenCount`, `pairedDevices`, `uptimeSeconds` | odpowiedź na `status` |
| `ack` | `command`, `ok` (bool) + opcjonalne `reason`, `id`, `name`, `newName`, `number`, `songs`, `setlists`, `pinned`, `days` | przyjęto komendę `restart_app` / `open_projection` / `close_projection` (bez rozszerzeń) albo wynik komendy kategorii/grup (z rozszerzeniami) |

##### Komendy ratunkowe dla sprzętu bez klawiatury (v1.63+, tryb serwerowy)

Mini PC w zakrystii nie ma ani klawiatury, ani operatora — jedyne wyjście z zawieszki prowadzi przez Pilota.

- `status` → `status_data`:
  `version` (np. `"1.62"`), `mode` (`dual`/`server`, ta sama wartość co ustawienie `app_mode`),
  `projectionOpen` (bool), `projectionScreen` (indeks ekranu; **-1 gdy projekcja zamknięta**),
  `screenCount` (liczba ekranów), `pairedDevices` (liczba tokenów), `uptimeSeconds` (czas pracy procesu).
- `ack` potwierdza **PRZYJĘCIE** komendy, nie jej skutek. Wysyła go **sam `RemoteControlServer`, ZANIM** wywoła event —
  przy `restart_app` proces zaraz znika i innej szansy na potwierdzenie nie ma. Skutek `open_projection` /
  `close_projection` Pilot sprawdza kolejnym `status`.
- Restart wykonuje **handler w `MainWindow`** (`Process.Start(Environment.ProcessPath)` + `Shutdown`), nie serwer —
  dzięki temu harness protokołu może testować ack bez ubijania własnego procesu.
  **Kolejność jest krytyczna:** najpierw `RemoteControlViewModel.StopForRestart()` (zwolnienie portu),
  dopiero potem `Process.Start`. Odwrotnie (tak było do v1.62) świeża kopia wchodziła na zajęte
  gniazdo, jej serwer pilota nie startował i mini PC zostawało bez ŻADNEGO interfejsu.
  `StopForRestart` celowo NIE zapisuje `pilot_was_running=0` — nowa kopia ma wstać w tym samym stanie.
- **Muteksu jednej instancji świadomie NIE ma.** Zablokowałby dokładnie ten scenariusz, który
  ratuje `restart_app` (nowy proces startuje, zanim stary zdąży zniknąć), a realny problem
  — zajęty port — jest teraz widoczny: log + pełnoekranowy komunikat na projekcji
  (`AppModeRules.ShouldShowServerFailure`, `ProjectionViewModel.ShowServerFailure`).
- **Awaria startu serwera pilota nie jest już niema** (v1.63). `ToggleServer` łapał `SocketException`
  i wychodził bez śladu; ekran parowania wisi na `IsRunning`, więc też się nie pokazywał.
  Teraz: `RemoteControlViewModel.StartFailure` (powód + port) → log `[Pilot]` → w trybie serwerowym
  pełnoekranowa warstwa w `ProjectionWindow` (klucze `Pilot.ServerFailedTitle` / `Pilot.ServerFailedHint`
  w pl/en/es). Warstwa wyprzedza ekran parowania — bez serwera nie ma czego parować.
- `open_projection` przy JUŻ OTWARTYM oknie nie wychodzi po cichu (tak było do v1.62, a `ack`
  potwierdzał nieistniejące działanie): przywraca okno ze stanu zminimalizowanego, odświeża
  `Topmost` i stawia je na ekranie z ustawienia `projection_screen`.
- Wszystkie cztery komendy przechodzą normalną bramą auth (`if (!authed) continue;`) — przed `auth_ok` są
  ignorowane bez żadnej odpowiedzi.
- **Zgodność wsteczna:** to wyłącznie DOPISANE typy. Żaden istniejący komunikat nie zmienił kształtu
  (test w harnessie porównuje pełne listy pól `slide`, `setlist`, `devices`, `auth_*`), a stary Pilot nowych
  komend nie zna, więc ich nie wyśle i niczego nie traci.
- Odpowiedzi składa **wyłącznie** `Services/PilotStatus` (`BuildStatusJson` / `BuildAckJson`) — jedna metoda na
  komunikat, żeby nie powstały dwie niezależne listy pól (ten sam układ zgubił notatki pozycji zestawu w v1.6).

##### Ekran parowania na projektorze (v1.63+, tryb serwerowy)

Problem kury i jajka: skąd tablet weźmie PIN, skoro nikt nie widzi okna Cantio. Dopóki nie ma ani jednego
sparowanego urządzenia, jedyne wyjście HDMI pokazuje pełnoekranowy ekran startowy z QR (`http://IP:PORT/?pin=1234`,
ten sam generator co panel pilota), PIN-em i **wszystkimi** adresami IPv4 (mini PC bywa w LAN i Wi-Fi naraz).
PIN zostaje obowiązkowy — sieć parafialna bywa otwarta.

- Reguła: `AppModeRules.ShouldShowPairingScreen(mode, pairedCount)` — tryb serwerowy **i** zero sparowanych.
  Wariant przyjmujący surowy JSON ustawienia `pilot_tokens` liczy tokeny przez `CountPairedDevices`;
  pusty/uszkodzony/nie-tablicowy JSON = 0 (lepiej pokazać ekran raz za dużo niż zostawić parafię bez PIN-u).
- Warstwa jest **wewnątrz `Views/ProjectionWindow.xaml`** (stan w `ProjectionViewModel.ShowPairing/PairingQr/
  PairingPin/PairingAddresses`), nie w osobnym oknie — dwa okna biłyby się o `Topmost`.
- Gaśnie po pierwszym udanym parowaniu **bez restartu**: `TokenIssued` → `RemoteControlViewModel.PairingStateChanged`
  → `MainWindow.RefreshPairingOverlay`. Wraca po „nowym PIN-ie" (czyszczenie tokenów) tą samą drogą.

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

##### Przypinanie zestawów (v1.63+)

Panel PRZYPIĘTE w oknie Cantio to zestawy bieżącego tygodnia (`Setlist.IsPinned`, „Przypnij tydzień”).
Stan jest **synchronizowany**: jedna prawda w bazie desktopu, widoczna tak samo w oknie i na każdym tablecie.

> **`UpdatedAt` zestawu przy pin/unpin zostaje NIETKNIĘTE.** Przypięcie to flaga UI, nie zmiana treści.
> Zapis idzie wyłącznie przez `DatabaseService.SetSetlistPinnedAsync` (nigdy `SaveSetlistAsync`) —
> inaczej każde kliknięcie pinezki wyglądałoby dla Pilota jak edycja na komputerze i przy najbliższej
> synchronizacji dawało fałszywy `setlist_sync_conflict`. Harness ma na to trzy asercje, w tym pełny
> scenariusz „przypnij → push z Pilota z dawnym `baseUpdatedAt`” (zob. tabela `UpdatedAt` niżej).

- P→D `setlist_pin {desktopId, pinned}` → `ack {command:"setlist_pin", ok, desktopId, pinned}` do NADAWCY.
  Nieznany/brakujący `desktopId` → `ok:false, reason:"not_found"` i **żadnego broadcastu**.
  Brak pola `pinned` = przypnij (najczęstsza intencja).
- Po udanej zmianie leci **mały** broadcast `setlist_pinned {desktopId, pinned}` do **wszystkich klientów,
  w tym do nadawcy** (potwierdzenie skutku, nie tylko przyjęcia). Świeżego `setlists_data` NIE rozgłaszamy —
  ta lista bywa duża (u użytkownika ~270 zestawów) i leci wyłącznie na żądanie.
- **Kierunek odwrotny działa tak samo:** pinezka kliknięta w oknie Cantio (`TogglePinSetlist`,
  `PinSetlistFromSearch`, `UnpinSetlist`, `PinNextWeek`) też rozgłasza `setlist_pinned`. Wszystkie te
  ścieżki przechodzą przez `DisplayViewModel.SetPinnedAsync` → event `SetlistPinChanged` → jedno
  podpięcie w `MainWindow`. Komunikat składa **wyłącznie** `PilotSetlistPin.BuildPinnedJson` — jeden
  builder, dwie ścieżki, żadnych dwóch list pól.
- Komenda z Pilota odświeża okno Cantio przez `DisplayViewModel.ApplyExternalPinAsync` (panel PRZYPIĘTE
  + stan pinezki, gdy dotyczy wczytanego zestawu) i **nie** przechodzi przez `SetlistPinChanged`, żeby
  ten sam broadcast nie poleciał dwa razy.
- Logika: `Services/PilotSetlistPin.cs` (`IsCommand` → routing w `RemoteControlServer`, `HandleAsync` →
  `Result(Response, Broadcast, DesktopId, Pinned)`). Handler w `MainWindow.xaml.cs` jest głupi:
  wyślij → rozgłoś → `Dispatcher` odświeża UI.
- **Zgodność wsteczna:** `setlist_pin`/`setlist_pinned` to DOPISANE typy, a `pinned` w `setlists_data`
  to DOPISANE pole na końcu elementu — `id`, `name`, `group`, `songCount`, `updatedAt` bez zmian
  (strażnik pełnej listy pól w harnessie). Stary Pilot nadmiarowego pola nie widzi, nowych komend nie
  wysyła i traci wyłącznie samą funkcję. `setlists_data` składa jedna metoda
  `PilotSetlistSync.BuildSetlistsJsonAsync` (przeniesiona z inline'a w `MainWindow`).
- Komenda przechodzi normalną bramą auth (`if (!authed) continue;`) — przed `auth_ok` jest ignorowana
  bez odpowiedzi.

##### „Przypnij tydzień” + podpisy obchodów (v1.63+)

Problem, który to zamyka: `PinNextWeek` nazywa zestaw dniem temporalnym („18 Pon”), a nazwę zmienia
tylko obchód, który REALNIE ją wypiera. **Wspomnienie obowiązkowe nigdzie nie wypływało** — organista
dowiadywał się o nim dopiero przy ołtarzu.

> **Dwa niezgodne cykle (decyzja użytkownika 2026-08-08).** „18 Sob” jest nazwą RUCHOMĄ (co roku inna
> data), a wspomnienie jest przywiązane do daty STAŁEJ. Pieśni o św. Dominiku dopisane do „18 Sob”
> wróciłyby za rok w sobotę, która nie ma z nim nic wspólnego. Dlatego wspomnienie OBOWIĄZKOWE dostaje
> WŁASNY zestaw pod nazwą obchodu (jak święto i uroczystość), wspomnienie DOWOLNE nie dostaje ani
> zestawu, ani podpisu (organista często go nie obchodzi), a podpis zostaje wyłącznie dla uroczystości,
> która nazwy NIE zabrała (niedziela uprzywilejowana — aplikacja nie przenosi obchodu na poniedziałek).
> Zestawy dni TEMPORALNYCH dalej mają nazwę nietkniętą, bo wracają co roku pod inną datą.

- **Nazwa zestawu dla przypinania:** `DiocesanCalendarService.PinSetlistName(date, day, diocese)` —
  jedyna różnica wobec `EffectiveSetlistName` to próg dnia POWSZEDNIEGO (wspomnienie obowiązkowe
  zamiast święta); w niedzielę próg zostaje twardy (uroczystość). Zwraca też sam obchód, więc wołający
  wie, czy nazwa pochodzi z sanktorału. **Pasek górny okna dalej używa `EffectiveSetlistName`** — to
  etykieta DNIA liturgicznego, nie nazwa zestawu.
- **Dopasowanie do ISTNIEJĄCEGO zestawu:** `Services/CelebrationStems.cs` (czysta). Organista ma własne
  zestawy nazwane po swojemu („Dominik”, „Teresa od Jezusa”, „Jana Sarkandra”), więc przed założeniem
  nowego `PilotPinWeek` szuka po RDZENIACH słów (6 znaków, tolerancja jednej litery na końcówkę
  fleksyjną, słowa funkcyjne odsiane, polskie znaki przez `DatabaseService.FoldPolish`).
  **Kierunek pokrycia:** tytuł obchodu pokrywa nazwę zestawu (+ główny rdzeń obchodu musi być w nazwie)
  — bez tego „Jan Paweł II” zlałby się ze „Św. Jana Sarkandra” przez jedno wspólne imię.
  **Przy niejednoznaczności NIE ZGADUJEMY**: powstaje nowy zestaw, a powód idzie do `AppLog`
  (kategoria `PinWeek`) — cudzy zestaw podstawiony pod obchód wygląda poprawnie i jest niewykrywalny,
  duplikat widać na liście. Nazwa DOKŁADNIE równa tytułowi obchodu wygrywa i nie jest kolizją.
  Szukamy w CAŁEJ bibliotece (zestaw obchodu nie należy do okresu), ale dopiero PO
  `GetSetlistForPinAsync` — identyfikacja po `(nazwa, SeasonKey)` zostaje pierwsza.
- **Czysta funkcja podpisu:** `Services/PinnedCelebrations.cs` (bez bazy, bez WPF). `CaptionFor(date,
  effectiveName, diocese)` bierze NAJWYŻSZY obchód rangi uroczystość i zwraca `""`, gdy to on wyparł
  nazwę dnia (nazwa już nim jest) albo gdy dzień ma własną, nie niższą rangę (obchód ruchomy).
  Wspomnienia podpisu nie dają w ogóle. `Build(pinned, from, days, diocese)` mapuje `id → podpis`,
  kojarząc zestaw z datą **po nazwie z `PinSetlistName`** (`DatabaseService.NameEquals`, pl-PL —
  dokładnie to, po czym rozpoznaje zestawy samo przypinanie). Podpis jest liczony PRZY WYŚWIETLANIU
  i w modelu żyje jako `[NotMapped] Setlist.Celebration` — do bazy nie ma jak wsiąknąć.
- **Logika przypinania:** `Services/PilotPinWeek.cs` — JEDNO miejsce dla przycisku w oknie
  (`DisplayViewModel.PinNextWeekAsync`) i komendy `pin_next_week`. Operacja jest **idempotentna**:
  zestaw zakładany tylko gdy go nie ma, ponowne kliknięcie daje `pinned: 0`. Flaga idzie przez
  `SetSetlistPinnedAsync`, więc `UpdatedAt` zostaje nietknięte (tabela niżej).
- P→D `pin_next_week` (bez pól) → `ack {command:"pin_next_week", ok:true, pinned:N,
  days:[{date:"2026-08-08", name:"Św. Dominika, prezbitera"}, {date:"2026-05-03",
  name:"5 Nie Wielkanocy", celebration:"NAJŚW. MARYI PANNY, KRÓLOWEJ POLSKI…"}, …]}`.
  `days` ma **zawsze 7 pozycji**, `celebration` jest pomijane, gdy podpisu nie ma; `N` = ile zestawów
  realnie przypięto/utworzono. Składa wyłącznie `PilotPinWeek.BuildAckJson`.
- D→P broadcast `pinned_celebrations {items:[{desktopId, celebration}]}` — po każdej zmianie pinów
  (pin / unpin / przypnij tydzień / zmiana diecezji / import) i na `ClientConnected`. Wysyłka wisi na
  `DisplayViewModel.PinnedListRefreshed` (odpalane na końcu `LoadPinnedSetlistsAsync`), więc przy
  przypinaniu tygodnia leci **jeden** komunikat, a nie siedem. Składa wyłącznie
  `PilotPinWeek.BuildCelebrationsJson`.
- **Diecezja** czytana z ustawienia `diocese` przez `PilotPinWeek.DioceseAsync` — kod bezgłowy
  (komendy, testy) NIE może polegać na statyku `DiocesanCalendarService.CurrentDiocese`, który
  ustawia okno. Stąd przeciążenia `ForDate(date, diocese)` i `EffectiveSetlistName(date, day, diocese)`.
- **Okno Cantio:** panel PRZYPIĘTE ma drugi wiersz (mały, szary, `TextTrimming` + tooltip), widoczny
  tylko przy niepustym podpisie. Po kliknięciu przycisku pojawia się podsumowanie 7 dni —
  **w trybie serwerowym NIE** (`AppMode.IsServer`: zero blokujących okien na mini PC bez klawiatury;
  Pilot i tak dostaje to samo ackiem).
- **Zgodność wsteczna:** `pin_next_week` i `pinned_celebrations` to DOPISANE typy; stary Pilot ich nie
  wysyła, a nadmiarowego broadcastu nie rozumie i ignoruje. Za bramą auth jak wszystko inne.
- Harness: `PinWeekTests.cs` — czysta funkcja na SZTYWNYCH datach kalendarza (3 IX = wspomnienie
  obowiązkowe, 30 V = dowolne powszechnie / obowiązkowe w katowickiej, 8 IX = święto wypierające,
  12 IX = wspomnienie diecezji gliwickiej, 13 IX 2026 = niedziela, 3 V 2026 = uroczystość w niedzielę
  Wielkanocy) + komenda przez realny `ClientWebSocket`. `CelebrationSetlistTests.cs` — miara rdzeni
  na literałach + REALNE przypięcie tygodnia na kopii bazy użytkownika. Sabotaże potwierdzone:
  porównanie całych słów zamiast rdzeni = 15 FAIL (w tym duplikat „Dominik” + „Św. Dominika,
  prezbitera” w bazie), zamiana kierunku pokrycia na „ile procent wspólnych” = 2 FAIL.

##### Kategorie pieśni i grupy zestawów (v1.63+)

Tablet ma zarządzać biblioteką bez chodzenia do komputera. Reguła kierunku prawdy jest sztywna:

> **Baza desktopu jest JEDYNYM źródłem prawdy. Tablet wyłącznie komenduje; desktop wykonuje,
> rozgłasza wynik do WSZYSTKICH klientów i odświeża własne UI tą samą ścieżką co po edycji lokalnej.**

Pilot nigdy nie zakłada, że jego kopia listy jest aktualna — po mutacji czeka na broadcast
`categories_data` / `setlist_groups_data`. Dwa tablety edytujące naraz nie mogą się więc rozjechać
ani ze sobą, ani z oknem Cantio. `ack` idzie TYLKO do nadawcy (niesie wynik jego komendy),
broadcast do wszystkich (niesie nowy stan listy).

Cała logika: `Services/PilotCategorySync.cs` (`IsCommand` → routing w `RemoteControlServer`,
`HandleAsync` → `(Response, Broadcast, Scope)`). Handler w `MainWindow.xaml.cs` jest głupi:
wyślij → rozgłoś → `Dispatcher` odświeża `RefreshCategoriesExternallyAsync()` /
`RefreshSetlistGroupsExternallyAsync()` (opakowania istniejących `ReloadCategoriesForEditorAsync` /
`LoadSetlistGroupsAsync`). Komunikaty składa wyłącznie `PilotCategorySync.BuildCategoriesJson` /
`BuildGroupsJson` + `PilotStatus.BuildAckJson` — **`categories_data` przestało być budowane inline
w `ClientConnected`**, bo druga lista pól to dokładnie ten układ, który zgubił notatki w v1.6.

`reason` przy `ok:false`: `duplicate` · `not_found` · `empty_name` · `not_empty` · `in_setlists` · `edge`.

**Kierunek odwrotny też rozgłasza** (v1.63): kategorie i grupy zmienione W OKNIE Cantio
(dodanie / zmiana nazwy / usunięcie / strzałki ▲▼) lecą do tabletów przez eventy
`DisplayViewModel.CategoriesChangedLocally` / `SetlistGroupsChangedLocally` → jedno podpięcie
w `MainWindow`. Komunikaty składa ten sam `PilotCategorySync.BuildCategoriesJson` / `BuildGroupsJson`.
Ścieżka zdalna rozgłasza sama (`Result.Broadcast`) i tych eventów NIE odpala — inaczej ten sam
komunikat poleciałby dwa razy.

**Duplikaty nazw rozstrzyga WYŁĄCZNIE `DatabaseService.NameEquals`** (CompareInfo pl-PL,
IgnoreCase, Trim) — nigdy SQLite `lower()`, który obsługuje tylko ASCII i przepuściłby
„MARYJNE" obok „Maryjne". Zmiana samej wielkości liter to ten sam rekord, nie duplikat.

**`category_delete` — trzy warianty (v1.63+).** `Song.CategoryId` jest od migracji
`ZmienCategoryIdNaNullableWSong` **nullable**, a FK ma **`ON DELETE SET NULL`** (dawniej `NOT NULL`
+ `ON DELETE CASCADE`). Dzięki temu istnieje wariant „usuń kategorię, zostaw pieśni":

- kategoria pusta → kasowana normalnie (`DeleteCategoryAsync`), niezależnie od flag;
- kategoria niepusta **bez żadnej flagi** → `ok:false, reason:"not_empty", songs:N`, **nic się nie dzieje**;
- `withSongs:true` → `DeleteCategoryWithSongsAsync` (najpierw `SetlistItems`, potem `Songs`, potem
  `Category` — inaczej pieśń w zapisanym zestawie wywraca całość na `RESTRICT`);
- `keepSongs:true` → `DeleteCategoryKeepSongsAsync`: pieśni dostają `CategoryId = NULL`, pozycje
  zestawów **zostają nietknięte** (pieśń dalej istnieje), ack niesie `songs:N` = ile odczepiono.
  Odczepienie robimy JAWNIE w kodzie, nie licząc na `ON DELETE SET NULL` — stąd dokładna liczba
  i niezależność od `PRAGMA foreign_keys`.
- Gdy przyjdą OBIE flagi, **wygrywa `keepSongs`** — nieodwracalne kasowanie wymaga jednoznacznej
  intencji. `withSongs:false` / `keepSongs:false` to NIE zgoda (liczy się wyłącznie jawne `true`).
- **`withSongs` ma DRUGĄ bramkę: `in_setlists`** (v1.63). `DeleteCategoryWithSongsAsync` kasuje
  pieśni RAZEM z ich pozycjami w ZAPISANYCH zestawach — dokładnie ten skutek, przed którym broni
  się `song_delete`. Gdy `CountSetlistsWithCategorySongsAsync > 0`, odpowiedź to
  `ok:false, reason:"in_setlists", songs:N, setlists:M` i **nic się nie dzieje**; odblokowuje
  dopiero `force:true`. `keepSongs` bramki nie ma — pieśni zostają, więc zestawy są nietknięte.
- Skasowanie kategorii, na którą wskazuje ustawienie `psalm_category_id`, **zeruje to ustawienie**
  (`DatabaseService.ClearPsalmCategoryIfDeletedAsync`, wołane z wszystkich trzech ścieżek
  kasowania). Bez tego tryb psalm cicho gasł: porównanie z kategorią pieśni nigdy już nie trafiało.

To samo w oknie Cantio: gałąź dialogu „Nie — usuń tylko kategorię" idzie teraz przez
`DeleteCategoryKeepSongsAsync` (do v1.62 wołała `DeleteCategoryAsync`, czyli kasowała pieśni
kaskadą, a przy pieśni w zapisanym zestawie wywracała się na `RESTRICT` i nie robiła NIC).

**Pieśni bez kategorii w UI i na łączu:**

- Okno Cantio: na liście KATEGORIE dochodzi **wirtualna pozycja „Bez kategorii"**
  (`CategoryEditorItem.IsVirtual`, `Id == -1`) — widoczna **tylko** gdy takie pieśni istnieją,
  bez ▲▼✎✕, nie zapisywana przy przenumerowaniu kolejności. Klucz lokalizacji
  `Category.Uncategorized` (pl/en/es). Dane: `DatabaseService.GetUncategorizedSongsAsync` /
  `CountUncategorizedSongsAsync`.
- **`categories_data` NIE zawiera tej pozycji** — to element UI desktopu, nie rekord. Wszystkie
  `id` w komunikacie są > 0 (strażnik w harnessie). Pilot dostanie własny odpowiednik w swoim zadaniu.
- `songs_data` (i tym samym `sync_push` w drugą stronę) niesie dla pieśni bez kategorii
  **`categoryId: 0`, nigdy `null`** — stary Pilot, zainstalowany już u użytkownika, ma tam twardy
  `int`. Komunikat składa wyłącznie `Services/PilotSongSync.BuildSongsDataJson`.
- Symetrycznie `DatabaseService.SyncPushSongsAsync` czyta `categoryId <= 0` (albo brak pola) jako
  **brak kategorii** i szuka pieśni po tytule wśród `CategoryId IS NULL`. Bez tego pieśń bez
  kategorii wróciłaby z telefonu jako DUPLIKAT w pierwszej kategorii. Niezerowe, ale nieistniejące
  ID (przestarzała lista kategorii w Pilocie) → jak dotąd fallback do pierwszej kategorii.

**Grupy zestawów = parytet z UI.** Grupa nie jest encją; jedynym nośnikiem jest CSV w ustawieniu
`setlist_groups`, a `Setlist.Group` to luźny string. Zmiana nazwy i usunięcie grupy **NIE dotykają
zestawów** — dokładnie jak `SaveGroupAsync`/`DeleteGroupAsync` w `DisplayViewModel`. Do acka trafia
`setlists:N` = ile zestawów zostaje przy starej nazwie (ostrzeżenie dla tabletu, nie błąd).
Identyfikatorem grupy jest jej nazwa. `setlist_group_rename` zachowuje POZYCJĘ w CSV i nie psuje
`NormalizeGroupKey`/`ResolveGroupNameAsync` (czyli „Przypnij tydzień"), o ile nowa nazwa nadal
normalizuje się do klucza okresu — zmiana „Zwykły" → „Okres Zwykły" jest bezpieczna, „Zwykły" →
„Moje pieśni" odcina zestawy od automatu przypinania.

**Zgodność wsteczna:** wyłącznie DOPISANE typy i DOPISANE pola w `ack`. `BuildAckJson` bez
rozszerzeń daje bajt w bajt `{"type":"ack","command":"…","ok":true}` (strażnik w harnessie),
`categories_data` nie zmieniło kształtu. Wszystkie dziewięć komend przechodzi normalną bramą auth.

##### Ustawienia projekcji — wygląd (v1.63+)

„Nie widać z ostatniej ławki” to reakcja na żywo, a nie powód do wstawania od tabletu. Cała zakładka
WYGLĄD jest więc dostępna zdalnie. Kierunek prawdy jak przy kategoriach: **baza desktopu jest jedynym
źródłem**, tablet wyłącznie komenduje, desktop zapisuje, przebudowuje slajdy i rozgłasza nowy stan.

- P→D `get_display_settings` → `display_settings_data` **do nadawcy**.
- P→D `set_display_settings {settings:{klucz:wartość,…}}` → `ack {command, ok, keys:N}` do NADAWCY
  + broadcast `display_settings_data` do **WSZYSTKICH** (drugi tablet musi zobaczyć zmianę).
- **Aktualizacja CZĘŚCIOWA, ale przyjmowana ATOMOWO.** Zapisywane są wyłącznie przysłane klucze;
  jeden nieznany klucz albo jedna zła wartość i **nie zapisujemy NICZEGO** z pakietu (ack `ok:false`,
  `reason`, `key` = winny klucz, **żadnego broadcastu**). Częściowy zapis zostawiłby projekcję
  w stanie w pół drogi — a to wygląda jak awaria w trakcie mszy.
- `reason`: `unknown_key` · `invalid_value` · `empty_payload`.

**Biała lista (26 kluczy — dokładnie te, których używa `DatabaseService.GetSettings`):**

| klucz | typ JSON | dozwolone |
|---|---|---|
| `font_family` | string | czcionka **wbudowana albo zainstalowana w systemie** (literówka = fallback WPF na inny krój w środku mszy) |
| `font_size` | number | 8–400 |
| `font_bold`, `font_auto_fit`, `shadow_enabled`, `bg_gradient_enabled` | bool | wyłącznie `true`/`false` (string `"true"` odrzucany) |
| `font_fit_scope` | string | `song` · `verse` (v1.70; zakres auto-dopasowania czcionki) |
| `text_align` | string | `left` · `center` · `right` |
| `text_position` | string | `top` · `center` · `bottom` |
| `text_color`, `bg_color`, `bg_gradient_color1`, `bg_gradient_color2` | string | `#RRGGBB` albo `#AARRGGBB` |
| `line_height` | number | 0,5–4 |
| `shadow_blur` | number | 0–100 |
| `shadow_depth` | number | 0–50 |
| `shadow_opacity`, `bg_image_opacity` | number | 0–1 |
| `bg_image` | string | `""` = wyłącz tło (jedyna sensowna zmiana z tabletu — telefon nie widzi dysku PC); niepusta ścieżka **musi istnieć** |
| `text_margin_h`, `text_margin_v` | number | 0–1000 |
| `bg_gradient_type` | string | `linear` · `radial` |
| `bg_gradient_angle` | number | 0–360 |
| `psalm_category_id` | number | ≥ 0 (0 = tryb psalm wyłączony) |
| `blank_color` | string | `#RRGGBB` albo `#AARRGGBB` (kolor wygaszonego ekranu) |
| `blank_image_path` | string | `""` = wyłącz obrazek; niepusta ścieżka **musi istnieć** — ale sprawdzana przez `ImageStorage.Resolve` (zob. niżej) |

Czego na liście NIE MA: `text_tags` (nie jest klucz→wartość, tylko lista obiektów z nazwą, kolorem,
flagą „tylko podgląd" i skrótem — potrzebuje własnego edytora na tablecie i osobnego kontraktu;
świadomie odłożone). `projection_screen`, `language`, `app_mode` i `pilot_*` mają własną rodzinę
komend („Ustawienia systemowe" niżej) — to nie jest „wygląd", a zdalna zmiana ekranu wymaga
bezpiecznika. `blank_color`/`blank_image_path` **doszły** w etapie 2 (2026-09-14): wygaszony ekran
jest wyglądem, a jego zmiana nie odcina nikogo od obrazu.

- **PUŁAPKA ścieżek obrazków (CLAUDE.md, v1.52).** `blank_image_path` zapisuje `ImageStorage.Import`,
  więc wartość jest **WZGLĘDNA** (`images\plik.jpg`). Walidacja gołym `File.Exists` odrzuciłaby
  ścieżkę, którą desktop sam przed chwilą przysłał w `display_settings_data` — czyli round-trip
  byłby niemożliwy. Stąd osobny walidator `IsClearOrExistingImage` (`File.Exists(ImageStorage.Resolve(v))`);
  `bg_image` zostaje przy `IsClearOrExistingFile`, bo tam ścieżka jest absolutna (`OpenFileDialog`).
  Zmiana obu kluczy wchodzi na ekran od razu — `ApplyExternalSettingsAsync` → `LoadAsync` →
  `SzablonViewModel.ApplyBlank` (`ProjectionViewModel.BlankBrush`/`BlankImagePath`).

- **Czcionki lecą w DWÓCH listach**, tak jak grupy w comboboksie okna Cantio: `fonts` (wbudowane —
  te same na każdym komputerze) i `systemFonts` (zainstalowane w Windows). Systemowych świadomie
  nie pomijamy: domyślne ustawienie parafii to „Segoe UI”, więc lista bez nich nie pozwoliłaby nawet
  wrócić do stanu wyjściowego. Koszt zmierzony u użytkownika: **9,6 kB przy 602 czcionkach** — rząd
  wielkości mniej niż `setlists_data`.
- **Trzy tryby dopasowania czcionki na DWÓCH kluczach** (v1.70). `font_auto_fit` **nie zmieniło
  znaczenia** (`false` = stała wielkość z ustawień), a `font_fit_scope` mówi tylko, co uznajemy
  za grupę przy wyrównywaniu rozmiaru policzonego per slajd: `song` = minimum z CAŁEJ pieśni
  (domyślne, zachowanie sprzed zmiany), `verse` = minimum w obrębie ZWROTKI (zwrotka podzielona
  na trzy slajdy ma na nich tę samą czcionkę, różnice występują między zwrotkami). Combobox
  w oknie Cantio pokazuje to jako jedną listę: stała / do zwrotki / do pieśni.
  **Przy `font_auto_fit: false` nowy klucz nie robi NIC** — każdy slajd i tak dostaje rozmiar
  z ustawień (`DisplayViewModel.BuildLayoutSettings` wymusza wtedy `Song`).
  Arytmetyka wyrównania: czysta `Cantio.Core/Services/SlideFontFit.Unify` — jedno miejsce dla
  wszystkich czterech grup slajdów (zwykłe, prywatne `p`, zwrotki psalmu, `BuildSlides`).
  **Zgodność wsteczna:** klucz jest DOPISANY; stary Pilot go nie zna, nie wyśle i dalej przełącza
  samo `font_auto_fit` — parafia dostaje wtedy dotychczasowe „dopasowana do pieśni”. Brak klucza
  w bazie = `song`.
- **Liczby zapisywane są w BIEŻĄCEJ kulturze** (`ToString(CultureInfo.CurrentCulture)`), bo tak
  zapisuje je `SzablonViewModel.SaveAsync` i tak czyta `GetSettings`. Zapis „1.45” w pl-PL wróciłby
  jako śmieć. Harness ma na to asercję round-tripu każdego z 26 kluczy.
- **Po zapisie MUSI iść przebudowa slajdów.** `MainWindow` woła `SzablonViewModel.ApplyExternalSettingsAsync()`
  (przeładowanie pól zakładki + `ProjectionViewModel.ApplySettings`) i `DisplayViewModel.RebuildSlides()`
  — tę samą parę co „ZAPISZ USTAWIENIA”. Bez przeładowania zakładki najbliższy zapis w oknie cofnąłby
  zmianę operatora przy tablecie.
- **Kierunek odwrotny:** „ZAPISZ USTAWIENIA” w oknie Cantio też rozgłasza `display_settings_data` —
  event `SzablonViewModel.Saved` w `MainWindow`. Komunikat składa **wyłącznie**
  `PilotDisplaySettings.BuildDataJson`; `ApplyExternalSettingsAsync` celowo NIE odpala `Saved`,
  żeby broadcast po komendzie z tabletu nie poleciał dwa razy.
- Logika: `Services/PilotDisplaySettings.cs` (`IsCommand` → routing w `RemoteControlServer`,
  `HandleAsync` → `Result(Response, Broadcast)`). Handler w `MainWindow.xaml.cs` jest głupi:
  wyślij → rozgłoś → `Dispatcher` odświeża UI.
- **Zgodność wsteczna:** wyłącznie DOPISANE typy — żaden istniejący komunikat nie zmienił kształtu.
  Stary Pilot nowych komend nie zna, więc ich nie wyśle, a nieznanego `display_settings_data`
  po prostu zignoruje. Obie komendy przechodzą normalną bramą auth (przed `auth_ok` cisza).

##### Ustawienia systemowe: tryb pracy, ekran, język, diecezja, lekcjonarz… (v1.70+)

W trybie serwerowym okno główne jest ukryte, a mini PC w zakrystii nie ma klawiatury — skrót
ratunkowy Ctrl+Alt+Shift+C jest tam bezużyteczny. Do tej pory oznaczało to, że **trybu
serwerowego nie dało się opuścić ani zmienić ekranu projekcji ŻADNYM sposobem**. Biała lista
`PilotDisplaySettings` świadomie tych kluczy nie dopuszcza („zdalna zmiana ekranu potrafi
odciąć operatora od obrazu") — ale to rozumowanie zakłada operatora PRZY komputerze, a takiego
tu nie ma. Stąd osobna rodzina komend, z własnym bezpiecznikiem.

| P→D | pola | akcja |
|---|---|---|
| `get_system_settings` | — | → `system_settings_data` do nadawcy |
| `set_system_settings` | `settings:{klucz:wartość,…}` | zapis + `ack {command, ok, keys, trial, restartRequired}` do NADAWCY + broadcast `system_settings_data` do WSZYSTKICH |
| `system_settings_confirm` | — | `ack {command, ok:true, confirmed}` — `confirmed:false` = nie było czego potwierdzać (odliczanie minęło albo go nie było) |
| `system_settings_revert` | — | **natychmiastowe** cofnięcie trwającej próby (ekran i/lub port): `ack {command, ok:true, reverted}` do NADAWCY + broadcast `system_settings_data`; `reverted:false` = nie było trwającej próby (nic nie zmieniamy, nic nie rozgłaszamy) |
| `pilot_forget_devices` | — | „nowy PIN" z tabletu: losowanie PIN-u + KASACJA wszystkich tokenów + rozłączenie klientów; `ack {command, ok, pin, pairedDevices:0}` + broadcast `system_settings_data` |

`system_settings_data` niesie: `settings`, `screens`, `languages`, **`dioceses`** (kanoniczna lista
z `DiocesanCalendarService.Diecezje` — tablet nie ma jej skąd wziąć), `restartRequired`, `trialSeconds`
oraz stan serwera pilota TYLKO DO ODCZYTU: **`pilotRunning`**, **`pairedDevices`** (liczba tokenów).

**Biała lista (11 kluczy):**

| klucz | typ JSON | dozwolone |
|---|---|---|
| `app_mode` | string | `dual` · `server` |
| `projection_screen` | number | `0..screens-1` (poza zakresem → `invalid_value`, **nic nie zapisane**) |
| `language` | string | `pl` · `en` · `es` |
| `diocese` | string | `""` (kalendarz ogólny) albo nazwa z `dioceses` — porównanie DOKŁADNE, bo literówka dałaby kalendarz bez obchodów diecezjalnych, wyglądający na poprawny |
| `lectionary` | string | `N` (nowy) · `S` (stary, druk 1975); **bez** `LectionaryFilter.Normalize` — ono zamienia śmieci na domyślne, a cicha podmiana wydania wygląda jak samowolna zmiana treści na projekcji |
| `loop_interval` | number | 3–120 s; **odrzucamy zamiast dociąć** (`ClampInterval`), tablet ma dostać odmowę, a nie inną liczbę niż wybrał |
| `load_last_setlist` | bool | w bazie `"1"`/`"0"` (tak zapisuje „ZAPISZ USTAWIENIA"), nie `"true"`/`"false"` |
| `run_on_startup` | bool | **NIE jest wierszem tabeli `settings`** — stoi w rejestrze Windows (`HKCU\…\Run`) |
| `pilot_pin` | string | DOKŁADNIE 4 cyfry ASCII (`12a4`, `12345`, `123`, `""`, `1234` jako liczba i cyfry pełnej szerokości → `invalid_value`); zmiana **NIE kasuje tokenów** |
| `pilot_require_pin` | bool | **wyłącznie `true`** — próba wyłączenia → `ok:false, reason:"not_allowed"`, zero zmian (niżej: „Asymetria PIN-u") |
| `pilot_port` | number | 1024–65535; zmiana na PRÓBĘ z automatycznym cofnięciem po 60 s |

**Czego świadomie NIE wystawiamy:** `pilot_remember` i `pilot_was_running` (`unknown_key`).
Ich wyłączenie oznacza mini PC, które po restarcie wstaje bez serwera pilota — dokładnie ten
lockout, przed którym broni cała ta rodzina komend.

- **Skutki uboczne wykonuje gospodarz, ale decyduje rdzeń.** `Result` niesie `DioceseChanged`
  i `LectionaryChanged` (ustawiane tylko przy REALNEJ zmianie wartości), a `MainWindow` odpala
  ISTNIEJĄCE zdarzenia `SzablonViewModel.RaiseDioceseChanged()` / `RaiseLectionaryChanged()` —
  te same, które odpala przełącznik w oknie, więc odbiorcy (dzień liturgiczny na pasku, podpisy
  PRZYPIĘTYCH, przeładowanie pieśni z filtrem lekcjonarza) są jedną listą, nie dwiema.
  Samo `ApplyExternalSettingsAsync` ich nie odpali — `LoadAsync` wczytuje te pola z guardami
  (`_dioceseLoading`, `_lectionaryLoading`), żeby nie zapisywać wartości drugi raz.
  `loop_interval` i wygaszony ekran wchodzą już w samym `LoadAsync`.
- **Autostart Windows przechodzi PORTEM.** `Cantio.Core` jest czystym `net10.0` i rejestru nie zna,
  więc gospodarz wstrzykuje `PilotSystemSettings.RunOnStartup = new RunOnStartupPort(Read, Write)`
  (ten sam wzorzec co `PilotImages.Scaler`). `Write` idzie przez `SzablonViewModel.RunOnStartup`,
  czyli DOKŁADNIE tą samą ścieżką co checkbox w oknie — inaczej zakładka USTAWIENIA kłamałaby
  o stanie rejestru. Broadcast po zapisie czyta port PONOWNIE, więc mówi o rejestrze, a nie
  o życzeniu tabletu. Brak portu (host bez rejestru) = `invalid_value`; świadomie nie udajemy,
  że zapis się udał. Harness podstawia atrapę portu i **nie dotyka rejestru**.

- Wzorzec 1:1 jak przy wyglądzie: klucze komunikatu = klucze tabeli `settings`, aktualizacja
  CZĘŚCIOWA przyjmowana ATOMOWO (jeden zły klucz = nie zapisujemy niczego, `ack ok:false`,
  `reason` + `key`, **żadnego broadcastu**), komunikaty składa WYŁĄCZNIE
  `Services/PilotSystemSettings.cs` (+ `PilotStatus.BuildAckJson`).
- **PUŁAPKA, dla której ta rodzina w ogóle dotyka kluczy pilota.** W trybie `dual` serwer
  pilota startuje TYLKO gdy `pilot_remember=1` **i** `pilot_was_running=1`
  (`AppModeRules.ShouldAutoStartPilotServer`, `RemoteControlViewModel.InitAsync`); w trybie
  serwerowym startuje zawsze. Naiwne przełączenie `server → dual` z tabletu kończyłoby się
  maszyną, która po restarcie **nie ma ŻADNEGO interfejsu** — ani okna, ani klawiatury, ani
  pilota, czyli stanem GORSZYM niż przed zmianą. Dlatego każdy zdalny zapis `app_mode` wymusza
  oba klucze na `"1"` (w obie strony — powrót ma być możliwy także po `dual → server`).
  Ścieżka UI (`SzablonViewModel.OnServerModeChanged`) wymusza tylko autostart **Windows**,
  co tu nie wystarcza. Niezmiennik dowiedziony sabotażem: zdjęcie wymuszenia = 5 FAIL.
  Sam `pilot_remember` z tabletu **nie jest przyjmowany** (`unknown_key`) — tablet nie może
  sobie odciąć drogi powrotu.
- **Zmiana ekranu „na próbę"** (wzorzec ze zmiany rozdzielczości w Windows) dotyczy WYŁĄCZNIE
  `projection_screen`: zmiana wchodzi od razu (`trial:true` w acku), desktop odlicza
  `trialSeconds` (20), a bez `system_settings_confirm` wraca na poprzedni ekran, przestawia
  okno projekcji i **rozgłasza `system_settings_data`** (tablet widzi powrót bez pytania).
  Reguła to czysta maszyna stanu `Services/SystemSettingsTrial.cs` — czas przychodzi z zewnątrz,
  więc całość jest testowalna bez czekania 20 s. Druga zmiana w trakcie odliczania **nie
  nadpisuje ekranu powrotu** (wracamy do stanu sprzed zdalnego grzebania), przedłuża tylko
  termin; budzik w `MainWindow` jest „głupi" i po przebudzeniu pyta maszynę stanu, więc
  spóźniony po prostu nic nie robi. Sabotaż „brak potwierdzenia nie cofa ekranu" = 6 FAIL.
  **Cofanie jest w JEDNYM miejscu** (`ApplyRevertAsync`): prowadzą do niego obie drogi —
  wygaśnięcie odliczania (bez acka) i `system_settings_revert` z tabletu (z ackiem). Dwie
  niezależne implementacje cofania to układ, który w tym projekcie gubił już dane (dwie listy
  pól przy zapisie zestawu, v1.6). Sabotaż „revert nie cofa ekranu" = 4 FAIL.
  `app_mode` bezpiecznika nie potrzebuje (działa dopiero po restarcie), `language` też nie.
- **Restartu desktop NIE robi sam** — `restartRequired` (zapisany `app_mode` różni się od trybu,
  w którym proces realnie działa) mówi tabletowi, żeby zapytał użytkownika i wysłał istniejącą
  komendę `restart_app`.
- **Wykonanie po stronie okna:** ekran przestawia `DisplayViewModel.OpenProjectionFromRemoteAsync`
  (ta sama ścieżka co `open_projection` — re-czyta `projection_screen` i przelicza metryki DPI;
  drugiego takiego miejsca nie piszemy), i to **tylko gdy projekcja JEST otwarta** — zmiana
  ustawienia nie jest poleceniem „otwórz projekcję". Zakładkę USTAWIENIA odświeża
  `SzablonViewModel.ApplyExternalSettingsAsync()` (stamtąd też idzie podmiana języka w locie,
  przez `OnSelectedLanguageChanged` → `LocalizationManager`), która celowo NIE odpala `Saved` —
  inaczej przy okazji poleciałby drugi broadcast `display_settings_data`.
- **Indeks ekranu w `system_settings_data` jest PRZYCIĘTY** do liczby monitorów: dokładnie tak
  zachowuje się otwieranie projekcji (indeks poza zakresem spada na ostatni ekran), więc tablet
  widzi stan faktyczny, a nie liczbę, której nie da się wybrać z listy.
- **Rdzeń nie zna ekranów.** `WpfScreenHelper` żyje w projekcie WPF, więc listę monitorów podaje
  gospodarz jako zwykłe dane (`PilotSystemSettings.ScreenInfo`); wymiary w FIZYCZNYCH pikselach
  (`Screen.Bounds`), bo operator poznaje monitor po rozdzielczości, nie po DIU.
- **Zgodność wsteczna:** wyłącznie DOPISANE typy, żaden istniejący komunikat nie zmienił kształtu.
  Stary desktop nowych komend nie rozpozna i zamilknie, więc Pilot musi użyć wzorca
  `RemoteQueryMachine` („funkcja wymaga nowszego Cantio"); stary Pilot ich nie wyśle.
  Wszystkie cztery komendy przechodzą normalną bramą auth (`if (!authed) continue;`) — przed
  `auth_ok` cisza.
- Harness: `SystemSettingsTests.cs` (y1–y15).

###### Serwer pilota z tabletu: PIN, port, sparowane urządzenia (etap 3)

Wszystko, co dotyka ŻYWEGO serwera (PIN w pamięci, tokeny, przeładowanie gniazda), wchodzi
portem `PilotSystemSettings.PilotServerPort` — ten sam wzorzec co `RunOnStartupPort`
i `PilotImages.Scaler`. Rdzeń mówi CO, a `RemoteControlViewModel` wykonuje to swoimi
ISTNIEJĄCYMI ścieżkami (`SetPinFromRemote`, `EnableRequirePinFromRemote`,
`ForgetPairedDevicesFromRemote` → `NewPin`, `ApplyPortFromRemote`), więc kod QR i ekran
parowania na projekcji odświeżają się przy okazji, bez drugiej implementacji.

**ASYMETRIA PIN-u — NIE „naprawiać".** Wymaganie PIN-u wolno zdalnie **włączyć**, ale
**nie wyłączyć**: `pilot_require_pin:false` → `ok:false, reason:"not_allowed"` i zero zmian
w bazie oraz w serwerze. Powody:
1. Nic w scenariuszu ratunkowym (mini PC bez klawiatury) nie wymaga ZDJĘCIA uwierzytelniania —
   wszystkie problemy tego etapu rozwiązuje się z tabletu, który JEST już sparowany.
2. To jedyne ustawienie, którego zdalna zmiana może **wyłącznie obniżyć** bezpieczeństwo.
   Brak uwierzytelniania nazwaliśmy „jedyną realną dziurą" i zamknęliśmy go PIN-em w v1.6;
   zdalny wyłącznik oddawałby tę zdobycz każdemu, kto ma jeden ważny token.
3. Odmowa jest REGUŁĄ, nie porównaniem ze stanem bazy — „wyłącz" odpada nawet wtedy, gdy PIN
   już jest wyłączony, żeby nie dało się jej obejść kolejnością zapisów.
   Wyłączenie zostaje przy komputerze. Ta sama asymetria co przy autostarcie serwera pilota.
   Sabotaż „wyłączenie zaczyna przechodzić" = 8 FAIL.

**Zmiana PIN-u to NIE „nowy PIN".** Zapis `pilot_pin` zmienia kod i tyle — tokeny zostają,
połączone tablety zostają połączone, nowy PIN dotyczy KOLEJNYCH parowań. Kasowanie tokenów robi
wyłącznie `pilot_forget_devices` (odpowiednik przycisku „nowy PIN" w oknie: losuje PIN, kasuje
tokeny, rozłącza klientów). Pierwsza operacja jest codzienna, druga awaryjna (zginął tablet) —
skręcenie ich w jedno wyrzucałoby parafię z połączenia przy każdej zmianie kodu.
Sabotaż „zmiana PIN-u kasuje tokeny" = 5 FAIL.
**Ack `pilot_forget_devices` wychodzi PRZED odpięciem urządzeń** (ten sam układ co
`restart_app`), bo odpięcie zrywa połączenie nadawcy, a ack niesie nowy PIN: rdzeń losuje go
sam, zapowiada wykonanie polem `Result.ApplyForgetPin`, a gospodarz odpina DOPIERO po wysłaniu
acka i broadcastu — i ustawia PIN PODANY, nigdy własny (inaczej tablet pokazałby kod, który nie
obowiązuje). Sabotaż „odpięcie wraca przed ack" = 5 FAIL (y16).
Tablet MUSI ostrzec użytkownika przed wysłaniem
(„stracisz też swoje parowanie") i po wykonaniu pokazać, gdzie szukać nowego PIN-u: na
projektorze (tryb serwerowy, ekran parowania wraca sam, bo tokenów jest zero) albo w oknie Cantio.

**Port na próbę (60 s, nie 20).** Zmiana `pilot_port` rozłącza WSZYSTKIE tablety — i właśnie
dlatego jest najczystszym możliwym testem: potwierdzenie może przyjść tylko stamtąd, gdzie
serwer naprawdę jest. Desktop zapisuje port, przeładowuje serwer i odlicza; brak
`system_settings_confirm` = tablet nie dał rady wrócić = port był zły → powrót na poprzedni
i ponowne przeładowanie. Termin jest dłuższy niż przy ekranie, bo musi starczyć na zauważenie
zerwanego połączenia, odnalezienie serwera i UWIERZYTELNIENIE.
- **NIEZMIENNIK: stan próby MUSI przeżyć zerwanie połączenia.** Potwierdzenie przychodzi INNYM
  gniazdem niż komenda, po ponownym auth. Stan żyje w jednym `SystemSettingsTrial` w `MainWindow`
  (nie per-klient) i **nie wolno go czyścić w `ClientDisconnected`** — a jest to łatwa pomyłka
  przez analogię, bo obok stoi `ClientDisconnected → PilotUploads.DropOwner` (porzucone wysyłki
  obrazków), gdzie sprzątanie po rozłączeniu jest poprawne. Sabotaż „rozłączenie czyści próbę"
  = 3 FAIL (w tym samo potwierdzenie z drugiego gniazda).
- **Kolejność przy przeładowaniu:** ack i broadcast wychodzą PIERWSZE, dopiero potem
  `ApplyPortFromRemote` (najpierw `StopForRestart` = zwolnienie gniazda, potem `ToggleServer`).
  Odwrotna kolejność zostawiła już raz mini PC bez żadnego interfejsu. Nieudany start (port
  zajęty) nie jest ciszą: `StartFailure` ląduje na projekcji, a bezpiecznik i tak wraca na
  poprzedni port tą samą metodą.
- **Serwer wyłączony = zwykły zapis** (`trial:false`): nie ma czego rozłączać ani czym potwierdzić.
- Cofanie ma dalej JEDNO miejsce: `SystemSettingsTrial` trzyma dwa przedmioty próby (ekran, port)
  z własnymi terminami, ale JEDNĄ regułą (prywatna klasa `Subject`), a `ApplyRevertAsync` cofa
  oba. Jedno `system_settings_confirm` rozbraja obie próby naraz — tablet, który je przysłał,
  udowodnił i że widzi obraz, i że dobił się na nowy port.

##### Folder wymiany i operacje konserwacyjne na plikach (v1.70+, etap 4A)

Mini PC w zakrystii pracuje bez klawiatury i z ukrytym oknem, a tablet jest jedynym interfejsem.
Wszystko, co wymaga PLIKU (kopia zapasowa, eksport archiwum, import śpiewnika), było poza jego
zasięgiem, bo tablet nie widzi dysku komputera.

**Mechanizm: FOLDER WYMIANY `%LocalAppData%\Cantio\wymiana`** (obok `cantio.db` i `images`,
tworzony przy pierwszym użyciu; ścieżki liczy `Helpers/AppPaths`). Cantio pilnuje JEDNEGO
katalogu, tablet widzi wyłącznie jego zawartość, a pliki wkłada tam CZŁOWIEK — pendrivem,
udziałem sieciowym, czymkolwiek. Tablet nie przegląda dysku i nie przesyła wielkich plików przez
łącze, które służy do sterowania projekcją.

- **Tablet NIGDY nie podaje ścieżki zapisu.** Kopia i eksport lądują w folderze wymiany pod nazwą
  z datą i godziną (`MaintenanceOps.BackupFileName`/`ExportFileName`). Gdyby ścieżkę podawał
  tablet, miałby wpływ na to, gdzie komputer zapisuje pliki — a to jest dokładnie ta władza,
  której folder wymiany ma nie dawać.
- **`exchange_files_data` niesie PEŁNĄ ŚCIEŻKĘ katalogu** — bez niej instrukcja „włóż plik do
  folderu wymiany" jest nie do wykonania, bo operator nie wie, gdzie ten folder jest.
- **Listujemy WYŁĄCZNIE pliki leżące w katalogu WPROST**: bez rekurencji, bez `..`, bez ścieżek
  absolutnych. Ta sama zasada co `PilotImages.IsSafeRef`, tylko OSTRZEJ — tam ścieżki absolutne
  przechodzą (legacy obrazków w bazach parafii), tu nie ma żadnego legacy. Jedyne miejsce, w
  którym nazwa z protokołu zamienia się w ścieżkę na dysku, to `ExchangeFolder.Resolve`
  (kształt nazwy + sprawdzenie, że ZNORMALIZOWANY wynik nadal leży wprost w katalogu) — na tym
  strażniku stanie 4B, gdzie tablet będzie nazwy PODAWAŁ. Sabotaż „listowanie przepuszcza
  ścieżki spoza katalogu" = 5 FAIL.
- **Pusty katalog = PUSTA LISTA, nie błąd.** „Nic jeszcze nie wrzuciłem" to normalny stan.

**Długa operacja: ack natychmiast, wynik broadcastem.** `maintenance_run {op}` odpowiada od razu
`ack {op, taskId}`, a operacja rusza DOPIERO po wysłaniu acka i leci w tle (handler w
`MainWindow` woła `Result.Work` **bez `await`**). Postęp i wynik idą broadcastem
`maintenance_progress` do WSZYSTKICH — drugi tablet w zakrystii ma widzieć, że ktoś właśnie robi
kopię. `percent` dziś: `0` przy starcie, rosnący przy pakowaniu archiwum, `100` przy `done`.

- **NIEZMIENNIK: operacja ZAWSZE kończy się komunikatem TERMINALNYM** (`done` albo `failed`),
  także gdy rzuci wyjątkiem. Po acku tablet CZEKA, więc cisza to zawieszony ekran bez wyjścia.
  To ten sam wniosek, co przy `get_songs` („desktop NIGDY nie milczy", v1.64) — tam połknięty
  `catch{}` potrafił zawiesić Pilota na stronie 0 biblioteki. Gwarancję daje jedno miejsce:
  `PilotMaintenance.ExecuteAsync` (całe ciało w `try`, każda ścieżka wyjścia wysyła komunikat,
  `finally` zwalnia blokadę). Nawet wysyłka jest osłonięta — zerwane łącze w trakcie kopiowania
  bazy jest normalne i nie może zjeść wyniku. Sabotaż „wyjątek przestaje wysyłać komunikat
  terminalny" = 1 FAIL.
- **Jedna operacja naraz.** Druga dostaje `ok:false, reason:"busy"` + `taskId` zadania, które
  TRWA. Sprawdzenie, rezerwacja i odczyt trwającego identyfikatora są w JEDNEJ sekcji krytycznej
  (`Runner.TryStart(out taskId)`) — odczyt „kto zajmuje" poza blokadą trafiał w `null`, gdy
  operacja kończyła się w tej samej milisekundzie (złapane w harnessie).
- **Nieznana operacja NIE rezerwuje blokady** — literówka z tabletu nie może zablokować maszyny
  na czas, którego nikt nie zwolni.
- **Stan blokady jest APLIKACYJNY, nie per-klient** (`MainWindow._maintenanceRunner`): operacja
  trwa dalej, gdy tablet się rozłączy, i drugi tablet ma wtedy dostać `busy`.

**JEDNA implementacja operacji dla okna i tabletu** — `Services/MaintenanceOps.cs`. Przycisk
w zakładce USTAWIENIA i komenda z tabletu robią dokładnie to samo, różniąc się WYŁĄCZNIE tym,
skąd bierze się ścieżka docelowa (okno dialogowe vs folder wymiany). Dwie niezależne kopie tej
samej operacji to układ, który w tym projekcie już raz zgubił dane (dwie listy pól przy zapisie
zestawu, v1.6). `import_psalms` idzie wprost przez `DatabaseService.ImportPsalmySeedAsync` (ta
sama metoda co przycisk); `-1` = brak kategorii „Psalmy responsoryjne" → `failed` z
`message:"psalms_category_missing"`, nie „sukces z zerem psalmów".

**Etap 4A świadomie NIE daje:** pobrania kopii na tablet (archiwum z obrazkami potrafi ważyć
setki megabajtów — plik czeka w folderze wymiany na człowieka) ani żadnej operacji NISZCZĄCEJ.
Przywrócenie bazy, import archiwum, czyszczenie bazy i importy OpenLP/OpenSong/OSZ to **4B**.
Razem z 4B do naprawy jest „zip slip" w `SzablonViewModel.ImportZip` (wpis `..\..\cokolwiek`
zapisuje plik poza `AppData\Cantio`) — dziś trzeba go samemu wyklikać, ale po 4B archiwum będzie
pochodzić z folderu, do którego pliki wkłada ktokolwiek.

**Zgodność wsteczna:** wyłącznie DOPISANE typy, żaden istniejący komunikat nie zmienił kształtu.
Stary desktop nowych komend nie rozpozna i zamilknie, więc Pilot musi użyć wzorca
`RemoteQueryMachine` („funkcja wymaga nowszego Cantio"); stary Pilot ich nie wyśle. Obie komendy
przechodzą normalną bramą auth — przed `auth_ok` cisza i zero plików na dysku.

Harness: `MaintenanceTests.cs` (m1–m8).

##### Edytor pieśni (v1.63+)

Parafia z samym tabletem (tryb serwerowy) musi móc poprawić literówkę, dodać nową pieśń i usunąć
zbędną — w BAZIE DESKTOPU, bo to ona jest źródłem prawdy dla projekcji. Kierunek prawdy jak przy
kategoriach i wyglądzie: tablet wyłącznie komenduje, desktop zapisuje, odświeża własne UI tą samą
ścieżką co po edycji lokalnej i rozgłasza wynik.

- P→D `song_get {id}` → `song_data` **do nadawcy**. `categoryId: 0` dla pieśni bez kategorii
  (ta sama konwencja co `songs_data` — stary Pilot ma tam twardy int); `author` i `playOrderJson`
  **nigdy nie są null** (pusty string = brak).
- P→D `song_create` / `song_update` → `ack {command, ok, id, title, verses}` do NADAWCY
  + **mały** broadcast `song_changed {id, action}` do WSZYSTKICH. Pełnego `songs_data` NIE rozgłaszamy —
  biblioteka pieśni bywa duża i leci wyłącznie na żądanie `get_songs`; Pilot po broadcaście dociąga sam.
- **`song_update`: BRAK POLA = NIE RUSZAJ.** Reguła obowiązuje jednakowo dla `verses`, `number`,
  `categoryId`, `author` i `playOrderJson` — telefon, który danego pola nie pokazuje, nie ma prawa
  go wyczyścić po cichu. Do v1.62 tylko `author` był tak chroniony, więc klient zmieniający SAM
  TYTUŁ kasował wszystkie zwrotki, numer i przypisanie do kategorii.
  - `verses` PRZYSŁANE → zastępują treść w CAŁOŚCI (`SaveSongAsync` kasuje stare zwrotki i wstawia
    nowe z pozycjami 0..n-1), dokładnie jak przycisk ZAPISZ w edytorze okna;
  - `verses: []` (jawna pusta tablica) w `song_update` → `ok:false, reason:"empty_verses"`, NIC
    nie zapisujemy. Pieśń bez zwrotek nie ma czego wyświetlić, więc odmowa zamiast cichej utraty;
  - `number: 0` / `categoryId: 0` PRZYSŁANE jawnie to normalne „wyczyść" — liczy się obecność pola,
    nie wartość;
  - `playOrderJson` bez pola: przy wymianie zwrotek → naturalna (stare indeksy są nieważne), przy
    nietkniętych zwrotkach → zostaje dotychczasowa.
- **Obrazki zwrotek przeżywają round-trip.** `song_data` nie niesie `ImagePath` ani
  `BackgroundImagePath` (plik leży na dysku komputera, telefon go nie widzi), więc pętla
  `song_get` → `song_update` wyzerowałaby tła ustawione w oknie. `PilotSongEdit.CarryOverVerseImages`
  przenosi je ze starej treści **po POZYCJI**; `ImagePath` dodatkowo tylko przy zgodnym typie
  zwrotki (zamiana zwrotki-obrazka na tekstową to zmiana rodzaju, nie edycja tła).
- Walidacja jest **uprzednia i atomowa** — jedna zła zwrotka i nie zapisujemy NICZEGO. Pieśń w pół
  drogi (część zwrotek nowych, część starych) wygląda na projektorze jak awaria w środku mszy.
- `reason` przy `ok:false`: `not_found` (pieśń albo `categoryId` > 0 bez pokrycia w bazie) ·
  `empty_title` · `empty_verses` · `unsupported_type` (+ `verseType`) · `invalid_play_order` ·
  `in_setlists` (+ `setlists`).
- **Typy zwrotek z tabletu: tylko `v`/`c`/`b`/`p`.** `img` jest świadomie odrzucany
  (`unsupported_type`) — obrazek wymaga pliku na dysku komputera, którego telefon nie widzi.
  Brak pola `type` = `v`. Zwrotki-obrazki edytuje się w oknie Cantio.
- `playOrderJson` to indeksy zwrotek jako JSON (tak trzyma to kolumna). Brak pola = kolejność
  naturalna (`null`) — pełne zastąpienie treści unieważnia stare indeksy. Indeks poza zakresem
  przysłanych zwrotek → `invalid_play_order`.
- **`song_delete` — dlaczego inaczej niż w oknie.** Okno pyta tylko „Usunąć pieśń?" i kasuje razem
  z pozycjami w ZAPISANYCH zestawach (`DeleteSongAsync` usuwa `SetlistItems`, bo FK ma RESTRICT).
  Przy tablecie nie ma nikogo, kto by ten skutek przewidział, więc protokół najpierw **odmawia**:
  `ok:false, reason:"in_setlists", setlists:N` i **nic nie rusza**. Dopiero `force:true` robi to samo,
  co przycisk w oknie. Pieśń spoza zestawów kasuje się bez pytania. Liczbę zestawów podaje
  `DatabaseService.CountSetlistsWithSongAsync` (distinct po `SetlistId`).
- **`song_delete` na pieśni otwartej w edytorze okna zamyka ten edytor** (v1.63).
  `_editingSong` wskazywałby na nieistniejący rekord, a przycisk ZAPISZ trafiałby w `throw`
  z `SaveSongAsync`, który `AsyncRelayCommand` przerzuca na wątek UI — czyli ubijał aplikację.
  Operator dostaje komunikat (w trybie serwerowym tylko log).
- **Poprawka pieśni, która JEST NA EKRANIE, wchodzi natychmiast.** `MainWindow` woła
  `DisplayViewModel.OnSongEditedExternallyAsync(id, deleted)` — odświeżenie list + (gdy `SelectedSong.Id`
  się zgadza) `LoadVersesAsync(id, keepPosition: true)`, czyli DOKŁADNIE ogon `SaveEditedSongAsync`.
  Pozycję trzyma kotwica zwrotka/część (`SlideAnchor`). Odraczania wejścia poprawki NIE wprowadzać —
  było testowane u organisty i cofnięte (v1.6 w głównym CLAUDE.md).
- Logika: `Services/PilotSongEdit.cs` (`IsCommand` → routing w `RemoteControlServer`, `HandleAsync` →
  `Result(Response, Broadcast, Change, SongId)`). Handler w `MainWindow.xaml.cs` jest głupi:
  wyślij → rozgłoś → `Dispatcher` odświeża UI. Komunikaty składa wyłącznie `PilotSongEdit.BuildSongDataJson`
  / `BuildSongChangedJson` + `PilotStatus.BuildAckJson`.
- **`sync_push` zostaje bez zmian** — to osobna, jednostronna ścieżka synchronizacji pieśni z Pilota;
  nowe komendy żyją obok niej i jej nie dotykają.
- **Zgodność wsteczna:** wyłącznie DOPISANE typy — żaden istniejący komunikat nie zmienił kształtu
  (strażniki pełnych list pól w harnessie). Stary Pilot nowych komend nie zna, więc ich nie wyśle,
  a nieznanego `song_changed` po prostu zignoruje. Wszystkie cztery komendy przechodzą normalną bramą
  auth (przed `auth_ok` cisza).

##### Pieśni: edycja OFFLINE i wykrywanie konfliktu (v1.65+)

Pilot edytuje pieśni desktopowe także bez połączenia i odtwarza zmiany po powrocie do sieci
(`song_update`). Ta sama pieśń mogła się w tym czasie zmienić w oknie Cantio — reguła jest jak przy
zestawach: **zmiana po jednej stronie → zastosuj po cichu; pytamy TYLKO przy realnym konflikcie**
(pytanie pokazuje Pilot, desktop go wyłącznie wykrywa).

- Nośnik: kolumna **`Songs.UpdatedAt`** (migracja `DodajUpdatedAtDoSong`, nieniszczące ADD COLUMN,
  backfill istniejących wierszy DATĄ MIGRACJI — zero wyglądałoby jak „nigdy nie zmieniona").
  Na łącze idzie zawsze jako **ms epoki UNIX** (`PilotSongSync.ToUnixMs`), nigdy jako tekst daty
  (format zależałby od kultury komputera).
- `baseUpdatedAt` (long, ms) = wartość `updatedAt`, którą Pilot ostatnio widział dla tej pieśni
  (z `songs_data`, `song_data`, acka albo broadcastu `song_changed`).
- **Konflikt = `baseUpdatedAt` przysłane i RÓŻNE od bieżącego `Songs.UpdatedAt`.** Wtedy desktop
  **nie zapisuje niczego**, nie rozgłasza `song_changed` i odsyła `song_update_conflict`
  z pełną wersją desktopową (ten sam komplet pól co `song_data`).
- **Różność, nie „nowszy" — i to jest różnica wobec zestawów.** Przy zestawach desktop zapisuje
  znacznik PRZYSŁANY przez telefon, więc porównuje `>`. Przy pieśniach znacznik nadaje **desktop
  własnym zegarem** i odsyła go w acku oraz w broadcastcie; telefon zapisuje tę wartość jako nową
  bazę. Dzięki temu zegary obu urządzeń w ogóle nie biorą udziału w porównaniu — a gdyby ktoś
  cofnął zegar PC, warunek „nowszy" ukryłby realną zmianę.
- `force: true` → pomija sprawdzenie i nadpisuje (Pilot wysyła po wyborze „wersja z telefonu").
- **Zgodność wsteczna:** brak `baseUpdatedAt` = zachowanie sprzed zmiany (bezwarunkowy zapis
  + `ack`) — tak działa Pilot już zainstalowany u użytkownika. `force` bez `baseUpdatedAt` niczego
  nie zmienia. Konflikt dotyczy WYŁĄCZNIE `song_update`; `song_create` nie ma czego porównywać.
- Rozstrzygnięcie „wersja z komputera" po stronie Pilota nie wymaga żadnej komendy — telefon bierze
  dane z `song_update_conflict` (albo z `song_get`) i nadpisuje siebie.
- Logika w JEDNYM miejscu: `Services/PilotSongEdit.SaveAsync` (sprawdzenie tuż po wczytaniu pieśni,
  PRZED jakąkolwiek walidacją treści) + `PilotSongEdit.BuildUpdateConflictJson`. Handler
  w `MainWindow.xaml.cs` jest nietknięty — konflikt jedzie zwykłym `Result.Response`, a `Broadcast`
  jest `null`, więc żadne UI się nie odświeża.
- Harness: `SongUpdatedAtTests.cs` (su0–su6). Sabotaże potwierdzone: wycięcie podbijania znacznika
  w `SaveSongAsync` = 1 FAIL („znacznik w bazie PODBITY"), wycięcie warunku konfliktu = 8 FAIL
  w (su2) przy nietkniętej reszcie kontraktu.

### `UpdatedAt` — kto podbija, a kto NIE (kluczowe dla wykrywania konfliktów)

Cała detekcja konfliktów opiera się na tym znaczniku (Pilot porównuje `desktop.updatedAt != lastSyncedUpdatedAt`), więc reguła jest sztywna:

**`Setlists.UpdatedAt` — zestawy:**

| metoda | podbija `UpdatedAt`? | dlaczego |
|---|---|---|
| `SaveSetlistAsync` | **TAK**, zawsze | zapis zestawu = zmiana treści |
| `SaveSetlistItemsAsync` | **TAK** (zestaw nadrzędny, w tej samej transakcji) | dodanie/usunięcie/przeniesienie pieśni |
| `CreateOrUpdateSetlistFromPilotAsync` | **NIE** — zapisuje znacznik **przysłany przez Pilota** | ta sama wartość wraca w `setlist_sync_ack` i staje się nową bazą `baseUpdatedAt`; własny czas desktopu = fałszywy konflikt przy każdej synchronizacji |
| `SetSetlistPinnedAsync` | **NIE** | przypięcie to flaga UI, nie zmiana treści; inaczej kliknięcie pinezki generowałoby konflikt. **Dotyczy tak samo komendy `setlist_pin` z Pilota (v1.63)** — jedyna droga zapisu tej flagi to ta metoda, nigdy `SaveSetlistAsync` |
| `SaveSetlistItemNotesAsync` | **NIE** | Pilot nie przenosi notatek; przy pełnym „ZAPISZ ZESTAW" i tak idzie `SaveSetlistItemsAsync` |
| `set_display_settings` (`PilotDisplaySettings`, v1.63) | **NIE** | wygląd projekcji to ustawienia aplikacji (tabela `settings`), nie treść zestawu — podbicie znacznika dałoby fałszywy konflikt na wszystkich zestawach naraz |
| komendy edytora pieśni (`PilotSongEdit`, v1.63) | **NIE** — żadna (mowa o `Setlists.UpdatedAt`) | pieśń nie należy do zestawu; podbicie znacznika po poprawieniu literówki dałoby fałszywy konflikt na wszystkich zestawach, które tę pieśń zawierają. `song_delete {force:true}` kasuje pozycje zestawów przez `DeleteSongAsync` (parytet z oknem) i też NIE dotyka `UpdatedAt` zestawu. **Znacznik samej PIEŚNI podbijają** — tabela niżej |
| komendy kategorii i grup (`PilotCategorySync`, v1.63) | **NIE** — żadna | kategorie nie należą do zestawu, a operacje na grupach ruszają wyłącznie ustawienie `setlist_groups`; zestawy nie są dotykane nawet przy `setlist_group_rename`/`delete` (parytet z UI), więc podbicie znacznika oznaczałoby fałszywy konflikt na wszystkich zestawach naraz |

**`Songs.UpdatedAt` — pieśni (v1.65+):** znacznik podbija **KAŻDA zmiana treści widocznej dla Pilota**.

| metoda / ścieżka | podbija `Songs.UpdatedAt`? | dlaczego |
|---|---|---|
| `SaveSongAsync` (pełny edytor okna, „wklej całość", `song_create`/`song_update` z Pilota, tekst jednorazowy zapisany jako pieśń) | **TAK**, zawsze | zapis pieśni = zmiana treści. Nowa wartość wraca na przekazanym obiekcie i leci do Pilota w acku **oraz** w `song_changed` — telefon zapisuje ją jako nową bazę `baseUpdatedAt` |
| `SaveSongWithVersesAsync` (importy OpenLP/OpenSong/OSZ) | **TAK** | import z nadpisaniem podmienia treść pieśni |
| `SaveVerseTextAsync` / `SaveVerseTextsAsync` (szybki edytor zwrotki) | **TAK** (pieśń nadrzędna, w tej samej transakcji — `TouchSongsAsync`) | poprawiona literówka MUSI być dla Pilota widoczna, inaczej telefon nadpisze ją swoją wersją |
| `SaveVerseOrderAsync` (kolejność zwrotek) | **TAK** (jw.) | kolejność jest częścią treści, którą Pilot pobiera |
| `SyncPushSongsAsync` (stara, jednostronna ścieżka `sync_push`) | **TAK** | treść przyjechała z telefonu = zmiana treści; drugi tablet musi ją zobaczyć |
| `TouchSongUsageAsync` (`LastUsedAt`, lista „Ostatnie") | **NIE** | wyświetlenie pieśni na projektorze nie jest edycją; podbijanie dawałoby konflikt po każdej mszy |
| `SaveSongFontSizeOverrideAsync` (`FontSizeOverride`) | **NIE** | ustawienie PROJEKCJI per pieśń, którego protokół w ogóle nie niesie — jak flaga UI przy pinowaniu zestawu |
| przypisanie pieśni do zestawu, przypięcie, notatki pozycji | **NIE** | to zmiany zestawu, nie pieśni |
| **kategoria pieśni** (`song_update {categoryId}`, „usuń kategorię, zostaw pieśni") | **TAK przy zapisie pieśni**; masowe odczepienie przy kasowaniu kategorii świadomie NIE | kategoria JEST treścią widoczną dla Pilota, ale `DeleteCategoryKeepSongsAsync` dotyka setek pieśni naraz, a Pilot i tak przeładuje bibliotekę po broadcastcie `categories_data` |

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
