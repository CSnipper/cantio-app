# -*- coding: utf-8 -*-
"""
parse_lekcjonarz_1975.py

Wyciaga PSALMY RESPONSORYJNE (refren + strofy + aklamacja) z 6 tomow PDF
"Lekcjonarza Mszalnego" (Pallottinum, 1972-1991/2004 = tzw. STARY lekcjonarz)
i przeklucza je do konwencji kluczy uzywanej przez apke (psalmy.json).

ETAP 1: cykl TEMPORALNY (Adwent, Narodzenie/BN, Wielki Post, Wielkanoc,
Okres Zwykly, niedziele A/B/C, dni powszednie ROK I/II). Swieci (Tom VI) sa
POMINIECI i wypisani w raporcie.

To jest skrypt DANYCH/RAPORTU. NIC nie wpina do apki, nie rusza assetow runtime.
Re-uruchamialny; waliduje przed zapisem; exit 1 gdy wynik podejrzany.

Wejscie:  docs/lekcjonarz/Lekcjonarz-Mszalny-Tom-I..VI.pdf
Wyjscie:  tools/psalmy_stary_1975.json
Raport:   <scratchpad>/lekc1975_raport.txt
"""

import os
import re
import sys
import json
import unicodedata
from collections import defaultdict, Counter

from pypdf import PdfReader

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PDF_DIR = os.path.join(REPO, "docs", "lekcjonarz")
OUT_JSON = os.path.join(REPO, "tools", "psalmy_stary_1975.json")
NIEZBEDNIK = os.path.join(REPO, "tools", "lekcjonarz-niezbednik.json")
PSALMY_NEW = r"C:\Users\konta\AndroidStudioProjects\CantioPilot\app\src\main\assets\psalmy.json"
PSALMY_DUAL = os.path.join(REPO, "tools", "psalmy_dual.json")
SCRATCH = (r"C:\Users\konta\AppData\Local\Temp\claude"
           r"\C--Users-konta-source-repos-Cantio"
           r"\3a4c920f-fb0f-450d-8c6e-1b4c6ccd74cb\scratchpad")
REPORT = os.path.join(SCRATCH, "lekc1975_raport.txt")

VOLUMES = ["I", "II", "III", "IV", "V", "VI"]
# Zakres etapu 1 (temporal). Tom VI (swieci) parsowany osobno / pominiety.
TEMPORAL_VOLUMES = ["I", "II", "III", "IV", "V"]

WD = {
    "PONIEDZIAŁEK": "Pon", "WTOREK": "Wto", "ŚRODA": "Śro",
    "CZWARTEK": "Czw", "PIĄTEK": "Pią", "SOBOTA": "Sob",
}

# ---------------------------------------------------------------------------
# Detekcja artefaktu "miekkiego lacznika" per tom.
# W tekscie glif dzielenia wyrazu / lacznika liczbowego jest renderowany jako
# ROZNA litera w kazdym tomie (font-dependent). Ujawnia to napis
# "POZNAŃ<X>WARSZAWA" na stronie tytulowej (X = ten glif).
# ---------------------------------------------------------------------------
def detect_hyphen(reader):
    t0 = (reader.pages[0].extract_text() or "")
    t0 = t0.replace("\n", " ")
    m = re.search(r"POZNA[ŃN]\s*(.)\s*WARSZAWA", t0)
    if m:
        c = m.group(1)
        if c.isalpha() and c.lower() not in ("w",):  # 'w' realny w IV -> zaakceptuj mimo to
            return c
        if c.isalpha():
            return c
    return None


# ---------------------------------------------------------------------------
# Ekstrakcja linii z tomu (z zachowaniem podzialu na strony).
# ---------------------------------------------------------------------------
def load_lines(path):
    reader = PdfReader(path)
    hy = detect_hyphen(reader)
    pages = []
    for i in range(len(reader.pages)):
        txt = reader.pages[i].extract_text() or ""
        pages.append(txt.split("\n"))
    return pages, hy


