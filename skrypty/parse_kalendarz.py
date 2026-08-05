# -*- coding: utf-8 -*-
"""Parser: Kalendarz diecezji polskich (PDF) -> JSON ze strukturą rang i diecezji."""
import re, json
from pypdf import PdfReader

PDF = r"D:\Pobrane\kalendarz diecezji polskich-7.10.2023.pdf"
# obie używane kopie danych (Pilot Android + desktop Cantio jako EmbeddedResource)
OUTS = [
    r"C:\Users\konta\AndroidStudioProjects\CantioPilot\app\src\main\assets\kalendarz_diecezji.json",
    r"C:\Users\konta\source\repos\Cantio\Cantio.Core\Assets\Data\kalendarz_diecezji.json",
]
# obchody ruchome NIE trafiają do assetu (patrz MOVABLE_HEADER_RE) — ten plik jest wyłącznie
# materiałem referencyjnym dla kodu kalendarza, żaden komponent go nie czyta
OUT_MOVABLE = r"C:\Users\konta\source\repos\Cantio\skrypty\kalendarz_ruchome.json"

# miejscownik (z PDF) -> mianownik kanoniczny (do selektora diecezji w apce)
DIECEZJE_MAP = {
    "białostockiej": "białostocka", "bielsko-żywieckiej": "bielsko-żywiecka", "bydgoskiej": "bydgoska",
    "częstochowskiej": "częstochowska", "drohiczyńskiej": "drohiczyńska", "elbląskiej": "elbląska",
    "ełckiej": "ełcka", "gdańskiej": "gdańska", "gliwickiej": "gliwicka", "gnieźnieńskiej": "gnieźnieńska",
    "kaliskiej": "kaliska", "katowickiej": "katowicka", "kieleckiej": "kielecka",
    "koszalińsko-kołobrzeskiej": "koszalińsko-kołobrzeska", "krakowskiej": "krakowska", "legnickiej": "legnicka",
    "lubelskiej": "lubelska", "opolskiej": "opolska", "pelplińskiej": "pelplińska", "poznańskiej": "poznańska",
    "przemyskiej": "przemyska", "płockiej": "płocka", "radomskiej": "radomska", "rzeszowskiej": "rzeszowska",
    "sandomierskiej": "sandomierska", "siedleckiej": "siedlecka", "sosnowieckiej": "sosnowiecka",
    "szczecińsko-kamieńskiej": "szczecińsko-kamieńska", "tarnowskiej": "tarnowska", "toruńskiej": "toruńska",
    "warmińskiej": "warmińska", "warszawskiej": "warszawska", "warszawsko-praskiej": "warszawsko-praska",
    "wrocławskiej": "wrocławska", "włocławskiej": "włocławska", "zamojsko-lubaczowskiej": "zamojsko-lubaczowska",
    "zielonogórsko-gorzowskiej": "zielonogórsko-gorzowska", "łomżyńskiej": "łomżyńska", "łowickiej": "łowicka",
    "łódzkiej": "łódzka", "świdnickiej": "świdnicka",
    "Ordynariacie Polowym Wojska Polskiego": "Ordynariat Polowy Wojska Polskiego",
}
UNKNOWN_DIEC = set()

# metropolie (miejscownik z PDF) -> diecezje wchodzące w skład metropolii
METROPOLIE_MAP = {
    "warszawskiej":    ["warszawska", "warszawsko-praska", "płocka"],
    "katowickiej":     ["katowicka", "gliwicka", "opolska"],
    "częstochowskiej": ["częstochowska", "radomska", "sosnowiecka"],
    "białostockiej":   ["białostocka", "drohiczyńska", "łomżyńska"],
    "wrocławskiej":    ["wrocławska", "legnicka", "świdnicka"],
}

