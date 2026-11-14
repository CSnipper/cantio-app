"""Strażnik motywów: te same klucze x:Key we wszystkich Theme.*.xaml i wartości w formacie #rrggbb;
oraz brak kolorów wpisanych wprost (#rrggbb / #aarrggbb) w XAML poza listą dozwolonych."""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Cantio"
DIR = ROOT / "Assets" / "Themes"
NAMES = ["Obsidian", "Monochrome", "Sage"]
ENTRY = re.compile(r'<SolidColorBrush\s+x:Key="([^"]+)"\s+Color="([^"]*)"')
HEX = re.compile(r"#[0-9a-fA-F]{6}")
LITERAL = re.compile(r"#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?(?![0-9a-fA-F])")

# Pliki pomijane w kontroli kolorów (nazwa pliku -> dlaczego).
EXCLUDED = {
    "ProjectionWindow.xaml": "ekran projekcji ma własne, stałe kolory (wygląd slajdu, nie motyw interfejsu)",
    "ProjectionView.xaml": "widok slajdu (też w podglądzie) pokazuje kolory slajdu, nie motyw",
    "ColorPickerWindow.xaml": "okno wyboru koloru ma stały ciemny wygląd, niezależny od motywu",
    "AccessibleWindow.xaml": "tryb wysokiego kontrastu ma własną, stałą paletę",
}

# Dozwolone literały: plik (względem Cantio/) -> {hex małymi literami: ile wystąpień}.
ALLOWED = {
    "MainWindow.xaml": {
        "#3d8b40": 1,  # kropka okresu liturgicznego (kolor liturgiczny = dane)
        "#4aad6f": 2,  # zielony: slajd prywatny w wierszu pieśni i znacznik zwrotki typu p
        "#7a9abf": 1,  # niebieski znacznik zwrotki typu b (kolor typu zwrotki)
        "#6db76d": 1,  # zielone potwierdzenie ✓ zapisu grupy (motyw nie ma roli sukcesu)
        "#343f58": 1,  # tło przycisku + (dodaj do zestawu), brak odpowiadającej roli
        "#2a3347": 1,  # ramka kontenera podglądu slajdu (podgląd to dane, bez zmian)
        "#3a6ea8": 1,  # niebieskie tło przycisku przy najechaniu
        "#7a2020": 1,  # ciemnoczerwone tło przycisku usuwania zestawu przy najechaniu
        "#ffffff": 3,  # biały tekst przy najechaniu na przyciski oraz tekst zastępczy grupy
        "#08ffffff": 1,  # półprzezroczyste podświetlenie
        "#0dc9a84c": 1,  # półprzezroczyste podświetlenie akcentem
        "#d44a4a15": 1,  # półprzezroczyste czerwone tło belki Trwa projekcja
    },
    "Views/RemoteControlTab.xaml": {
        "#3a1e1e": 1,  # tło ostrzeżenia o braku PIN-u (odcień bez roli w motywie)
        "#7a3030": 1,  # ramka ostrzeżenia o braku PIN-u
        "#e0a0a0": 2,  # tekst ostrzeżenia o braku PIN-u i komunikatu o odrzuconym urządzeniu
    },
}


def check_themes():
    errors, keysets = [], {}
    for n in NAMES:
        f = DIR / f"Theme.{n}.xaml"
        if not f.exists():
            errors.append(f"{f}: brak pliku")
            continue
        entries = ENTRY.findall(f.read_text(encoding="utf-8"))
        keysets[n] = {k for k, _ in entries}
        for k, v in entries:
            if not HEX.fullmatch(v):
                errors.append(f"{n}: {k} ma wartość '{v}' (oczekiwano #rrggbb)")
    union = set().union(*keysets.values()) if keysets else set()
    for n, ks in keysets.items():
        if union - ks:
            errors.append(f"{n}: brakuje kluczy {sorted(union - ks)}")
    return errors, len(union)


def check_colors():
    errors = []
    for f in sorted(ROOT.rglob("*.xaml")):
        rel = f.relative_to(ROOT)
        if rel.parts[0] in ("bin", "obj") or DIR in f.parents or f.name in EXCLUDED:
            continue
        allowed = ALLOWED.get(rel.as_posix(), {})
        seen = {}
        for no, line in enumerate(f.read_text(encoding="utf-8-sig").splitlines(), 1):
            for h in LITERAL.findall(line):
                h = h.lower()
                seen[h] = seen.get(h, 0) + 1
                if seen[h] > allowed.get(h, 0):
                    errors.append(f"{rel.as_posix()}:{no}: {h}")
    return errors


def main():
    errors, count = check_themes()
    if errors:
        print("BŁĄD motywów:\n  " + "\n  ".join(errors))
        return 1
    print(f"OK: {count} kluczy w {len(NAMES)} motywach")
    errors = check_colors()
    if errors:
        print("BŁĄD: kolor wpisany wprost (użyj Brush.* albo dopisz do ALLOWED):\n  " + "\n  ".join(errors))
        return 1
    print("OK: brak niedozwolonych kolorów wpisanych wprost w XAML")
    return 0


sys.exit(main())
