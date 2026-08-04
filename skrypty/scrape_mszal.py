#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Scraper Mszału Rzymskiego z ordo.pallotyni.pl -> JSON (z parserem sekcji)

Dla każdego formularza wyodrębnia:
  - rangę (uroczystość / święto / wspomnienie obowiązkowe / dowolne)
  - biogram / wstęp (tekst przed pierwszym nagłówkiem)
  - sekcje liturgiczne (antyfona wejścia, kolekta, modlitwa nad darami,
    antyfona komunii, modlitwa po komunii, modlitwa nad ludem)
  - rubryki wewnątrz formularza ("Nie odmawia się Chwała.", "Odmawia się Wierzę.")
  - odniesienia do prefacji i Mszy wspólnych
  - fallback: content_html i content_text z całego artykułu

Brakujące sekcje mają wartość null -- dzięki temu można łatwo zapytać
"które formularze mają tylko kolektę" itp.

Użycie:
    pip install requests beautifulsoup4 lxml
    python scrape_mszal.py
"""

import json
import re
import time
import hashlib
from pathlib import Path
from urllib.parse import urljoin, urlparse

import requests
from bs4 import BeautifulSoup

# Auto-wybor parsera: lxml jesli jest, w przeciwnym razie wbudowany html.parser
try:
    BeautifulSoup("<x/>", "lxml")
    BS_PARSER = "lxml"
except Exception:
    BS_PARSER = "html.parser"

# ------------------------------------------------------------------
# Konfiguracja
# ------------------------------------------------------------------
ROOT_URL = "https://www.ordo.pallotyni.pl/index.php/mszal-rzymski"
BASE = "https://www.ordo.pallotyni.pl"

OUTPUT_JSON = "mszal_rzymski.json"
CACHE_DIR = Path("cache")
CACHE_DIR.mkdir(exist_ok=True)

HEADERS = {
    "User-Agent": (
        "MszalScraper/1.0 (osobiste archiwum liturgiczne; "
        "kontakt: twoj-email@example.com)"
    ),
    "Accept-Language": "pl-PL,pl;q=0.9",
}

REQUEST_DELAY = 0.6
TIMEOUT = 20

WANTED_CATEGORY_TITLES = {
    "Formularze mszalne - Adwent",
    "Formularze mszalne - Okres Narodzenia Pańskiego",
    "Formularze mszalne - Okres Wielkiego Postu",
    "Formularze mszalne - Okres Wielkanocny",
    "Formularze mszalne - Okres Zwykły",
    "Formularze własne o świętych - Styczeń",
    "Formularze własne o świętych - Luty",
    "Formularze własne o świętych - Marzec",
    "Formularze własne o świętych - Kwiecień",
    "Formularze własne o świętych - Maj",
    "Formularze własne o świętych - Czerwiec",
    "Formularze własne o świętych - Lipiec",
    "Formularze własne o świętych - Sierpień",
    "Formularze własne o świętych - Październik",
    "Formularze Mszy wspólnych",
    "Formularze Mszy obrzędowych",
}

# ------------------------------------------------------------------
# Definicje sekcji formularza mszalnego
# ------------------------------------------------------------------
SECTION_ORDER = [
    ("ANTYFONA NA WEJŚCIE", "antyfona_wejscia"),
    ("KOLEKTA", "kolekta"),
    ("MODLITWA NAD DARAMI", "modlitwa_nad_darami"),
    ("ANTYFONA NA KOMUNIĘ", "antyfona_komunii"),
    ("MODLITWA PO KOMUNII", "modlitwa_po_komunii"),
    ("MODLITWA NAD LUDEM", "modlitwa_nad_ludem"),
]
SECTION_KEYS = {h: k for h, k in SECTION_ORDER}
SECTION_HEADERS_RE = re.compile(
    r"(?<![A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż])("
    + "|".join(re.escape(h) for h, _ in SECTION_ORDER)
    + r")(?![A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż])"
)

RUBRIC_PATTERNS = [
    r"Nie odmawia się Chwała[^.]*\.",
    r"Odmawia się Chwała[^.]*\.",
    r"Odmawia się Wierzę[^.]*\.",
    r"Nie odmawia się Wierzę[^.]*\.",
    r"Odmawia się hymn Chwała[^.]*\.",
]
RUBRIC_RE = re.compile("|".join(RUBRIC_PATTERNS))

RANK_RE = re.compile(
    r"\b(uroczystość|święto|wspomnienie\s+obowiązkowe"
    r"|wspomnienie\s+dowolne|wspomnienie)\b",
    re.IGNORECASE,
)

COMMON_RE = re.compile(
    r"Msz[ay]\s+wspóln[aej][^\n]*",
    re.IGNORECASE,
)

PREFACE_RE = re.compile(
    r"\b(?:\d+\s+)?[Pp]refacja[^\n]*",
)

DATE_RE = re.compile(
    r"\b(\d{1,2})\s+(stycznia|lutego|marca|kwietnia|maja|czerwca"
    r"|lipca|sierpnia|września|października|listopada|grudnia)\b",
    re.IGNORECASE,
)


# ------------------------------------------------------------------
# Pobieranie z cache'em
# ------------------------------------------------------------------
def cache_path(url: str) -> Path:
    h = hashlib.sha1(url.encode("utf-8")).hexdigest()[:16]
    slug = re.sub(r"[^a-z0-9]+", "-", urlparse(url).path.lower()).strip("-")[-60:]
    return CACHE_DIR / f"{slug}-{h}.html"


def fetch(url: str, session: requests.Session) -> str:
    cp = cache_path(url)
    if cp.exists():
        return cp.read_text(encoding="utf-8")

    print(f"  GET {url}")
    resp = session.get(url, headers=HEADERS, timeout=TIMEOUT)
    resp.raise_for_status()
    resp.encoding = resp.apparent_encoding or "utf-8"
    html = resp.text
    cp.write_text(html, encoding="utf-8")
    time.sleep(REQUEST_DELAY)
    return html


# ------------------------------------------------------------------
# Parsowanie listy podkategorii i artykułów
# ------------------------------------------------------------------
def parse_subcategories(html: str):
    soup = BeautifulSoup(html, BS_PARSER)
    results = []

    container = soup.find(
        "div", class_=re.compile(r"(subcategories|items-leading|category-list)")
    )
    if container:
        anchors = container.find_all("a", href=True)
    else:
        main = soup.find("div", class_="item-page") or soup
        anchors = [
            a for h in main.find_all(["h3", "h2"])
            for a in h.find_all("a", href=True)
        ]

    seen = set()
    for a in anchors:
        title = a.get_text(strip=True)
        href = urljoin(BASE, a["href"])
        if "/mszal-rzymski/" not in href:
            continue
        if href in seen:
            continue
        seen.add(href)
        results.append({"title": title, "url": href})
    return results


def parse_article_list(html: str, category_url: str):
    soup = BeautifulSoup(html, BS_PARSER)
    for nav in soup.find_all(["nav", "header"]):
        nav.decompose()
    for m in soup.find_all(class_=re.compile(r"(menu|nav|breadcrumb)", re.I)):
        m.decompose()

    container = (
        soup.find("div", class_=re.compile(r"category-list"))
        or soup.find("table", class_=re.compile(r"category"))
    )
    search_root = container if container else soup

    cat_path = urlparse(category_url).path.rstrip("/")
    art_url_re = re.compile(re.escape(cat_path) + r"/\d+-[^/?#]+/?$")

    articles = []
    seen = set()
    for a in search_root.find_all("a", href=True):
        href = urljoin(BASE, a["href"])
        if not art_url_re.search(urlparse(href).path):
            continue
        title = a.get_text(strip=True)
        if not title or title == "Tytuł":
            continue
        if href in seen:
            continue
        seen.add(href)
        articles.append({"title": title, "url": href})
    return articles


# ------------------------------------------------------------------
# Parsowanie pojedynczego formularza mszalnego
# ------------------------------------------------------------------
def clean_body(soup: BeautifulSoup) -> BeautifulSoup:
    page = soup.find("div", class_="item-page") or soup.find("article") or soup
    body = page.find("div", attrs={"itemprop": "articleBody"}) or page

    kill_selectors = [
        "form", "ul.actions", "dl.article-info",
        "div.icons", "div.btn-group", "span.hits",
        "div.voting-symbol", "div.content_rating", "div.form-voting",
        "div.contentpaneopen_edit",
    ]
    for sel in kill_selectors:
        for el in body.select(sel):
            el.decompose()
    return body


def extract_references(body: BeautifulSoup):
    """Linki wewnątrz artykułu skategoryzowane: prefacja / msza_wspolna / inne."""
    refs = []
    for a in body.find_all("a", href=True):
        href = urljoin(BASE, a["href"])
        text = a.get_text(" ", strip=True)
        if not text:
            continue
        if any(x in href for x in ("tmpl=component", "mailto", "?print=")):
            continue
        low = (text + " " + href).lower()
        if "prefacja" in low:
            kind = "prefacja"
        elif "wspóln" in low or "wspoln" in low or "msze-wspolne" in low:
            kind = "msza_wspolna"
        elif "lekcjonarz" in low:
            kind = "lekcjonarz"
        else:
            kind = "inny"
        refs.append({"kind": kind, "text": text, "url": href})
    return refs


def split_sections(text: str):
    """
    Dzieli tekst formularza na sekcje liturgiczne.
    Brakujące sekcje => None. Antyfony dodatkowo rozbite na {bible_ref, text}.
    """
    out = {"_intro": None}
    for _, key in SECTION_ORDER:
        out[key] = None

    parts = SECTION_HEADERS_RE.split(text)

    intro = parts[0].strip()
    if intro:
        out["_intro"] = intro

    i = 1
    while i < len(parts) - 1:
        header = parts[i].strip()
        body = parts[i + 1].strip()
        key = SECTION_KEYS.get(header)
        if key:
            if "ANTYFONA" in header:
                lines = body.split("\n", 1)
                first = lines[0].strip()
                rest = lines[1].strip() if len(lines) > 1 else ""
                # Heurystyka "wygląda jak odniesienie biblijne":
                # zaczyna się od skrótu księgi (opt. "Por.", opt. "1"/"2")
                # + liczba rozdziału/wersetu.
                bible_like = re.match(
                    r"^(Por\.\s+)?[12]?\s?[A-ZŁŚŻ][a-ząćęłńóśźż]{1,4}\s+\d",
                    first,
                )
                if bible_like and rest:
                    out[key] = {"bible_ref": first, "text": rest}
                else:
                    out[key] = {"bible_ref": None, "text": body}
            else:
                out[key] = body
        i += 2
    return out


def parse_article(html: str):
    soup = BeautifulSoup(html, BS_PARSER)
    page = soup.find("div", class_="item-page") or soup

    title_el = page.find(["h1", "h2"], class_=re.compile(r"(item-title|page-header)"))
    if not title_el:
        title_el = page.find(["h1", "h2"])
    title = title_el.get_text(strip=True) if title_el else ""

    body = clean_body(soup)
    content_html = "".join(str(c) for c in body.contents).strip()

    for br in body.find_all("br"):
        br.replace_with("\n")
    text = body.get_text("\n\n", strip=True)
    text = re.sub(r"\n{3,}", "\n\n", text).strip()

    sections = split_sections(text)
    intro = sections.pop("_intro", None)

    rank_match = RANK_RE.search(text)
    rank = rank_match.group(0).lower() if rank_match else None

    rubrics = list(dict.fromkeys(r.strip() for r in RUBRIC_RE.findall(text)))
    common_refs = list(dict.fromkeys(m.strip() for m in COMMON_RE.findall(text)))
    preface_refs = list(dict.fromkeys(m.strip() for m in PREFACE_RE.findall(text)))

    links = extract_references(body)

    day_match = DATE_RE.search(title) or (DATE_RE.search(intro) if intro else None)
    day = day_match.group(0).lower() if day_match else None

    present = [k for _, k in SECTION_ORDER if sections.get(k)]
    if len(present) >= 5:
        formulary_type = "pelny"
    elif len(present) >= 2:
        formulary_type = "skrocony"
    elif len(present) == 1:
        formulary_type = "minimalny"
    else:
        formulary_type = "brak_sekcji"

    return {
        "title": title,
        "day": day,
        "rank": rank,
        "formulary_type": formulary_type,
        "sections_present": present,
        "intro": intro,
        "sections": sections,
        "rubrics": rubrics,
        "common_refs": common_refs,
        "preface_refs": preface_refs,
        "links": links,
        "content_html": content_html,
        "content_text": text,
    }


# ------------------------------------------------------------------
# Główna pętla
# ------------------------------------------------------------------
def main():
    session = requests.Session()
    print(f"[1/3] Pobieram stronę główną: {ROOT_URL}")
    root_html = fetch(ROOT_URL, session)
    subcategories = parse_subcategories(root_html)
    print(f"     Znaleziono {len(subcategories)} podkategorii.")

    subcategories = [s for s in subcategories if s["title"] in WANTED_CATEGORY_TITLES]
    print(f"     Po filtrze: {len(subcategories)} kategorii.")
    for s in subcategories:
        print(f"       - {s['title']}")

    result = {
        "source": BASE,
        "root": ROOT_URL,
        "scraped_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "categories": [],
    }

    print("\n[2/3] Przechodzę po kategoriach...")
    for i, cat in enumerate(subcategories, 1):
        print(f"  ({i}/{len(subcategories)}) {cat['title']}")
        try:
            cat_html = fetch(cat["url"], session)
            articles = parse_article_list(cat_html, cat["url"])
        except Exception as e:
            print(f"     [BŁĄD] {e}")
            articles = []

        print(f"     {len(articles)} artykułów.")
        cat_out = {"title": cat["title"], "url": cat["url"], "articles": []}

        for art in articles:
            try:
                art_html = fetch(art["url"], session)
                data = parse_article(art_html)
            except Exception as e:
                print(f"       [BŁĄD art] {art['url']} -> {e}")
                continue
            data["url"] = art["url"]
            if not data.get("title"):
                data["title"] = art["title"]
            cat_out["articles"].append(data)
        result["categories"].append(cat_out)

        with open(OUTPUT_JSON, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"\n[3/3] Gotowe! Zapisano do: {OUTPUT_JSON}")
    total = sum(len(c["articles"]) for c in result["categories"])
    print(f"     Łącznie: {len(result['categories'])} kategorii, {total} artykułów.")

    from collections import Counter
    types = Counter()
    for c in result["categories"]:
        for a in c["articles"]:
            types[a.get("formulary_type", "?")] += 1
    print(f"     Typy formularzy: {dict(types)}")


if __name__ == "__main__":
    main()
