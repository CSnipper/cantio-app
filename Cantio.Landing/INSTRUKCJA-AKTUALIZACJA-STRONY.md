# Instrukcja aktualizacji strony Cantio (index.html)

## Kontekst

Zaktualizuj `Cantio.Landing/index.html` tak, żeby odzwierciedlała nową wersję aplikacji.
Strona już istnieje — nie twórz od zera, tylko edytuj. Zachowaj całą warstwę CSS, animacje, typografię, kolory i ogólną strukturę. Zmieniasz wyłącznie treść sekcji showcaseów, features grid, skrótów i powiązane screenshots.

**Plik do edycji:** `Cantio.Landing/index.html`
**Pliki graficzne:** wszystkie screenshoty idą do katalogu `Cantio.Landing/` (obok index.html).
Użyj tagów `<img src="NAZWA.png" onerror="...">` z istniejącym wzorcem img-placeholder.

---

## Co się zmieniło w aplikacji (względem poprzedniej wersji strony)

### Stara wersja miała zakładki:
Wyświetlanie | Edycja | Zestawy | Ustawienia | Import

### Nowa wersja ma zakładki:
Pokaż (Wyświetlanie) | Ustawienia | Import

Zestawy i edycja NIE są już osobnymi zakładkami. Zostały zintegrowane z zakładką Pokaż.

### Kluczowe nowe/zmienione funkcje:

1. **Szybka edycja (inline editor)** — panel po prawej stronie zakładki Pokaż. Kliknięcie ikony ołówka przy pieśni w zestawie otwiera edytor w miejscu podglądu projektora. Użytkownik może zmienić tekst dowolnej zwrotki bez opuszczania widoku sterowania. Zwrotki mają przyciski ↑↓ do zmiany kolejności. Zapis przez Ctrl+S lub przycisk Zapisz.

2. **Popup zestawów** — zamiast osobnej zakładki Zestawy, zestawy otwierają się w popup okienku wywoływanym przyciskiem „ZESTAWY" lub skrótem klawiaturowym (domyślnie Ctrl+Shift+O). W popup: lista zestawów z grupami, wyszukiwarka, podgląd zawartości, przycisk ładowania. Popup można zamknąć Escape.

3. **Edytor pieśni** — wciąż w osobnym panelu (ikonka edycji lub lista pieśni na zakładce Pokaż). Nowe funkcje: **drag & drop kolejności zwrotek** (uchwyty z lewej strony), **etykiety zwrotek** 1 / 2 / R (refren) / B (bridge) zamiast numeru od 0. Oznaczenia są widoczne zarówno w edytorze jak i w liście slajdów zakładki Pokaż.

4. **Etykiety zwrotek na slajdach** — lista slajdów w zakładce Pokaż pokazuje etykiety: `1`, `2`, `R`, `B` zamiast bezsensownych liczb zaczynających się od 0.

5. **Wklejanie całego tekstu** — w edytorze przycisk „Wklej tekst" dzieli wklejony tekst na zwrotki po pustych liniach.

---

## Zmiany sekcja po sekcji

### 1. NAV (header)

Zmień linki nawigacji:
```html
<a href="#features">Funkcje</a>
<a href="#display">Wyświetlanie</a>
<a href="#quick-edit">Szybka edycja</a>
<a href="#install">Instalacja</a>
<a href="https://github.com/CSnipper/cantio-app">GitHub</a>
```

### 2. HERO

Bez zmian strukturalnych. Zaktualizuj główny screenshot:
- Plik: `screenshot-main.png`
- Alt: `Cantio — widok główny zakładka Pokaż`
- Opis pod zdjęciem (hero-sub): bez zmian — obecny opis jest ok.

### 3. FEATURES GRID (6 kart)

Zastąp całe `<div class="features-grid">` nową treścią. Zachowaj 6 kart, ten sam HTML, zmień ikony i treść:

```
Karta 1 — ikona 🖥️
Tytuł: Osobny ekran projekcji
Opis: Tekst pieśni wyświetla się na projektorze lub drugim monitorze w dużej, czytelnej czcionce. Sterowanie odbywa się na komputerze organisty — bez ingerencji w obraz dla wiernych.

Karta 2 — ikona ✏️
Tytuł: Szybka edycja w miejscu
Opis: Zmień tekst dowolnej zwrotki bez opuszczania widoku sterowania. Panel szybkiej edycji otwiera się w miejscu podglądu — zapisujesz Ctrl+S i jesteś gotowy.

Karta 3 — ikona ⌨️
Tytuł: Skróty klawiaturowe
Opis: Strzałki do zmiany slajdów i pieśni, Escape do zaciemnienia ekranu. Wszystkie skróty można skonfigurować pod pilota, klawiaturę lub ekran dotykowy.

Karta 4 — ikona 📋
Tytuł: Zestawy nabożeństw
Opis: Przygotuj kolejność pieśni przed nabożeństwem i załaduj jednym kliknięciem z popupu zestawów. Zapisuj, grupuj i przypinaj ulubione zestawy.

Karta 5 — ikona 🎨
Tytuł: Wygląd do konfiguracji
Opis: Czcionka, kolory, tło, gradient, własna grafika, cień tekstu, wyrównanie. Projektor wygląda tak, jak chcesz — i zapamiętuje ustawienia.

Karta 6 — ikona 📥
Tytuł: Import z OpenLP i OpenSong
Opis: Zaimportuj całą bazę pieśni z OpenLP (SQLite, XML) lub OpenSong. Przeniesienie danych zajmuje kilka sekund — łącznie z zestawami .osz.
```