# ---------------------------------------------------------------------------
# Czyszczenie tekstu psalmu / referencji.
# ---------------------------------------------------------------------------
def clean_ref(ref, hy):
    """Czysci linie odnosnika 'Ps 122 (121), 1p2. 4p5 ...' -> '... 1-2. 4-5 ...'."""
    if hy:
        # cyfra[litera] <HY> cyfra  ->  z myslnikiem (kontekst wersetow, bezpieczny)
        ref = re.sub(r"(\d[a-z]?)\s*" + re.escape(hy) + r"\s*(\d)", r"\1-\2", ref)
    # "R.: p or. 1a" -> "R.: por. 1a" (glif rozbil 'por.')
    ref = re.sub(r"\bp\s+or\.", "por.", ref)
    ref = re.sub(r"\b" + (re.escape(hy) if hy else "X") + r"\s+or\.", "por.", ref)
    ref = " ".join(ref.split())
    return ref.strip()


def clean_verse_line(ln, hy):
    """Czysci pojedyncza linie strofy."""
    s = ln.rstrip()
    # usun wiodacy numer wersetu: "  1 Ucieszylem" / " 12 ..."
    s = re.sub(r"^\s*\d+\s+", "", s)
    s = s.strip()
    # gwiazdki/krzyzyki: "text: * " -> "text:*"
    s = re.sub(r"\s+([*†])", r"\1", s)
    # cudzyslowy francuskie -> polskie
    s = s.replace("« ", "„").replace(" »", "”").replace("«", "„").replace("»", "”")
    s = s.replace("“", "„").replace("”", "”")
    # laczniki liczbowe w cytatach
    if hy:
        s = re.sub(r"(\d)" + re.escape(hy) + r"(\d)", r"\1-\2", s)
    s = " ".join(s.split())
    return s


def is_marker(up):
    return (up.startswith("PIERWSZE CZYTANIE") or up.startswith("DRUGIE CZYTANIE")
            or up.startswith("CZYTANIE") or up.startswith("ŚPIEW PRZED EWANGE")
            or up.startswith("EWANGELIA") or up.startswith("PSALM RESPONSOR")
            or up.startswith("ALLELUJA PRZED") or up.startswith("SEKWENCJA")
            or up.startswith("ANTYFONA"))


# ---------------------------------------------------------------------------
# Parsowanie jednego bloku psalmu, poczynajac od indeksu linii z markerem.
# Zwraca (psalm_responsoryjny_str, ref, refren_text, aklamacja_str, next_idx).
# ---------------------------------------------------------------------------
def parse_psalm_block(flat, start, hy):
    n = len(flat)
    i = start
    # linia markera moze zawierac odnosnik po 'PSALM RESPONSORYJNY'
    first = flat[i].strip()
    ref = ""
    m = re.search(r"PSALM RESPONSORYJNY\s*(.*)$", first)
    tail = m.group(1).strip() if m else ""
    i += 1
    if tail:
        ref = tail
    else:
        # odnosnik w kolejnych liniach zaczyna sie od 'Ps'
        while i < n and not flat[i].strip():
            i += 1
        if i < n and re.match(r"^\s*Ps\b", flat[i]):
            ref = flat[i].strip()
            i += 1
    ref = clean_ref(ref, hy)

    # Refren
    refren = ""
    while i < n and not flat[i].strip():
        i += 1
    if i < n and re.match(r"^\s*Refren\s*:", flat[i]):
        refren = re.sub(r"^\s*Refren\s*:\s*", "", flat[i].strip())
        i += 1
        # refren moze byc kontynuowany, dopoki nie zacznie sie werset (cyfra)
        while i < n:
            s = flat[i].strip()
            if not s:
                i += 1
                continue
            if re.match(r"^\d", s) or re.match(r"^Refren", s) or is_marker(s.upper()):
                break
            if s.upper() in ("ROK I", "ROK II", "ROK A", "ROK B", "ROK C"):
                break
            if re.match(r"^(lub|albo)\s*:", s, re.I):
                break
            refren += " " + s
            i += 1
    # utnij ewentualny alternatywny refren doklejony w tej samej linii
    refren = re.split(r"\s+(?:albo|lub)\s*:", refren, maxsplit=1, flags=re.I)[0]
    refren = clean_verse_line(refren, hy)

    # Strofy: linie do nastepnego markera / naglowka dnia. Bloki "Refren." dziela.
    strophes = []
    cur = []
    while i < n:
        raw = flat[i]
        s = raw.strip()
        up = s.upper()
        if not s:
            i += 1
            continue
        if is_marker(up) and not up.startswith("PSALM RESPONSOR"):
            break
        if up.startswith("PSALM RESPONSOR"):
            break
        if up in ("ROK I", "ROK II", "ROK A", "ROK B", "ROK C"):
            break
        if is_day_header(s):
            break
        if re.match(r"^Refren\.?\s*$", s) or s.lower().startswith("lub:") or s == "Refren":
            if cur:
                strophes.append(cur)
                cur = []
            i += 1
            continue
        if re.match(r"^Refren\b", s):
            if cur:
                strophes.append(cur)
                cur = []
            i += 1
            continue
        cur.append(clean_verse_line(raw, hy))
        i += 1
    if cur:
        strophes.append(cur)

    strophes = [[l for l in st if l] for st in strophes]
    strophes = [st for st in strophes if st]

    if refren:
        parts = ["Refren: " + refren]
    else:
        parts = []
    for st in strophes:
        parts.append("\n".join(st))
    psalm_str = "\n\n".join(parts)
    return psalm_str, ref, refren, i


