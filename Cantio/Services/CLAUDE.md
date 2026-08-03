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
| `show_song` | `songId` | pokaż pieśń NA EKRANIE bez dodawania jej do zestawu (odpowiednik 👁 w oknie Cantio) |
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
| `setlist_pin` | `desktopId`, `pinned` (bool) | przypnij/odepnij zestaw w panelu PRZYPIĘTE (→ `ack` + broadcast `setlist_pinned`) |
| `pin_next_week` | — | „Przypnij tydzień”: przypina 7 kolejnych dni od dziś (→ `ack {pinned, days[]}` + broadcasty `setlist_pinned` i `pinned_celebrations`) |
| `get_categories` | — | → `categories_data` **do nadawcy** |
| `category_add` | `name` | nowa kategoria na końcu kolejności |
| `category_rename` | `id`, `name` | zmiana nazwy |
| `category_delete` | `id`, **`withSongs?`**, **`keepSongs?`** | usunięcie; niepusta kategoria WYMAGA `withSongs:true` (kasuje pieśni) albo `keepSongs:true` (pieśni zostają bez kategorii) |
| `category_move` | `id`, `direction` (`up`/`down`) | przesunięcie w kolejności (1:1 ze strzałkami ▲▼) |
| `get_setlist_groups` | — | → `setlist_groups_data` **do nadawcy** |
| `setlist_group_add` | `name` | nowa grupa zestawów |
| `setlist_group_rename` | `name`, `newName` | zmiana nazwy grupy |
| `setlist_group_delete` | `name` | usunięcie grupy |
| `song_get` | `id` | → `song_data` **do nadawcy** (pełna treść pieśni do edycji) |
| `song_create` | `title`, `number?`, `categoryId?`, `author?`, `verses[]` (`{type,text}`), `playOrderJson?` | nowa pieśń (→ `ack` z nadanym `id` + broadcast `song_changed`) |
| `song_update` | `id` + te same pola co `song_create` | zastąpienie treści W CAŁOŚCI (→ `ack` + broadcast `song_changed`) |
| `song_delete` | `id`, **`force?`** | usunięcie pieśni; pieśń w zapisanych zestawach wymaga `force:true` |
| `get_display_settings` | — | → `display_settings_data` **do nadawcy** |
| `set_display_settings` | `settings` (obiekt klucz→wartość) | częściowa zmiana wyglądu projekcji (→ `ack` + broadcast `display_settings_data`) |
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
| `categories_data` | `categories[]` (`{id,name,number}`) | kategorie — na `ClientConnected`, na `get_categories` (do nadawcy) i **broadcastem po każdej mutacji** (v1.63) |
| `setlist_groups_data` | `groups[]` (stringi, kolejność z CSV) | grupy zestawów — na `get_setlist_groups` i broadcastem po mutacji (v1.63) |
| `songs_data` / `setlist_detail` / `sync_push_ack` | — | dane sync |
| `setlists_data` | `setlists[]` (`{id,name,group,songCount,updatedAt,`**`pinned`**`}`) | biblioteka zestawów — TYLKO na żądanie `get_setlists` (bywa duża); `pinned` dopisane w v1.63 |
| `setlist_pinned` | `desktopId`, `pinned` | zmieniono przypięcie zestawu — broadcast do WSZYSTKICH (v1.63) |
| `pinned_celebrations` | `items[]` (`{desktopId, celebration}`) | podpisy obchodów pod przypiętymi zestawami (np. „wsp. Św. Dominika, prezbitera”) — broadcast po KAŻDEJ zmianie pinów i na `ClientConnected`; wyłącznie wpisy z NIEPUSTYM podpisem, pusta lista = skasuj podpisy (v1.63) |
| `setlist_sync_ack` | `desktopId`, `name`, `updatedAt` | zestaw zapisany; `desktopId` = ID nadane przez desktop, `updatedAt` = wartość przysłana przez Pilota (nowa baza do `baseUpdatedAt`) |
| `setlist_sync_conflict` | `desktopId`, `name`, `updatedAt`, `songs[]` (`{id,title}`) | zestaw zmieniono po obu stronach — NIC nie zapisano; pola niosą wersję **desktopową** do pokazania użytkownikowi |
| `setlist_delete_ack` | `desktopId`, `existed` (bool) | zestaw usunięty; `existed=false` = już go nie było |
| `song_data` | `id, title, number, categoryId, author, verses[]` (`{type,text}`), `playOrderJson` | pełna treść pieśni — TYLKO na żądanie `song_get` (v1.63) |
| `song_changed` | `id`, `action` (`created`/`updated`/`deleted`) | pieśń dodano/zmieniono/usunięto — broadcast do WSZYSTKICH, w tym do nadawcy (v1.63) |
| `display_settings_data` | `settings` (23 klucze wyglądu), `fonts[]` (wbudowane), `systemFonts[]` (zainstalowane w Windows) | ustawienia projekcji — na `get_display_settings` (do nadawcy) i broadcastem po każdej zmianie: z tabletu ORAZ po „ZAPISZ USTAWIENIA" w oknie Cantio (v1.63) |
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
tylko obchód, który REALNIE ją wypiera (uroczystość/święto). **Wspomnienie obowiązkowe nigdzie nie
wypływało** — organista dowiadywał się o nim dopiero przy ołtarzu.

