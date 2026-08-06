# -*- coding: utf-8 -*-
"""
parse_lekcjonarz_1975_full.py

Wyciaga CALY temporal STAREGO lekcjonarza (Pallottinum 1972-1991) z 6 tomow PDF:
CZYTANIA + PSALMY + AKLAMACJE, i przeklucza je do konwencji kluczy uzywanej
przez apke (czytania.json / psalmy.json).

WAZNE: ekstrakcja idzie przez PyMuPDF, nie pypdf. pypdf na tych plikach gubi
warstwe tekstowa (glif miekkiego lacznika renderowany jako litera p/o/e/w,
rozjechane spacje w srodku slow). PyMuPDF czyta te same strony poprawnie:
prawdziwe myslniki, prawdziwe cudzyslowy, zero wtracanych spacji.
Dodatkowo daje rozmiar czcionki, co pozwala odsiac numery wersetow (8 pkt)
i rubryki (13 pkt) od tekstu liturgicznego (14,5 pkt).

To jest skrypt DANYCH/RAPORTU. NIC nie wpina do apki, nie rusza assetow runtime.
Re-uruchamialny; waliduje przed zapisem; exit 1 gdy kanarek padnie.

Wejscie:  docs/lekcjonarz/Lekcjonarz-Mszalny-Tom-I..VI.pdf
Wyjscie:  tools/lekcjonarz_stary_1975.json
Raport:   <scratchpad>/lekc1975_full_raport.txt
"""

import os
import re
import sys
import json
import unicodedata
from collections import defaultdict, Counter

import pymupdf

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PDF_DIR = os.path.join(REPO, "docs", "lekcjonarz")
OUT_JSON = os.path.join(REPO, "tools", "lekcjonarz_stary_1975.json")
CZYTANIA_NEW = (r"C:\Users\konta\AndroidStudioProjects\CantioPilot"
                r"\app\src\main\assets\czytania.json")
PSALMY_NEW = (r"C:\Users\konta\AndroidStudioProjects\CantioPilot"
              r"\app\src\main\assets\psalmy.json")
CZYTANIA_STARY = os.path.join(REPO, "tools", "czytania_stary.json")
SCRATCH = (r"C:\Users\konta\AppData\Local\Temp\claude"
           r"\C--Users-konta-source-repos-Cantio"
           r"\3a4c920f-fb0f-450d-8c6e-1b4c6ccd74cb\scratchpad")
REPORT = os.path.join(SCRATCH, "lekc1975_full_raport.txt")

VOLUMES = ["I", "II", "III", "IV", "V"]

# Progi rozmiarow czcionki zmierzone na tych plikach.
SZ_TINY = 10.0     # numery wersetow psalmu (8 pkt) — odrzucane
SZ_BODY = 14.4     # tekst liturgiczny (14,5 pkt); ponizej: markery/rubryki/tytuly

WD = {"PONIEDZIAŁEK": "Pon", "WTOREK": "Wto", "ŚRODA": "Śro",
      "CZWARTEK": "Czw", "PIĄTEK": "Pią", "SOBOTA": "Sob"}
WEEK_WORDS = {"PIERWSZY": 1, "DRUGI": 2, "TRZECI": 3, "CZWARTY": 4,
              "PIĄTY": 5, "SZÓSTY": 6, "SIÓDMY": 7}

MARKER_RE = re.compile(
    r"^(PIERWSZE CZYTANIE|DRUGIE CZYTANIE|TRZECIE CZYTANIE|CZWARTE CZYTANIE|"
    r"PIĄTE CZYTANIE|SZÓSTE CZYTANIE|SIÓDME CZYTANIE|ÓSME CZYTANIE|"
    # 'PSALM REPONSORYJNY' — literowka zecerska w Tomie II, s. 360
    r"PSALM RE\w*PONSORYJNY|ŚPIEW PRZED EWANGELIĄ|ALLELUJA PRZED EWANGELIĄ|"
    r"SEKWENCJA|EWANGELIA)")

REF_RE = re.compile(r"^(Por\.\s*)?([1-3]\s*)?[A-ZŚŁŻŹĆŃÓĘĄ][A-Za-ząćęłńóśżźĄĆĘŁŃÓŚŻŹ]{0,5}"
                    r"\.?(\s*\([A-Za-z]+\.?\))?[\s,]+\d")

RUBRIC_HINTS = (
    "str.", "można", "Można", "Poniższych", "Zamiast", "W krajach", "Początek",
    "Koniec", "lub w ", "Powyższ", "Zob.", "zamiast", "Wybiera", "opuszcza",
    "Jeżeli", "Gdy ", "Tekst", "podanych", "umieszczon",
)


# ---------------------------------------------------------------------------
# 1. Ekstrakcja linii
# ---------------------------------------------------------------------------
class Line:
    __slots__ = ("vol", "page", "text", "size", "x0", "x1", "font")

    def __init__(self, vol, page, text, size, x0, x1, font):
        self.vol, self.page, self.text = vol, page, text
        self.size, self.x0, self.x1, self.font = size, x0, x1, font

    def __repr__(self):
        return "<%s p%d %.1f %r>" % (self.vol, self.page, self.size, self.text[:40])


VARIANT_ONLY = re.compile(r"^[\s,—-]*(DŁUŻSZ\w*|KRÓTSZ\w*|KTÓRSZ\w*|W\s+ROKU\s+[ABC]|"
                          r"W\s+LATACH\s+[ABC](\s*i\s*[ABC])?)[\s,]*$", re.I)


def merge_variant_lines(lines):
    """'EWANGELIA' + 'DŁUŻSZA' w dwoch liniach to jeden marker.
    Bez tego 'DŁUŻSZA' wyglada jak naglowek dnia i urywa sekcje."""
    out = []
    for ln in lines:
        if (out and VARIANT_ONLY.match(ln.text.strip())
                and MARKER_RE.match(out[-1].text.strip())):
            out[-1].text = out[-1].text.rstrip() + " " + ln.text.strip()
            continue
        out.append(ln)
    return out


def extract_lines(vol):
    """Zwraca (lines, rmargin_per_page)."""
    path = os.path.join(PDF_DIR, "Lekcjonarz-Mszalny-Tom-%s.pdf" % vol)
    doc = pymupdf.open(path)
    lines = []
    rmargin = {}
    for pno in range(len(doc)):
        page = doc[pno]
        for block in page.get_text("dict")["blocks"]:
            for ln in block.get("lines", []):
                spans = [s for s in ln["spans"]
                         if s["size"] >= SZ_TINY and s["font"] != "Wingdings"]
                if not spans:
                    continue
                text = "".join(s["text"] for s in spans)
                if not text.strip():
                    continue
                size = max(s["size"] for s in spans)
                x0 = min(s["bbox"][0] for s in spans)
                x1 = max(s["bbox"][2] for s in spans)
                lines.append(Line(vol, pno + 1, text, size, x0, x1, spans[0]["font"]))
                if size >= SZ_BODY:
                    rmargin[pno + 1] = max(rmargin.get(pno + 1, 0), x1)
    doc.close()
    return merge_variant_lines(lines), rmargin


# ---------------------------------------------------------------------------
# 2. Czyszczenie tekstu
# ---------------------------------------------------------------------------
INTRA_HYPHEN = re.compile(r"(?<=[a-ząćęłńóśżź])-(?=[a-ząćęłńóśżź])")
# realne zlozenia pisane z lacznikiem malymi literami po obu stronach
HYPHEN_KEEP = ("biało-", "czarno-", "polsko-", "grecko-", "rzymsko-", "słodko-",
               "wielko-", "bogo-",
               # realne zlozenia biblijne (zweryfikowane na wyniku)
               "nie-", "kapłan-", "bóg-", "człowiek-", "król-")

intra_joins = Counter()