def parse_aklamacja(flat, start, hy):
    """Parsuje blok SPIEW PRZED EWANGELIA -> 'Aklamacja: ...'."""
    n = len(flat)
    i = start + 1
    refrain = ""
    verse = []
    # znajdz pierwsza linie 'Aklamacja:' lub 'Alleluja'
    seen_refrain = False
    count_refrain = 0
    while i < n:
        s = flat[i].strip()
        up = s.upper()
        if not s:
            i += 1
            continue
        if is_day_header(s) or (is_marker(up) and not up.startswith("ŚPIEW")):
            break
        m = re.match(r"^(Aklamacja|Alleluja)\s*:?\s*(.*)$", s)
        if m:
            count_refrain += 1
            if count_refrain == 1:
                refrain = clean_verse_line(s.split(":", 1)[-1] if ":" in s else s, hy)
                if refrain.lower().startswith("aklamacja"):
                    refrain = re.sub(r"^Aklamacja\s*:?\s*", "", refrain)
                seen_refrain = True
                i += 1
                continue
            else:
                break  # powtorzenie refrenu = koniec
        if s.lower().startswith("lub:"):
            i += 1
            continue
        if seen_refrain:
            verse.append(clean_verse_line(flat[i], hy))
        i += 1
    verse = [v for v in verse if v]
    if not refrain and not verse:
        return "", i
    out = "Aklamacja: " + refrain
    if verse:
        out += "\n\n" + "\n".join(verse)
    return out, i


# ---------------------------------------------------------------------------
# Rozpoznawanie naglowkow dnia i budowanie klucza `dzien`.
# ---------------------------------------------------------------------------
def is_day_header(s):
    up = s.upper()
    if re.match(r"^\d+\s+(NIEDZIELA|TYDZIEŃ)\b", up):
        return True
    if up in WD:
        return True
    if up.startswith("ŚRODA POPIELCOWA") or up.startswith("CZWARTEK PO POPIELCU") \
       or up.startswith("PIĄTEK PO POPIELCU") or up.startswith("SOBOTA PO POPIELCU"):
        return True
    if up.startswith("NIEDZIELA PALMOWA"):
        return True
    return False


class DayState:
    def __init__(self):
        self.period = None      # ADW / BN / WP / WK / ZW
        self.week = None        # int
        self.weekday = None     # Pon/Wto/...
        self.cycle_year = None  # 1 / 2 (dla ZW weekdays)
        self.sunday_cycle = None  # A/B/C
        self.mode = None        # 'sunday' | 'weekday' | 'special'
        self.special = None     # pelna nazwa dla WP specjalnych

    def key(self):
        if self.mode == "special":
            return self.special
        if self.mode == "sunday":
            if self.period == "ADW":
                return f"ADW {self.week} Nie {self.sunday_cycle}"
            if self.period == "WP":
                return f"WP {self.week} Nie {self.sunday_cycle}"
            if self.period == "WK":
                return f"WK {self.week} Nie {self.sunday_cycle}"
            if self.period == "ZW":
                return f"ZW {self.week} Nie {self.sunday_cycle}"
            return None
        if self.mode == "weekday":
            if self.weekday is None or self.week is None:
                return None
            if self.period == "ZW":
                if self.cycle_year is None:
                    return None
                return f"ZW {self.week} {self.weekday} {self.cycle_year}"
            if self.period == "ADW":
                return f"ADW {self.week} {self.weekday}"
            if self.period == "WP":
                return f"WP {self.week} {self.weekday}"
            if self.period == "WK":
                return f"WK {self.week} {self.weekday}"
        return None


