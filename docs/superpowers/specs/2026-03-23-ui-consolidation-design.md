# UI Consolidation — Design Spec

## Cel

Usunięcie zakładek Edycja (PaneSongs) i Zestawy (PaneSets). Wszystkie funkcje przeniesione do zakładki Wyświetlanie. Uproszczenie nawigacji do 3 zakładek: Wyświetlanie · Ustawienia · Import.

---

## Zmiany w zakładce Wyświetlanie

### Layout (3 kolumny globalne)

```
┌──────────┬──────────────────────────────────────┬───────────┐
│ ZWROTKI  │ KATEGORIE / PIEŚNI                   │  ZESTAW   │
│          │                                      │           │
│[slajdy]  │ [kat1][kat2][+ kat]   [NOWA PIEŚŃ]   │[pieśń 1]  │
│          │ [szukaj...]                           │[pieśń 2]  │
│          │ [pieśń 1  ✎]                          │[pieśń 3]  │
│          │ [pieśń 2  ✎]                          │           │
│          ├────────────────────────┬──────────────┤[ZAPISZ]   │
│          │     PODGLĄD 16:9       │  PRZYPIĘTE   │[WYCZYŚĆ]  │
│          │                        │  [Zestaw A]  │           │
│          │                        │  [Zestaw B]  │           │
├──────────┤                        │              │           │
│ ● LIVE   │                        │              │           │
│[POK][◀▶] │                        │              │           │
└──────────┴────────────────────────┴──────────────┴───────────┘
```

**Uwaga:** PRZYPIĘTE to podkolumna wewnątrz środkowej kolumny (Grid.Column w Grid), nie osobna kolumna globalna. Środkowa kolumna na dole dzieli się wewnętrznie: `[Podgląd 16:9 | Przypięte]`.

- Kontrolki (LIVE/POKAŻ/◀▶) pod kolumną ZWROTKI — już zaimplementowane
- Przypięte zestawy przenoszone z dołu kolumny ZESTAW do wewnętrznej podkolumny przy podglądzie
- Kolumna ZESTAW: tylko pozycje zestawu + ZAPISZ/WYCZYŚĆ

### Kategorie — zarządzanie inline

- Kategorie jako klikalne tagi/przyciski w poziomie (jak teraz)
- `+ KAT` — dodaje nową przez inline input (tak jak w obecnym PaneSongs)
- Edycja / usunięcie kategorii: ikona `✎` widoczna przy hover na tagu kategorii — otwiera inline rename. Usunięcie przez przycisk `✕` w trybie rename. **Nie używamy long-press** (brak natywnego wsparcia WPF) — tylko hover + ikona.
- Przeniesione z PaneSongs bez zmian funkcjonalnych

### Przycisk "Nowa pieśń"

- W nagłówku sekcji PIEŚNI
- Kliknięcie → przełącza środkową kolumnę w tryb edycji pieśni (nowy, pusty formularz)

### Edycja istniejącej pieśni

- Przy każdej pieśni na liście: ikona `✎` widoczna przy hover
- Kliknięcie `✎` → tryb edycji pieśni z wypełnionym formularzem

---

## Tryb edycji pieśni (Song Edit Mode)

Środkowa kolumna (`Col 1`) w całości przełącza się — categories/songs/preview/pinned są ukryte. `IsEditMode` (bool) w `DisplayViewModel` kontroluje widoczność przez Visibility.

### Layout środkowej kolumny w trybie edycji

```
┌─────────────────────────────────────────┐
│ [← Wróć]                                │
├─────────────────────────────────────────┤
│ TYTUŁ  [________________________________]│
│ NR [___]  KATEGORIA [__________▼]       │
├─────────────────────────────────────────┤
│ ZWROTKI                                 │
│                                         │
│  Zwrotka 1              [↑][↓][✕]       │
│  [tekst zwrotki 1....]                  │
│                                         │
│  Refren                 [↑][↓][✕]       │
│  [tekst refrenu....]                    │
│                                         │
│  [+ Zwrotka][+ Refren][+ Bridge]        │
│  [✎ Edytuj całość]                      │
├─────────────────────────────────────────┤
│ [ZAPISZ PIEŚŃ]              [USUŃ]      │
└─────────────────────────────────────────┘
```

### Zachowanie

