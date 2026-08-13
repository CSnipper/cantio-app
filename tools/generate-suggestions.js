/**
 * Generuje liturgical_suggestions.json na podstawie spiewnik_siedleckiego.csv
 *
 * Uruchomienie:
 *   node tools/generate-suggestions.js
 */

const fs = require('fs');
const path = require('path');

const CSV_PATH   = path.join(__dirname, '..', 'spiewnik_siedleckiego.csv');
const OUT_PATH   = path.join('C:\\Users\\konta\\AndroidStudioProjects\\CantioPilot\\app\\src\\main\\assets\\liturgical_suggestions.json');

// Parsowanie prostego CSV (uwzględnia wieloliniowe pola w cudzysłowach)
function parseCsv(text) {
    const rows = [];
    let i = 0;
    // Pomiń BOM
    if (text.charCodeAt(0) === 0xFEFF) i = 1;

    // Pomiń nagłówek
    while (i < text.length && text[i] !== '\n') i++;
    i++; // skip \n

    while (i < text.length) {
        const row = {};
        const keys = ['kategoria', 'numer', 'tytul', 'tekst'];
        for (const key of keys) {
            if (i >= text.length) break;
            if (text[i] === '"') {
                // Pole w cudzysłowach
                i++; // pomiń otwierający "
                let val = '';
                while (i < text.length) {
                    if (text[i] === '"' && text[i + 1] === '"') {
                        val += '"'; i += 2;
                    } else if (text[i] === '"') {
                        i++; break; // zamykający "
                    } else {
                        val += text[i++];
                    }
                }
                row[key] = val;
            } else {
                let val = '';
                while (i < text.length && text[i] !== ',' && text[i] !== '\n') {
                    val += text[i++];
                }
                row[key] = val;
            }
            if (text[i] === ',') i++;
        }
        // Pomiń resztę linii
        while (i < text.length && text[i] !== '\n') i++;
        if (text[i] === '\n') i++;

        if (row.tytul) rows.push(row);
    }
    return rows;
}

// ── Mapowanie kategorii CSV na okresy liturgiczne ────────────────────────────

const CATEGORY_SEASON = {
    'Pieśni na Adwent':           'Adwent',
    'Narodzenie Pańskie':         'Boże Narodzenie',
    'Pastorałki':                 'Boże Narodzenie',
    'Pieśni Wielkanocne':         'Wielkanoc',
    'Pieśni Eucharystyczne':      null,   // specjalne traktowanie — komunia/ofiarowanie
    'Pieśni do Maryi Panny':      null,   // różne uroczystości
    'Pieśni Przygodne':           'Zwykły',
    'Pieśni za zmarłych':         'Za zmarłych',
    'Śpiewy w języku łacińskim':  null,   // pomijamy — w bazie usera nie będzie tytułów łac.
};

// Moment domyślny dla kategorii
const CATEGORY_MOMENT = {
    'Pieśni na Adwent':       'wejście',
    'Narodzenie Pańskie':     'wejście',
    'Pastorałki':             'wejście',
    'Pieśni Wielkanocne':     'wejście',
    'Pieśni Eucharystyczne':  'komunia',
    'Pieśni do Maryi Panny':  'komunia',
    'Pieśni Przygodne':       'wejście',
    'Pieśni za zmarłych':     'wejście',
};

// Pieśni komunijne / ofiarowania na podstawie słów kluczowych w tytule lub tekście
const COMMUNION_KEYWORDS = [
    'kłaniam', 'zbliżam', 'przed tak', 'bądźże pozdrowion', 'o salutaris',
    'tantum ergo', 'sakrament', 'euchar', 'komunia', 'chleb', 'ciało',
    'krew pańsk', 'boże ciało', 'baranku', 'agnus', 'przyjmuję',
];

const OFFERTORY_KEYWORDS = [
    'ofiaruję', 'dary', 'przyjmij', 'ofiara', 'ofiaruj',
];

function getMoment(song) {
    const base = CATEGORY_MOMENT[song.kategoria] || 'wejście';
    if (song.kategoria === 'Pieśni Eucharystyczne') {
        const lower = (song.tytul + ' ' + song.tekst).toLowerCase();
        if (OFFERTORY_KEYWORDS.some(k => lower.includes(k))) return 'ofiarowanie';
        if (COMMUNION_KEYWORDS.some(k => lower.includes(k))) return 'komunia';
        // Domyślnie eucharystyczne = komunia
        return 'komunia';
    }
    return base;
}

// ── Główna logika ────────────────────────────────────────────────────────────