### 4. SHOWCASE 1 — Zakładka Pokaż (zastępuje stary "Wyświetlanie")

Id sekcji: `id="display"`

```
Label: Zakładka Pokaż
Nagłówek: Sterowanie podczas nabożeństwa

Akapit 1:
Trzy kolumny: lista kategorii i pieśni po lewej, zwrotki w środku (z etykietami 1 / 2 / R / B), podgląd projektora i zestaw po prawej. Kliknij zwrotkę — pojawia się na projektorze.

Akapit 2:
Długie zwrotki dzielone są automatycznie na kilka slajdów z zachowaniem czytelności. Przycisk WYGAŚ zaciemnia projektor bez utraty pozycji w pieśni.

Screenshot: screenshot-display.png
```

### 5. SHOWCASE 2 — Szybka edycja (NOWA sekcja — dodaj po showcase 1)

Wstaw nową sekcję z `class="showcase reverse"`, id: `id="quick-edit"`:

```
Label: Szybka edycja
Nagłówek: Edytuj w miejscu, bez przerywania

Akapit 1:
Kliknij ikonę ołówka przy pieśni w zestawie — panel szybkiej edycji otwiera się w miejscu podglądu. Zmieniasz tekst każdej zwrotki, zmieniasz kolejność, zapisujesz Ctrl+S.

Akapit 2:
Nie tracisz pozycji w zestawie, nie zmieniasz widoku. Poprawka literówki lub wklejenie brakującej zwrotki zajmuje kilka sekund.

Screenshot: screenshot-quick-edit.png
Strona obrazka: lewa (showcase reverse)
```

### 6. SHOWCASE 3 — Edytor pieśni (zastępuje stary "Edycja")

```
Label: Edytor pieśni
Nagłówek: Baza pieśni pod kontrolą

Akapit 1:
Dodawaj i edytuj pieśni bezpośrednio w programie. Przypisz do kategorii (Adwent, Wielkanoc, Różaniec…), nadaj numer ze śpiewnika, ustaw kolejność zwrotek przeciągając je myszą.

Akapit 2:
Zwrotki mają czytelne etykiety: 1, 2, R (refren), B (bridge). Wklej cały tekst pieśni naraz — program sam podzieli go na zwrotki po pustych liniach.

Screenshot: screenshot-editor.png
```

### 7. SHOWCASE 4 — Popup zestawów (zastępuje stary "Zestawy")

```
Label: Zestawy
Nagłówek: Plan nabożeństwa gotowy z wyprzedzeniem

Akapit 1:
Ułóż kolejność pieśni przed nabożeństwem, zapisz zestaw z nazwą i otwórz jednym kliknięciem z popupu. Zestawy można grupować, przypinać i przeszukiwać.

Akapit 2:
Popup otwiera się skrótem klawiszowym lub przyciskiem w zakładce Pokaż — bez zmiany widoku sterowania. Obsługa importu zestawów z OpenLP (.osz).

Screenshot: screenshot-setlist-popup.png
```

### 8. SEKCJA USTAWIEŃ z zakładkami

Zachowaj istniejący wygląd (tab switcher). Zmień podzakładki i opisy:

```
Zakładka 1: "Ekran"
data-img="screenshot-settings-screen.png"
data-desc="Wybór ekranu projekcji i języka interfejsu. Cantio obsługuje polskie, angielskie i ukraińskie menu."

Zakładka 2: "Czcionka"
data-img="screenshot-settings-font.png"
data-desc="Czcionka, rozmiar podstawowy, automatyczne dopasowanie (auto-fit), interlinię i wyrównanie tekstu na projektorze."

Zakładka 3: "Kolory"
data-img="screenshot-settings-colors.png"
data-desc="Kolor tła, gradient liniowy lub radialny, własna grafika w tle z regulacją krycia i cień tekstu."

Zakładka 4: "Format"
data-img="screenshot-settings-format.png"
data-desc="Własne tagi formatowania tekstu (np. {verse}…{/verse}) z przypisanym kolorem i skrótem klawiaturowym (Ctrl+klawisz)."

Zakładka 5: "Skróty"
data-img="screenshot-settings-shortcuts.png"
data-desc="Konfigurowalne skróty klawiaturowe — dostosuj pod klawiaturę, pilota prezentacyjnego lub ekran dotykowy."
```

Zaktualizuj też domyślnie wyświetlany screenshot (pierwszy aktywny tab): `src="screenshot-settings-screen.png"`.

### 9. SEKCJA SKRÓTÓW KLAWIATUROWYCH

Usuń wiersz „Następny slajd (zawsze) — Spacja" (Spacja jest teraz jako wbudowany, nie konfigurowalny skrót).
Dodaj:

