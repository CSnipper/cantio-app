<img width="1916" height="1128" alt="image" src="https://github.com/user-attachments/assets/ba8ed303-b830-4b4c-8ca2-6875da646033" />


# Cantio

> Aplikacja do wyświetlania pieśni liturgicznych na projektorze podczas nabożeństw.
> A song display application for projecting liturgical music during church services.

---

## 🇵🇱 Polski

### O programie

Cantio to natywna aplikacja Windows do wyświetlania tekstu pieśni na ekranie projektora w kościele. Zaprojektowana z myślą o obsłudze dotykowej i wygodzie podczas nabożeństw.

### Funkcje

- Wyświetlanie tekstu pieśni na drugim ekranie / projektorze
- Automatyczny podział długich zwrotek na slajdy
- Automatyczne dopasowanie rozmiaru czcionki do ekranu
- Zestawy pieśni na nabożeństwo z grupowaniem i przeciąganiem
- Edytor pieśni z kategoriami i typami zwrotek (zwrotka, refren, bridge)
- Szablon projekcji — czcionka, kolory, tło, gradient, cień, marginesy
- Tagi formatowania tekstu z własną definicją i skrótami klawiaturowymi
- Import z OpenLP, OpenSong, OSZ
- Wielojęzyczny interfejs: Polski / English / Español
- Obsługa wielu monitorów
- Konfigurowalny zestaw skrótów klawiaturowych

### Wymagania

- Windows 10 / 11 (64-bit)
- Drugi ekran lub projektor (zalecane)

### Instalacja

Pobierz najnowszy instalator z sekcji [Releases](../../releases) i uruchom.
Podczas instalacji możesz wybrać czy chcesz zainstalować przykładową bazę pieśni, czy zacząć z pustą bazą.

### Układ interfejsu

Okno główne podzielone jest na trzy kolumny:

| Kolumna | Zawartość |
|---|---|
| **Lewa** | Lista slajdów bieżącej pieśni + kontrolki (LIVE, Pokaż/Wygaś, ◀ ▶) |
| **Środkowa** | Kategorie i pieśni (góra) + miniatura podglądu (dół) |
| **Prawa** | Magazynek — zestaw pieśni na nabożeństwo |

### Wczytywanie pieśni

- **Pojedyncze kliknięcie** — zaznacza pieśń / pozycję w zestawie (bez wpływu na projekcję)
- **Dwukrotne kliknięcie** lub **ikona 👁** — wczytuje pieśń i pokazuje ją w podglądzie
- Po wczytaniu miniatura podglądu natychmiast pokazuje treść slajdu
- **Przycisk „Pokaż"** — wysyła bieżący slajd na projektor i włącza tryb LIVE

### Obsługa klawiatury

Skróty nawigacyjne są **konfigurowalne** w zakładce Ustawienia → Skróty.

Wartości domyślne:

| Klawisz | Akcja |
|---|---|
| `→` / `Spacja` | Następny slajd |
| `←` | Poprzedni slajd |
| `↑` / `↓` | Poprzednia / następna pieśń w zestawie |
| `Home` | Pierwszy slajd |
| `Esc` | Wygaś / Pokaż ekran (toggle LIVE) |
| `Ctrl+F` | Skocz do wyszukiwarki pieśni |

Pozostałe konfigurowalne skróty: otwieranie wyszukiwarki zestawów, przełączanie zakładek.

### Zestaw pieśni (magazynek)

- Dodaj pieśń przyciskiem `+` na liście pieśni
- Zmień kolejność przeciągając pozycje w zestawie
- Edytuj tytuł inline przyciskiem ✏
- Usuń pozycję przyciskiem ×
- Zapisz zestaw (`Ctrl+S`) — możliwość nadpisania istniejącego lub zapisu jako nowy
- Zestawy grupuj w kolekcje (np. „Niedziela", „Środa")
- Przypnij często używane zestawy do panelu szybkiego dostępu