def period_for_volume(vol):
    return {
        "I": "ADW",   # + BN
        "II": "WP",   # + WK
        "III": "ZW",
        "IV": "ZW",
        "V": "ZW",
    }.get(vol)


def update_state_from_header(st, s, vol):
    up = s.upper()
    # Niedziele: "N NIEDZIELA <OKRES>, ROK X"
    m = re.match(r"^(\d+)\s+NIEDZIELA\s+([A-ZŚĄĘÓŻŹŁŃ]+),?\s*ROK\s+([ABC])", up)
    if m:
        st.week = int(m.group(1))
        okr = m.group(2)
        if "ADWENT" in okr:
            st.period = "ADW"
        elif "WIELKIEGO" in up or "POSTU" in up:
            st.period = "WP"
        elif "WIELKANOC" in okr or "WIELKANOCN" in okr:
            st.period = "WK"
        elif "ZWYK" in okr:
            st.period = "ZW"
        st.sunday_cycle = m.group(3)
        st.mode = "sunday"
        st.weekday = None
        st.cycle_year = None
        return True
    # "N NIEDZIELA WIELKIEGO POSTU, ROK X"
    m = re.match(r"^(\d+)\s+NIEDZIELA\s+WIELKIEGO\s+POSTU,?\s*ROK\s+([ABC])", up)
    if m:
        st.week = int(m.group(1)); st.period = "WP"; st.sunday_cycle = m.group(2)
        st.mode = "sunday"; st.weekday = None; st.cycle_year = None
        return True
    # "N NIEDZIELA WIELKANOCNA, ROK X"
    m = re.match(r"^(\d+)\s+NIEDZIELA\s+WIELKANOCN\w*,?\s*ROK\s+([ABC])", up)
    if m:
        st.week = int(m.group(1)); st.period = "WK"; st.sunday_cycle = m.group(2)
        st.mode = "sunday"; st.weekday = None; st.cycle_year = None
        return True
    # "N TYDZIEŃ ZWYKŁY" / "N TYDZIEŃ ADWENTU" / "N TYDZIEŃ WIELKIEGO POSTU"
    m = re.match(r"^(\d+)\s+TYDZIEŃ\s+([A-ZŚĄĘÓŻŹŁŃ]+)", up)
    if m:
        st.week = int(m.group(1))
        okr = m.group(2)
        if "ZWYK" in okr:
            st.period = "ZW"
        elif "ADWENT" in okr:
            st.period = "ADW"
        elif "WIELKIEGO" in up:
            st.period = "WP"
        elif "WIELKANOC" in okr:
            st.period = "WK"
        st.weekday = None
        return True
    # WP specjalne
    if up.startswith("ŚRODA POPIELCOWA"):
        st.period="WP"; st.mode="special"; st.special="WP Środa Popielcowa"; return True
    if up.startswith("CZWARTEK PO POPIELCU"):
        st.period="WP"; st.mode="special"; st.special="WP Czwartek PO Popielcu"; return True
    if up.startswith("PIĄTEK PO POPIELCU"):
        st.period="WP"; st.mode="special"; st.special="WP Piątek PO Popielcu"; return True
    if up.startswith("SOBOTA PO POPIELCU"):
        st.period="WP"; st.mode="special"; st.special="WP Sobota PO Popielcu"; return True
    if up.startswith("NIEDZIELA PALMOWA"):
        st.period="WP"; st.mode="special"; st.special="WP Niedziela Palmowa"; return True
    # Dzien tygodnia
    if up in WD:
        st.weekday = WD[up]
        st.mode = "weekday"
        st.cycle_year = None
        st.special = None
        return True
    return False


