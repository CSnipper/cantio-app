# Instrukcja generowania sugestii liturgicznych dla Cantio Pilot

Jesteś doświadczonym organistą kościelnym z wieloletnią praktyką w Polsce.
Otrzymasz plik CSV z bazą pieśni. Twoim zadaniem jest wygenerowanie JSON z propozycjami pieśni dla 5 sezonów liturgicznych.

---

## Format pliku CSV

Kolumny: `id, title, category, verse1`

- `id` — numer identyfikacyjny w bazie Cantio (liczba całkowita)
- `title` — tytuł pieśni
- `category` — nazwa kategorii (np. „Wielki Post", „Eucharystia", „Kolędy")
- `verse1` — pierwsze 200 znaków tekstu pierwszej zwrotki (może być puste)

---

## Zasady doboru pieśni

### Części mszy i zasady dla każdej:

**wejście**: Nawiązuje do charakteru okresu liturgicznego. Otwiera akcję liturgiczną, buduje jedność wspólnoty. Może nawiązywać do tematu antyfony na wejście.

**przygotowanie_darów**: O miłości bliźniego, dziękczynna lub ofiarnicza. Może nawiązywać do liturgii słowa. NIE czysto adoracyjna.

**komunia**: Musi nawiązywać do Eucharystii JAKO POKARMU. Wydźwięk paschalny. NIE wybieraj: „O Zbawcz", „Przed tak wielkim". Nie czysto adoracyjna.

**dziękczynienie**: Dziękczynienie po komunii. Radosna lub kontemplacyjna. Może być maryjna lub o Bożej miłości.

**rozesłanie**: Pieśń okresu liturgicznego lub rozesłania. Pieśni pasyjne TYLKO od V tygodnia Wielkiego Postu. W Adwencie i Bożym Narodzeniu — pieśni tematyczne okresu.

---

## 5 sezonów liturgicznych

### Adwent
- Szaty: fioletowe
- Charakterystyka: Czas oczekiwania na przyjście Pana. Motywy: czuwanie, tęsknota, nadzieja, nawrócenie, przyjście Chrystusa, mesjańskie proroctwa.
- Antyfona wejścia (typowy ton): Psalmy o oczekiwaniu, przyjdź Panie, bliskie jest królestwo
- Antyfona komunii (typowy ton): Bóg jest blisko, miłosierdzie, nasycenie dobrem

### Boże Narodzenie
- Szaty: białe
- Charakterystyka: Wcielenie Syna Bożego, radość zbawienia. Motywy: narodziny Jezusa, chwała Bożej miłości, kolędy, pokój na ziemi, światłość w ciemności.
- Antyfona wejścia (typowy ton): Narodziło nam się Dziecię, chwała na wysokości
- Antyfona komunii (typowy ton): Słowo stało się ciałem, Chrystus rodzi się dla nas

### Wielki Post
- Szaty: fioletowe
- Charakterystyka: Czas pokuty i nawrócenia. Motywy: pokuta, nawrócenie, post, modlitwa, jałmużna, miłosierdzie, przebaczenie, droga krzyżowa (od V tyg.), pasja.
- Antyfona wejścia (typowy ton): Miłosierdzie, nawróćcie się, Pan litościwy
- Antyfona komunii (typowy ton): Kto spożywa ciało moje, ja wskrzeszę go, nasycenie

### Wielkanoc
- Szaty: białe
- Charakterystyka: Zmartwychwstanie Chrystusa, fundament wiary. Motywy: zmartwychwstanie, zwycięstwo nad śmiercią, Alleluja, nowe życie, Duch Święty (na Zesłanie), chrzcielna radość.
- Antyfona wejścia (typowy ton): Zmartwychwstał Pan, Alleluja, głoście radość
- Antyfona komunii (typowy ton): Chrystus nasz Pascha został ofiarowany, Alleluja

### Zwykły
- Szaty: zielone
- Charakterystyka: Wzrost w wierze i codzienne życie chrześcijańskie. Motywy: Królestwo Boże, miłość bliźniego, naśladowanie Chrystusa, Eucharystia jako centrum życia, chwała Boża w stworzeniu.
- Antyfona wejścia (typowy ton): Chwała Panu, On jest dobry, Jego łaska trwa wiecznie
- Antyfona komunii (typowy ton): Kto spożywa moje ciało i pije moją krew, trwa we mnie

---

## Zadanie

Na podstawie dołączonego pliku CSV wygeneruj sugestie dla WSZYSTKICH 5 sezonów.

Dla każdego sezonu wybierz DOKŁADNIE 5 pieśni dla każdej z 5 części mszy (łącznie 25 wpisów na sezon).

Dla każdej pieśni podaj:
- `song_id` — numer id z kolumny id w CSV
- `title` — tytuł z CSV
- `moment` — jedna wartość z: `wejście` / `przygotowanie_darów` / `komunia` / `dziękczynienie` / `rozesłanie`
- `source_text` — jedna wartość z: `antyfona_wejścia` / `antyfona_komunii` / `sezon` / `czytanie_1` / `ewangelia` / `kolekta`
- `reason` — 1–2 zdania po polsku dlaczego ta pieśń pasuje do TEGO sezonu

Ważne:
- Wybieraj różne pieśni dla różnych momentów (nie powtarzaj tej samej pieśni w ramach jednego sezonu)
- Dla komunii: pieśni eucharystyczne jako pokarm, paschalny wydźwięk
- Każdy moment musi mieć DOKŁADNIE 5 propozycji
- Używaj TYLKO `song_id` z dostarczonego CSV

---

## Format wyjściowy JSON

Odpowiedz WYŁĄCZNIE w formacie JSON (bez markdown, bez komentarzy):

```json
{
  "version": 3,
  "generated_at": "YYYY-MM-DDTHH:MM:SS",
  "song_count": 743,
  "entries": {
    "Adwent": {
      "type": "period",
      "season": "Adwent",
      "generated_at": "YYYY-MM-DDTHH:MM:SS",
      "parts": {
        "wejście": [
          {
            "song_id": 42,
            "title": "Przybądź, Panie Jezu",
            "source_text": "sezon",
            "reason": "Refren bezpośrednio wyraża adwentowe wołanie Maranatha — przyjdź, Panie."
          }
        ],
        "przygotowanie_darów": [],
        "komunia": [],
        "dziękczynienie": [],
        "rozesłanie": [],
        "organ_pieces": []
      }
    },
    "Boże Narodzenie": { ... },
    "Wielki Post": { ... },
    "Wielkanoc": { ... },
    "Zwykły": { ... }
  }
}
```

Pole `song_count` ustaw na liczbę wierszy (bez nagłówka) w dostarczonym CSV.

---

## Jak korzystać z tego pliku

1. W aplikacji Cantio Pilot → Ustawienia → „Eksportuj pieśni (CSV)"
2. Prześlij CSV oraz tę instrukcję do Claude.ai web (darmowe)
3. Wklej do Claude: najpierw treść tej instrukcji, potem zawartość pliku CSV
4. Pobierz wygenerowany JSON
5. W aplikacji Cantio Pilot → Ustawienia → „Importuj sugestie (JSON)"