### Edytor pieśni

- Nowa pieśń: przycisk `+ NOWA PIEŚŃ` w nagłówku listy
- Edycja: ikona ✎ na liście lub bezpośrednio z zestawu (ikona ✏)
- Typy zwrotek: zwrotka (1, 2, 3…), refren (R), bridge (B)
- Zmiana kolejności zwrotek przeciąganiem
- Własna kolejność wykonania (drag & drop w trybie edycji zestawu)
- `Ctrl+S` — zapisz

### Budowanie ze źródeł

```bash
# Wymagania: .NET 10 SDK
git clone https://github.com/CSnipper/cantio-app.git
cd cantio-app
dotnet run --project Cantio
```

Budowanie instalatora (wymaga Inno Setup 6):
```bash
cd Installer
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

---

## 🇬🇧 English

### About

Cantio is a native Windows application for displaying song lyrics on a projector screen during church services. Designed for touch-friendly operation and ease of use during worship.

### Features

- Song text display on a secondary screen / projector
- Automatic splitting of long verses into slides
- Automatic font size fitting to screen dimensions
- Song sets for services with group management and drag & drop reordering
- Song editor with categories and verse types (verse, chorus, bridge)
- Projection template — font, colors, background, gradient, shadow, margins
- Custom format tags with keyboard shortcuts
- Import from OpenLP, OpenSong, OSZ
- Multilingual UI: Polski / English / Español
- Multi-monitor support
- Fully configurable keyboard shortcuts

### Requirements

- Windows 10 / 11 (64-bit)
- Secondary screen or projector (recommended)

### Installation

Download the latest installer from the [Releases](../../releases) section and run it.
During installation you can choose between a sample song database or an empty one.

### Interface layout

The main window is divided into three columns:

| Column | Content |
|---|---|
| **Left** | Slide list for the current song + controls (LIVE, Show/Blank, ◀ ▶) |
| **Center** | Categories and songs (top) + slide preview thumbnail (bottom) |
| **Right** | Set list — songs queued for the service |

### Loading songs

- **Single click** — selects a song / set item (no effect on projection)
- **Double-click** or **👁 icon** — loads the song and shows it in the preview
- After loading, the preview thumbnail immediately shows the slide content
- **"Show" button** — sends the current slide to the projector and activates LIVE mode

### Keyboard shortcuts

Navigation shortcuts are **configurable** in Settings → Shortcuts.

Default values:

| Key | Action |
|---|---|
| `→` / `Space` | Next slide |
| `←` | Previous slide |
| `↑` / `↓` | Previous / next song in set |
| `Home` | First slide |
| `Esc` | Blank / Show screen (toggle LIVE) |
| `Ctrl+F` | Focus song search box |

Other configurable shortcuts: open set search, switch tabs.

### Song set

- Add a song using the `+` button on the song list
- Reorder by dragging items in the set list
- Edit title inline with the ✏ button
- Remove with the × button
- Save the set (`Ctrl+S`) — overwrite existing or save as new
- Organise sets into groups (e.g. "Sunday", "Wednesday")
- Pin frequently used sets to the quick-access panel

### Song editor

- New song: `+ NEW SONG` button in the list header
- Edit: ✎ icon on the list or ✏ directly from the set
- Verse types: verse (1, 2, 3…), chorus (R), bridge (B)
- Reorder verses by dragging
- Custom playback order (drag & drop in set edit mode)
- `Ctrl+S` — save

### Building from source

```bash
# Requires: .NET 10 SDK
git clone https://github.com/CSnipper/cantio-app.git
cd cantio-app
dotnet run --project Cantio
```

Building the installer (requires Inno Setup 6):
```bash
cd Installer
powershell -ExecutionPolicy Bypass -File build-installer.ps1
```

---

## Stack

- C# 12 / .NET 10 / WPF
- CommunityToolkit.Mvvm
- Entity Framework Core + SQLite
- WpfScreenHelper

## License

MIT
