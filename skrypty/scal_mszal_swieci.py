# -*- coding: utf-8 -*-
"""Scalanie scrape'u formularzy mszalnych o świętych z `baza_mszal.json` (asset Pilota).

CO ROBI: dokłada BRAKUJĄCE formularze świętych do jedynego źródła prawdy mszału
(`CantioPilot/app/src/main/assets/baza_mszal.json`). Istniejących wpisów NIE RUSZA —
ani tekstów, ani tytułów, ani kluczy. Skrypt jest re-uruchamialny i IDEMPOTENTNY:
drugie uruchomienie na tym samym wejściu nie dodaje niczego (dopasowanie tytułów
działa tak samo na tytule „czystym”, jak i na tytule z prefiksem daty).

PO CO ISTNIEJE: scrape będzie POWTARZANY — dzisiejsze wejście ma tylko miesiące
01–08 i 10 (brak IX, XI, XII). Po dograniu reszty wystarczy podmienić plik wejściowy
i uruchomić skrypt ponownie.

WALIDACJA PRZED ZAPISEM (jak w `parse_kalendarz.py`): cokolwiek nie gra → exit 1
i ŻADNEGO zapisu. Sprawdzane: parsowalność wyniku, NIETKNIĘTOŚĆ starych wpisów
(porównanie serializacji co do znaku), nazwy diecezji z kanonicznej listy kalendarza,
kształt kluczy `dzien`.

ŹRÓDŁO ZAKRESU DIECEZJALNEGO = KALENDARZ, nie proza scrape'u. Pole `wprowadzenie`
w wejściu jest BIOGRAFIĄ świętego; wzmianka o diecezji znaczy tam zwykle „tu pracował”
albo „tu ma wyższą rangę”, a NIE „ten formularz obowiązuje wyłącznie tam”. Naiwne
szukanie nazw diecezji w tym polu robi z Jana Pawła II obchód wyłącznie krakowski.
Autorytetem jest `kalendarz_diecezji.json` (te same pola `zakres`/`diecezje`).

ZNANE OGRANICZENIE FORMATU (świadomy kompromis, nie przeoczenie): format wpisu potrafi
DODAĆ formularz w diecezji, ale nie potrafi go WYGASIĆ tam, gdzie się nie stosuje.
Gdy obchód jest w jednej diecezji przeniesiony na inną datę (Reguła 3 niżej), emitujemy
DODATKOWY wpis pod datą zastępczą, zostawiając bazowy pod datą pierwotną — więc np.
w archidiecezji gdańskiej św. Maksymilian Kolbe będzie widoczny i 14, i 18 sierpnia.
"""
import json
import re
import sys
import unicodedata
from collections import Counter

IN_SCRAPE = r"C:\Users\konta\source\repos\Cantio\tools\mszal-swieci.json"
ASSET = r"C:\Users\konta\AndroidStudioProjects\CantioPilot\app\src\main\assets\baza_mszal.json"
KALENDARZ = r"C:\Users\konta\AndroidStudioProjects\CantioPilot\app\src\main\assets\kalendarz_diecezji.json"

# Kanoniczna lista diecezji — MUSI zgadzać się z DiocesanCalendarRepository.DIECEZJE (Pilot);
# pilnuje tego test MassProperDioceseTest, który po scaleniu musi zostać zielony.
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

# miejscownik (jak w nagłówkach scrape'u i PDF kalendarza) -> mianownik kanoniczny
DIECEZJE_MIEJSC = {
    "białostockiej": "białostocka", "bielsko-żywieckiej": "bielsko-żywiecka", "bydgoskiej": "bydgoska",
    "częstochowskiej": "częstochowska", "drohiczyńskiej": "drohiczyńska", "elbląskiej": "elbląska",
    "ełckiej": "ełcka", "gdańskiej": "gdańska", "gliwickiej": "gliwicka", "gnieźnieńskiej": "gnieźnieńska",
    "kaliskiej": "kaliska", "katowickiej": "katowicka", "kieleckiej": "kielecka",
    "koszalińsko-kołobrzeskiej": "koszalińsko-kołobrzeska", "krakowskiej": "krakowska",
    "legnickiej": "legnicka", "lubelskiej": "lubelska", "opolskiej": "opolska",
    "pelplińskiej": "pelplińska", "poznańskiej": "poznańska", "przemyskiej": "przemyska",
    "płockiej": "płocka", "radomskiej": "radomska", "rzeszowskiej": "rzeszowska",
    "sandomierskiej": "sandomierska", "siedleckiej": "siedlecka", "sosnowieckiej": "sosnowiecka",
    "szczecińsko-kamieńskiej": "szczecińsko-kamieńska", "tarnowskiej": "tarnowska",
    "toruńskiej": "toruńska", "warmińskiej": "warmińska", "warszawskiej": "warszawska",
    "warszawsko-praskiej": "warszawsko-praska", "wrocławskiej": "wrocławska",
    "włocławskiej": "włocławska", "zamojsko-lubaczowskiej": "zamojsko-lubaczowska",
    "zielonogórsko-gorzowskiej": "zielonogórsko-gorzowska", "łomżyńskiej": "łomżyńska",
    "łowickiej": "łowicka", "łódzkiej": "łódzka", "świdnickiej": "świdnicka",
}

