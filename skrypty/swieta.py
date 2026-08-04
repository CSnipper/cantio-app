import requests
from bs4 import BeautifulSoup
import json
import time

BAZA_DANYCH = []

def formatuj_blok(linie):
    """Zgrabne formatowanie tekstu"""
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


def pobierz_tresc_uroczystosci(url, nazwa_dnia):
    """Pobiera konkretne czytanie dla uroczystości z danego linku"""
    headers = {"User-Agent": "Mozilla/5.0"}
    
    try:
        response = requests.get(url, headers=headers)
        response.raise_for_status()
    except Exception as e:
        print(f"Błąd dla {url}: {e}")
        return
        
    soup = BeautifulSoup(response.content, "html.parser")
    tekst_strony = soup.get_text(separator="\n").split("\n")
    
    psalm_linie = []
    aklamacja_linie = []
    zapisuj_psalm = False
    zapisuj_aklamacje = False
    
    for linia in tekst_strony:
        linia_czysta = linia.strip()
        if not linia_czysta:
            continue
            
        if "ŚPIEW PRZED EWANGELIĄ" in linia_czysta.upper() or "AKLAMACJA PRZED EWANGELIĄ" in linia_czysta.upper():
            zapisuj_psalm = False
            zapisuj_aklamacje = True
            continue
            
        if "EWANGELIA" in linia_czysta.upper() and zapisuj_aklamacje:
            break 
            
        if "PSALM RESPONSORYJNY" in linia_czysta.upper():
            zapisuj_psalm = True
            continue
            
        # UWAGA: Ten warunek blokuje pobieranie drugiego czytania i sekwencji!
        if "DRUGIE CZYTANIE" in linia_czysta.upper() or "SEKWENCJA" in linia_czysta.upper():
            zapisuj_psalm = False
            continue
            
        if zapisuj_psalm: psalm_linie.append(linia_czysta)
        if zapisuj_aklamacje: aklamacja_linie.append(linia_czysta)
            
    gotowy_psalm = formatuj_blok(psalm_linie)
    gotowa_aklamacja = formatuj_blok(aklamacja_linie)
    
    cykl = "Stałe/Własne"
    nazwa_upper = nazwa_dnia.upper()
    if "ROK A" in nazwa_upper: cykl = "A"
    elif "ROK B" in nazwa_upper: cykl = "B"
    elif "ROK C" in nazwa_upper: cykl = "C"
    
    BAZA_DANYCH.append({
        "okres": "Uroczystości i Święta",
        "cykl": cykl,
        "dzien": nazwa_dnia,
        "psalm_responsoryjny": gotowy_psalm if gotowy_psalm else None,
        "aklamacja": gotowa_aklamacja if gotowa_aklamacja else None,
        "zrodlo": url
    })


# ==========================================
# GŁÓWNA PĘTLA URUCHOMIENIOWA
# ==========================================

url_glowny = "https://www.paulus.org.pl/czytania,uroczystosci"
headers = {"User-Agent": "Mozilla/5.0"}

print("Pobieram listę uroczystości i wspomnień...")
odpowiedz = requests.get(url_glowny, headers=headers)
zupa = BeautifulSoup(odpowiedz.content, "html.parser")

znaleziono = 0
slowa_klucze = ["UROCZYSTOŚĆ", "ŚWIĘTO", "WSPOMNIENIE", "WIGILIA", "ROK A", "ROK B", "ROK C", 
                "STYCZEŃ", "LUTY", "MARZEC", "KWIECIEŃ", "MAJ", "CZERWIEC", 
                "LIPIEC", "SIERPIEŃ", "WRZESIEŃ", "PAŹDZIERNIK", "LISTOPAD", "GRUDZIEŃ"]

for a in zupa.find_all('a', href=True):
    href = a['href']
    nazwa = a.get_text(strip=True)
    nazwa_upper = nazwa.upper()
    
    if len(nazwa) > 10 and any(slowo in nazwa_upper for slowo in slowa_klucze):
        if href.startswith('http'):
            pelny_url = href
        else:
            pelny_url = f"https://www.paulus.org.pl/{href.lstrip('/')}"
            
        print(f" -> Pobieram: {nazwa}")
        pobierz_tresc_uroczystosci(pelny_url, nazwa)
        znaleziono += 1
        time.sleep(1) 

nazwa_pliku_json = "baza_uroczystosci.json"
with open(nazwa_pliku_json, "w", encoding="utf-8") as plik_json:
    json.dump(BAZA_DANYCH, plik_json, ensure_ascii=False, indent=4)

print(f"\nGotowe! Pomyślnie pobrano {znaleziono} uroczystości i zapisano w pliku {nazwa_pliku_json}.")