- `← Wróć` gdy `IsDirty = false` → powrót bez pytania
- `← Wróć` gdy `IsDirty = true` → dialog potwierdzenia ("Masz niezapisane zmiany. Wyjść?")
- Lista zwrotek z etykietami: `1`, `2`... dla verse; `R`, `R2`... dla chorus; `B` dla bridge
- `↑` / `↓` przy każdej zwrotce — zmiana kolejności w kolekcji `EditingVerses`, aktualizacja `Position`
- `✕` — usuwa zwrotkę (po potwierdzeniu jeśli ma treść)
- `+ Zwrotka/Refren/Bridge` — nowa zwrotka danego typu na końcu listy z pustym TextBox
- `✎ Edytuj całość` — otwiera overlay z polami tekstowymi (rozszerzony o ↑↓, patrz niżej)
- `ZAPISZ PIEŚŃ` — zapisuje Song + Verses do DB, wraca do widoku normalnego, odświeża listę pieśni
- `USUŃ` — dialog potwierdzenia, usuwa pieśń z DB, powrót do widoku normalnego

### Modele pomocnicze

`VerseEditorItem` i `CategoryEditorItem` (aktualnie wewnątrz `SongEditorViewModel.cs`) przenoszone do osobnych plików lub do `DisplayViewModel.cs` przy migracji logiki.

---

## Ulepszenia Inline Editora (kontekst magazynku)

Istniejący overlay `IsInlineEditorOpen` (otwierany z pozycji ZESTAW) otrzymuje:

### Zmiana kolejności zwrotek

- Obok każdego pola tekstowego: przyciski `↑` i `↓`
- Kliknięcie zmienia kolejność w `EditableVerses` (ObservableCollection swap)
- Przy zapisie: nowa kolejność utrwalana przez nową metodę `DatabaseService.SaveVerseOrderAsync(IEnumerable<(int id, int position)>)`

### Odświeżenie slajdów po zapisie

- `SaveInlineEditAsync` → zapis tekstu i kolejności → `RebuildSlides()`
- Jeśli `SelectedSetlistItem.Song` nie jest null → załadować pieśń ponownie z DB i ustawić na `SetlistItem.Song` żeby odzwierciedlić zmiany

---

## Auto-detekcja refrenu w "Edytuj całość"

### Punkt wejścia

Auto-detekcja wywoływana w metodzie parsującej tekst na zwrotki (`ParseRawText` lub odpowiednik), **nie tylko przy finalnym zapisie** — dzięki temu lista zwrotek jest poprawna od razu po kliknięciu Zapisz.

### Algorytm

1. Podziel tekst po podwójnym `\n\n`
2. Dla każdego bloku: jeśli zaczyna się od `Refren:` (case-insensitive, trim) → typ `"c"`, tekst = reszta bloku po `Refren:`
3. Jeśli wiele bloków `Refren:` → każdy traktowany jako osobna zwrotka typu `"c"`. Do PlayOrderJson wstawiany jest tylko **pierwszy wykryty refren** (indeks `chorusIndex`) po każdej zwrotce (verse).
4. Budowanie `PlayOrderJson` (lista indeksów do tablicy `Verses`):
   - Indeks `chorusIndex` = pozycja pierwszego refrenu w liście zwrotek
   - Jeśli refren jest na pozycji 0 (pierwszy blok): `[R, Z1, R, Z2, R, ...]`
   - Jeśli refren jest na pozycji > 0: `[Z1, R, Z2, R, ...]` (od zwrotki poprzedzającej refren)
5. Zapisz `PlayOrderJson` razem ze zwrotkami

### Przykłady

```
Wejście:                              →  Kolejność wykonania:
──────────────────────────────────────────────────────────────
Refren:\nAlleluja...                     R → Z1 → R → Z2 → R
Zwrotka 1\nPan jest...
Zwrotka 2\nJego chwała...

Zwrotka 1\nPan jest...                   Z1 → R → Z2 → R
Refren:\nAlleluja...
Zwrotka 2\nJego chwała...
```

---

## Bugfix: nazwa grupy nie wyświetla się w combo (Display)

`SetlistGroup` (string) w `DisplayViewModel` jest bindowany do ComboBoxa z `ItemsSource=SetlistGroups` (ObservableCollection\<string\>). Typowa przyczyna w WPF: użycie `SelectedValue` + `SelectedValuePath` na kolekcji stringów zamiast `SelectedItem`. Fix: upewnić się że ComboBox używa `SelectedItem="{Binding SetlistGroup}"` bez `SelectedValuePath`.

---

## Usunięcie zakładek

### PaneSets (Zestawy)

- Sekcja `<Grid x:Name="PaneSets">` usuwana z MainWindow.xaml
- Przycisk `TabSets` usuwany
- `SetlistViewModel` — usuwany. Logika pinowania (`PinnedChanged` event, `LoadForDisplayRequested`) przeniesiona bezpośrednio do `DisplayViewModel`. Grupy: `setlist_groups` z DB nadal ładowane przez `DisplayViewModel.LoadSetlistGroupsAsync()`.
- Wiring eventów w `MainWindow.xaml.cs` (`PinnedChanged`, `LoadForDisplayRequested`) — usuwany

