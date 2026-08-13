/**
 * Scraper lekcjonarza z niezbednik.niedziela.pl
 * Wynik: tools/lekcjonarz-niezbednik.json  (NOWY plik — nie rusza czytania.json.gz)
 *
 * Serwis indeksuje czytania DATĄ, a baza Cantio kluczem liturgicznym ("ZW 18 Nie A").
 * Mapę data→klucz liczy RDZEŃ (LiturgicalCalendarService) — nie parsujemy polskich nazw
 * ze strony, bo scraper musi widzieć dokładnie ten sam dzień co aplikacja.
 *
 * Przygotowanie mapy (scratchpad/keygen, projekt referuje Cantio.Core):
 *   dotnet run -c Release -- 2016-11-27 2026-08-04 > tools/tmp/date-keys.json
 *
 * Uruchomienie:
 *   cd C:\Users\konta\source\repos\Cantio\tools
 *   node scrape-lekcjonarz.js
 *   node scrape-lekcjonarz.js --limit 40      # próbka
 *   node scrape-lekcjonarz.js --resume        # dopisz do istniejącego wyniku
 */

const fs = require('fs');
const path = require('path');
const cheerio = require('cheerio');

const BASE = 'https://niezbednik.niedziela.pl/liturgia';
const KEYS_FILE = path.join(__dirname, 'tmp', 'date-keys.json');
const OUTPUT_FILE = path.join(__dirname, 'lekcjonarz-niezbednik.json');
const DELAY_MS = 700;          // uprzejmy odstęp między żądaniami
// Pola oczekiwane w KAŻDYM formularzu — służą tylko raportowi kompletności.
// Zestaw sekcji realnie obecnych na stronie jest zmienny (zob. parseLekcjonarz).
const SECTIONS = ['czytanie_1', 'psalm_responsoryjny', 'aklamacja', 'ewangelia'];

const args = process.argv.slice(2);
const LIMIT = args.includes('--limit') ? parseInt(args[args.indexOf('--limit') + 1], 10) : Infinity;
const RESUME = args.includes('--resume');

const delay = ms => new Promise(r => setTimeout(r, ms));

/** Tekst bloku: <br>/<p> → łamania linii (innerText tu nie ma, jesteśmy poza przeglądarką). */
function blockText($, el) {
    const $el = $(el).clone();
    $el.find('br').replaceWith('\n');
    $el.find('p, div, li').append('\n\n');
    return $el.text()
        .replace(/\r\n/g, '\n')
        .replace(/[ \t ]+/g, ' ')
        .split('\n').map(l => l.trim()).join('\n')
        .replace(/\n{3,}/g, '\n\n')
        .trim();
}

/**
 * Etykieta zakładki → pole bazy czytań.
 * Zwraca null dla „Całość" (to zbiorczy podgląd, nie osobna sekcja).
 */
function poleDlaEtykiety(label) {
    const t = label.replace(/\s+/g, ' ').trim();
    if (/^Całość$/i.test(t)) return null;
    if (/^Psalm/i.test(t)) return 'psalm_responsoryjny';
    if (/^Sekwencja/i.test(t)) return 'sekwencja';
    if (/^Aklamacja/i.test(t)) return 'aklamacja';
    if (/^Ewangelia/i.test(t)) return 'ewangelia';
    const m = t.match(/^(\d+)\.\s*czytanie/i);
    if (m) return `czytanie_${m[1]}`;
    return null;
}

/**
 * Jeden formularz jednej wersji lekcjonarza.
 *
 * UKŁAD SEKCJI JEST ZMIENNY i mapowanie po POZYCJI cicho psuje dane:
 * dzień powszedni nie ma 2. czytania (Aklamacja lądowała w `czytanie_2`,
 * a Ewangelia wypadała poza zakres), Wielkanoc dokłada Sekwencję, a Wigilia
 * Paschalna ma 5 czytań i kilka psalmów. Dlatego czytamy ETYKIETY zakładek.
 *
 * Zakres zawężamy do kontenera formularza, bo same id są niejednoznaczne:
 * `tabnowy10` to zarówno formularz 1 / sekcja 0, jak i formularz 0 / sekcja 10.
 *
 * Powtórzone sekcje (np. kilka psalmów Wigilii Paschalnej albo dwa warianty
 * 2. czytania) trafiają do `warianty[]` — nic nie ginie po cichu.
 */
function parseLekcjonarz($, wersja, formIdx) {
    const scope = $(`#tab${wersja}${formIdx}`);
    if (!scope.length) return null;

    const out = {};
    const warianty = [];

    scope.find('button.nav-link[data-bs-target]').each((_, btn) => {
        const pole = poleDlaEtykiety($(btn).text());
        if (!pole) return;

        const panel = $($(btn).attr('data-bs-target'));
        if (!panel.length) return;

        const naglowek = panel.find('h2').first().text().replace(/\s+/g, ' ').trim();
        const ref = (naglowek.match(/\(([\s\S]*)\)\s*$/) || [])[1] || null;
        const tytul = panel.find('h4').first().text().replace(/\s+/g, ' ').trim() || null;

        const body = panel.clone();
        body.find('h2').first().remove();
        body.find('h4').first().remove();
        const tekst = blockText($, body);
        if (!tekst) return;

        const sekcja = { ref, tytul, tekst };
        if (out[pole]) warianty.push({ pole, ...sekcja });
        else out[pole] = sekcja;
    });

    if (!Object.keys(out).length) return null;
    if (warianty.length) out.warianty = warianty;
    return out;
}