MIESIACE = {
    "stycznia": 1, "lutego": 2, "marca": 3, "kwietnia": 4, "maja": 5, "czerwca": 6,
    "lipca": 7, "sierpnia": 8, "września": 9, "października": 10, "listopada": 11, "grudnia": 12,
}

# Nagłówek zakresu z datą zastępczą: „W archidiecezji gdańskiej: 18 sierpnia”.
SCOPE_ALT_DATE_RE = re.compile(
    r"^[Ww]e?\s+(?:archi)?diecezji\s+([\wąćęłńóśżź-]+)\s*:\s*(\d{1,2})\s+(\w+)", re.UNICODE)

# ── Czyszczenie tytułu ze scrape'u ───────────────────────────────────────────
# Scrape doklejał do tytułu rubrykę ze strony źródłowej: rangę i nagłówek zakresu
# („Bł. Bolesławy Marii Lament, dziewicy Wspomnienie dowolne W archidiecezji
# białostockiej:”, „Św. Zygmunta, króla i męczennika W Płocku: Głównego patrona
# miasta -”). Ogon trzeba uciąć, bo tytuł jest i wyświetlany, i użyty przez
# `MassProperRepository.detectCommonCategory` do doboru Mszy wspólnej.
# UWAGA: informacja o diecezji z tego ogona jest ŚWIADOMIE wyrzucana — zakres bierzemy
# z kalendarza (Reguła 2), a nie z prozy scrape'u.
TITLE_TAIL_RE = re.compile(
    r"\s+(?:"
    r"[Ww]spomnienie\b"
    r"|Święto\b|Uroczystość\b"
    r"|[Ww]e?\s+(?:archidiecezji|diecezji|metropolii|Ordynariacie|sanktuarium|Zjednoczeniu|Stowarzyszeniu|parafiach|kościołach)\b"
    r"|W\s+[A-ZĄĆĘŁŃÓŚŻŹ][\wąćęłńóśżź-]+\s*:"
    r")", re.UNICODE)


def oczysc_tytul(tytul: str) -> str:
    t = (tytul or "").strip()
    m = TITLE_TAIL_RE.search(t)
    if m and m.start() > 0:
        t = t[:m.start()]
    return t.strip().strip("-–—:,").strip()


# ── Dopasowanie tytułów ──────────────────────────────────────────────────────
# Stary asset ma tytuły z prefiksem daty („2 stycznia: św. Bazylego…”), nowy scrape
# i kalendarz mają tytuły czyste. Prefiks zdejmujemy PRZED porównaniem — dzięki temu
# ta sama funkcja rozpoznaje duplikat po pierwszym uruchomieniu skryptu (idempotencja).
DATE_PREFIX_RE = re.compile(r"^\d+\s+\w+\s*:\s*", re.UNICODE)

