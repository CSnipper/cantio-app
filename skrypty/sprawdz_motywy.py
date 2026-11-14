"""Strażnik motywów: te same klucze x:Key we wszystkich Theme.*.xaml i wartości w formacie #rrggbb."""
import re
import sys
from pathlib import Path

DIR = Path(__file__).resolve().parent.parent / "Cantio" / "Assets" / "Themes"
NAMES = ["Obsidian", "Monochrome", "Sage"]
ENTRY = re.compile(r'<SolidColorBrush\s+x:Key="([^"]+)"\s+Color="([^"]*)"')
HEX = re.compile(r"#[0-9a-fA-F]{6}")


def main():
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
    if errors:
        print("BŁĄD motywów:\n  " + "\n  ".join(errors))
        return 1
    print(f"OK: {len(union)} kluczy w {len(NAMES)} motywach")
    return 0


sys.exit(main())
