import requests
from bs4 import BeautifulSoup
import json
import time
import re

# --- KONFIGURACJA ---
OKRESY = {
    "1": "Adwent", 
    "2": "BożeNarodzenie", 
    "4": "WielkiPost", 
    "5": "Wielkanoc", 
    "3": "Zwykły"
}
ROKI = ["1", "2", "A", "B", "C"]

MAPA_DNI = {
    "PONIEDZIAŁEK": "Pon", "WTOREK": "Wto", "ŚRODA": "Śro",
    "CZWARTEK": "Czw", "PIĄTEK": "Pią", "SOBOTA": "Sob", "NIEDZIELA": "Nie"
}

# Rozszerzona mapa aż do 34 tygodni Okresu Zwykłego
MAPA_RZYMSKIE = {
    "I": "1", "II": "2", "III": "3", "IV": "4", "V": "5", "VI": "6", "VII": "7", "VIII": "8", "IX": "9", "X": "10",
    "XI": "11", "XII": "12", "XIII": "13", "XIV": "14", "XV": "15", "XVI": "16", "XVII": "17", "XVIII": "18", "XIX": "19", "XX": "20",
    "XXI": "21", "XXII": "22", "XXIII": "23", "XXIV": "24", "XXV": "25", "XXVI": "26", "XXVII": "27", "XXVIII": "28", "XXIX": "29", "XXX": "30",
    "XXXI": "31", "XXXII": "32", "XXXIII": "33", "XXXIV": "34"
}

BAZA_DANYCH = []

def wyciagnij_tydzien(nazwa):
    """Wyciąga rzymski lub arabski numer tygodnia"""
    match = re.search(r'\b([IVX]+|\d+)\b\s*(?:tygodnia|tydzień|niedziela|niedzieli)', nazwa, re.IGNORECASE)
    if match: return match.group(1).upper()
    match = re.search(r'(?:tydzień|tygodni|niedziel)\s*\b([IVX]+|\d+)\b', nazwa, re.IGNORECASE)
    if match: return match.group(1).upper()
    return "Brak"

def stworz_krotki_tytul(pelna_nazwa, nazwa_okresu, tydzien_wykryty):
    """Generuje format: Zwykły 11 Śro ALBO Adwent 20 Grudnia"""
    
    if tydzien_wykryty.isdigit():
        tydzien_arabski = tydzien_wykryty
    else:
        tydzien_arabski = MAPA_RZYMSKIE.get(tydzien_wykryty, "")
    
    # 1. Jeżeli mamy numer tygodnia
    if tydzien_arabski:
        skrot_dnia = "???"
        for pl, skr in MAPA_DNI.items():
            if pl in pelna_nazwa.upper():
                skrot_dnia = skr
                break
        return f"{nazwa_okresu} {tydzien_arabski} {skrot_dnia}"
    
    # 2. Jeżeli NIE MA numeru tygodnia (daty, Wielki Tydzień itp.)
    else:
        # Usuwamy ewentualne końcówki jak ", ROK I", ", ROK A" itp.
        czysta = re.sub(r',?\s*ROK\s+[IVXABC12]+', '', pelna_nazwa, flags=re.IGNORECASE)
        # Zamienia na ładny tytuł (np. 20 Grudnia, Wielki Wtorek)
        return f"{nazwa_okresu} {czysta.strip().title()}"

def formatuj_blok(linie):
    """Formatowanie tekstu Psalmu i Aklamacji"""
    wynik = []
    i = 0
    naglowek_zapisany = False
    while i < len(linie):
        linia = linie[i].strip()
        if not linia:
            i += 1
            continue
        if linia in ["*", "†"]:
            if wynik: wynik[-1] = wynik[-1] + linia
            i += 1
            continue
        if linia.upper() == "REFREN:":
            if i + 1 < len(linie):
                tekst_refrenu = linie[i+1].strip()
                if not naglowek_zapisany:
                    wynik.append(f"Refren: {tekst_refrenu}")
                    wynik.append("") 
                    naglowek_zapisany = True
                else:
                    wynik.append("") 
                i += 2
                continue
        if linia.upper() == "AKLAMACJA:":
            if i + 1 < len(linie):
                tekst_aklamacji = linie[i+1].strip()
                if not naglowek_zapisany:
                    wynik.append(f"Aklamacja: {tekst_aklamacji}")
                    wynik.append("") 
                    naglowek_zapisany = True
                i += 2
                continue
        wynik.append(linia)
        i += 1
    czysty_wynik = []
    for linia in wynik:
        if linia == "" and (not czysty_wynik or czysty_wynik[-1] == ""):
            continue
        czysty_wynik.append(linia)
    while czysty_wynik and czysty_wynik[-1] == "":
        czysty_wynik.pop()
    return "\n".join(czysty_wynik)