def fix_intra_hyphen(s):
    """Skleja zgubione miekkie laczniki w SRODKU linii ('wszyst-kich')."""
    def rep(m):
        i = m.start()
        left = s[max(0, i - 8):i]
        for k in HYPHEN_KEEP:
            if (left + "-").lower().endswith(k):
                return "-"
        # zapisz do przegladu
        j = i
        while j > 0 and s[j - 1].isalpha():
            j -= 1
        k = m.end()
        while k < len(s) and s[k].isalpha():
            k += 1
        intra_joins[s[j:i] + "|" + s[i + 1:k]] += 1
        return ""
    return INTRA_HYPHEN.sub(rep, s)


def tidy(s):
    """Kosmetyka: spacje w cudzyslowach, gwiazdki, wielokrotne spacje."""
    s = s.replace("\u00a0", " ")
    s = re.sub(r"[ \t]+", " ", s).strip()
    # cudzyslowy: PDF drukuje « tekst », apka pisze «tekst»
    s = re.sub(r"«\s+", "«", s)
    s = re.sub(r"\s+»", "»", s)
    s = s.replace("“ ", "„").replace(" ”", "”").replace("“", "„")
    # gwiazdka/krzyzyk przyklejone do poprzedniego slowa (konwencja czytania.json)
    s = re.sub(r"\s+([*†])", r"\1", s)
    s = re.sub(r"\s+([,.;:!?])", r"\1", s)
    s = re.sub(r"[ \t]+", " ", s).strip()
    return s


def is_rubric(s):
    t = s.strip()
    if not t:
        return True
    if any(h in t for h in RUBRIC_HINTS):
        return True
    if t[0].islower():
        return True
    if len(t) > 75:
        return True
    return False


# ---------------------------------------------------------------------------
# 3. Skladanie tekstu czytania (proza vs poezja)
# ---------------------------------------------------------------------------
INTRO_RE = re.compile(r"^(Czytanie z |Początek |Słowa Ewangelii )")


def join_body(body, rmargin):
    """Laczy linie czytania. Linia pelnej szerokosci = kontynuacja akapitu;
    linia krotsza = koniec akapitu/wers poezji -> lamanie zostaje."""
    out = []
    for ln in body:
        raw = ln.text.rstrip()
        txt = raw.strip()
        if not txt:
            continue
        rm = rmargin.get(ln.page, 0)
        # laczymy gdy poprzednia linia byla pelnej szerokosci ALBO konczy sie
        # lacznikiem przenoszenia wyrazu (zdarza sie tez w krotkich wersach)
        if out and (out[-1][1] or out[-1][0].rstrip().endswith("-")):
            prev = out[-1][0]
            if prev.endswith("-"):
                # przenoszenie wyrazu: sklej bez lacznika gdy dalej mala litera
                if txt[:1].islower():
                    out[-1][0] = prev[:-1] + txt
                else:
                    out[-1][0] = prev + txt
            else:
                out[-1][0] = prev + " " + txt
            out[-1][1] = ln.x1 >= rm - 14
        else:
            out.append([txt, ln.x1 >= rm - 14])
    res = [fix_intra_hyphen(tidy(t)) for t, _ in out if tidy(t)]
    # Formula wprowadzajaca ('Czytanie z Listu swietego Pawla Apostola / do Rzymian.')
    # lamie sie na dwie linie — w apce jest jedna.
    merged = []
    for t in res:
        if (merged and INTRO_RE.match(merged[-1]) and not merged[-1].endswith(".")
                and t[:1].islower()):
            merged[-1] = merged[-1] + " " + t
        else:
            merged.append(t)
    return merged


# ---------------------------------------------------------------------------
# 4. Parsowanie jednej sekcji (marker + zawartosc)
# ---------------------------------------------------------------------------
class Section:
    def __init__(self, kind, variant, year, ref, title, body, page):
        self.kind, self.variant, self.year = kind, variant, year
        self.ref, self.title, self.body, self.page = ref, title, body, page


def classify_marker(t):
    """Zwraca (kind, variant, year) albo None."""
    u = t.upper()
    m = MARKER_RE.match(u)
    if not m:
        return None
    head = m.group(1)
    # Marker jest ZAWSZE wersalikami. Bez tego sprawdzenia tytul perykopy
    # 'Ewangelia szerzy się poza Jerozolimą' udaje marker EWANGELIA
    # i zabiera tekst pierwszego czytania.
    # Wyjatek: w kilku miejscach zecer zlozyl sam marker minuskula ('Ewangelia'),
    # ale wtedy jest to CALA linia — tytul zawsze ma dalszy ciag.
    if not t.startswith(head) and t[m.end():].strip():
        return None
    rest = u[m.end():]
    year = None
    ym = re.search(r"\bW ROKU ([ABC])\b|\bW LATACH ([ABC])\b", rest)
    if ym:
        year = ym.group(1) or ym.group(2)
    variant = None
    if "DŁUŻSZ" in rest:
        variant = "long"
    elif "KRÓTSZ" in rest or "KTÓRSZ" in rest:
        variant = "short"
    if head.startswith("PSALM RE"):
        kind = "psalm"
    elif head == "EWANGELIA":
        kind = "ewangelia"
    elif head in ("ŚPIEW PRZED EWANGELIĄ", "ALLELUJA PRZED EWANGELIĄ"):
        kind = "aklamacja"
    elif head == "SEKWENCJA":
        kind = "sekwencja"
    elif head == "PIERWSZE CZYTANIE":
        kind = "c1"
    elif head == "DRUGIE CZYTANIE":
        kind = "c2"
    else:
        kind = "wigilia"          # 3.-8. czytanie: tylko Wigilia Paschalna
    return kind, variant, year


def marker_tail(t):
    """Odnosnik doklejony do linii markera ('PIERWSZE CZYTANIE Rdz 1, 1')."""
    m = MARKER_RE.match(t.upper())
    tail = t[m.end():].strip() if m else ""
    prev = None
    while prev != tail:
        prev = tail
        tail = re.sub(r"^[\s,—-]*(DŁUŻSZ\w*|KRÓTSZ\w*|KTÓRSZ\w*|W\s+ROKU\s+[ABC]|"
                      r"W\s+LATACH\s+[ABC](\s*i\s*[ABC])?)",
                      "", tail, flags=re.I).strip()
    return tail.lstrip(",—- ").strip()


# ---------------------------------------------------------------------------
# 5. Naglowki dni
# ---------------------------------------------------------------------------
IGNORE_HEADS = (
    "NA DNI POWSZEDNIE", "NA NIEDZIELE I DNI POWSZEDNIE", "UPROSZCZONE TEKSTY",
    "PSALMY RESPONSORYJNE", "REFRENY", "MSZA DO WYBORU", "SKRÓTY BIBLIJNE",
    "WSTĘP", "DEKRET", "WPROWADZIENIE", "OGÓLNY UKŁAD", "SZCZEGÓŁOWY OPIS",
    "OPRACOWANIE POLSKIEGO", "WYKONYWANIE LITURGII", "CZĘŚĆ PIERWSZA",
    "CZĘŚĆ DRUGA", "LEKCJONARZ MSZALNY", "LEKCJONASZ MSZALNY", "OKRES ",
    "PROCESJA Z PALMAMI", "MSZA Z POŚWIĘCENIEM KRZYŻMA", "ŚWIĘTE TRIDUUM",
    "WIGILIA PASCHALNA", "KSIĘGI STAREGO", "KSIĘGI NOWEGO", "INNE SKRÓTY",
    "PRYMAS POLSKI", "EPISKOPATU", "PRZEWODNICZĄCY", "SACRA CONGREGATIO",
    "DIŒCESIUM", "PALLOTTINUM", "POZNAŃ", "TOM ", "WIELKI TYDZIEŃ",
    "ŚWIĘTA KONGREGACJA", "W RÓŻNYCH OKOLICZNOŚCIACH", "PRZYJĘTE W DOBIERANIU",
)