# Słowa funkcyjne: tytuły liturgiczne różnią się nimi swobodnie („św. Fabiana” /
# „św. Fabian, papież i męczennik”), więc do porównania zostają same imiona/nazwy.
STOPWORDS = {
    "sw", "bl", "swietego", "swietej", "swietych", "blogoslawionego", "blogoslawionej",
    "blogoslawionych", "biskup", "biskupa", "biskupow", "prezbiter", "prezbitera",
    "prezbiterow", "dziewica", "dziewicy", "dziewic", "meczennik", "meczennika",
    "meczennicy", "meczennikow", "meczennica", "zakonnik", "zakonnika", "zakonnicy",
    "zakonnica", "opat", "opata", "papiez", "papieza", "doktor", "doktora",
    "kosciol", "kosciola", "wspomnienie", "dowolne", "dowolne", "obowiazkowe",
    "swieto", "uroczystosc", "diakon", "diakona", "wdowa", "wdowy", "krol", "krola",
    "krolowa", "krolowej", "apostol", "apostola", "apostolow", "ewangelista",
    "ewangelisty", "pustelnik", "pustelnika", "misjonarz", "misjonarza",
    "zakonodawca", "zakonodawcy", "prorok", "proroka", "matka", "matki",
    "towarzysze", "towarzyszy", "towarzyszow", "i", "oraz", "z", "ze", "w", "we",
    "na", "o", "od", "oraz", "jego", "jej", "ich", "pierwszych", "oraz",
    # Tytuły maryjne różni WYŁĄCZNIE przydomek („NMP Tuchowskiej” / „NMP Kodeńskiej” /
    # „NMP Licheńskiej” — trzy różne obchody tego samego dnia). Bez odsiania wspólnego
    # członu wychodziło 0,75 podobieństwa i jeden obchód zjadał dwa pozostałe.
    "najswietszej", "najswietszy", "najswietsza", "najswietszego", "najsw", "nmp",
    "maryi", "maryja", "maryjnej", "panny", "panna", "bozej", "boza", "bozego",
}

STEM = 6       # porównujemy RDZENIE — polska deklinacja psuje porównanie całych słów
               # („Fabiana” vs „Fabian”, „Klary” vs „Klara”)
STEM_MATCH = 5  # dwa rdzenie uznajemy za ten sam wyraz, gdy zgadza się tyle znaków —
                # „Mątowów”/„Mątów” i „Reginy”/„Regina” to jedna osoba, a przy sztywnym
                # porównaniu 6 znaków wypadały poniżej progu duplikatu


def deaccent(s: str) -> str:
    s = s.replace("ł", "l").replace("Ł", "L")
    return "".join(c for c in unicodedata.normalize("NFD", s)
                   if unicodedata.category(c) != "Mn")


def _words(tytul: str):
    t = DATE_PREFIX_RE.sub("", tytul or "")
    t = deaccent(t).lower()
    t = re.sub(r"[^a-z0-9]+", " ", t)
    return [w for w in t.split() if len(w) > 1]


def title_key(tytul: str):
    """Zbiór rdzeni znaczących słów tytułu (fallback: rdzenie WSZYSTKICH słów).

    Fallback jest konieczny dla tytułów zbudowanych wyłącznie ze słów funkcyjnych —
    „Najświętszej Maryi Panny, Królowej” po odsianiu zostawiałoby zbiór pusty,
    a pusty zbiór dopasowuje się do wszystkiego.
    """
    ws = _words(tytul)
    znaczace = [w for w in ws if w not in STOPWORDS]
    użyte = znaczace if znaczace else ws
    return {w[:STEM] for w in użyte}


def similarity(a: str, b: str) -> float:
    ka, kb = title_key(a), title_key(b)
    if not ka or not kb:
        return 0.0
    n = sum(1 for x in ka if any(x[:STEM_MATCH] == y[:STEM_MATCH] for y in kb))
    return n / min(len(ka), len(kb))


# Progi zmierzone na tym wejściu: ≥0.6 duplikat, <0.3 nowy, między = szara strefa.
PROG_DUPLIKAT = 0.6
PROG_NOWY = 0.3

# ── Wyjątki rozstrzygnięte PRZEZ CZŁOWIEKA ───────────────────────────────────
# Szara strefa (0.3–0.6) sprawdzona ręcznie wpis po wpisie 2026-08-05. Lista jest
# JAWNA (a nie zaszyta w progach), żeby przy kolejnym scrape'ie było widać, co było
# decyzją człowieka, a co wynikiem miary. Klucz: (data, tytuł ze scrape'u).
RECZNE_DECYZJE = {
    # duplikaty: stary asset ma ten sam obchód pod inną odmianą tytułu
    # (01-18 rozstrzygał człowiek przy STEM=6; po wprowadzeniu STEM_MATCH miara zgadza się
    #  z tą decyzją — wpis zostaje jako ślad, że przypadek był oglądany)
    ("01-18", "Bł. Reginy Protmann, dziewicy"): "duplikat",          # stary: „bł. Regina Protmann”
    ("01-25", "NAWRÓCENIE ŚW. PAWŁA, APOSTOŁA"): "duplikat",
    # nowe: stary asset ma na 2 VII tylko NMP Kodeńską, obie poniższe są osobnymi obchodami
    ("07-02", "Najświętszej Maryi Panny Tuchowskiej"): "nowy",
    ("07-02", "Najświętszej Maryi Panny Licheńskiej"): "nowy",
    # inny święty tego samego dnia; wspólne jest tylko imię „Maria”
    # (stary asset na 5 VII: „św. Antoniego Marii Zaccarii”)
    ("07-05", "Św. Marii Goretti, dziewicy i męczennicy"): "nowy",
}