# Podział administracyjny państwa nie pokrywa się z kościelnym — PRZYBLIŻENIE:
# diecezje, których terytorium leży (w całości lub w części) w danym województwie.
# Świadomie hojne: lepiej pokazać obchód w diecezji sąsiadującej niż ukryć go parafiom,
# które realnie go obchodzą. Oryginalny nagłówek zostaje w polu `zakres_raw`.
WOJEWODZTWA_MAP = {
    "kujawsko-pomorskiego": ["bydgoska", "toruńska", "włocławska", "pelplińska",
                             "gnieźnieńska", "płocka"],
    "małopolskiego":        ["krakowska", "tarnowska", "kielecka", "sosnowiecka",
                             "bielsko-żywiecka", "rzeszowska"],
}
WOJ_UWAGA = ("Obchód patrona województwa — lista diecezji jest przybliżeniem terytorium "
             "(podział administracyjny państwa nie pokrywa się z kościelnym)")

# Kanoniczna lista diecezji — musi zgadzać się z DiocesanCalendarRepository.DIECEZJE (Pilot).
DIECEZJE_KANON = {
    "białostocka", "bielsko-żywiecka", "bydgoska", "częstochowska", "drohiczyńska",
    "elbląska", "ełcka", "gdańska", "gliwicka", "gnieźnieńska", "kaliska", "katowicka",
    "kielecka", "koszalińsko-kołobrzeska", "krakowska", "legnicka", "lubelska", "łomżyńska",
    "łowicka", "łódzka", "opolska", "pelplińska", "płocka", "poznańska", "przemyska",
    "radomska", "rzeszowska", "sandomierska", "siedlecka", "sosnowiecka",
    "szczecińsko-kamieńska", "świdnicka", "tarnowska", "toruńska", "warmińska", "warszawska",
    "warszawsko-praska", "włocławska", "wrocławska", "zamojsko-lubaczowska",
    "zielonogórsko-gorzowska", "Ordynariat Polowy Wojska Polskiego",
}

# Nagłówek zakresu: „W archidiecezji…", „W diecezji…", „W metropolii…",
# „W Ordynariacie Polowym…", „W parafiach należących do województwa…".
SCOPE_RE = re.compile(r"^[Ww]e?\s+(archidiecezj|diecezj|metropoli|Ordynariacie|parafiach)")

MONTHS = {
    "STYCZEŃ": 1, "LUTY": 2, "MARZEC": 3, "KWIECIEŃ": 4, "MAJ": 5, "CZERWIEC": 6,
    "LIPIEC": 7, "SIERPIEŃ": 8, "WRZESIEŃ": 9, "PAŹDZIERNIK": 10, "LISTOPAD": 11, "GRUDZIEŃ": 12,
}

# kolejność ważna: dłuższe/bardziej szczegółowe najpierw
RANKS = [
    ("Wsp. obowiązkowe", "wsp_obowiazkowe"),
    ("Wsp. dowolne",     "wsp_dowolne"),
    ("Uroczystość",      "uroczystosc"),
    ("Święto",           "swieto"),
]