# Druga linia naglowka albo podtytul — nie wolno nimi kasowac stanu dnia
# (np. 'PIĄTY DZIEŃ' + 'W OKTAWIE NARODZENIA PAŃSKIEGO' to jeden naglowek).
NOOP_HEADS = (
    "W OKTAWIE NARODZENIA PAŃSKIEGO", "NARODZENIA PAŃSKIEGO",
    "ZMARTWYCHWSTANIA PAŃSKIEGO", "MSZA RANNA", "CZYLI MĘKI PAŃSKIEJ",
    "W OKRESIE ZWYKŁYM", "UROCZYSTOŚCI PAŃSKIE", "OKRESU ZWYKŁEGO",
    "PRZED WNIEBOWSTĄPIENIEM", "PO WNIEBOWSTĄPIENIU", "DO ŚPIEWANIA",
)

SPECIALS = {
    "ŚRODA POPIELCOWA": "WP Środa Popielcowa",
    "CZWARTEK PO POPIELCU": "WP Czwartek PO Popielcu",
    "PIĄTEK PO POPIELCU": "WP Piątek PO Popielcu",
    "SOBOTA PO POPIELCU": "WP Sobota PO Popielcu",
    "NIEDZIELA PALMOWA": "WP Niedziela Palmowa",
    "WIELKI PONIEDZIAŁEK": "WP Wielki Poniedziałek",
    "WIELKI WTOREK": "WP Wielki Wtorek",
    "WIELKA ŚRODA": "WP Wielka Środa",
    "MSZA WIECZERZY PAŃSKIEJ": "WP Wielki Czwartek",
    "WIELKI PIĄTEK MĘKI PAŃSKIEJ": "WP Wielki Piątek",
    "PONIEDZIAŁEK W OKTAWIE WIELKANOCY": "WK 1 Pon",
    "WTOREK W OKTAWIE WIELKANOCY": "WK 1 Wto",
    "ŚRODA W OKTAWIE WIELKANOCY": "WK 1 Śro",
    "CZWARTEK W OKTAWIE WIELKANOCY": "WK 1 Czw",
    "PIĄTEK W OKTAWIE WIELKANOCY": "WK 1 Pią",
    "SOBOTA W OKTAWIE WIELKANOCY": "WK 1 Sob",
    "WNIEBOWSTĄPIENIE PAŃSKIE": "Wniebowstąpienie Pańskie - ROK {}",
    "OBJAWIENIE PAŃSKIE": "BN Objawienie Pańskie, Uroczystość",
    "ŚWIĘTEJ RODZINY JEZUSA, MARYI I JÓZEFA":
        "BN Świętej Rodziny Jezusa, Maryi i Józefa - Święto",
    "ŚWIĘTEJ BOŻEJ RODZICIELKI MARYI": "BN Świętej Bożej Rodzicielki Maryi, Uroczystość",
    "PIĄTY DZIEŃ": "BN Piąty Dzień Oktawy Narodzenia Pańskiego",
    "SZÓSTY DZIEŃ": "BN Szósty Dzień Oktawy Narodzenia Pańskiego",
    "SIÓDMY DZIEŃ": "BN Siódmy Dzień Oktawy Narodzenia Pańskiego",
    "2 NIEDZIELA PO NARODZENIU PAŃSKIM": "BN 2 Nie {}",
    "CHRZEST CHRYSTUSA": "Chrzest Pański - ROK {}",
    "MSZA WIGILII": "Wigilia Zesłania Ducha Świętego",
}

MIXED_SPECIALS = {
    "Wieczorna Msza wigilijna": "Narodzenie Pańskie - Wieczorna MSZA Wigilijna",
    "Msza w nocy": None,
    "Msza o świcie": None,
    "Msza w dzień": "@CTX",
}


class State:
    def __init__(self):
        self.period = None
        self.week = None
        self.weekday = None
        self.cycle = None       # I / II dla ZW
        self.sunday = None      # A/B/C
        self.special = None     # gotowy klucz (moze byc szablonem z {})
        self.ctx = None         # BN_NAR / EASTER / PENT
        self.last_special = None

    def key(self):
        if self.special is not None:
            return self.special
        if self.sunday and self.period and self.week:
            return "%s %d Nie %s" % (self.period, self.week, self.sunday)
        if self.weekday and self.period and self.week:
            if self.period == "ZW":
                if not self.cycle:
                    return None
                return "ZW %d %s %d" % (self.week, self.weekday, self.cycle)
            return "%s %d %s" % (self.period, self.week, self.weekday)
        return None


def apply_header(st, t, nxt):
    """Zwraca True gdy linia byla naglowkiem dnia/sekcji."""
    u = " ".join(t.upper().split()).rstrip(",")
    raw = " ".join(t.split()).rstrip(",")

    # naglowki dwuliniowe
    two = None
    if nxt:
        two = " ".join((t + " " + nxt).upper().split()).rstrip(",")

    m = re.match(r"^(\d+)\s+NIEDZIELA\s+(ADWENTU|WIELKIEGO POSTU|WIELKANOCNA|ZWYKŁA)"
                 r"\s*,?\s*ROK\s+([ABC])", u)
    if m:
        st.period = {"ADWENTU": "ADW", "WIELKIEGO POSTU": "WP",
                     "WIELKANOCNA": "WK", "ZWYKŁA": "ZW"}[m.group(2)]
        st.week = int(m.group(1))
        st.sunday = m.group(3)
        st.weekday = st.cycle = st.special = None
        return True

    m = re.match(r"^(\d+)\s+TYDZIEŃ\s+ZWYKŁY", u)
    if m:
        st.period = "ZW"
        st.week = int(m.group(1))
        st.weekday = st.cycle = st.sunday = st.special = None
        return True

    m = re.match(r"^(PIERWSZY|DRUGI|TRZECI|CZWARTY|PIĄTY|SZÓSTY|SIÓDMY)\s+TYDZIEŃ"
                 r"\s+(ADWENTU|W\.\s*POSTU|WIELKIEGO POSTU|WIELKANOCNY)", u)
    if m:
        st.period = {"ADWENTU": "ADW", "W. POSTU": "WP", "WIELKIEGO POSTU": "WP",
                     "WIELKANOCNY": "WK"}[m.group(2).replace("W.  POSTU", "W. POSTU")]
        st.week = WEEK_WORDS[m.group(1)]
        st.weekday = st.cycle = st.sunday = st.special = None
        return True

    m = re.match(r"^UROCZYSTOŚĆ\s+NAJŚWIĘTSZEJ\s+TRÓJCY\s*,?\s*ROK\s+([ABC])", u)
    if m:
        st.special = "Najświętszej Trójcy - ROK " + m.group(1)
        st.last_special = st.special
        return True
    m = re.match(r"^UROCZYSTOŚĆ\s+CHRYSTUSA\s+KRÓLA\s*,?\s*ROK\s+([ABC])", u)
    if m:
        st.special = "ZW 34 Nie " + m.group(1)
        st.last_special = st.special
        return True
    if two:
        m = re.match(r"^UROCZYSTOŚĆ\s+NAJŚWIĘTSZEGO\s+CIAŁA\s+I\s+KRWI\s+CHRYSTUSA"
                     r"\s*,?\s*ROK\s+([ABC])", two)
        if m:
            st.special = "Najświętszego Ciała i KRWI Chrystusa - ROK " + m.group(1)
            st.last_special = st.special
            return True
        m = re.match(r"^UROCZYSTOŚĆ\s+NAJŚWIĘTSZEGO\s+SERCA\s+PANA\s+JEZUSA"
                     r"\s*,?\s*ROK\s+([ABC])", two)
        if m:
            st.special = "Najświętszego Serca PANA Jezusa - ROK " + m.group(1)
            st.last_special = st.special
            return True

    if u in WD:
        st.weekday = WD[u]
        st.cycle = None
        st.sunday = None
        st.special = None
        return True

    if u in ("ROK I", "ROK II"):
        st.cycle = 1 if u == "ROK I" else 2
        st.sunday = None
        return True

    m = re.match(r"^(\d+)\s+GRUDNIA$", u)
    if m and raw.endswith("GRUDNIA"):
        n = int(m.group(1))
        st.special = "ADW 24 Grudnia - Wigilia" if n == 24 else "ADW %d Grudnia" % n
        st.last_special = st.special
        return True

    m = re.match(r"^(\d+)\s+STYCZNIA$", u)
    if m and raw.endswith("STYCZNIA"):
        st.special = "BN %d Stycznia" % int(m.group(1))
        st.last_special = st.special
        return True

    for h in NOOP_HEADS:
        if u == h:
            return True

    if raw in MIXED_SPECIALS or u in ("MSZA W DZIEŃ", "MSZA W NOCY", "MSZA O ŚWICIE"):
        v = (MIXED_SPECIALS.get(raw)
             if raw in MIXED_SPECIALS
             else {"MSZA W DZIEŃ": "@CTX"}.get(u))
        if v == "@CTX":
            v = {"BN_NAR": "BN Narodzenie Pańskie",
                 "EASTER": "WK Zmartwychwstanie Pańskie – MSZA W Dzień",
                 "PENT": "Zesłanie Ducha Świętego - ROK {}"}.get(st.ctx)
        st.special = v
        st.last_special = v
        return True

    if u == "NARODZENIE PAŃSKIE":
        st.ctx = "BN_NAR"
        st.special = None
        return True
    if u.startswith("NIEDZIELA WIELKANOCNA"):
        st.ctx = "EASTER"
        st.special = None
        return True
    if u == "NIEDZIELA" or u.startswith("ZESŁANIA DUCHA"):
        if u.startswith("ZESŁANIA DUCHA"):
            st.ctx = "PENT"
        return True

    for k, v in SPECIALS.items():
        if u == k or u.startswith(k + " "):
            st.special = v
            st.last_special = v
            return True

    if u == "MSZA":
        st.special = st.last_special
        return True

    for h in IGNORE_HEADS:
        if u.startswith(h):
            # NIE kasujemy period/week: po 'MSZA DO WYBORU' w Wielkim Poscie
            # wraca zwykly PONIEDZIAŁEK tego samego tygodnia, a bez numeru
            # tygodnia caly 3.-5. tydzien WP wypadal z wyniku.
            st.special = None
            st.weekday = st.cycle = st.sunday = None
            return True
    return False