# ---------------------------------------------------------------------------
# Parsowanie calego tomu.
# ---------------------------------------------------------------------------
def parse_volume(vol):
    path = os.path.join(PDF_DIR, f"Lekcjonarz-Mszalny-Tom-{vol}.pdf")
    pages, hy = load_lines(path)
    flat = []
    for pg in pages:
        for ln in pg:
            flat.append(ln)
    st = DayState()
    st.period = period_for_volume(vol)
    results = []  # (dzien, psalm_str, ref, refren, aklamacja, note)
    pending_akl = {}  # dzien -> aklamacja
    i = 0
    n = len(flat)
    last_key = None
    while i < n:
        raw = flat[i]
        s = raw.strip()
        if not s:
            i += 1
            continue
        up = s.upper()
        # naglowek ROK I / ROK II (dni powszednie ZW)
        if up == "ROK I":
            st.cycle_year = 1
            if st.mode != "weekday":
                st.mode = "weekday"
            i += 1
            continue
        if up == "ROK II":
            st.cycle_year = 2
            st.mode = "weekday"
            i += 1
            continue
        if is_day_header(s):
            update_state_from_header(st, s, vol)
            i += 1
            continue
        # dodatkowe naglowki ktore ustawiaja stan (niedziele/tygodnie)
        if re.match(r"^\d+\s+(NIEDZIELA|TYDZIEŃ)\b", up):
            update_state_from_header(st, s, vol)
            i += 1
            continue
        if up.startswith("PSALM RESPONSOR"):
            key = st.key()
            psalm_str, ref, refren, ni = parse_psalm_block(flat, i, hy)
            if key and refren:
                results.append([key, psalm_str, ref, refren, "", ""])
                last_key = key
            i = ni
            continue
        if up.startswith("ŚPIEW PRZED EWANGE") or up.startswith("ALLELUJA PRZED"):
            akl, ni = parse_aklamacja(flat, i, hy)
            if akl and last_key:
                # przypisz do ostatniego psalmu tego dnia
                for r in reversed(results):
                    if r[0] == last_key:
                        r[4] = akl
                        break
            i = ni if ni > i else i + 1
            continue
        i += 1
    return results, hy


# ---------------------------------------------------------------------------
# Normalizacja do porownan walidacyjnych.
# ---------------------------------------------------------------------------
def norm_refren(s):
    if not s:
        return ""
    s = s.replace("Refren:", "").replace("Aklamacja:", "")
    s = s.lower()
    s = s.replace("*", " ").replace("†", " ")
    s = s.replace("„", "").replace("”", "").replace('"', "").replace("«", "").replace("»", "")
    s = re.sub(r"[.,:;!?]", " ", s)
    s = "".join(c for c in unicodedata.normalize("NFKD", s) if not unicodedata.combining(c))
    s = " ".join(s.split())
    return s