> **Nazwa zestawu zostaje NIETKNIĘTA.** Podpis jest liczony PRZY WYŚWIETLANIU i nigdy nie trafia do
> `Setlist.Name`: te same zestawy wracają co roku („18 Pon” w 2027 ma inne wspomnienie), więc doklejenie
> obchodu do nazwy zerwałoby dopasowanie w `GetSetlistForPinAsync` przy kolejnym przypinaniu.
> W modelu podpis żyje jako `[NotMapped] Setlist.Celebration` — do bazy nie ma jak wsiąknąć.

- **Czysta funkcja:** `Services/PinnedCelebrations.cs` (bez bazy, bez WPF). `CaptionFor(date,
  effectiveName, diocese)` bierze NAJWYŻSZY obchód rangi ≥ wspomnienie obowiązkowe i zwraca `""`, gdy:
  (a) to on wyparł nazwę dnia — nazwa już nim jest, podpis byłby duplikatem; (b) jest niedziela, a ranga
  < uroczystość — liturgicznie się tego nie obchodzi, więc podpis byłby fałszywą podpowiedzią.
  Prefiks `wsp. ` tylko dla wspomnienia; święto/uroczystość mówią same za siebie.
  `Build(pinned, from, days, diocese)` mapuje `id → podpis`, kojarząc zestaw z datą **po nazwie**
  (`DatabaseService.NameEquals`, pl-PL — dokładnie to, po czym rozpoznaje zestawy samo przypinanie).
- **Logika przypinania:** `Services/PilotPinWeek.cs` — JEDNO miejsce dla przycisku w oknie
  (`DisplayViewModel.PinNextWeekAsync`) i komendy `pin_next_week`. Operacja jest **idempotentna**:
  zestaw zakładany tylko gdy go nie ma, ponowne kliknięcie daje `pinned: 0`. Flaga idzie przez
  `SetSetlistPinnedAsync`, więc `UpdatedAt` zostaje nietknięte (tabela niżej).
- P→D `pin_next_week` (bez pól) → `ack {command:"pin_next_week", ok:true, pinned:N,
  days:[{date:"2026-08-08", name:"18 Sob", celebration:"wsp. Św. Dominika, prezbitera"}, …]}`.
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
  obowiązkowe, 8 IX = święto wypierające, 12 IX = wspomnienie diecezji gliwickiej, 13 IX 2026 =
  niedziela) + komenda przez realny `ClientWebSocket`. Sabotaż potwierdzony: wycięcie obu strażników
  w `CaptionFor` daje 2 FAIL.

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

`reason` przy `ok:false`: `duplicate` · `not_found` · `empty_name` · `not_empty` · `edge`.

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

**Biała lista (23 klucze — dokładnie te, których używa `DatabaseService.GetSettings`):**

| klucz | typ JSON | dozwolone |
|---|---|---|
| `font_family` | string | czcionka **wbudowana albo zainstalowana w systemie** (literówka = fallback WPF na inny krój w środku mszy) |
| `font_size` | number | 8–400 |
| `font_bold`, `font_auto_fit`, `shadow_enabled`, `bg_gradient_enabled` | bool | wyłącznie `true`/`false` (string `"true"` odrzucany) |
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

Czego na liście NIE MA i nie będzie bez osobnej decyzji: `projection_screen`, `language`, `app_mode`,
`pilot_*`, `blank_*`, `text_tags`. Zdalna zmiana ekranu projekcji albo trybu pracy potrafi odciąć
operatora od obrazu, a to nie jest „wygląd”.