def is_header_line(ln, t):
    """Kandydat na naglowek dnia: wielkie litery lub znany naglowek mieszany."""
    s = t.strip()
    if not s:
        return False
    if s in MIXED_SPECIALS:
        return True
    letters = [c for c in s if c.isalpha()]
    if not letters or len(s) > 70:
        return False
    if not all(c.isupper() for c in letters):
        return False
    if MARKER_RE.match(s):
        return False
    # Samotne 'KRÓTSZA' / 'DŁUŻSZA' w srodku sekcji to wariant perykopy,
    # nie naglowek dnia — potraktowane jak naglowek urywalo ewangelie niedziel.
    if VARIANT_ONLY.match(s):
        return False
    if len(letters) < 3:
        return False
    return True


# ---------------------------------------------------------------------------
# 6. Budowanie rekordow
# ---------------------------------------------------------------------------
def build_psalm(body):
    """Zamienia linie bloku psalmu w string 'Refren: X\\n\\n<strofa>\\n\\n...'."""
    refren = ""
    strophes = []
    cur = []
    for ln in body:
        s = tidy(ln.text)
        if not s:
            continue
        if re.match(r"^(albo|lub)\s*:", s, re.I):
            continue
        if re.match(r"^Refren\s*[.:]{0,2}\s*$", s):
            if cur:
                strophes.append(cur)
                cur = []
            continue
        # w druku trafia sie 'Refren.:' — bez tego refren ladowal jako strofa
        m = re.match(r"^Refren\s*[.:]{1,2}\s*(.+)$", s)
        if m:
            if not refren:
                refren = fix_intra_hyphen(m.group(1).strip())
            else:
                if cur:
                    strophes.append(cur)
                    cur = []
            continue
        cur.append(fix_intra_hyphen(s))
    if cur:
        strophes.append(cur)
    if not refren and not strophes:
        return ""
    parts = []
    if refren:
        parts.append("Refren: " + refren)
    for st in strophes:
        parts.append("\n".join(st))
    return "\n\n".join(parts)


def build_aklamacja(body):
    head = ""
    verse = []
    seen = 0
    for ln in body:
        s = tidy(ln.text)
        if not s:
            continue
        if re.match(r"^(albo|lub)\s*:", s, re.I):
            continue
        m = re.match(r"^(Aklamacja|Alleluja|Chwała Tobie)\s*:\s*(.*)$", s)
        if m:
            seen += 1
            if seen == 1:
                head = s
            else:
                break
            continue
        if seen >= 1:
            verse.append(fix_intra_hyphen(s))
    if not head and not verse:
        return ""
    out = head if head else "Aklamacja:"
    if verse:
        out += "\n\n" + "\n".join(verse)
    return out


GOSPEL_INTRO = re.compile(r"^Słowa Ewangelii według\b")


def build_reading(title, body_lines, rmargin, gospel=False):
    txt = join_body(body_lines, rmargin)
    if gospel:
        txt = [t for t in txt if not GOSPEL_INTRO.match(t)]
    # utnij wszystko po formule koncowej (wpadal tam podtytul kolejnego dnia)
    for i in range(len(txt) - 1, -1, -1):
        if txt[i].strip() in ("Oto słowo Boże.", "Oto słowo Pańskie."):
            txt = txt[:i + 1]
            break
    parts = []
    if title:
        parts.append(title)
    if txt:
        parts.append("\n".join(txt))
    return "\n\n".join(parts)


class Record:
    def __init__(self, key, page, vol):
        self.key, self.page, self.vol = key, page, vol
        self.date = None
        self.c1 = self.c2 = self.psalm = self.akl = self.seq = None
        self.gospel = None
        self.gospels = {}     # rok -> (ref, tekst)

    def empty(self):
        return not (self.c1 or self.psalm)


def parse_volume(vol, diag):
    lines, rmargin = extract_lines(vol)
    st = State()
    records = []
    cur = None
    # Dzien powszedni okresu zwyklego ma DWA formularze czytan (ROK I i ROK II),
    # ale JEDNA ewangelie i jeden spiew przed ewangelia — drukowane raz, po ROK II.
    # 'group' trzyma rekordy tego samego dnia, zeby rozdac im ewangelie.
    group = []
    i = 0
    n = len(lines)

    def flush():
        nonlocal cur
        if cur and not cur.empty():
            records.append(cur)
        cur = None

    while i < n:
        ln = lines[i]
        t = ln.text.strip()

        if is_header_line(ln, t):
            nxt = lines[i + 1].text.strip() if i + 1 < n else ""
            before = st.key()
            u = " ".join(t.upper().split()).rstrip(",")
            if apply_header(st, t, nxt):
                # Flush takze gdy naglowek USTAWIL TEN SAM klucz: '7 STYCZNIA'
                # wystepuje dwa razy (msza dla krajow z Objawieniem w niedziele
                # i wlasciwa) — bez tego drugi formularz cicho ginal.
                if u not in ("ROK I", "ROK II") and u not in NOOP_HEADS:
                    flush()
                    group = []
                elif st.key() != before:
                    flush()
                i += 1
                continue

        cm = classify_marker(t)
        if cm:
            kind, variant, year = cm
            key = st.key()
            # Drugie PIERWSZE CZYTANIE pod tym samym kluczem to alternatywa
            # ("gdy powyzsze czytanie wykorzystano w niedziele, mozna odczytac...").
            # NIE dzielimy dnia — inaczej psalm i ewangelia laduja w innym rekordzie
            # niz czytanie, a dedup zostawia okrojona polowke.
            if key is None:
                # sekcja poza zakresem (dodatki, wprowadzenie) — przeskocz
                sec, i = read_section(lines, i, rmargin)
                continue
            if cur is None or cur.key != key:
                flush()
                cur = Record(key, ln.page, vol)
                group.append(cur)
            sec, i = read_section(lines, i, rmargin)
            store(cur, kind, variant, year, sec, rmargin, diag)
            if kind in ("ewangelia", "aklamacja"):
                share(group, cur)
            continue
        i += 1
    flush()
    return records