# ── obchody ruchome ──────────────────────────────────────────────────────────
# PDF drukuje je w bloku BEZ numeru dnia, pod nagłówkiem opisującym dzień liczony od Wielkanocy
# lub od niedzieli („Piątek po drugiej niedzieli po Zesłaniu Ducha Świętego:”, „7 Niedziela
# Wielkanocy:”, „Ostatnia niedziela zwykła:”). Bloki takie stoją zwykle NA KOŃCU miesiąca,
# więc bez rozpoznania nagłówka parser przypisywał je do ostatniego wydrukowanego dnia —
# 30 VI dostawało co roku uroczystość Najświętszego Serca Pana Jezusa, a 31 V Wniebowstąpienie
# (uroczystość wypiera dzień powszedni, więc stawała się DOMYŚLNYM formularzem dnia).
#
# DECYZJA: takie obchody NIE trafiają do assetu. Asset jest kluczowany przez MM-DD i nie ma
# w nim reprezentacji daty ruchomej — każdy „znacznik” i tak musiałby siedzieć pod zmyśloną
# datą, a konsumenci (DiocesanCalendarService, DiocesanCalendarRepository) czytają po dacie.
# Daty ruchome liczy kod kalendarza. Pełna lista trafia do OUT_MOVABLE do wglądu.
#
# Zbiór wzorców ustalony z danych — WSZYSTKIE nagłówki ruchome w PDF 7.10.2023:
#   Niedziela po 6 stycznia | Niedziela w oktawie Wielkanocy | 7 Niedziela Wielkanocy
#   Poniedziałek/Czwartek po Zesłaniu Ducha Świętego | Pierwsza niedziela po Zesłaniu…
#   Czwartek po Najświętszej Trójcy | Piątek/Sobota po drugiej niedzieli po Zesłaniu…
#   Ostatnia niedziela października | Ostatnia niedziela zwykła
#   Niedziela w oktawie Narodzenia Pańskiego.        (kropka zamiast dwukropka)
_DZIEN_TYG = r"(?:Niedziela|Poniedziałek|Wtorek|Środa|Czwartek|Piątek|Sobota)"
_LICZEBNIK = (r"(?:\d{1,2}|Pierwsza|Druga|Trzecia|Czwarta|Piąta|Szósta|Siódma|Ósma|Ostatnia)")
MOVABLE_HEADER_RE = re.compile(
    rf"^(?:{_LICZEBNIK}\s+)?{_DZIEN_TYG}\b[^:]*[:.]$", re.IGNORECASE)

# Ten sam kształt sprawdzany na TYTULE gotowego wpisu — łapie regresję, gdyby nagłówek
# ruchomy przeciekł do tytułu inną drogą (tak wygląda dziś łatka w Pilocie).
MOVABLE_TITLE_RE = re.compile(rf"^(?:\d+\s+)?{_DZIEN_TYG}\b[^:]*:")

# Obchody, o których WIEMY, że są ruchome: żaden nie ma prawa wyjść jako wpis datowany,
# nawet jeśli jego tytuł nie niesie opisu dnia (diecezjalna „NMP MATKI KOŚCIOŁA, głównej
# patronki…” stała pod nagłówkiem „Poniedziałek po Zesłaniu” i przeciekała bez opisu dnia).
MOVABLE_PHRASES = (
    "MATKI KOŚCIOŁA", "SERCA PANA JEZUSA", "SERCA NAJŚW. MARYI PANNY", "WNIEBOWSTĄPIENIE",
    "WIECZNEGO KAPŁANA", "CIAŁA I KRWI CHRYSTUSA", "NAJŚWIĘTSZEJ TRÓJCY",
    "KRÓLA WSZECHŚWIATA", "CHRZEST PAŃSKI", "MIŁOSIERDZIA BOŻEGO",
    "ŚWIĘTEJ RODZINY", "KOŚCIOŁA WŁASNEGO",
)


def clean(s: str) -> str:
    # usuń glify z prywatnego obszaru Unicode (artefakty fontów)
    return "".join(ch for ch in s if not (0xE000 <= ord(ch) <= 0xF8FF))


def find_rank(text: str):
    t = text.rstrip()
    for kw, code in RANKS:
        if t.endswith(kw):
            return t[:-len(kw)].rstrip(), code
    return None


def _names(chunk: str):
    """'białostockiej oraz drohiczyńskiej, łomżyńskiej i radomskiej' -> ['białostockiej', ...]"""
    return [p.strip() for p in re.split(r"\boraz\b|\bi\b|,", chunk) if p.strip()]