function parseDay(html, meta) {
    const $ = cheerio.load(html);

    const obchod = $('p.font-serif.fw-bold em').first().text().replace(/\s+/g, ' ').trim() || null;
    const kolor = ($('p.font-sans').filter((_, e) => /Kolor szat/.test($(e).text()))
        .first().find('span').text() || '').trim() || null;

    // Formularze mszalne ("Msza wigilii" / "Msza w dzień"). Gdy zakładek nie ma,
    // dzień ma jeden formularz o indeksie 0 i bez własnej nazwy.
    const nazwy = $('[id^="tabmsza"][id$="-tab"]')
        .map((_, e) => $(e).text().replace(/\s+/g, ' ').trim()).get();
    const liczba = Math.max(1, nazwy.length);

    const formularze = [];
    for (let i = 0; i < liczba; i++) {
        const nowy = parseLekcjonarz($, 'nowy', i);
        const stary = parseLekcjonarz($, 'stary', i);
        if (!nowy && !stary) continue;
        formularze.push({ nazwa: nazwy[i] || null, nowy, stary });
    }

    if (!formularze.length) return null;

    return {
        data: meta.data,
        dzien: meta.dzien,
        okres: meta.okres,
        cykl: meta.cykl,
        ranga: meta.ranga,
        obchod,
        kolor,
        formularze,
        zrodlo: `${BASE}/${meta.data}`,
    };
}

async function main() {
    if (!fs.existsSync(KEYS_FILE)) {
        console.error(`Brak ${KEYS_FILE} — najpierw wygeneruj mapę data→klucz (zob. nagłówek pliku).`);
        process.exit(1);
    }
    const dni = JSON.parse(fs.readFileSync(KEYS_FILE, 'utf8'));

    let wynik = [];
    if (RESUME && fs.existsSync(OUTPUT_FILE)) {
        wynik = JSON.parse(fs.readFileSync(OUTPUT_FILE, 'utf8'));
        console.log(`--resume: wczytano ${wynik.length} dni z poprzedniego przebiegu`);
    }

    // Dwie osie deduplikacji, bo dwie rzeczy powtarzają się różnym rytmem:
    //  - klucz temporalny (dzień+cykl) wraca co 1–3 lata,
    //  - sanktoral siedzi na MM-DD i wraca co rok.
    // Fetch robimy, gdy CHOĆ JEDNA oś wnosi coś nowego — inaczej po pierwszym roku
    // przestalibyśmy widzieć nowe cykle, a przy dedupie tylko po kluczu zgubilibyśmy
    // obchody przypisane do daty (np. 24 VI: wigilia + msza w dzień).
    const widzianeKlucze = new Set(wynik.map(d => `${d.dzien}|${d.cykl}`));
    const widzianeDaty = new Set(wynik.map(d => d.data.slice(5)));
    const zrobione = new Set(wynik.map(d => d.data));

    let pobrane = 0, pominiete = 0, bledy = 0;
    const zapisz = () => fs.writeFileSync(OUTPUT_FILE, JSON.stringify(wynik, null, 1), 'utf8');

    for (const meta of dni) {
        if (pobrane >= LIMIT) break;
        if (zrobione.has(meta.data)) continue;

        const klucz = `${meta.dzien}|${meta.cykl}`;
        const mmdd = meta.data.slice(5);
        if (widzianeKlucze.has(klucz) && widzianeDaty.has(mmdd)) { pominiete++; continue; }

        try {
            const res = await fetch(`${BASE}/${meta.data}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const dzien = parseDay(await res.text(), meta);

            if (!dzien) {
                console.log(`  ${meta.data} — brak czytań na stronie`);
                bledy++;
            } else {
                wynik.push(dzien);
                widzianeKlucze.add(klucz);
                widzianeDaty.add(mmdd);
                const w = dzien.formularze.map(f =>
                    (f.nowy ? 'N' : '-') + (f.stary ? 'S' : '-')).join(' ');
                console.log(`✓ ${meta.data}  ${meta.dzien.padEnd(22)} ${(meta.cykl || '-').padEnd(3)} ` +
                    `form=${dzien.formularze.length} [${w}]`);
            }
            pobrane++;
            if (pobrane % 25 === 0) zapisz();
        } catch (e) {
            console.error(`  ✗ ${meta.data}: ${e.message}`);
            bledy++;
        }

        await delay(DELAY_MS);
    }

    zapisz();

    // ── Raport ────────────────────────────────────────────────────────────────
    const formularzy = wynik.reduce((s, d) => s + d.formularze.length, 0);
    const zNowym = wynik.filter(d => d.formularze.some(f => f.nowy)).length;
    const zeStarym = wynik.filter(d => d.formularze.some(f => f.stary)).length;

    console.log(`\n✓ ${wynik.length} dni / ${formularzy} formularzy → ${OUTPUT_FILE}`);
    console.log(`  pobrane teraz: ${pobrane} | pominięte (duplikat klucza i daty): ${pominiete} | błędy: ${bledy}`);
    console.log(`  nowy lekcjonarz: ${zNowym} dni | stary lekcjonarz: ${zeStarym} dni`);

    // Każdy formularz musi mieć te cztery sekcje — brak którejkolwiek to sygnał,
    // że parser znowu rozjechał się z układem strony (tak wyszła wcześniej
    // zjedzona Ewangelia w dni powszednie).
    const braki = SECTIONS.map(s => {
        const n = wynik.filter(d => !d.formularze.some(f =>
            (f.nowy && f.nowy[s]) || (f.stary && f.stary[s]))).length;
        return `${s}: ${n}`;
    });
    console.log(`  dni bez sekcji → ${braki.join(' | ')}`);

    const zWariantami = wynik.filter(d => d.formularze.some(f =>
        (f.nowy && f.nowy.warianty) || (f.stary && f.stary.warianty))).length;
    console.log(`  dni z wariantami sekcji (Wigilia Paschalna itp.): ${zWariantami}`);
}

main().catch(console.error);