- **Czcionki lecą w DWÓCH listach**, tak jak grupy w comboboksie okna Cantio: `fonts` (wbudowane —
  te same na każdym komputerze) i `systemFonts` (zainstalowane w Windows). Systemowych świadomie
  nie pomijamy: domyślne ustawienie parafii to „Segoe UI”, więc lista bez nich nie pozwoliłaby nawet
  wrócić do stanu wyjściowego. Koszt zmierzony u użytkownika: **9,6 kB przy 602 czcionkach** — rząd
  wielkości mniej niż `setlists_data`.
- **Liczby zapisywane są w BIEŻĄCEJ kulturze** (`ToString(CultureInfo.CurrentCulture)`), bo tak
  zapisuje je `SzablonViewModel.SaveAsync` i tak czyta `GetSettings`. Zapis „1.45” w pl-PL wróciłby
  jako śmieć. Harness ma na to asercję round-tripu każdego z 23 kluczy.
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
- **Zapis zastępuje treść w CAŁOŚCI** (komplet zwrotek), dokładnie jak przycisk ZAPISZ w edytorze okna:
  `DatabaseService.SaveSongAsync` kasuje stare zwrotki i wstawia nowe z pozycjami 0..n-1.
  Wyjątek: brak pola `author` w `song_update` zostawia dotychczasowego autora (telefon, który tego
  pola nie pokazuje, nie ma prawa go wyczyścić po cichu).
- Walidacja jest **uprzednia i atomowa** — jedna zła zwrotka i nie zapisujemy NICZEGO. Pieśń w pół
  drogi (część zwrotek nowych, część starych) wygląda na projektorze jak awaria w środku mszy.
- `reason` przy `ok:false`: `not_found` (pieśń albo `categoryId` > 0 bez pokrycia w bazie) ·
  `empty_title` · `unsupported_type` (+ `verseType`) · `invalid_play_order` · `in_setlists` (+ `setlists`).
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

### `UpdatedAt` — kto podbija, a kto NIE (kluczowe dla wykrywania konfliktów)

Cała detekcja konfliktów opiera się na tym znaczniku (Pilot porównuje `desktop.updatedAt != lastSyncedUpdatedAt`), więc reguła jest sztywna:

| metoda | podbija `UpdatedAt`? | dlaczego |
|---|---|---|
| `SaveSetlistAsync` | **TAK**, zawsze | zapis zestawu = zmiana treści |
| `SaveSetlistItemsAsync` | **TAK** (zestaw nadrzędny, w tej samej transakcji) | dodanie/usunięcie/przeniesienie pieśni |
| `CreateOrUpdateSetlistFromPilotAsync` | **NIE** — zapisuje znacznik **przysłany przez Pilota** | ta sama wartość wraca w `setlist_sync_ack` i staje się nową bazą `baseUpdatedAt`; własny czas desktopu = fałszywy konflikt przy każdej synchronizacji |
| `SetSetlistPinnedAsync` | **NIE** | przypięcie to flaga UI, nie zmiana treści; inaczej kliknięcie pinezki generowałoby konflikt. **Dotyczy tak samo komendy `setlist_pin` z Pilota (v1.63)** — jedyna droga zapisu tej flagi to ta metoda, nigdy `SaveSetlistAsync` |
| `SaveSetlistItemNotesAsync` | **NIE** | Pilot nie przenosi notatek; przy pełnym „ZAPISZ ZESTAW" i tak idzie `SaveSetlistItemsAsync` |
| `set_display_settings` (`PilotDisplaySettings`, v1.63) | **NIE** | wygląd projekcji to ustawienia aplikacji (tabela `settings`), nie treść zestawu — podbicie znacznika dałoby fałszywy konflikt na wszystkich zestawach naraz |
| komendy edytora pieśni (`PilotSongEdit`, v1.63) | **NIE** — żadna | pieśń nie należy do zestawu; podbicie znacznika po poprawieniu literówki dałoby fałszywy konflikt na wszystkich zestawach, które tę pieśń zawierają. `song_delete {force:true}` kasuje pozycje zestawów przez `DeleteSongAsync` (parytet z oknem) i też NIE dotyka `UpdatedAt` |
| komendy kategorii i grup (`PilotCategorySync`, v1.63) | **NIE** — żadna | kategorie nie należą do zestawu, a operacje na grupach ruszają wyłącznie ustawienie `setlist_groups`; zestawy nie są dotykane nawet przy `setlist_group_rename`/`delete` (parytet z UI), więc podbicie znacznika oznaczałoby fałszywy konflikt na wszystkich zestawach naraz |

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