def parse_dioceses(raw: str):
    """Nagłówek zakresu -> lista kanonicznych nazw diecezji.

    Obsługiwane formy (mieszane, sumowane):
      'W archidiecezji białostockiej i warszawskiej oraz diecezji kieleckiej:'
      'W metropolii białostockiej oraz diecezji drohiczyńskiej, łomżyńskiej i radomskiej:'  (metropolia rozwijana)
      'W metropolii katowickiej i wrocławskiej:'                                            (dwie metropolie)
      'W Ordynariacie Polowym Wojska Polskiego:'
      'W parafiach należących do województwa kujawsko-pomorskiego i małopolskiego:'         (przybliżenie)
    """
    s = raw.strip().rstrip(":").strip()
    s = re.sub(r"^[Ww]e?\s+", "", s)
    out = []

    def add(name):
        if name not in out:
            out.append(name)

    if re.match(r"^Ordynariacie\s+Polowym", s):
        add("Ordynariat Polowy Wojska Polskiego")
        return out

    if s.startswith("parafiach"):
        hit = False
        for woj, diec in WOJEWODZTWA_MAP.items():
            if woj in s:
                hit = True
                for d in diec:
                    add(d)
        if not hit:
            UNKNOWN_DIEC.add(s)
        return out

    # znaczniki trybu: @D@ = dalej idą diecezje, @M@ = dalej idą metropolie
    s = re.sub(r"\b(?:archidiecezj|diecezj)(?:i|ach)\b", "@D@", s)
    s = re.sub(r"\bmetropoli(?:i|ach)\b", "@M@", s)
    mode = "D"
    for chunk in re.split(r"(@D@|@M@)", s):
        if chunk in ("@D@", "@M@"):
            mode = chunk[1]
            continue
        for p in _names(chunk):
            if mode == "M":
                if p in METROPOLIE_MAP:
                    for d in METROPOLIE_MAP[p]:
                        add(d)
                else:
                    UNKNOWN_DIEC.add("metropolia: " + p)
            elif p in DIECEZJE_MAP:
                add(DIECEZJE_MAP[p])
            else:
                UNKNOWN_DIEC.add(p)
                add(p)  # zachowaj surowe, ale zgłoś do przeglądu
    return out