def mmdd(month: int, day: int) -> str:
    return "%02d-%02d" % (month, day)


def sekcje_na_wpis(sekcje: dict) -> dict:
    """Sekcje scrape'u -> pola docelowego wpisu assetu.

    Nazwy różnią się między scrape'ami (`antyfona_na_wejscie` vs `antyfona_wejscia`).
    Antyfony są obiektami {ref, tekst}, modlitwy zwykłymi stringami — tak czyta je
    `MassProperRepository.parseAll`. Sekcje spoza tej mapy (`prefacja`,
    `uroczyste_blogoslawienstwo`) nie mają reprezentacji w formacie i są pomijane.
    """
    out = {}

    def tekst(name):
        s = (sekcje.get(name) or {}).get("tekst")
        return s.strip() if isinstance(s, str) and s.strip() else None

    def antyfona(name):
        sek = sekcje.get(name) or {}
        t = sek.get("tekst")
        if not (isinstance(t, str) and t.strip()):
            return None
        return {"ref": (sek.get("ref") or "").strip(), "tekst": t.strip()}

    for src, dst in (("antyfona_na_wejscie", "antyfona_wejscia"),):
        v = antyfona(src)
        if v:
            out[dst] = v
    for name in ("kolekta", "modlitwa_nad_darami"):
        v = tekst(name)
        if v:
            out[name] = v
    v = antyfona("antyfona_na_komunie")
    if v:
        out["antyfona_komunii"] = v
    v = tekst("modlitwa_po_komunii")
    if v:
        out["modlitwa_po_komunii"] = v
    return out


def wybierz_formularz(formularze):
    """Jeden formularz z wpisu scrape'u.

    Wpisy z dwoma formularzami to pary „MSZA W WIGILIĘ” + „MSZA W DZIEŃ” (24 VI, 29 VI,
    15 VIII) — bierzemy MSZĘ W DZIEŃ, bo to formularz samego obchodu. Wszystkie trzy są
    dziś w asecie, więc i tak odpadają jako duplikaty; reguła jest na przyszłe scrape'y.
    """
    if not formularze:
        return None
    if len(formularze) == 1:
        return formularze[0]
    for f in formularze:
        if "W DZIEŃ" in (f.get("nazwa") or "").upper():
            return f
    return formularze[0]


def zbuduj_wpis(data: str, tytul: str, formularz: dict, diecezje=None) -> dict:
    wpis = {"dzien": data, "tytul": tytul}
    wpis.update(sekcje_na_wpis(formularz.get("sekcje") or {}))
    if diecezje:
        wpis["zakres"] = "diecezja"
        wpis["diecezje"] = list(diecezje)
    return wpis


