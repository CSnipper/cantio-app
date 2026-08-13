/**
 * Scraper śpiewnika Siedleckiego
 * Wynik: tools/siedlecki-songs.json
 *
 * Uruchomienie:
 *   cd C:\Users\konta\source\repos\Cantio\tools
 *   node scrape-siedlecki.js
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE_URL = 'https://spiewniksiedleckiego.pl';
const CATEGORIES_PAGE = `${BASE_URL}/?page_id=17`;
const OUTPUT_FILE = path.join(__dirname, 'siedlecki-songs.json');

// page_id kategorii (nie-pieśni) — pomijamy je przy skanowaniu linków
const CATEGORY_PAGE_IDS = new Set([
    '17', '2632',
    '41', '252', '419', '3164', '5786',
    '480', '540', '604', '5797',
    '652', '788', '804', '815',
    '3516', '3114', '11223', '3321', '4907',
    '5871',
    '3610', '8307', '8309', '8311',
    '4008',
    '5767', '5753', '1087', '5750', '5756',
    '4260', '8437',
    '852', '4000', '3979', '8453',
    '3545', '1110', '2054',
    '1186', '3265', '3267', '1219',
    '3240', '5195', '307',
    '6571', '6634', '6642', '6563', '6308',
]);

const CATEGORY_LABELS = {
    '41':    'Adwent',
    '252':   'Boże Narodzenie',
    '419':   'Pastorałki',
    '3164':  'Okres Narodzenia Pańskiego',
    '5786':  'Ofiarowanie Pańskie',
    '480':   'Wielki Post',
    '540':   'Pasyjne',
    '604':   'Gorzkie żale',
    '5797':  'Wielki Tydzień',
    '652':   'Wielkanocne',
    '788':   'Miłosierdzie Boże',
    '804':   'Wniebowstąpienie',
    '815':   'Duch Święty',
    '3516':  'Mszalne',
    '3114':  'Hymny',
    '11223': 'Suplikacje',
    '3321':  'Cykle mszalne',
    '4907':  'Psalmy',
    '5871':  'Pokropienie',
    '4008':  'Litanie',
    '5753':  'Do Trójcy',
    '5767':  'Do Jezusa Wiecznego Kapłana',
    '1087':  'Do Serca Jezusa',
    '5750':  'Przemienienie Pańskie',
    '5756':  'Chrystusa Króla',
    '4260':  'Koronka Miłosierdzia',
    '852':   'Do Maryi Panny',
    '3979':  'Różaniec',
    '4000':  'Godzinki',
    '3610':  'Nieszpory I',
    '8307':  'Nieszpory II',
    '8309':  'Nieszpory III',
    '8311':  'Nieszpory IV',
    '8437':  'Nieszpory o Najśw. Sakramencie',
    '8453':  'Nieszpory o NMP',
    '3545':  'Taizé',
    '1110':  'Eucharystyczne',
    '2054':  'Przygodne',
    '1186':  'O Świętych',
    '3265':  'Św. Józef',
    '3267':  'Św. Aniołowie',
    // „Pieśni własne o Świętych" są rozbite na 6 stron (437–572), ale w menu
    // widnieje TYLKO pierwsza — pozostałe da się znaleźć jedynie przez wyszukiwarkę
    // strony. Bez nich ginie 136 pieśni, czyli największa luka w numeracji.
    '1219':  'Własne o Świętych',
    '6571':  'Własne o Świętych',
    '6634':  'Własne o Świętych',
    '6642':  'Własne o Świętych',
    '6563':  'Własne o Świętych',
    '6308':  'Własne o Świętych',
    '3240':  'Za zmarłych',
    '5195':  'Pogrzeb',
    '307':   'Łacińskie',
};

async function delay(ms) {
    return new Promise(r => setTimeout(r, ms));
}

/**
 * Dla danej strony kategorii: zbiera tytuły i teksty pieśni.
 *
 * Strona używa DWÓCH układów akordeonu i część stron ma oba naraz
 * (np. Maryja: 13 Elementor + 80 Bootstrap):
 *   - Elementor: .elementor-tab-title  → aria-controls="elementor-tab-content-N"
 *   - Bootstrap: a[href="#collapse-…"] → panel o tym samym id
 * Treść obu jest w surowym HTML (akordeon tylko ją ukrywa), więc NIE klikamy —
 * czytamy textContent od razu. Klikanie było źródłem powolności i zawodności.
 */