def main():
    reader = PdfReader(PDF)
    lines = []
    for idx, page in enumerate(reader.pages):
        txt = clean(page.extract_text() or "")
        plines = txt.split("\n")
        j = 0
        while j < len(plines) and plines[j].strip() == "":
            j += 1
        # zrzuć wiodący numer strony (drukowany numer = idx+1, strona 1 = tytułowa bez numeru)
        if j < len(plines) and plines[j].strip() == str(idx + 1):
            del plines[j]
        lines.extend(plines)

    entries = []   # datowane obchody
    movable = []   # obchody ruchome (bez daty)
    review = []    # linie bez rozpoznanej rangi (do ręcznego przeglądu)

    month = None
    day = None
    scope = None          # None => powszechny; inaczej (raw, [diecezje])
    scope_accum = None    # akumulator wieloliniowego nagłówka zakresu
    buf = []              # bufor bieżącego obchodu
    # Blok ruchomy jest stanem NIEZALEŻNYM od zakresu diecezjalnego — trzymanie obu w jednej
    # zmiennej (dawne `scope = "RUCHOME"`) powodowało, że nagłówek diecezjalny WEWNĄTRZ bloku
    # ruchomego wypychał obchód z powrotem do wpisów datowanych (tak diecezjalna „NMP MATKI
    # KOŚCIOŁA, głównej patronki…” lądowała na 31 V), a kolejny nagłówek ruchomy dziedziczył
    # cudzy zakres diecezjalny (święto Jezusa Chrystusa Najwyższego Kapłana — powszechne w PL —
    # wychodziło jako obchód trzech diecezji).
    movable_desc = [None]  # opis dnia ruchomego albo None; lista, bo domknięcie w flush()

    def flush(target_scope):
        nonlocal buf
        if not buf:
            return
        raw = re.sub(r"\s+", " ", " ".join(x.strip() for x in buf)).strip()
        buf = []
        if not raw:
            return
        raw = raw.replace("Wsp. odowolne", "Wsp. dowolne")  # literówka w źródłowym PDF
        r = find_rank(raw)
        if not r:
            # Wspomnienie Wszystkich Wiernych Zmarłych (2 XI) – w PDF bez standardowej etykiety rangi
            if "WSZYSTKICH WIERNYCH ZMAR" in raw.upper() and movable_desc[0] is None:
                entries.append({
                    "data": "%02d-%02d" % (month, day) if day else None,
                    "tytul": raw.strip(), "ranga": "wsp_obowiazkowe", "zakres": "powszechny",
                    "uwaga": "Wspomnienie Wszystkich Wiernych Zmarłych – pierwszeństwo przed niedzielą zwykłą",
                })
                return
            review.append((month, day, raw))
            return
        title, code = r
        title = re.sub(r"^Oktawa Narodzenia Pańskiego\s+", "", title)  # opis temporalny 1 I
        is_movable = movable_desc[0] is not None
        # data ruchoma nie istnieje w formacie assetu — wpis ruchomy NIGDY jej nie dostaje
        rec = {"data": None if is_movable else ("%02d-%02d" % (month, day) if day else None),
               "tytul": title, "ranga": code}
        if target_scope is None:
            rec["zakres"] = "powszechny"
        else:
            rec["zakres"] = "diecezja"
            rec["zakres_raw"] = target_scope[0]
            rec["diecezje"] = target_scope[1]
            if re.match(r"^[Ww]e?\s+parafiach", target_scope[0]):
                rec["uwaga"] = WOJ_UWAGA
        if is_movable:
            movable.append({"miesiac": month, "opis": movable_desc[0], **rec})
        else:
            entries.append(rec)

    for raw_line in lines:
        line = raw_line.rstrip()
        s = line.strip()
        if s == "":
            continue

        # nagłówek miesiąca
        if s in MONTHS:
            flush(scope)
            month = MONTHS[s]; day = 0
            scope = None; scope_accum = None; movable_desc[0] = None
            continue
        if month is None:
            continue  # strona tytułowa / wstęp

        # domknięcie wieloliniowego nagłówka zakresu diecezjalnego
        if scope_accum is not None:
            scope_accum += " " + s
            if s.endswith(":"):
                scope = (re.sub(r"\s+", " ", scope_accum).strip(), parse_dioceses(scope_accum))
                scope_accum = None
            continue

        # nagłówek dnia ruchomego — MUSI być sprawdzony przed linią dnia i przed nagłówkiem
        # zakresu, bo otwiera nowy blok i kasuje zakres diecezjalny poprzedniego bloku
        if MOVABLE_HEADER_RE.match(s):
            flush(scope)
            movable_desc[0] = s.rstrip(":.").strip()
            scope = None
            continue

        # linia dnia: wiodący numer == day+1 (każdy dzień jest wydrukowany, także pusty)
        m = re.match(r"^\s*(\d{1,2})(?=\s|$)", line)
        if m and int(m.group(1)) == (day or 0) + 1 and int(m.group(1)) <= 31:
            flush(scope)
            day = int(m.group(1)); scope = None; movable_desc[0] = None
            rest = line[m.end():].strip()
            if rest:
                # numer dnia może stać w jednej linii z nagłówkiem diecezjalnym (" 5 W diecezji łowickiej:")
                if SCOPE_RE.match(rest):
                    scope_accum = rest
                    if rest.endswith(":"):
                        scope = (re.sub(r"\s+", " ", scope_accum).strip(), parse_dioceses(scope_accum))
                        scope_accum = None
                else:
                    buf.append(rest)
                    if find_rank(rest):
                        flush(scope)
            continue

        # nagłówek zakresu diecezjalnego
        if SCOPE_RE.match(s):
            flush(scope)
            scope_accum = s
            if s.endswith(":"):
                scope = (re.sub(r"\s+", " ", scope_accum).strip(), parse_dioceses(scope_accum))
                scope_accum = None
            continue

        # zwykła linia obchodu (start lub kontynuacja)
        buf.append(s)
        if find_rank(s):
            flush(scope)

    flush(scope)

    # ── ręczne uzupełnienia: obchody nieobecne w PDF-ie 7.10.2023, ale obecne w mszale ──
    # (ma to znaczenie tylko wtedy, gdy obchód realnie ma formularz w `baza_mszal.json` —
    # inaczej wpis w kalendarzu wisiałby bez treści). Dopisywane po parsowaniu, żeby nie
    # majstrować przy logice PDF-a. Tytuł musi zgadzać się z tytułem formularza na tyle,
    # żeby dopasowanie po rdzeniach (MassProperRepository, próg 0,6) je połączyło.
    SUPPLEMENTS = [
        {
            "data": "10-10", "tytul": "Św. Jana Leonardiego, prezbitera",
            "ranga": "wsp_dowolne", "zakres": "powszechny",
            "uwaga": "Brak w PDF-ie 7.10.2023 (luka źródła) — dopisane ręcznie, formularz "
                     "jest w baza_mszal.json pod kluczem 10-10.",
        },
    ]
    for sup in SUPPLEMENTS:
        entries.append(sup)

    # ── walidacja PRZED zapisem (lepiej brak zapisu niż zepsute dane w apce) ──
    problems = []
    for e in entries:
        if re.match(r"^[Ww]e?\s", e["tytul"]):
            problems.append(f"[{e['data']}] prefiks zakresu został w tytule: {e['tytul'][:80]}")
        if e["zakres"] == "diecezja" and not e.get("diecezje"):
            problems.append(f"[{e['data']}] zakres=diecezja z pustą listą diecezji: {e['tytul'][:60]}")
        for d in e.get("diecezje", []):
            if d not in DIECEZJE_KANON:
                problems.append(f"[{e['data']}] nazwa spoza kanonicznej listy: {d!r}")
        # ── obchód ruchomy zaparkowany pod datą (regresja z 7.10.2023) ──
        # Uroczystość wypiera dzień powszedni, więc taki wpis podmienia DOMYŚLNY formularz dnia
        # w każdym roku — objaw: „30 VI: NAJŚWIĘTSZEGO SERCA PANA JEZUSA”.
        if MOVABLE_TITLE_RE.match(e["tytul"]):
            problems.append(f"[{e['data']}] obchód RUCHOMY jako wpis datowany "
                            f"(opis dnia w tytule): {e['tytul'][:80]}")
        up = e["tytul"].upper()
        for ph in MOVABLE_PHRASES:
            if ph in up:
                problems.append(f"[{e['data']}] obchód RUCHOMY jako wpis datowany "
                                f"(fraza {ph!r}): {e['tytul'][:80]}")
                break
    if UNKNOWN_DIEC:
        problems.append("nierozpoznane nazwy w nagłówkach zakresu: " + repr(sorted(UNKNOWN_DIEC)))
    if problems:
        print("!!! WALIDACJA NIEUDANA — NIC NIE ZAPISANO:")
        for p in problems:
            print("   ", p)
        raise SystemExit(1)

    for out in OUTS:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(entries, f, ensure_ascii=False, indent=2)
        print("ZAPISANO:", out)

    # materiał referencyjny — poza aplikacjami; obchody ruchome liczy kod kalendarza
    with open(OUT_MOVABLE, "w", encoding="utf-8") as f:
        json.dump(movable, f, ensure_ascii=False, indent=2)
    print("ZAPISANO (referencyjnie, poza apkami):", OUT_MOVABLE)

    from collections import Counter
    by_rank = Counter(e["ranga"] for e in entries)
    by_month = Counter(e["data"][:2] for e in entries if e["data"])
    diec = sum(1 for e in entries if e["zakres"] == "diecezja")
    print("Datowane obchody:", len(entries), "| diecezjalne:", diec, "| powszechne:", len(entries) - diec)
    print("Rangi:", dict(by_rank))
    print("Ruchome (NIE w asecie):", len(movable))
    for m in movable:
        print(f"    [{m['opis']}] {m['tytul'][:70]} ({m['ranga']}, {m['zakres']})")
    print("Wg miesiąca:", dict(sorted(by_month.items())))
    print("--- BEZ RANGI (review):", len(review))
    for mth, dy, txt in review[:40]:
        print(f"  [{mth:>2}-{str(dy):>2}] {txt[:90]}")
    print("--- NIEZNANE DIECEZJE (do mapy):", sorted(UNKNOWN_DIEC))


if __name__ == "__main__":
    main()