def share(group, src):
    """Rozdaje ewangelie/aklamacje rodzenstwu tego samego dnia (ROK I vs ROK II)."""
    for r in group:
        if r is src:
            continue
        if src.gospel and r.gospel is None and not r.gospels:
            r.gospel = src.gospel
        for y, g in src.gospels.items():
            r.gospels.setdefault(y, g)
        if src.akl and not r.akl:
            r.akl = src.akl


# ---------------------------------------------------------------------------
# 6b. Tom VI — sanktoral (czytania wlasne o swietych)
# ---------------------------------------------------------------------------
MONTHS = {"stycznia": 1, "lutego": 2, "marca": 3, "kwietnia": 4, "maja": 5,
          "czerwca": 6, "lipca": 7, "sierpnia": 8, "września": 9,
          "października": 10, "listopada": 11, "grudnia": 12}
MONTH_HEADS = ("STYCZEŃ", "LUTY", "MARZEC", "KWIECIEŃ", "MAJ", "CZERWIEC",
               "LIPIEC", "SIERPIEŃ", "WRZESIEŃ", "PAŹDZIERNIK", "LISTOPAD",
               "GRUDZIEŃ")
DATE_RE = re.compile(r"^(Również\s+|Albo\s+)?(\d{1,2})\s+(%s)\s*$"
                     % "|".join(MONTHS), re.I)
SAINT_FIRST_PAGE = 46
SAINT_LAST_PAGE = 458          # dalej ida CZYTANIA WSPÓLNE (commons)


def parse_saints(diag):
    lines, rmargin = extract_lines("VI")
    lines = [l for l in lines if SAINT_FIRST_PAGE <= l.page <= SAINT_LAST_PAGE]
    out = []
    cur = None
    title_parts = []
    date = None
    started = False
    i, n = 0, len(lines)

    def flush():
        nonlocal cur
        if cur and not cur.empty() and cur.key:
            out.append(cur)
        cur = None

    while i < n:
        ln = lines[i]
        t = ln.text.strip()
        if ln.size >= 14.9 and t:
            u = " ".join(t.upper().split())
            m = DATE_RE.match(t)
            if m:
                flush()
                title_parts = []
                started = False
                date = "%02d-%02d" % (MONTHS[m.group(3).lower()], int(m.group(2)))
                i += 1
                continue
            if u in MONTH_HEADS:
                flush()
                title_parts = []
                started = False
                i += 1
                continue
            letters = [c for c in t if c.isalpha()]
            if letters and all(c.isupper() for c in letters):
                if started:
                    flush()
                    title_parts = []
                    started = False
                title_parts.append(t)
                i += 1
                continue
        cm = classify_marker(t)
        if cm:
            kind, variant, year = cm
            if not title_parts:
                sec, i = read_section(lines, i, rmargin)
                continue
            if cur is None:
                cur = Record(" ".join(" ".join(title_parts).split()), ln.page, "VI")
                cur.date = date
            started = True
            sec, i = read_section(lines, i, rmargin)
            store(cur, kind, variant, year, sec, rmargin, diag)
            continue
        i += 1
    flush()
    return out


_STOPWORDS_RAW = """św świętego świętej świętych święto święta bł błogosławionej
błogosławionego apostoła apostołów apostoł ewangelisty ewangelista biskupa
biskupów biskup męczennika męczennicy męczenników męczennik dziewicy dziewica
prezbitera prezbiter doktora doktor kościoła kościół opata opat papieża papież
zakonnicy zakonnika zakonnik patrona patronki patronów uroczystość wspomnienie
i oraz z ze na w o towarzyszy królewicza diakona ojca matki panny panna maryi
najświętszej najświętszego najświętsze najśw nmp msza dzień rok mnicha rodziców
"""
# UWAGA: 'wigilia'/'wieczorna' NIE sa stopwordami — inaczej klucz wigilii
# dostaje formularz mszy dnia, co wyglada poprawnie i jest niewykrywalne.


def _flat(s):
    s = s.lower().replace("ł", "l")
    s = "".join(c for c in unicodedata.normalize("NFKD", s)
                if not unicodedata.combining(c))
    return re.sub(r"[^a-z0-9 ]", " ", s)


STOPWORDS = set(_flat(_STOPWORDS_RAW).split())


def stems(s):
    ws = [w for w in _flat(s).split() if w not in STOPWORDS and len(w) > 2]
    return set(w[:6] for w in ws)


def match_saints(saint_recs, app_keys):
    """Przypisuje klucz apki do tytulu z Tomu VI po RDZENIACH slow.
    Wymaga JEDNOZNACZNOSCI — przy dwoch kandydatach nic nie przypisujemy
    (kolekta/czytania cudzego swietego wygladaja poprawnie i sa niewykrywalne)."""
    cand = defaultdict(list)
    for key in app_keys:
        ks = stems(key)
        if not ks:
            continue
        for r in saint_recs:
            ts = stems(r.key)
            if not ts:
                continue
            gap = ks - ts
            # 'Wigilia'/'Wieczorna Msza' rozroznia DWA formularze tego samego
            # swieta — tego rdzenia nie wolno wybaczyc tolerancja.
            if gap & {"wigili", "wieczo"}:
                continue
            miss = len(gap)
            if miss == 0 or (miss == 1 and len(ks) >= 4):
                # drugie kryterium: ile rdzeni tytulu jest NADMIAROWYCH —
                # 'ŚW. JAKUBA, APOSTOŁA' bije 'ŚWIĘTYCH APOSTOŁÓW FILIPA I JAKUBA'
                cand[key].append(((miss, len(ts - ks)), r))
    mapping = {}
    ambiguous = []
    for key, lst in cand.items():
        best = min(s for s, _ in lst)
        top = [r for s, r in lst if s == best]
        if len(top) == 1:
            mapping[key] = top[0]
        else:
            ambiguous.append((key, [r.key for r in top]))
    return mapping, ambiguous


def read_section(lines, i, rmargin):
    """Czyta sekcje zaczynajaca sie markerem w lines[i]."""
    n = len(lines)
    head = lines[i]
    tail = marker_tail(head.text.strip())
    i += 1
    smalls = []
    body = []
    while i < n:
        ln = lines[i]
        t = ln.text.strip()
        if not t:
            i += 1
            continue
        # Marker bywa zlozony 20 pkt (Tom II) — wiekszym niz tekst liturgiczny.
        # Warunek 'mniejszy niz tekst' doklejal wtedy naglowek do czytania.
        if classify_marker(t):
            break
        if is_header_line(ln, t):
            break
        if ln.size < SZ_BODY:
            if not body:
                smalls.append(t)
            i += 1
            continue
        body.append(ln)
        i += 1

    ref = tail
    rest = []
    for s in smalls:
        if not ref and REF_RE.match(s):
            ref = s
            continue
        rest.append(s)
    title_parts = [s for s in rest
                   if not is_rubric(s) and not VARIANT_ONLY.match(s)
                   and not REF_RE.match(s)]
    title = tidy(" ".join(title_parts))
    ref = tidy(ref)
    return Section(None, None, None, ref, title, body, head.page), i


XREF = re.compile(r"^s\.\s*\d|czytań wspólnych|Czytania,\s*s\.|wspomnienia jest własne")