async function scrapeCategoryPage(page, url, categoryLabel) {
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await delay(500);

    const raw = await page.evaluate(() => {
        // UWAGA: panele akordeonu są ukryte (display:none), a `innerText` na
        // ukrytym elemencie degraduje się do `textContent` i GUBI łamania linii —
        // refren skleja się ze zwrotką ("odkupienie.1. Z głębokości…").
        // Dlatego łamania odtwarzamy z HTML, zamiast ufać innerText.
        const readText = (panel) => {
            if (!panel) return '';
            const clone = panel.cloneNode(true);
            clone.querySelectorAll('br').forEach(br => br.replaceWith('\n'));
            clone.querySelectorAll('p, div, li, h1, h2, h3, h4, h5, h6')
                .forEach(el => el.append('\n\n'));
            return clone.textContent;
        };

        const items = [];

        // 1) Elementor
        document.querySelectorAll('.elementor-tab-title').forEach(t => {
            const id = t.getAttribute('aria-controls');
            items.push({
                title: t.textContent || '',
                text: readText(id ? document.getElementById(id) : null),
            });
        });

        // 2) Bootstrap collapse
        document.querySelectorAll('a[href^="#collapse-"], a[href^="#Collapse-"]').forEach(a => {
            const id = a.getAttribute('href').slice(1);
            const panel = document.getElementById(id)
                || document.querySelector(`[id="${id}" i]`);
            items.push({ title: a.textContent || '', text: readText(panel) });
        });

        return items;
    });

    const songs = [];
    const seen = new Set();

    for (const item of raw) {
        const rawTitle = item.title.replace(/\s+/g, ' ').trim();
        if (!rawTitle) continue;

        // Nagłówki menu Elementora, nie pieśni
        if (/^(PIEŚNI|MSZA|O NAS)$/i.test(rawTitle)) continue;

        // Nagłówki części Różańca ("Część I. Tajemnice radosne – …") —
        // elementy strukturalne akordeonu, nie pieśni.
        if (/^Część\s+[IVXLC]+\s*[.)]/i.test(rawTitle)) continue;

        // Numer porządkowy: "1.", "155b.", "805a.", "1D." (pastorałki),
        // "779ł." (Nieszpory — sufiks bywa polską literą)
        const m = rawTitle.match(/^(\d+\s*[a-zA-ZąćęłńóśźżĄĆĘŁŃÓŚŹŻ]?)\s*[.)]\s*/);
        const number = m ? m[1].replace(/\s+/g, '') : null;
        const title = m ? rawTitle.slice(m[0].length).trim() : rawTitle;
        if (!title) continue;

        // Strony z dwoma układami potrafią wystawić tę samą pieśń dwa razy
        const key = `${number || ''}|${title.toLowerCase()}`;
        if (seen.has(key)) continue;
        seen.add(key);

        const songText = item.text
            .replace(/\r\n/g, '\n')
            .replace(/[ \t ]+/g, ' ')
            .split('\n').map(l => l.trim()).join('\n')
            .replace(/\n{3,}/g, '\n\n')
            .replace(/Odtwarzacz plików dźwiękowych[\s\S]*$/m, '')
            .trim();

        songs.push({ category: categoryLabel, number, title, text: songText, url });
    }

    const bezTekstu = songs.filter(s => !s.text).length;
    const bezNumeru = songs.filter(s => !s.number).length;
    console.log(`  ${songs.length} pieśni` +
        (bezTekstu ? ` (⚠ ${bezTekstu} bez tekstu)` : '') +
        (bezNumeru ? ` (⚠ ${bezNumeru} bez numeru)` : ''));

    return songs;
}

async function main() {
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
        userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36'
    });
    const page = await context.newPage();

    const allSongs = [];

    const categoryIds = Object.keys(CATEGORY_LABELS);
    console.log(`Scrapuję ${categoryIds.length} kategorii...\n`);

    for (const id of categoryIds) {
        const label = CATEGORY_LABELS[id];
        const url = `${BASE_URL}/?page_id=${id}`;
        console.log(`▶ [${label}] (page_id=${id})`);
        try {
            const songs = await scrapeCategoryPage(page, url, label);
            allSongs.push(...songs);
            console.log(`  → ${songs.length} pieśni dodano\n`);
        } catch (e) {
            console.error(`  BŁĄD: ${e.message}\n`);
        }
    }

    await browser.close();

    fs.writeFileSync(OUTPUT_FILE, JSON.stringify(allSongs, null, 2), 'utf8');
    console.log(`\n✓ Łącznie: ${allSongs.length} pieśni → ${OUTPUT_FILE}`);

    // Kontrola kompletności: śpiewnik numeruje pieśni od 1 do 805 (ostatnia
    // pozycja to pogrzeb 805a–805k). Luka = kategoria pominięta albo zepsuty parser.
    const found = new Set();
    for (const s of allSongs) {
        if (s.number) found.add(parseInt(s.number, 10));
    }
    const gaps = [];
    for (let i = 1; i <= 805; i++) if (!found.has(i)) gaps.push(i);

    // Biała lista: numery, których serwis NIE publikuje (sprawdzone ręcznie).
    // 74 brakuje wewnątrz Narodzenia (30–78), 795–796 wewnątrz Litanii (793–804),
    // pozostałe leżą między kategoriami i nie ma dla nich żadnej strony.
    // Luka SPOZA tej listy = pominięta kategoria albo zepsuty parser.
    const BRAK_NA_STRONIE = new Set([
        74,
        666, 667, 668, 669, 670, 671,
        686, 687,
        742, 743, 744, 745, 746, 747, 748, 749, 750,
        751, 752, 753, 754, 755, 756, 757,
        789, 795, 796,
    ]);
    const nieoczekiwane = gaps.filter(n => !BRAK_NA_STRONIE.has(n));

    console.log(`  numery: ${found.size}/805 pokrytych ` +
        `(${gaps.length} brak, w tym ${gaps.length - nieoczekiwane.length} znanych braków serwisu)`);
    if (nieoczekiwane.length) {
        console.log(`  ⚠ NIEOCZEKIWANE luki (${nieoczekiwane.length}): ${nieoczekiwane.join(', ')}`);
    } else {
        console.log('  ✓ pokrycie pełne wobec tego, co serwis publikuje');
    }
    const bezNumeru = allSongs.filter(s => !s.number);
    if (bezNumeru.length) {
        console.log(`  ⚠ ${bezNumeru.length} pozycji bez numeru: ` +
            bezNumeru.slice(0, 10).map(s => `${s.category}/${s.title}`).join(', '));
    }
}

main().catch(console.error);