def pobierz_tresc_dnia(url, nazwa_okresu, nazwa_roku):
    headers = {"User-Agent": "Mozilla/5.0"}
    try:
        response = requests.get(url, headers=headers)
        response.raise_for_status()
        soup = BeautifulSoup(response.content, "html.parser")
        
        pelna_nazwa_dnia = "Nieznany"
        article = soup.find('article', class_='article')
        if article:
            for tag in article.find_all(['span', 'strong', 'b']):
                tekst = tag.get_text(strip=True)
                if tekst and len(tekst) > 4 and "CZYTANIE" not in tekst.upper() and "PSALM" not in tekst.upper():
                    pelna_nazwa_dnia = tekst
                    break
        
        tydzien_rzymski = wyciagnij_tydzien(pelna_nazwa_dnia)
        krotki_tytul = stworz_krotki_tytul(pelna_nazwa_dnia, nazwa_okresu, tydzien_rzymski)
        
        print(f" -> Pobrałem: {krotki_tytul}")
        
        tekst_strony = soup.get_text(separator="\n").split("\n")
        psalm_linie, aklamacja_linie = [], []
        zapisuj_psalm = zapisuj_aklamacje = False
        
        for linia in tekst_strony:
            linia_czysta = linia.strip()
            if not linia_czysta: continue
            if "ŚPIEW PRZED EWANGELIĄ" in linia_czysta.upper() or "AKLAMACJA PRZED EWANGELIĄ" in linia_czysta.upper():
                zapisuj_psalm, zapisuj_aklamacje = False, True
                continue
            if "EWANGELIA" in linia_czysta.upper() and zapisuj_aklamacje: break 
            if "PSALM RESPONSORYJNY" in linia_czysta.upper():
                zapisuj_psalm = True
                continue
            if zapisuj_psalm: psalm_linie.append(linia_czysta)
            if zapisuj_aklamacje: aklamacja_linie.append(linia_czysta)
        
        BAZA_DANYCH.append({
            "okres": nazwa_okresu,
            "tydzien": MAPA_RZYMSKIE.get(tydzien_rzymski, tydzien_rzymski) if tydzien_rzymski != "Brak" else "",
            "cykl": nazwa_roku,
            "dzien": krotki_tytul,
            "psalm_responsoryjny": formatuj_blok(psalm_linie),
            "aklamacja": formatuj_blok(aklamacja_linie),
            "zrodlo": url
        })
    except Exception as e:
        print(f"Błąd dla {url}: {e}")

def skanuj_okresy():
    for rok in ROKI:
        for id_okresu, nazwa_okresu in OKRESY.items():
            url_szukaj = f"https://www.paulus.org.pl/czytania,szukaj?rok={rok}&okres={id_okresu}"
            print(f"\n--- Skanuję: {nazwa_okresu} (Cykl {rok}) ---")
            try:
                res = requests.get(url_szukaj, headers={"User-Agent": "Mozilla/5.0"})
                soup = BeautifulSoup(res.content, "html.parser")
                sekcja_linkow = soup.find('section', class_='readings-table') or soup
                for a in sekcja_linkow.find_all('a', href=True):
                    href = a['href']
                    if "czytania," in href and "szukaj" not in href and "uroczystosci" not in href:
                        pelny_url = f"https://www.paulus.org.pl/{href.lstrip('/')}"
                        pobierz_tresc_dnia(pelny_url, nazwa_okresu, rok)
                        time.sleep(0.4) 
            except Exception as e:
                print(f"Błąd: {e}")

skanuj_okresy()
with open("baza_czytan_skrocona.json", "w", encoding="utf-8") as f:
    json.dump(BAZA_DANYCH, f, ensure_ascii=False, indent=4)
print("\nZakończono.")