def real_reading(v):
    """Odsiewa 'czytania' bedace ODSYLACZEM do czytan wspolnych
    ('z czytań wspólnych o Apostołach, s. 520') — to nie jest tekst."""
    ref, txt = v
    if len(txt.strip()) < 90:
        return False
    if XREF.search(ref or "") or XREF.search(txt or ""):
        return False
    return True


def store(rec, kind, variant, year, sec, rmargin, diag):
    if variant == "short":
        return  # zawsze bierzemy wersje dluzsza / podstawowa
    if kind == "psalm":
        s = build_psalm(sec.body)
        if s and not rec.psalm:
            rec.psalm = s
            rec.psalm_ref = sec.ref
    elif kind == "aklamacja":
        s = build_aklamacja(sec.body)
        if s and not rec.akl:
            rec.akl = s
    elif kind == "sekwencja":
        s = "\n".join(tidy(l.text) for l in sec.body if tidy(l.text))
        if s and not rec.seq:
            rec.seq = s
    elif kind == "ewangelia":
        txt = build_reading(sec.title, sec.body, rmargin, gospel=True)
        if not txt:
            return
        if not real_reading((sec.ref, txt)):
            return
        if year:
            rec.gospels.setdefault(year, (sec.ref, txt))
        elif rec.gospel is None:
            rec.gospel = (sec.ref, txt)
    elif kind == "c1":
        if rec.c1 is None:
            v = (sec.ref, build_reading(sec.title, sec.body, rmargin))
            rec.c1 = v if real_reading(v) else None
    elif kind == "c2":
        if rec.c2 is None:
            v = (sec.ref, build_reading(sec.title, sec.body, rmargin))
            rec.c2 = v if real_reading(v) else None
    elif kind == "wigilia":
        diag["wigilia_sections"] += 1


# ---------------------------------------------------------------------------
# 7. Rekordy -> JSON
# ---------------------------------------------------------------------------
def okres_of(key):
    for p in ("ADW", "BN", "WP", "WK", "ZW"):
        if key.startswith(p + " ") or key == p:
            return p
    return "US"


KEY_YEAR_RE = re.compile(r"\bNie ([ABC])$|ROK ([ABC])$|[-–,]\s*([ABC])$")


def key_year(key):
    m = KEY_YEAR_RE.search(key)
    return (m.group(1) or m.group(2) or m.group(3)) if m else None


def cykl_of(key, fallback=None):
    y = key_year(key)
    if y:
        return y
    m = re.search(r"\b(Pon|Wto|Śro|Czw|Pią|Sob) ([12])$", key)
    if m:
        return "I" if m.group(2) == "1" else "II"
    return fallback


def to_entries(rec):
    """Zwraca liste wpisow JSON (moze byc kilka: szablon {} lub ewangelie per rok)."""
    keys = []
    if "{}" in rec.key:
        years = sorted(rec.gospels) or ["A", "B", "C"]
        for y in years:
            keys.append((rec.key.format(y), y))
    elif rec.gospels:
        # Klucz apki sam niesie rocznik ('Przemienienie Pańskie - C') — wtedy
        # NIE wolno iterowac po wszystkich latach, bo dedup zostawi pierwszy
        # (rok A) pod kluczem roku C: cudza ewangelia wygladajaca poprawnie.
        ky = key_year(rec.key)
        if ky:
            keys.append((rec.key, ky))
        else:
            for y in sorted(rec.gospels):
                keys.append((rec.key, y))
    else:
        keys.append((rec.key, None))

    out = []
    for key, year in keys:
        if year and year in rec.gospels:
            g = rec.gospels[year]
        else:
            g = rec.gospel or (rec.gospels.get(year) if year else None)
        e = {
            "dzien": key,
            "okres": okres_of(key),
            "cykl": cykl_of(key, year),
            "czytanie_1": {"ref": rec.c1[0], "tekst": rec.c1[1]} if rec.c1 else None,
            "psalm_responsoryjny": rec.psalm or None,
            "czytanie_2": {"ref": rec.c2[0], "tekst": rec.c2[1]} if rec.c2 else None,
            "sekwencja": rec.seq or None,
            "aklamacja": rec.akl or None,
            "ewangelia": {"ref": g[0], "tekst": g[1]} if g else None,
            "zrodlo": "Lekcjonarz Mszalny (stare wydanie), Tom %s, s. %d"
                      % (rec.vol, rec.page),
        }
        out.append(e)
    return out


# ---------------------------------------------------------------------------
# 8. Walidacja
# ---------------------------------------------------------------------------
def norm(s):
    if not s:
        return ""
    s = s.lower()
    s = re.sub(r"[«»„”\"'*†]", " ", s)
    s = re.sub(r"[.,:;!?()\[\]]", " ", s)
    s = "".join(c for c in unicodedata.normalize("NFKD", s)
                if not unicodedata.combining(c))
    return " ".join(s.split())


def norm_ref(r):
    if not r:
        return ""
    r = re.sub(r"\(R\.?:.*?\)", "", r)
    r = re.sub(r"\(\d+\)", "", r)
    r = r.replace("Por.", "").replace("por.", "")
    r = re.sub(r"[\s]+", "", r)
    r = r.replace("–", "-").replace("—", "-")
    return r.lower()


# 1975 drukuje inne skroty ksiag niz wydanie wspolczesne — do porownania
# odnosnikow trzeba je sprowadzic do wspolnego mianownika.
BOOK_ALIAS = {"gal": "ga", "tym": "tm", "tes": "tes", "kor": "kor", "flp": "flp",
              "hebr": "hbr", "efez": "ef", "jak": "jk", "apok": "ap",
              "dzieje": "dz", "pnp": "pnp", "koh": "koh", "syr": "syr"}


def book_of(r):
    r = re.sub(r"\(R\.?:.*?\)", "", r or "")
    m = re.match(r"\s*((?:[1-3]\s*)?[A-ZŚŁŻŹĆŃÓĘĄ][A-Za-ząćęłńóśżź]*)", r)
    if not m:
        return ""
    b = re.sub(r"\s+", "", m.group(1)).lower()
    num = ""
    if b[:1].isdigit():
        num, b = b[0], b[1:]
    b = "".join(c for c in unicodedata.normalize("NFKD", b)
                if not unicodedata.combining(c))
    return num + BOOK_ALIAS.get(b, b)


def first_chapter(r):
    m = re.search(r"(\d+)\s*,", r or "")
    return m.group(1) if m else ""


def ref_key(r):
    return book_of(r) + "|" + first_chapter(r)


CANARY_KEY = "ZW 18 Śro 2"
CANARY_PHRASES = ["W tamtych czasach", "naród ocalały od miecza",
                  "zachowałem dla ciebie łaskawość"]