### PaneSongs (Edycja)

- Sekcja `<Grid x:Name="PaneSongs">` usuwana z MainWindow.xaml
- Przycisk `TabSongs` usuwany
- `SongEditorViewModel` — usuwany. Logika przeniesiona do `DisplayViewModel`.

---

## Szczegóły implementacyjne

### IsDirty w trybie edycji pieśni

`DisplayViewModel` otrzymuje właściwość `IsEditDirty` (bool). Ustawiana na `true` gdy:
- zmiana `EditingTitle`, `EditingNumber`, `EditingCategory`
- zmiana tekstu dowolnej zwrotki w `EditingVerses`
- dodanie lub usunięcie zwrotki
- zmiana kolejności zwrotek (↑/↓)

Resetowana na `false` po zapisie lub powrocie bez zmian.

### Etykiety wielu refrenów

Zachowanie jak w istniejącym kodzie: `VerseEditorItem.Label` zwraca `"R"` dla pierwszego refrenu, `"R2"` dla drugiego itd. (jeśli istniejąca implementacja nie obsługuje liczenia — dodać). Bridge analogicznie: `"B"`, `"B2"`.

### SaveInlineEditAsync — zmiana ciała metody

Metoda musi:
1. Dla każdego `EditableVerse` zapisać tekst przez `SaveVerseTextAsync(ev.Id, ev.Text)` (istniejące)
2. Dodać: zebrać `(id, position)` z `EditableVerses` i wywołać `SaveVerseOrderAsync(...)` (nowa metoda)
3. Następnie `RebuildSlides()` (istniejące)

---

## Pliki do zmiany

| Plik | Zmiana |
|------|--------|
| `MainWindow.xaml` | Usunięcie PaneSets + PaneSongs; nowy layout środkowej kolumny (wewnętrzna podkolumna Przypięte przy podglądzie); tryb edycji pieśni w Col 1; usunięcie przycisków TabSets/TabSongs |
| `MainWindow.xaml.cs` | Usunięcie: `_setlistVm`, `_songEditorVm`, handlerów TabSets/TabSongs, drag-drop handlerów (`PlayOrderItem_MouseMove/Drop`, `SetlistEditorItem_MouseMove/Drop`), `SearchBoxSongs_PreviewKeyDown`, gałęzi `"sets"`/`"songs"` w `Delete`-key handlerze, gałęzi `_songEditorVm`/`_setlistVm` w `HandleSave()`, call sites `IsMatch(…, ShortcutService.TabSongs/TabSets)` w `OnPreviewKeyDown`. Dodanie: logika przełączania trybu edycji pieśni. |
| `ViewModels/DisplayViewModel.cs` | Dodanie: `IsEditMode`, `IsEditDirty`, `EditingVerses`, `EditingTitle`, `EditingNumber`, `EditingCategory`; komendy New/Save/Delete/AddVerse/RemoveVerse/MoveVerseUp/Down; kategorie inline (AddCategory, RenameCategory, DeleteCategory); auto-chorus w ParseRawText; przeniesienie logiki pinowania z SetlistViewModel |
| `ViewModels/SongEditorViewModel.cs` | **Usunięcie** — logika przeniesiona do DisplayViewModel; klasy pomocnicze `VerseEditorItem`, `CategoryEditorItem`, `EditableVerse` przenoszone do osobnych plików lub do DisplayViewModel |
| `ViewModels/SetlistViewModel.cs` | **Usunięcie** — logika zintegrowana w DisplayViewModel |
| `Services/DatabaseService.cs` | Dodanie `SaveVerseOrderAsync(IEnumerable<(int id, int position)>)`; weryfikacja CRUD dla Song/Verse/Category |
| `Services/ShortcutService.cs` | Usunięcie stałych `TabSongs`, `TabSets` z `AllActions` |
| `ViewModels/ShortcutsViewModel.cs` | Usunięcie `_tabSongs`, `_tabSets`; aktualizacja Load/Save/Reset |
| `Assets/Localization/Strings.pl.xaml` | Usunięcie `Tab.Songs`, `Tab.Sets`, `Shortcuts.TabSongs`, `Shortcuts.TabSets`; dodanie nowych kluczy |
| `Assets/Localization/Strings.en.xaml` | j.w. |
| `Assets/Localization/Strings.es.xaml` | j.w. |