const csvText = fs.readFileSync(CSV_PATH, 'utf8');
const songs = parseCsv(csvText);
console.log(`Wczytano ${songs.length} pieśni`);

// Grupuj po sezonie
const bySeason = {};

for (const song of songs) {
    const season = CATEGORY_SEASON[song.kategoria];
    if (!season) continue; // pomijamy eucharystyczne i łacińskie — obsługujemy osobno

    if (!bySeason[season]) bySeason[season] = [];
    bySeason[season].push({
        title: song.tytul,
        moment: getMoment(song)
    });
}

// Eucharystyczne — dodaj je do każdego sezonu jako komunia
const eucharistic = songs
    .filter(s => s.kategoria === 'Pieśni Eucharystyczne')
    .map(s => ({ title: s.tytul, moment: getMoment(s) }));

// Maryjna — mapuj do konkretnych uroczystości
const marian = songs
    .filter(s => s.kategoria === 'Pieśni do Maryi Panny')
    .map(s => ({ title: s.tytul, moment: 'wejście' }));

// Zbuduj listę wpisów suggestions
const entries = [];

// 1. Sezony z CSV
for (const [season, titles] of Object.entries(bySeason)) {
    entries.push({ period: season, titles });
}

// 2. Eucharystyczne — wpis globalny (pasuje do każdego sezonu, moment=komunia)
entries.push({
    period: 'Eucharystyczne',
    titles: eucharistic
});

// 3. Maryja — główne uroczystości
const MARIAN_FEASTS = [
    { dayKey: 'Wniebowzięcie NMP',              titles: marian },
    { dayKey: 'Niepokalane Poczęcie NMP',        titles: marian },
    { dayKey: 'Narodzenie NMP',                  titles: marian },
    { dayKey: 'Matki Bożej Różańcowej',          titles: marian },
    { dayKey: 'Matki Bożej Nieustającej Pomocy', titles: marian },
];
for (const feast of MARIAN_FEASTS) {
    entries.push(feast);
}

// 4. Wielki Post i Duch Święty — placeholder (brak w CSV)
entries.push({
    period: 'Wielki Post',
    titles: [
        { title: 'W krzyżu cierpienie',      moment: 'uwielbienie' },
        { title: 'Jezu, do Ciebie biec',     moment: 'wejście' },
        { title: 'Zbliżam się w pokorze',    moment: 'komunia' },
        { title: 'Kłaniam się Tobie',        moment: 'komunia' },
        { title: 'Z tej biednej ziemi',      moment: 'wejście' },
        { title: 'Przed tak wielkim',        moment: 'komunia' },
    ]
});
entries.push({
    dayKey: 'Zesłanie Ducha Świętego',
    titles: [
        { title: 'Przybądź Duchu Święty',   moment: 'wejście' },
        { title: 'Duchu Święty, przyjdź',   moment: 'wejście' },
        { title: 'Veni Creator Spiritus',   moment: 'wejście' },
        { title: 'Przyjdź, Duchu Święty',   moment: 'wejście' },
    ]
});
entries.push({
    dayKey: 'Wielki Piątek',
    titles: [
        { title: 'Któryś za nas cierpiał rany', moment: 'wejście' },
        { title: 'W krzyżu cierpienie',          moment: 'uwielbienie' },
        { title: 'Ludu mój ludu',                moment: 'komunia' },
    ]
});
entries.push({
    dayKey: 'Niedziela Zmartwychwstania',
    titles: [
        { title: 'Wesoły nam dzień nastał',         moment: 'wejście' },
        { title: 'Chrystus zmartwychwstan jest',    moment: 'wejście' },
        { title: 'Alleluja, śpiewajmy',             moment: 'wejście' },
    ]
});

// 5. Za zmarłych
const zmarliSongs = songs
    .filter(s => s.kategoria === 'Pieśni za zmarłych')
    .map(s => ({ title: s.tytul, moment: 'wejście' }));
entries.push({ dayKey: 'Za Wszystkich Wiernych Zmartwychwstałych', titles: zmarliSongs });

// Zapisz wynik
fs.writeFileSync(OUT_PATH, JSON.stringify(entries, null, 2), 'utf8');
console.log(`\n✓ Zapisano ${entries.length} wpisów → ${OUT_PATH}`);

// Statystyki
const totalTitles = entries.reduce((sum, e) => sum + (e.titles ? e.titles.length : 0), 0);
console.log(`  Łącznie tytułów: ${totalTitles}`);
entries.forEach(e => {
    const label = e.dayKey || e.period;
    console.log(`  [${label}] ${e.titles ? e.titles.length : 0} pieśni`);
});