def main():
    diag = Counter()
    all_recs = []
    per_vol = {}
    for vol in VOLUMES:
        recs = parse_volume(vol, diag)
        per_vol[vol] = len(recs)
        all_recs.extend(recs)

    entries = []
    for r in all_recs:
        entries.extend(to_entries(r))

    # Chrzest Panski = 1 Niedziela Zwykla; apka uzywa OBU rodzin kluczy.
    for e in list(entries):
        m = re.match(r"^Chrzest Pański - ROK ([ABC])$", e["dzien"])
        if m:
            alias = dict(e)
            alias["dzien"] = "ZW 1 Nie " + m.group(1)
            entries.append(alias)
        if e["dzien"] == "Chrzest Pański - ROK B":
            alias = dict(e)
            alias["dzien"] = "Chrzest Pański, ROK B"
            entries.append(alias)
        if e["dzien"] == "WK Zmartwychwstanie Pańskie – MSZA W Dzień":
            for a in ("WK Wielkanoc", "WK Zmartwychwstanie Pańskie – Msza W Dzień"):
                alias = dict(e)
                alias["dzien"] = a
                entries.append(alias)

    # --- Tom VI: sanktoral ---
    have = set(e["dzien"] for e in entries)
    want = set()
    for src in (CZYTANIA_NEW, PSALMY_NEW):
        try:
            want |= set(e["dzien"] for e in json.load(open(src, encoding="utf-8")))
        except Exception:
            pass
    want = {k for k in want - have if okres_of(k) == "US"
            and not k.startswith("Wigilia Paschalna")
            and not re.search(r"\bPs\d+$", k)}
    saint_recs = parse_saints(diag)
    smap, ambiguous = match_saints(saint_recs, want)
    for key, r in sorted(smap.items()):
        r2 = Record(key, r.page, r.vol)
        r2.c1, r2.c2, r2.psalm, r2.akl, r2.seq = r.c1, r.c2, r.psalm, r.akl, r.seq
        r2.gospel, r2.gospels = r.gospel, r.gospels
        entries.extend(to_entries(r2))
    diag["saint_records"] = len(saint_recs)
    diag["saint_mapped"] = len(smap)

    # dedup: dla kluczy datowanych BN wygrywa OSTATNI (pierwszy wariant
    # '7 STYCZNIA' to msza dla krajow z Objawieniem w niedziele), reszta pierwszy
    by_key = {}
    collisions = Counter()
    for e in entries:
        k = (e["dzien"], e["cykl"])
        if k in by_key:
            collisions[e["dzien"]] += 1
            if re.match(r"^BN \d+ Stycznia$", e["dzien"]):
                by_key[k] = e
            continue
        by_key[k] = e
    out = list(by_key.values())
    out.sort(key=lambda e: (e["dzien"], e["cykl"] or ""))

    # ---- kanarek ----
    canary = [e for e in out if e["dzien"] == CANARY_KEY]
    canary_txt = (canary[0]["czytanie_1"] or {}).get("tekst", "") if canary else ""
    canary_hit = [p for p in CANARY_PHRASES if p in canary_txt]
    canary_ok = len(canary_hit) == len(CANARY_PHRASES)

    # ---- porownanie z niezbednikiem (wydanie STARE, przekluczone) ----
    oracle = {}
    try:
        for e in json.load(open(CZYTANIA_STARY, encoding="utf-8")):
            oracle.setdefault(e["dzien"], e)
    except Exception:
        pass

    cmp_n = ref1_ok = ev_ok = psalm_ok = 0
    mism = []
    for e in out:
        o = oracle.get(e["dzien"])
        if not o:
            continue
        cmp_n += 1
        m1 = (e["czytanie_1"] and o.get("czytanie_1")
              and ref_key(e["czytanie_1"]["ref"]) == ref_key(o["czytanie_1"]["ref"]))
        m2 = (e["ewangelia"] and o.get("ewangelia")
              and ref_key(e["ewangelia"]["ref"]) == ref_key(o["ewangelia"]["ref"]))
        op = o.get("psalm_responsoryjny")
        if isinstance(op, dict):
            op = op.get("tytul") or ""
        mr = re.match(r"Refren:\s*([^\n]*)", e["psalm_responsoryjny"] or "")
        mine = norm(mr.group(1)) if mr else ""
        theirs = norm(re.sub(r"^Refren:\s*", "", (op or "").split("\n")[0]))
        m3 = bool(mine) and bool(theirs) and (mine == theirs
                                              or mine in theirs or theirs in mine)
        ref1_ok += bool(m1)
        ev_ok += bool(m2)
        psalm_ok += bool(m3)
        if not m1 or not m2:
            mism.append((e["dzien"],
                         (e["czytanie_1"] or {}).get("ref"),
                         (o.get("czytanie_1") or {}).get("ref"),
                         (e["ewangelia"] or {}).get("ref"),
                         (o.get("ewangelia") or {}).get("ref")))

    # ---- pokrycie ----
    new_keys = set()
    try:
        new_keys = set(e["dzien"] for e in
                       json.load(open(CZYTANIA_NEW, encoding="utf-8")))
    except Exception:
        pass
    ps_keys = set()
    try:
        ps_keys = set(e["dzien"] for e in
                      json.load(open(PSALMY_NEW, encoding="utf-8")))
    except Exception:
        pass
    mine_keys = set(e["dzien"] for e in out)
    missing_cz = sorted(new_keys - mine_keys)
    missing_ps = sorted(ps_keys - mine_keys)

    # ---- kompletnosc pol ----
    field = Counter()
    for e in out:
        for f in ("czytanie_1", "psalm_responsoryjny", "czytanie_2",
                  "aklamacja", "ewangelia"):
            if e[f]:
                field[f] += 1
    per_period = Counter(e["okres"] for e in out)

    suspects = []
    for e in out:
        if not e["czytanie_1"]:
            suspects.append((e["dzien"], "brak czytania_1"))
        elif len(e["czytanie_1"]["tekst"]) < 120:
            suspects.append((e["dzien"], "krótkie czytanie_1 (%d zn.)"
                             % len(e["czytanie_1"]["tekst"])))
        if not e["ewangelia"]:
            suspects.append((e["dzien"], "brak ewangelii"))
        elif len(e["ewangelia"]["tekst"]) < 120:
            suspects.append((e["dzien"], "krótka ewangelia (%d zn.)"
                             % len(e["ewangelia"]["tekst"])))
        if e["psalm_responsoryjny"] and "Refren:" not in e["psalm_responsoryjny"]:
            suspects.append((e["dzien"], "psalm bez refrenu"))
        if not e["psalm_responsoryjny"]:
            suspects.append((e["dzien"], "brak psalmu"))

    # --- jakosc tekstu (gotowosc do projekcji) ---
    qa = Counter()
    qa_ex = defaultdict(list)

    def flag(k, key, s):
        qa[k] += 1
        if len(qa_ex[k]) < 4:
            qa_ex[k].append((key, s))

    for e in out:
        for f in ("czytanie_1", "czytanie_2", "ewangelia"):
            v = e[f]
            if not v:
                continue
            t = v["tekst"]
            qa["_lekcji"] += 1
            if not t.rstrip().endswith(("Oto słowo Boże.", "Oto słowo Pańskie.")):
                flag("bez formuly koncowej", e["dzien"], t[-60:])
            if "  " in t:
                flag("podwojna spacja", e["dzien"], t[:60])
            if re.search(r"[a-ząćęłńóśżź]-\s*$", t, re.M):
                flag("lacznik na koncu linii", e["dzien"], t[:60])
            if re.search(r"\bs\.\s*\d+", t):
                flag("odsylacz do strony w tekscie", e["dzien"], t[:60])
            # 'q' nie wystepuje w polszczyznie — czuly wykrywacz wad warstwy
            # tekstowej PDF ('Koryntiqn' zamiast 'Koryntian')
            mq = re.search(r"[A-Za-ząćęłńóśżź]*q[A-Za-ząćęłńóśżź]*", t)
            if mq:
                flag("litera 'q' (wada zrodla)", e["dzien"], mq.group(0))
        p = e["psalm_responsoryjny"] or ""
        if p:
            qa["_psalmow"] += 1
            if not p.startswith("Refren:"):
                flag("psalm nie zaczyna sie od 'Refren:'", e["dzien"], p[:60])
            elif p.count("\n\n") < 2:
                flag("psalm ma mniej niz 2 strofy", e["dzien"], p[:60])
        a = e["aklamacja"] or ""
        if a and not re.match(r"^(Aklamacja|Alleluja|Chwała)", a):
            flag("aklamacja bez naglowka", e["dzien"], a[:60])

    write_report(out, per_vol, per_period, field, canary_ok, canary_hit, canary_txt,
                 cmp_n, ref1_ok, ev_ok, psalm_ok, mism, missing_cz, missing_ps,
                 new_keys, ps_keys, suspects, collisions, diag, smap, ambiguous,
                 qa, qa_ex)

    if not canary_ok:
        print("BRAMKA: kanarek padl — JSON NIE zapisany. Zob. raport.")
        print("  znalezione frazy:", canary_hit)
        return 1
    if len(out) < 500:
        print("BRAMKA: za malo wpisow (%d) — JSON NIE zapisany." % len(out))
        return 1

    json.dump(out, open(OUT_JSON, "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("OK: %d wpisow -> %s" % (len(out), OUT_JSON))
    print("Kanarek: OK")
    print("Zgodnosc ze starym niezbednikiem: czytanie_1 %d/%d, ewangelia %d/%d, "
          "refren %d/%d" % (ref1_ok, cmp_n, ev_ok, cmp_n, psalm_ok, cmp_n))
    return 0


def write_report(out, per_vol, per_period, field, canary_ok, canary_hit, canary_txt,
                 cmp_n, ref1_ok, ev_ok, psalm_ok, mism, missing_cz, missing_ps,
                 new_keys, ps_keys, suspects, collisions, diag, smap, ambiguous,
                 qa, qa_ex):
    L = []
    A = L.append
    A("=" * 74)
    A("RAPORT: STARY LEKCJONARZ 1975 — CZYTANIA + PSALMY + AKLAMACJE")
    A("=" * 74)
    A("")
    A("PODSUMOWANIE")
    A("  Wpisow w JSON: %d" % len(out))
    A("  Per okres: %s" % dict(per_period))
    A("  Rekordow per tom: %s" % per_vol)
    A("  Wypelnienie pol: %s" % dict(field))
    A("  Ekstrakcja: PyMuPDF (pypdf na tych plikach psuje warstwe tekstowa)")
    A("")
    A("TEST-KANAREK (%s, Jr 31, 1-7)" % CANARY_KEY)
    A("  Wynik: %s" % ("OK" if canary_ok else "PADL"))
    for p in CANARY_PHRASES:
        A("    %s %r" % ("+" if p in canary_hit else "-", p))
    A("  Cytat:")
    for l in (canary_txt or "").split("\n")[:8]:
        A("    " + l)
    A("")
    A("ZGODNOSC Z NIEZBEDNIKIEM (wydanie STARE, tools/czytania_stary.json)")
    if cmp_n:
        A("  Wspolnych kluczy: %d" % cmp_n)
        A("  czytanie_1 (ksiega+rozdzial): %d (%.1f%%)" % (ref1_ok, 100 * ref1_ok / cmp_n))
        A("  ewangelia  (ksiega+rozdzial): %d (%.1f%%)" % (ev_ok, 100 * ev_ok / cmp_n))
        A("  refren psalmu:                %d (%.1f%%)" % (psalm_ok, 100 * psalm_ok / cmp_n))
    else:
        A("  BRAK wspolnych kluczy!")
    A("  Przyklady rozjazdow (moj 1975 | niezbednik-stary):")
    for k, a, b, c, d in mism[:20]:
        A("   --- %s" % k)
        A("       czyt1: %r | %r" % (a, b))
        A("       ewang: %r | %r" % (c, d))
    A("  (rozjazdow lacznie: %d)" % len(mism))
    A("")
    A("POKRYCIE")
    A("  Klucze czytania.json (%d): pokryte %d, BRAK %d"
      % (len(new_keys), len(new_keys) - len(missing_cz), len(missing_cz)))
    A("  Klucze psalmy.json  (%d): pokryte %d, BRAK %d"
      % (len(ps_keys), len(ps_keys) - len(missing_ps), len(missing_ps)))
    A("")
    A("=" * 74)
    A("LISTA BRAKUJACYCH DNI — do doskanowania fizycznego")
    A("=" * 74)
    allmiss = sorted(set(missing_cz) | set(missing_ps))
    groups = defaultdict(list)
    for k in allmiss:
        groups[okres_of(k)].append(k)
    names = {"ADW": "ADWENT", "BN": "NARODZENIE PANSKIE", "WP": "WIELKI POST",
             "WK": "OKRES WIELKANOCNY", "ZW": "OKRES ZWYKLY",
             "US": "SWIETA I SWIECI (Tom VI — nieparsowany)"}
    for p in ("ADW", "BN", "WP", "WK", "ZW", "US"):
        if p not in groups:
            continue
        A("")
        A("--- %s (%d)" % (names[p], len(groups[p])))
        for k in groups[p]:
            src = []
            if k in missing_cz:
                src.append("czytania")
            if k in missing_ps:
                src.append("psalmy")
            A("    %-70s [%s]" % (k, "+".join(src)))
    A("")
    A("=" * 74)
    A("JAKOSC TEKSTU (gotowosc do projekcji)")
    A("=" * 74)
    A("  Przeskanowano lekcji: %d, psalmow: %d" % (qa["_lekcji"], qa["_psalmow"]))
    for k in sorted(qa):
        if k.startswith("_"):
            continue
        A("  %-38s %d" % (k, qa[k]))
        for key, ex in qa_ex[k]:
            A("      %-28s %r" % (key, ex))
    if not [k for k in qa if not k.startswith("_")]:
        A("  Brak zastrzezen.")
    A("")
    A("=" * 74)
    A("PODEJRZANE WPISY (do przegladu)")
    A("=" * 74)
    cnt = Counter(w for _, w in suspects)
    for w, c in cnt.most_common():
        A("  %s: %d" % (w, c))
    A("")
    for k, w in suspects[:60]:
        A("  %-40s %s" % (k, w))
    A("  (lacznie: %d)" % len(suspects))
    A("")
    A("KOLIZJE KLUCZY (ten sam dzien sparsowany >1x)")
    for k, c in collisions.most_common(30):
        A("  %s: +%d" % (k, c))
    A("")
    A("ARTEFAKTY: sklejone laczniki WEWNATRZ linii (%d roznych)" % len(intra_joins))
    A("  (zgubione miekkie laczniki, np. 'wszyst-kich' -> 'wszystkich';")
    A("   lista do przegladu, gdyby ktores bylo realnym zlozeniem)")
    for k, c in intra_joins.most_common(40):
        A("    %s  x%d" % (k.replace("|", "-"), c))
    A("")
    A("=" * 74)
    A("TOM VI — SANKTORAL: przypisanie tytulu obchodu do klucza apki")
    A("=" * 74)
    A("  Sparsowanych formularzy o swietych: %d" % diag["saint_records"])
    A("  Przypisanych do kluczy apki: %d" % diag["saint_mapped"])
    A("  Miara: rdzenie slow (6 znakow), rdzenie klucza musza sie zawrzec")
    A("  w tytule z PDF; przy dwoch kandydatach NIC nie przypisujemy.")
    A("  DO PRZEGLADU OCZAMI — kazda linia to decyzja 'ten swiety = ten klucz':")
    for k, r in sorted(smap.items()):
        A("    %-62s <- s.%-4d %s" % (k, r.page, r.key))
    if ambiguous:
        A("  NIEJEDNOZNACZNE (pominiete):")
        for k, lst in ambiguous:
            A("    %-50s ? %s" % (k, " | ".join(lst)))
    A("")
    A("POMINIETE SWIADOMIE")
    A("  - Wigilia Paschalna (7 czytan + epistola: nie miesci sie w formacie")
    A("    czytanie_1/czytanie_2); sekcji 3.-8. czytania: %d" % diag["wigilia_sections"])
    A("  - Msza z poswieceniem Krzyzma, Msza w nocy / o swicie (Boze Narodzenie)")
    A("  - dodatki 'NA DNI POWSZEDNIE...' (alternatywne spiewy przed ewangelia)")
    A("  - 'MSZA DO WYBORU' w Wielkim Poscie (formularze roku A na dni powszednie)")
    A("  - Tom VI (swieci) — osobna tura")
    open(REPORT, "w", encoding="utf-8").write("\n".join(L))


if __name__ == "__main__":
    sys.exit(main())