```
"Popup zestawów"  →  Ctrl+Shift+O  (lub skonfigurowany)
"Szukaj pieśni"   →  Ctrl+F
"Otwórz szybką edycję"  →  (brak domyślnego — klikasz ołówek)
```

Zaktualizowana tabela 8 wierszy (2 kolumny = 4 wiersze na kolumnę):

```
Następny slajd        →   → (strzałka)
Poprzedni slajd       →   ← (strzałka)
Następna pieśń        →   ↓
Poprzednia pieśń      →   ↑
Wygaś / Pokaż ekran  →   Esc
Szukaj pieśni         →   Ctrl+F
Popup zestawów        →   Ctrl+Shift+O
Zapisz                →   Ctrl+S
```

### 10. IMPORT (showcase)

Bez zmian treści. Jedynie zaktualizuj screenshot:
- Plik: `screenshot-import.png`

### 11. CTA (dolna sekcja pobierania)

Zaktualizuj listę `download-info` — usuń "3 języki interfejsu", zastąp dokładną informacją:

```html
<span class="dl-meta">Windows 10 / 11 (64-bit)</span>
<span class="dl-meta">Bezpłatny · open-source</span>
<span class="dl-meta">Polski · English · Українська</span>
<span class="dl-meta">Instalator ~50 MB</span>
```

### 12. FOOTER

Zaktualizuj wersję: `v1.0.0` → `v1.1.0` (lub aktualną z Cantio.csproj).

---

## Lista plików screenshot do dostarczenia przez użytkownika

Użytkownik musi dostarczyć screenshoty i wrzucić je do `Cantio.Landing/`. Poniżej lista z opisem co powinno być na każdym:

| Plik | Co powinno być widoczne |
|---|---|
| `screenshot-main.png` | Pełne okno Cantio, zakładka Pokaż, pieśń wyświetlana, lista zwrotek z etykietami 1/2/R/B |
| `screenshot-display.png` | Zbliżenie na lewą i środkową kolumnę zakładki Pokaż: lista kategorii/pieśni + lista slajdów z etykietami |
| `screenshot-quick-edit.png` | Panel szybkiej edycji otwarty: lista zwrotek z polami tekstowymi, przyciski ↑↓, nagłówek z tytułem pieśni |
| `screenshot-editor.png` | Pełny edytor pieśni: zwrotki z etykietami 1/R/B, uchwyt drag&drop, pole nazwy i kategorii |
| `screenshot-setlist-popup.png` | Popup zestawów: lista zestawów z grupami, wyszukiwarka, podgląd pieśni w zestawie |
| `screenshot-settings-screen.png` | Ustawienia → podzakładka ekranu/języka |
| `screenshot-settings-font.png` | Ustawienia → czcionka, rozmiar, auto-fit suwak |
| `screenshot-settings-colors.png` | Ustawienia → kolory tła, gradient, krycie grafiki |
| `screenshot-settings-format.png` | Ustawienia → lista tagów formatowania |
| `screenshot-settings-shortcuts.png` | Ustawienia → lista skrótów klawiaturowych |
| `screenshot-import.png` | Zakładka Import: pola wyboru pliku, log importu |

Screenshoty powinny być w rozdzielczości min. 1200×750 px, PNG. Mogą być robione na 1920×1080 i skalowane. Strona używa `width: 100%` na `<img>`, więc proporcje mają znaczenie (zalecane 16:9 lub 4:3).

---

## Czego NIE zmieniać

- Całego CSS (`:root`, `.btn`, `.screenshot-frame`, `.features-grid`, `.showcase`, `.shortcuts-grid`, `.install-steps`, animacje, responsive) — zachowaj bez zmian.
- Sekcji instalacji (kroki 01–04) — treść jest nadal aktualna.
- Linków GitHub w nav, header, footer — nie zmieniaj adresów URL.
- Struktury HTML poza wskazanymi sekcjami.

---

## Kolejność pracy

1. Zaktualizuj nav (dodaj link `#quick-edit`).
2. Podmień features grid (wszystkie 6 kart).
3. Showcase 1 (Pokaż) — zaktualizuj tekst i screenshot.
4. Wstaw nowy Showcase 2 (Szybka edycja) — **nowa sekcja z `<hr class="divider" />`** przed i po niej.
5. Showcase 3 (Edytor) — zaktualizuj tekst i screenshot.
6. Showcase 4 (Popup zestawów) — zaktualizuj tekst i screenshot.
7. Tab switcher ustawień — zmień data-img i data-desc wszystkich zakładek.
8. Shortcuts grid — podmień 8 wierszy.
9. Import showcase — tylko screenshot.
10. CTA download-info — zaktualizuj tekst.
11. Footer — zaktualizuj wersję.

Po edycji otwórz `index.html` w przeglądarce i sprawdź że:
- Nie ma broken layout (wszystkie placeholdery obrazków się pokazują).
- Tab switcher ustawień działa (JS nie jest zmieniony, więc powinien).
- Linki w nav prowadzą do właściwych sekcji (sprawdź `id=` na sekcjach).
