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
- Zestawy pieśni na nabożeństwo z grupowaniem
- Edytor pieśni z kategoriami i typami zwrotek (zwrotka, refren, bridge)
- Szablon projekcji — czcionka, kolory, tło, gradient, cień, marginesy
- Tagi formatowania tekstu z własną definicją i skrótami klawiaturowymi
- Import z OpenLP, OpenSong, OSZ
- Wielojęzyczny interfejs: Polski / English / Español
- Obsługa wielu monitorów

### Wymagania

- Windows 10 / 11 (64-bit)
- Drugi ekran lub projektor (zalecane)

### Instalacja

Pobierz najnowszy instalator z sekcji [Releases](../../releases) i uruchom.
Podczas instalacji możesz wybrać czy chcesz zainstalować przykładową bazę pieśni, czy zacząć z pustą bazą.

### Obsługa klawiatury (tryb wyświetlania)

| Klawisz | Akcja |
|---|---|
| `→` / `Spacja` / `Page Down` | Następny slajd |
| `←` / `Page Up` | Poprzedni slajd |
| `Ctrl + →` | Następna pieśń |
| `Ctrl + ←` | Poprzednia pieśń |
| `↑` / `↓` | Poprzednia / następna pieśń |
| `Home` | Pierwszy slajd |
| `Esc` | Wygaś / Pokaż ekran |

### Budowanie ze źródeł

```bash
# Wymagania: .NET 10 SDK
git clone https://github.com/CSnipper/cantio-app.git
cd cantio-app
dotnet run --project Cantio
```

Budowanie instalatora:
```bash
dotnet publish Cantio/Cantio.csproj /p:PublishProfile=win-x64
# Następnie skompiluj Installer/cantio.iss w Inno Setup 6
```

---

## 🇬🇧 English

### About

Cantio is a native Windows application for displaying song lyrics on a projector screen during church services. Designed for touch-friendly operation and ease of use during worship.

### Features

- Song text display on a secondary screen / projector
- Automatic splitting of long verses into slides
- Automatic font size fitting to screen dimensions
- Song sets for services with group management
- Song editor with categories and verse types (verse, chorus, bridge)
- Projection template — font, colors, background, gradient, shadow, margins
- Custom format tags with keyboard shortcuts
- Import from OpenLP, OpenSong, OSZ
- Multilingual UI: Polski / English / Español
- Multi-monitor support

### Requirements

- Windows 10 / 11 (64-bit)
- Secondary screen or projector (recommended)

### Installation

Download the latest installer from the [Releases](../../releases) section and run it.
During installation you can choose between a sample song database or an empty one.

### Keyboard shortcuts (display mode)

| Key | Action |
|---|---|
| `→` / `Space` / `Page Down` | Next slide |
| `←` / `Page Up` | Previous slide |
| `Ctrl + →` | Next song |
| `Ctrl + ←` | Previous song |
| `↑` / `↓` | Previous / next song |
| `Home` | First slide |
| `Esc` | Blank / Show screen |

### Building from source

```bash
# Requires: .NET 10 SDK
git clone https://github.com/CSnipper/cantio-app.git
cd cantio-app
dotnet run --project Cantio
```

Building the installer:
```bash
dotnet publish Cantio/Cantio.csproj /p:PublishProfile=win-x64
# Then compile Installer/cantio.iss with Inno Setup 6
```

---

## Stack

- C# 12 / .NET 10 / WPF
- CommunityToolkit.Mvvm
- Entity Framework Core + SQLite
- WpfScreenHelper

## License

MIT