def norm_psref(ref):
    if not ref:
        return ""
    # usun wulgate "(121)"
    ref = re.sub(r"\(\d+\)", "", ref)
    # usun "(R.: ...)"
    ref = re.sub(r"\(R\.?:.*?\)", "", ref)
    m = re.search(r"Ps\s+(\d+)", ref)
    if not m:
        return ""
    return "Ps " + m.group(1)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    all_results = []
    per_vol_counts = {}
    hy_map = {}
    for vol in TEMPORAL_VOLUMES:
        res, hy = parse_volume(vol)
        hy_map[vol] = hy
        per_vol_counts[vol] = len(res)
        all_results.extend(res)

    # Dedup po kluczu (pierwszy wygrywa; zbieraj kolizje)
    by_key = {}
    dup_keys = Counter()
    for r in all_results:
        k = r[0]
        if k in by_key:
            dup_keys[k] += 1
            continue
        by_key[k] = r

    # Zbuduj rekordy wyjsciowe w formacie psalmy.json
    out = []
    for k, r in by_key.items():
        rec = {"dzien": k, "psalm_responsoryjny": r[1]}
        if r[4]:
            rec["aklamacja"] = r[4]
        out.append(rec)

    # Statystyki per okres
    per_period = Counter()
    for k in by_key:
        per_period[k.split()[0]] += 1

    # -------- WALIDACJA wzgledem niezbednika (stary) --------
    niez = json.load(open(NIEZBEDNIK, encoding="utf-8"))
    niez_stary = defaultdict(list)  # dzien(bez sufiksu) -> [(refren_norm, psref_norm, refren_raw, ref_raw)]
    for e in niez:
        dz = e["dzien"]
        for f in e.get("formularze", []):
            st = f.get("stary")
            if not st:
                continue
            pr = st.get("psalm_responsoryjny") or {}
            tit = pr.get("tytul") or ""
            ref = pr.get("ref") or ""
            niez_stary[dz].append((norm_refren(tit), norm_psref(ref), tit, ref))

    def strip_cycle(k):
        return re.sub(r"\s+[12]$", "", k)

    compared = 0
    refren_match = 0
    ref_match = 0
    both_match = 0
    mismatches = []
    for k, r in by_key.items():
        base = strip_cycle(k)
        cands = niez_stary.get(base) or niez_stary.get(k)
        if not cands:
            continue
        compared += 1
        my_ref = norm_refren(r[3])
        my_psref = norm_psref(r[2])
        rmatch = any(my_ref and my_ref == c[0] for c in cands)
        pmatch = any(my_psref and my_psref == c[1] for c in cands)
        if rmatch:
            refren_match += 1
        if pmatch:
            ref_match += 1
        if rmatch and pmatch:
            both_match += 1
        if not rmatch:
            mismatches.append((k, r[3], r[2], cands[0][2], cands[0][3]))

    # -------- POKRYCIE wzgledem psalmy.json (814) --------
    try:
        pnew = json.load(open(PSALMY_NEW, encoding="utf-8"))
        new_keys = set(e["dzien"] for e in pnew)
    except Exception:
        new_keys = set()

    my_base_keys = set(strip_cycle(k) for k in by_key)
    my_keys = set(by_key)
    covered = 0
    for nk in new_keys:
        if nk in my_keys or strip_cycle(nk) in my_base_keys or nk in my_base_keys:
            covered += 1

    # -------- Domkniecie luk "starego" wzgledem psalmy_dual.json --------
    gap_closed = 0
    gap_total = 0
    try:
        dual = json.load(open(PSALMY_DUAL, encoding="utf-8"))
        # luka = klucz bez distinct 'S'. Heurystyka: brak zwrotek oznaczonych S.
        for e in dual:
            parts = e.get("zwrotki") or e.get("verses") or []
            has_s = False
            if isinstance(parts, list):
                for p in parts:
                    lk = (p.get("lekcjonarz") or p.get("Lekcjonarz")) if isinstance(p, dict) else None
                    if lk == "S":
                        has_s = True
                        break
            if not has_s:
                gap_total += 1
                dz = e.get("dzien")
                if dz and (dz in my_keys or strip_cycle(dz) in my_base_keys):
                    gap_closed += 1
    except Exception as ex:
        gap_total = -1

    # -------- ANALIZA TROJSTRONNA (1975 vs NEW vs STARY) --------
    # Kluczowe pytanie: czy 1975 to wydanie "stary" (niezbednik), "nowy"
    # (psalmy.json), czy odrebne trzecie zrodlo?
    new_ref = {}
    try:
        for e in pnew:
            m = re.search(r"Refren:\s*([^\n]*)", e.get("psalm_responsoryjny") or "")
            new_ref[e["dzien"]] = norm_refren(m.group(1)) if m else ""
    except Exception:
        pass
    ns_ref = {}
    for dz, cands in niez_stary.items():
        ns_ref[dz] = cands[0][0]  # znormalizowany refren pierwszego

    threeway = {"both": 0, "new_only": 0, "stary_only": 0, "neither": 0}
    neither_examples = []
    for k, r in by_key.items():
        b = strip_cycle(k)
        if k not in new_ref or b not in ns_ref:
            continue
        mn = norm_refren(r[3])
        nn = new_ref[k]
        sn = ns_ref[b]
        if not nn or not sn or nn == sn:
            continue  # dzien nie rozroznia wydan
        is_new = (mn == nn)
        is_stary = (mn == sn)
        if is_new and is_stary:
            threeway["both"] += 1
        elif is_new:
            threeway["new_only"] += 1
        elif is_stary:
            threeway["stary_only"] += 1
        else:
            threeway["neither"] += 1
            if len(neither_examples) < 12:
                neither_examples.append((k, r[3], r[2]))
    discriminating = sum(threeway.values())

    # -------- WALIDACJA BRAMKA --------
    problems = []
    if per_period.get("ZW", 0) < 200:
        problems.append(f"Za malo kluczy ZW: {per_period.get('ZW',0)} (<200)")
    if compared and refren_match / compared < 0.55:
        problems.append(f"Niska zgodnosc refrenow: {refren_match}/{compared}")
    if len(out) < 400:
        problems.append(f"Za malo sparsowanych kluczy: {len(out)} (<400)")

    # Podejrzane rekordy (mozliwe artefakty czyszczenia)
    suspects = []
    for k, r in by_key.items():
        ps = r[1]
        if not r[3]:
            suspects.append((k, "brak refrenu"))
        elif len(r[3]) < 8:
            suspects.append((k, f"krotki refren: '{r[3]}'"))
        elif ps.count("\n\n") < 1:
            suspects.append((k, "brak strof (tylko refren?)"))
        # artefakt: pojedyncze osamotnione litery-glify
        if re.search(r"\b[a-ząćęłńóśżź]\s+or\.", ps):
            suspects.append((k, "mozliwy artefakt 'por.'"))

    # -------- Zapis JSON (tylko gdy brak twardych problemow) --------
    out.sort(key=lambda e: e["dzien"])
    if problems:
        write_report(REPORT, hy_map, per_vol_counts, per_period, out, compared,
                     refren_match, ref_match, both_match, mismatches, covered,
                     len(new_keys), gap_closed, gap_total, suspects, dup_keys, problems,
                     threeway, discriminating, neither_examples, wrote=False)
        print("BRAMKA: wynik podejrzany, JSON NIE zapisany. Zob. raport.")
        for p in problems:
            print("  -", p)
        return 1

    json.dump(out, open(OUT_JSON, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    write_report(REPORT, hy_map, per_vol_counts, per_period, out, compared,
                 refren_match, ref_match, both_match, mismatches, covered,
                 len(new_keys), gap_closed, gap_total, suspects, dup_keys, problems,
                 threeway, discriminating, neither_examples, wrote=True)
    print(f"OK: zapisano {len(out)} kluczy do {OUT_JSON}")
    print(f"Zgodnosc refrenow z niezbednikiem: {refren_match}/{compared}")
    return 0


def write_report(path, hy_map, per_vol_counts, per_period, out, compared,
                 refren_match, ref_match, both_match, mismatches, covered,
                 new_total, gap_closed, gap_total, suspects, dup_keys, problems,
                 threeway, discriminating, neither_examples, wrote):
    L = []
    L.append("=" * 70)
    L.append("RAPORT: PSALMY RESPONSORYJNE ze STAREGO LEKCJONARZA (Pallottinum 1975)")
    L.append("=" * 70)
    L.append("")
    L.append("PODSUMOWANIE")
    L.append(f"  Sparsowanych kluczy (unikalne): {len(out)}")
    L.append(f"  JSON zapisany: {'TAK' if wrote else 'NIE (bramka)'}")
    L.append(f"  Per okres: {dict(per_period)}")
    L.append(f"  Per tom (surowe bloki): {per_vol_counts}")
    L.append(f"  Glif lacznika per tom: {hy_map}")
    L.append("")
    L.append("ZGODNOSC z niezbednikiem (wydanie STARY)")
    if compared:
        L.append(f"  Porownano kluczy: {compared}")
        L.append(f"  Refren zgodny:  {refren_match} ({100*refren_match/compared:.1f}%)")
        L.append(f"  Odnosnik Ps zgodny: {ref_match} ({100*ref_match/compared:.1f}%)")
        L.append(f"  Oba zgodne: {both_match} ({100*both_match/compared:.1f}%)")
    else:
        L.append("  Brak wspolnych kluczy do porownania!")
    L.append("")
    L.append("POKRYCIE")
    L.append(f"  Klucze psalmy.json (814) z psalmem 1975: {covered}/{new_total}")
    if gap_total >= 0:
        L.append(f"  Luki 'starego' (bez distinct S) domkniete: {gap_closed}/{gap_total}")
    else:
        L.append("  Luki 'starego': nie policzono (format psalmy_dual.json inny)")
    L.append("")
    L.append("ANALIZA TROJSTRONNA — z ktorym wydaniem zgadza sie 1975?")
    L.append("  (tylko dni, gdzie NEW=psalmy.json ROZNI sie od STARY=niezbednik)")
    L.append(f"  Dni rozrozniajace wydania: {discriminating}")
    L.append(f"    1975 == NEW (a != stary):   {threeway['new_only']}")
    L.append(f"    1975 == STARY (a != new):   {threeway['stary_only']}")
    L.append(f"    1975 == oba:                {threeway['both']}")
    L.append(f"    1975 == zadne (trzecia lekcja / blad parsera): {threeway['neither']}")
    L.append("  WNIOSEK: jesli new_only ~ stary_only, 1975 to ODREBNE trzecie")
    L.append("  wydanie, nie czysty 'stary' -> podmiana 'S' na 1975 skazilaby")
    L.append("  ~polowe dni rozroznionych trescia 'nowa'.")
    L.append("  Przyklady '1975 == zadne' (do przegladu, mozliwe artefakty):")
    for k, mr, mp in neither_examples:
        L.append(f"    {k}: {mr!r}  [{mp}]")
    L.append("")
    L.append("PRZYKLADY ROZJAZDOW REFRENOW (moj 1975  vs  niezbednik-stary)")
    L.append("  [do oceny: blad parsera/czyszczenia czy inne wydanie]")
    for k, myref, myps, nref, nps in mismatches[:15]:
        L.append(f"  --- {k}")
        L.append(f"      1975 : {myref!r}  [{myps}]")
        L.append(f"      niez : {nref!r}  [{nps}]")
    L.append(f"  (rozjazdow lacznie: {len(mismatches)})")
    L.append("")
    L.append("REKOMENDACJA")
    nw = threeway["new_only"]; so = threeway["stary_only"]; ne = threeway["neither"]
    L.append(f"  Na dniach rozrozniajacych wydania 1975 zgadza sie z NOWYM {nw}x,")
    L.append(f"  ze STARYM {so}x. 1975 (Pallottinum, tomy ZW datowane 1991) jest")
    L.append("  z tej samej tradycji co obecne 'nowy' (psalmy.json), NIE jest")
    L.append("  wydaniem 'stary' z niezbednika (te dwa roznia sie na >50% dni).")
    L.append("  => NIE podmieniac 'S' na 1975 — skazilaby wydanie stare trescia")
    L.append(f"     nowa na ~{nw} dniach. 1975 nadaje sie natomiast jako niezalezny")
    L.append("     kontrola/backfill dla wydania NOWEGO (pokrycie 630/814).")
    L.append(f"  '1975 == zadne' ({ne}) = kandydaci na trzecia lekcje albo artefakt")
    L.append("     glifu (patrz nizej) — wymagaja recznego przegladu.")
    L.append("")
    L.append("RYZYKO ARTEFAKTOW GLIFOWYCH (warstwa tekstowa PDF)")
    L.append("  Glif miekkiego lacznika rozny per tom (p/o/e/w) — wykryty i czysczony.")
    L.append("  Poza tym niektore znaki bywaja mis-mapowane w warstwie tekstowej,")
    L.append("  np. 'wyslychal' zamiast 'wysluchal', co daje falszywy rozjazd refrenu.")
    L.append("  Skala: odnosnik Ps zgodny w " + f"{ref_match}/{compared}" + " dni, refren w "
             + f"{refren_match}/{compared}" + f" — roznica ~{ref_match-refren_match} dni to")
    L.append("  ten sam psalm o innym brzmieniu refrenu (glif/drobne wydanie), reszta")
    L.append("  rozjazdow = inny psalm (rzeczywista roznica wydan).")
    L.append("")
    L.append("PODEJRZANE REKORDY (mozliwe bledy czyszczenia / do przegladu)")
    for k, why in suspects[:40]:
        L.append(f"  {k}: {why}")
    L.append(f"  (lacznie podejrzanych: {len(suspects)})")
    L.append("")
    L.append("KOLIZJE KLUCZY (ten sam dzien parsowany >1x)")
    for k, c in dup_keys.most_common(20):
        L.append(f"  {k}: +{c}")
    L.append("")
    L.append("SWIECI / US — POMINIETI w etapie 1")
    L.append("  Tom VI (Czytania w Mszach o Swietych, 2004) NIE byl parsowany.")
    L.append("  Mapowanie nazw swietych na klucze US/nazwane wymaga osobnej tury")
    L.append("  (dopasowanie tytul obchodu -> klucz jak w scal_mszal_swieci.py).")
    L.append("")
    if problems:
        L.append("PROBLEMY BRAMKI:")
        for p in problems:
            L.append(f"  - {p}")
    open(path, "w", encoding="utf-8").write("\n".join(L))


if __name__ == "__main__":
    sys.exit(main())