def main():
    zapis = "--dry-run" not in sys.argv

    scrape = json.load(open(IN_SCRAPE, encoding="utf-8"))
    asset = json.load(open(ASSET, encoding="utf-8"))
    kalendarz = json.load(open(KALENDARZ, encoding="utf-8"))

    asset_przed = [json.dumps(e, ensure_ascii=False, sort_keys=True) for e in asset]

    # indeksy po dacie
    asset_po_dacie = {}
    for e in asset:
        asset_po_dacie.setdefault(e["dzien"], []).append(e)
    kal_po_dacie = {}
    for c in kalendarz:
        if c.get("data"):
            kal_po_dacie.setdefault(c["data"], []).append(c)

    dodane = []
    pominiete_status = []
    pominiete_ruchome = []
    pominiete_duplikat = []
    pominiete_szara = []       # szara strefa bez decyzji człowieka — NIE dodajemy
    pominiete_wewnetrzne = []  # duplikat wewnątrz samego pliku wejściowego
    bez_dopasowania_w_kal = []
    daty_zastepcze = []
    problemy_zakresu = []
    porzucone_sekcje = []
    zrodla = {}  # (data, tytuł) -> skąd wziął się zakres (do raportu)

    def duplikat_wzgledem(data, tytul):
        """(werdykt, najlepszy_stary_tytul, wynik) — werdykt: duplikat/nowy/szara.

        Decyzje człowieka ([RECZNE_DECYZJE]) rozstrzygają WYŁĄCZNIE szarą strefę.
        Gdyby nadpisywały też pewne werdykty, skrypt przestałby być IDEMPOTENTNY:
        wpis oznaczony ręcznie jako „nowy" dokładałby się przy każdym uruchomieniu,
        bo po pierwszym scaleniu jest już swoim własnym duplikatem.
        """
        kandydaci = asset_po_dacie.get(data, []) + [{"tytul": w["tytul"]} for w in dodane if w["dzien"] == data]
        best, best_t = 0.0, None
        for stary in kandydaci:
            s = similarity(tytul, stary["tytul"])
            if s > best:
                best, best_t = s, stary["tytul"]
        if best >= PROG_DUPLIKAT:
            return "duplikat", best_t, best
        if best < PROG_NOWY:
            return "nowy", best_t, best
        return RECZNE_DECYZJE.get((data, tytul), "szara"), best_t, best

    def zakres_z_kalendarza(data, tytul):
        """(diecezje|None, opis_zrodla). Brak dopasowania → powszechny + pozycja do przeglądu.

        SUMA wszystkich pasujących wpisów kalendarza tej daty, nie „najlepszy": ten sam
        obchód bywa wpisany kilka razy z różną rangą per diecezja (bł. Dorota z Mątów ma
        trzy wpisy: gdańska+koszalińsko-kołobrzeska, warmińska, elbląska). Wzięcie jednego
        wpisu ukryłoby formularz przed pozostałymi diecezjami. Jeśli którykolwiek wpis jest
        powszechny — obchód jest powszechny.
        """
        pasujace = [(similarity(tytul, c["tytul"]), c) for c in kal_po_dacie.get(data, [])]
        trafione = [c for s, c in pasujace if s >= PROG_DUPLIKAT]
        if not trafione:
            best = max(pasujace, default=(0.0, None), key=lambda x: x[0])
            bez_dopasowania_w_kal.append(
                (data, tytul, best[1]["tytul"] if best[1] else "—", round(best[0], 2)))
            return None, "brak dopasowania w kalendarzu → powszechny (DO PRZEGLĄDU)"
        if any(c.get("zakres") != "diecezja" for c in trafione):
            return None, "kalendarz: powszechny"
        diec = []
        for c in trafione:
            for d in c.get("diecezje") or []:
                if d not in diec:
                    diec.append(d)
        return diec, "kalendarz: " + "; ".join(sorted({c["tytul"] for c in trafione}))

    for e in scrape:
        tytul = oczysc_tytul(e.get("tytul"))
        formularz = wybierz_formularz(e.get("formularze") or [])
        if e.get("status") != "ok" or formularz is None:
            pominiete_status.append((e.get("dzien"), tytul, e.get("status"),
                                     len(e.get("formularze") or [])))
            continue
        if not e.get("dzien"):
            pominiete_ruchome.append((tytul, e.get("dzien_ruchomy")))
            continue

        data = e["dzien"]
        raw_zakres = (e.get("zakres") or "").strip()

        werdykt, stary_tytul, wynik = duplikat_wzgledem(data, tytul)
        if werdykt == "duplikat":
            (pominiete_wewnetrzne if any(w["dzien"] == data and similarity(tytul, w["tytul"]) >= PROG_DUPLIKAT
                                         for w in dodane) else pominiete_duplikat).append(
                (data, tytul, stary_tytul, round(wynik, 2)))
        elif werdykt == "szara":
            pominiete_szara.append((data, tytul, stary_tytul, round(wynik, 2)))
        else:
            diecezje, zrodlo = zakres_z_kalendarza(data, tytul)
            # Nagłówek zakresu ze scrape'u a werdykt kalendarza — rozjazd zostawiamy człowiekowi
            if raw_zakres and not diecezje and not SCOPE_ALT_DATE_RE.match(raw_zakres):
                problemy_zakresu.append((data, tytul, raw_zakres, zrodlo))
            zrodla[(data, tytul)] = zrodlo
            dodane.append(zbuduj_wpis(data, tytul, formularz, diecezje))
            for sek in (formularz.get("sekcje") or {}):
                if sek not in ("antyfona_na_wejscie", "kolekta", "modlitwa_nad_darami",
                               "antyfona_na_komunie", "modlitwa_po_komunii"):
                    porzucone_sekcje.append((data, tytul, sek))

        # ── Reguła 3: obchód przeniesiony w jednej diecezji na inną datę ──
        # NIEZALEŻNE od werdyktu wpisu bazowego: gdy obchód jest już w asecie pod datą
        # pierwotną (duplikat), pod datą zastępczą wciąż go NIE MA — a to właśnie ten wpis
        # niesie informację, że w tej jednej diecezji obchód wypada innego dnia.
        m = SCOPE_ALT_DATE_RE.match(raw_zakres)
        if m:
            diec_loc, dzien_alt, mies_alt = m.group(1).lower(), int(m.group(2)), m.group(3).lower()
            diec = DIECEZJE_MIEJSC.get(diec_loc)
            mies = MIESIACE.get(mies_alt)
            if diec and mies:
                data_alt = mmdd(mies, dzien_alt)
                w2, st2, sc2 = duplikat_wzgledem(data_alt, tytul)
                if w2 == "duplikat":
                    pominiete_duplikat.append((data_alt, tytul + " [data zastępcza]", st2, round(sc2, 2)))
                else:
                    zrodla[(data_alt, tytul)] = f"nagłówek daty zastępczej w scrape'ie: {raw_zakres!r}"
                    dodane.append(zbuduj_wpis(data_alt, tytul, formularz, [diec]))
                    daty_zastepcze.append((data, data_alt, diec, tytul, raw_zakres))
            else:
                problemy_zakresu.append((data, tytul, raw_zakres, "nierozpoznana data/diecezja zastępcza"))

    # Kolejność wpisów niczego nie rozstrzyga (`select` filtruje po diecezji, nie po pozycji),
    # ale posortowane dopiski są czytelne w diffie pliku.
    dodane.sort(key=lambda w: w["dzien"])
    wynik = asset + dodane

    # ── walidacja PRZED zapisem ──────────────────────────────────────────────
    problemy = []
    if [json.dumps(e, ensure_ascii=False, sort_keys=True) for e in wynik[:len(asset)]] != asset_przed:
        problemy.append("stare wpisy zostały zmienione — scalanie ma TYLKO dodawać")
    if len(wynik) != len(asset) + len(dodane):
        problemy.append("nie zgadza się liczba wpisów w wyniku")
    stare_klucze = {e["dzien"] for e in asset}
    for w in dodane:
        if not re.match(r"^\d\d-\d\d$", w["dzien"]) and w["dzien"] not in stare_klucze:
            problemy.append(f"[{w['dzien']}] klucz nie jest datą MM-DD ani znanym kluczem nazwanym")
        if not w.get("tytul"):
            problemy.append(f"[{w['dzien']}] wpis bez tytułu")
        if not any(k in w for k in ("kolekta", "antyfona_wejscia", "modlitwa_po_komunii")):
            problemy.append(f"[{w['dzien']}] {w['tytul'][:50]}: wpis bez jakiejkolwiek treści")
        if "zakres" in w:
            if w["zakres"] != "diecezja":
                problemy.append(f"[{w['dzien']}] zakres inny niż 'diecezja': {w['zakres']!r}")
            if not w.get("diecezje"):
                problemy.append(f"[{w['dzien']}] zakres=diecezja z pustą listą diecezji")
        for d in w.get("diecezje", []):
            if d not in DIECEZJE_KANON:
                problemy.append(f"[{w['dzien']}] nazwa diecezji spoza kanonicznej listy: {d!r}")
    try:
        json.loads(json.dumps(wynik, ensure_ascii=False))
    except Exception as ex:  # pragma: no cover
        problemy.append(f"wynik nie jest poprawnym JSON-em: {ex}")

    # ── raport ───────────────────────────────────────────────────────────────
    print(f"WEJŚCIE: {len(scrape)} wpisów scrape'u | ASSET: {len(asset)} wpisów")
    print(f"DODANE: {len(dodane)} (w tym {sum(1 for w in dodane if 'zakres' in w)} diecezjalnych)")
    print(f"POMINIĘTE: duplikat {len(pominiete_duplikat)} | duplikat w pliku wejściowym "
          f"{len(pominiete_wewnetrzne)} | status/brak formularza {len(pominiete_status)} | "
          f"ruchome {len(pominiete_ruchome)} | szara strefa bez decyzji {len(pominiete_szara)}")

    print("\n--- DODANE (wszystkie) ---")
    for w in dodane:
        zakres = ("diec: " + ", ".join(w["diecezje"])) if "zakres" in w else "powszechny"
        pola = ",".join(k for k in ("antyfona_wejscia", "kolekta", "modlitwa_nad_darami",
                                    "antyfona_komunii", "modlitwa_po_komunii") if k in w)
        print(f"  [{w['dzien']}] {w['tytul'][:58]:58} | {zakres[:45]:45} | {pola}")

    print("\n--- DODANE DIECEZJALNE (diecezja ← źródło) ---")
    for w in dodane:
        if "zakres" in w:
            print(f"  [{w['dzien']}] {w['tytul'][:55]} ← {', '.join(w['diecezje'])}")
            print(f"           źródło: {zrodla.get((w['dzien'], w['tytul']), '?')}")
    print("\n--- DATY ZASTĘPCZE (dodatkowy wpis; obchód widoczny w OBU datach) ---")
    for d0, d1, diec, tyt, raw in daty_zastepcze:
        print(f"  {d0} → {d1} [{diec}] {tyt[:50]}  ({raw})")
    print("\n--- BEZ DOPASOWANIA W KALENDARZU (dodane jako powszechne — DO PRZEGLĄDU) ---")
    for data, tyt, kand, sc in bez_dopasowania_w_kal:
        print(f"  [{data}] {tyt[:55]}  | najbliższy w kalendarzu: {kand[:45]} ({sc})")
    print("\n--- NAGŁÓWEK ZAKRESU ZE SCRAPE'U vs KALENDARZ (DO DECYZJI) ---")
    for data, tyt, raw, zr in problemy_zakresu:
        print(f"  [{data}] {tyt[:45]} | scrape: {raw!r} | {zr}")
    print("\n--- POMINIĘTE: obchody ruchome (brak daty — osobna decyzja) ---")
    for tyt, opis in pominiete_ruchome:
        print(f"  {tyt[:60]}  ({opis})")
    print("\n--- POMINIĘTE: status != ok / brak formularzy ---")
    for data, tyt, st, nf in pominiete_status:
        print(f"  [{data}] {tyt[:55]} (status={st}, formularzy={nf})")
    print("\n--- POMINIĘTE: duplikat wewnątrz pliku wejściowego ---")
    for data, tyt, stary, sc in pominiete_wewnetrzne:
        print(f"  [{data}] {tyt[:55]} ≈ {stary[:45]} ({sc})")
    if pominiete_szara:
        print("\n--- SZARA STREFA BEZ DECYZJI CZŁOWIEKA (NIE dodane — uzupełnij RECZNE_DECYZJE) ---")
        for data, tyt, stary, sc in pominiete_szara:
            print(f"  [{data}] {tyt[:55]} ≈ {stary[:45]} ({sc})")
    if porzucone_sekcje:
        print("\n--- SEKCJE BEZ REPREZENTACJI W FORMACIE (pominięte) ---")
        for data, tyt, sek in porzucone_sekcje:
            print(f"  [{data}] {tyt[:45]}: {sek}")
    graniczne = [x for x in pominiete_duplikat if x[3] < 0.75]
    if graniczne:
        print("\n--- DUPLIKATY GRANICZNE (0.6–0.75 — warte oka przy kolejnym scrape'ie) ---")
        for data, tyt, stary, sc in graniczne:
            print(f"  [{data}] {tyt[:50]} ≈ {stary[:50]} ({sc})")
    print("\nDODANE wg miesiąca:", dict(sorted(Counter(w["dzien"][:2] for w in dodane).items())))

    if problemy:
        print("\n!!! WALIDACJA NIEUDANA — NIC NIE ZAPISANO:")
        for p in problemy:
            print("   ", p)
        raise SystemExit(1)

    if not zapis:
        print("\n(--dry-run: nic nie zapisano)")
        return
    with open(ASSET, "w", encoding="utf-8") as f:
        json.dump(wynik, f, ensure_ascii=False, indent=2)
    print(f"\nZAPISANO: {ASSET}  ({len(wynik)} wpisów)")


if __name__ == "__main__":
    main()
