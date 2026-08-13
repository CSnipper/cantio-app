/**
 * Scraper formularzy własnych o świętych — ordo.pallotyni.pl (Mszał Rzymski)
 * Wynik: tools/mszal-swieci.json
 *
 * Serwis publikuje 9 miesięcy (brak września, listopada i grudnia).
 * Każdy miesiąc to lista linków do osobnych stron obchodów.
 *
 * DWIE PUŁAPKI, które trzeba było obejść:
 *
 * 1. PAGINACJA. Tylko październik jest stronicowany (?start=8) — pozostałe miesiące
 *    zwracają komplet od razu. Bez obsługi paginacji październik dawał 8 z 31 obchodów,
 *    i to bez żadnego błędu. Dlatego paginację przechodzimy dla KAŻDEGO miesiąca.
 *
 * 2. LITURGIA SŁOWA WKLINOWANA W MODLITWY. Czytania siedzą między KOLEKTĄ a MODLITWĄ
 *    NAD DARAMI, a ich nagłówki — poza pierwszym — są pisane małą literą („Psalm
 *    responsoryjny", „Śpiew przed Ewangelią"). Cięcie po samych WERSALIKACH wpycha całe
 *    czytania do pola `kolekta`; to dokładnie ta skaza, którą ma baza_mszal.json.
 *    Nagłówki liturgii słowa rozpoznajemy więc niezależnie od wielkości liter,
 *    ZAKOTWICZONE NA POCZĄTKU LINII (bez kotwicy „Ewangelia" trafia w środku zdania modlitwy).
 *
 * Uruchomienie:
 *   cd C:\Users\konta\source\repos\Cantio\tools
 *   node scrape-mszal-swieci.js
 *   node scrape-mszal-swieci.js --cache      # parsuj z tmp/mszal-pages, bez sieci
 */

const fs = require('fs');
const path = require('path');
const cheerio = require('cheerio');

const BASE = 'https://www.ordo.pallotyni.pl';
const CACHE_DIR = path.join(__dirname, 'tmp', 'mszal-pages');
const OUTPUT_FILE = path.join(__dirname, 'mszal-swieci.json');
const DELAY_MS = 400;

const MIESIACE = {
    styczen: { slug: 'swieci-styczen', nr: '01', dopelniacz: 'stycznia' },
    luty: { slug: 'swieci-luty', nr: '02', dopelniacz: 'lutego' },
    marzec: { slug: 'swieci-marzec', nr: '03', dopelniacz: 'marca' },
    kwiecien: { slug: 'swieci-kwiecien', nr: '04', dopelniacz: 'kwietnia' },
    maj: { slug: 'swieci-maj', nr: '05', dopelniacz: 'maja' },
    czerwiec: { slug: 'swieci-czerwiec', nr: '06', dopelniacz: 'czerwca' },
    lipiec: { slug: 'swieci-lipiec', nr: '07', dopelniacz: 'lipca' },
    sierpien: { slug: 'swieci-sierpien', nr: '08', dopelniacz: 'sierpnia' },
    pazdziernik: { slug: '60-formularze-wasne-o-witych-padziernik', nr: '10', dopelniacz: 'października' },
};

// ── Nagłówki ────────────────────────────────────────────────────────────────────
//
// PUŁAPKA: `\b` w JS opiera się o [A-Za-z0-9_], więc PO POLSKIEJ LITERZE NIE TWORZY
// GRANICY. `^ANTYFONA NA WEJŚCIE\b` działało (kończy się na „E”), ale `KOMUNIĘ\b`
// i `EWANGELIĄ\b` nie dopasowywały się NIGDY — antyfony komunijne wychodziły zerem
// przy 93 obecnych w danych. Stąd własna granica: koniec linii, spacja albo cyfra
// (po nagłówku idą sigla, np. „ANTYFONA NA KOMUNIĘ 2 Kor 5, 14-15”).
const KON = '(?=$|[ ]|[0-9])';
const naglowek = (wzor, flagi = '') => new RegExp('^(?:' + wzor + ')' + KON, flagi);

// Modlitwy: WERSALIKI. „ANTYFNA" to literówka serwisu (1 wystąpienie) — zostaje,
// bo poprawianie danych u źródła nie jest zadaniem scrapera, a bez niej gubimy antyfonę.
const MODLITWY = [
    ['antyfona_na_wejscie', naglowek('ANTYFONA NA WEJŚCIE')],
    ['antyfona_na_komunie', naglowek('(?:ANTYFONA|ANTYFNA) NA KOMUNIĘ')],
    ['kolekta', naglowek('KOLEKTA')],
    ['modlitwa_nad_darami', naglowek('MODLITWA NAD DARAMI')],
    ['modlitwa_po_komunii', naglowek('MODLITWA PO KOMUNII')],
    ['prefacja', naglowek('PREFACJA')],
    ['uroczyste_blogoslawienstwo', naglowek('UROCZYSTE BŁOGOSŁAWIEŃSTWO')],
];

// Liturgia słowa: wielkość liter dowolna, ale KOTWICA na początku linii jest konieczna —
// „Ewangelia" i „czytanie" trafiają się w środku zdań modlitw i rubryk.
const LITURGIA = [
    ['pierwsze_czytanie', naglowek('PIERWSZE CZYTANIE', 'i')],
    ['drugie_czytanie', naglowek('DRUGIE CZYTANIE', 'i')],
    ['trzecie_czytanie', naglowek('TRZECIE CZYTANIE', 'i')],
    ['psalm_responsoryjny', naglowek('PSALM RESPONSORYJNY', 'i')],
    ['spiew_przed_ewangelia', naglowek('ŚPIEW PRZED EWANGELIĄ', 'i')],
    ['ewangelia', naglowek('EWANGELIA', 'i')],
];

// Podformularze jednego dnia (np. 24 VI: wigilia i msza w dzień).
// Bez własnej granicy (KON) nie działa: „WIGILIĘ" i „DZIEŃ" kończą się polską literą.
const PODFORMULARZ = naglowek('MSZA W WIGILIĘ|MSZA W DZIEŃ|MSZA WIGILIJNA|MSZA W NOCY|MSZA O ŚWICIE');

// Rubryki — zdania wykonawcze, nie treść modlitwy. Trzymamy je osobno, żeby nie
// zaśmiecały tekstów, ale i nie ginęły.
const RUBRYKA = /^(Odmawia się|Prefacja\b|W Modlitwach eucharystycznych|Tej Mszy używa się|Tę Mszę|Można użyć|Stosuje się|Używa się|Szaty |W kościołach|Albo:)/i;

// Ta sama pułapka co wyżej: `\b(święto)` NIE dopasowuje się nigdy, bo „ś" nie jest
// znakiem słownym dla \b — a „uroczystość" i „wspomnienie" zaczynają się od liter ASCII
// i działały. Efekt: 42 obchody bez rangi, wszystkie akurat rangi „święto".
const RANGI = /(?:^|\s)(uroczystość|święto|wspomnienie obowiązkowe|wspomnienie dowolne|wspomnienie)\s*$/i;

const delay = ms => new Promise(r => setTimeout(r, ms));

/**
 * Linie treści strony obchodu; puste odfiltrowane, spacje znormalizowane.
 *
 * Część stron wklejono z Worda razem z blokiem <xml> (w:WordDocument), który
 * `.text()` wciąga jako treść — dawało to nagłówki „Normal / 0 / 21 / false / PL"
 * i przesuwało datę oraz tytuł o 8 linii. Usuwamy je przed odczytem.
 */
function linesOf(html) {
    const $ = cheerio.load(html);
    const box = $('.art-article').first();
    if (!box.length) return [];
    box.find('style, script, xml, o\\:p').remove();
    return box.text()
        .replace(/\r/g, '')
        .replace(/\u00A0/g, ' ')
        .replace(/[ \t]+/g, ' ')
        .split('\n')
        .map(l => l.trim())
        .filter(Boolean);
}

function dopasuj(line, tabela) {
    for (const [pole, re] of tabela) {
        const m = line.match(re);
        if (m) return { pole, reszta: line.slice(m[0].length).trim() };
    }
    return null;
}

function parseObchod(html, miesiac, url) {
    const lines = linesOf(html);
    if (!lines.length) return null;

    const meta = MIESIACE[miesiac];

    // Data bywa nie w pierwszej linii, więc jej SZUKAMY zamiast zakładać pozycję.
    // Część obchodów nie ma stałej daty („2 sobota maja", „Poniedziałek po Zesłaniu
    // Ducha Świętego") — to obchody RUCHOME; zostają z dzien = null i opisem w
    // `dzien_ruchomy`, bo wciśnięcie ich na siłę w MM-DD nadpisałoby świętego z tej daty.
    // „Również 10 października" = kolejny obchód tej samej daty, więc dzień może być
    // poprzedzony słowem — kotwiczymy na początku linii, ale dopuszczamy ten prefiks.
    const reData = new RegExp(`^(?:również\\s+)?(\\d{1,2})\\s+${meta.dopelniacz}`, 'i');
    const reRuchome = /^(?:\d+\s+)?(?:poniedziałek|wtorek|środa|czwartek|piątek|sobota|niedziela|\d\s+sobota|ostatnia|pierwsza|druga|trzecia)\b/i;

    let iData = lines.findIndex(l => reData.test(l));
    if (iData < 0) iData = lines.findIndex(l => reRuchome.test(l));
    if (iData < 0) iData = 0;

    const dataLine = lines[iData];
    const dm = dataLine.match(reData);
    const dzien = dm ? `${meta.nr}-${dm[1].padStart(2, '0')}` : null;

    // Między datą a tytułem bywa nota o zasięgu („W diecezji opolskiej: 20 czerwca",
    // „W Opolu: Głównej patronki miasta - Uroczystość"). Bez pominięcia jej TYTUŁEM
    // obchodu zostawał zasięg, a prawdziwy tytuł przepadał.
    const NOTA_ZASIEGU = /^W\s+(?:(?:archi)?diecezji|metropolii|prowincji|zakonie|kościołach|Ordynariacie|[A-ZĄĆĘŁŃÓŚŹŻ][a-ząćęłńóśźż]+:)/i;

    let tytul = null, ranga = null, iTytul = iData;
    const noty = [];
    for (let i = iData + 1; i < Math.min(lines.length, iData + 5); i++) {
        if (dopasuj(lines[i], MODLITWY) || dopasuj(lines[i], LITURGIA) || PODFORMULARZ.test(lines[i])) break;
        // Nota zasięgu jest tytułem tylko wtedy, gdy nic innego nie zostało.
        if (NOTA_ZASIEGU.test(lines[i]) && lines[i + 1] && !RANGI.test(lines[i])) {
            noty.push(lines[i]); iTytul = i; continue;
        }
        const rm = lines[i].match(RANGI);
        tytul = (rm ? lines[i].slice(0, rm.index) : lines[i]).trim() || null;
        ranga = rm ? rm[1].toLowerCase() : null;
        iTytul = i;
        break;
    }
    // Ranga bywa w osobnej linii pod tytułem.
    if (tytul && !ranga && lines[iTytul + 1]) {
        const rm = lines[iTytul + 1].match(new RegExp(`^${RANGI.source}`, 'i'));
        if (rm) { ranga = rm[1].toLowerCase(); iTytul++; }
    }

    const formularze = [];
    let form = null;
    const nowyFormularz = nazwa => {
        form = { nazwa, sekcje: {}, liturgia_slowa: [], rubryki: [] };
        formularze.push(form);
        return form;
    };

    let cel = null;                 // { kosz: 'sekcje'|'liturgia', pole, ref, tytul, buf[] }
    const wprowadzenie = [];
    let mszaWspolna = null;

    const zamknij = () => {
        if (!cel) return;
        const tekst = cel.buf.join('\n').trim();
        if (tekst || cel.ref) {
            const wpis = { ref: cel.ref || null, tekst };
            if (cel.kosz === 'liturgia') {
                form.liturgia_slowa.push({ typ: cel.pole, tytul: cel.tytul || null, ...wpis });
            } else if (form.sekcje[cel.pole]) {
                // Powtórzona modlitwa = wariant („Albo:”); nie nadpisujemy po cichu.
                (form.warianty ||= []).push({ pole: cel.pole, ...wpis });
            } else {
                form.sekcje[cel.pole] = wpis;
            }
        }
        cel = null;
    };

    for (let i = iTytul + 1; i < lines.length; i++) {
        const line = lines[i];

        // pf[0], nie pf[1] — `naglowek()` opakowuje wzorzec w grupę NIEprzechwytującą,
        // więc grupy 1 nie ma i nazwy podformularzy wychodziły puste.
        const pf = line.match(PODFORMULARZ);
        if (pf) { zamknij(); nowyFormularz(pf[0].trim()); continue; }

        if (!form) {
            // Przed pierwszym nagłówkiem sekcji: nota biograficzna i odsyłacz do mszy wspólnej.
            if (/^Msza wspólna/i.test(line) && !mszaWspolna) { mszaWspolna = line; continue; }
        }

        const mod = dopasuj(line, MODLITWY);
        const lit = mod ? null : dopasuj(line, LITURGIA);

        if (mod || lit) {
            zamknij();
            if (!form) nowyFormularz(null);
            const { pole, reszta } = mod || lit;
            cel = { kosz: mod ? 'sekcje' : 'liturgia', pole, ref: reszta || null, tytul: null, buf: [] };
            continue;
        }

        if (!cel) {
            if (!form) wprowadzenie.push(line);
            else if (RUBRYKA.test(line)) form.rubryki.push(line);
            else if (/^Msza wspólna/i.test(line) && !mszaWspolna) mszaWspolna = line;
            else form.rubryki.push(line);
            continue;
        }

        if (RUBRYKA.test(line)) { form.rubryki.push(line); continue; }

        // Czytania mają linię tytułową („Jak śmierć potężna jest miłość") przed
        // formułą „Czytanie z…”. Bierzemy ją jako tytuł perykopy, nie jako tekst.
        if (cel.kosz === 'liturgia' && !cel.buf.length && !cel.tytul
            && !/^(Czytanie z|Słowa Ewangelii|\+ Słowa|Refren|Aklamacja)/i.test(line)) {
            cel.tytul = line;
            continue;
        }

        cel.buf.push(line);
    }
    zamknij();

    // Serwis ma obchody zapowiedziane, ale bez tekstów („[wkrótce]", „[w przygotowaniu]").
    // Zapisujemy je z pustą listą formularzy — pominięcie wyglądałoby jak luka scrapera,
    // a to jest luka źródła i warto ją widzieć.
    const zapowiedz = lines.find(l => /^\[.*(wkrótce|przygotowaniu|opracowaniu).*\]$/i.test(l));
    const status = formularze.length ? 'ok' : (zapowiedz ? 'brak_tekstow' : 'nierozpoznane');

    // Zakres bywa w linii daty: „6 czerwca w diecezji toruńskiej:".
    const zm = dataLine.match(/\b(w\s+(?:archi)?diecezji\s+[^:,]+|w\s+archidiecezji\s+[^:,]+)/i);

    return {
        dzien,
        status,
        zakres: zm ? zm[1].trim() : (noty.length ? noty.join(' | ') : null),
        miesiac,
        dzien_ruchomy: dzien ? null : dataLine,
        dzien_tekst: dataLine,
        tytul,
        ranga,
        wprowadzenie: wprowadzenie.join('\n').trim() || null,
        msza_wspolna: mszaWspolna,
        formularze,
        zrodlo: url,
    };
}

/** Linki obchodów danego miesiąca — z przejściem całej paginacji. */
async function zbierzLinki(slug) {
    const found = new Set();
    let start = 0, pusto = 0;
    while (start < 400) {
        const url = `${BASE}/index.php/mszal-rzymski/${slug}${start ? `?start=${start}` : ''}`;
        const $ = cheerio.load(await fetch(url).then(r => r.text()));
        const links = [...new Set($('a[href]').map((_, e) => $(e).attr('href')).get()
            .filter(x => x && x.includes(`/${slug}/`)))];
        const nowe = links.filter(l => !found.has(l));
        nowe.forEach(l => found.add(l));
        if (!nowe.length) { if (++pusto >= 2) break; } else pusto = 0;
        start += 8;
        await delay(250);
    }
    return [...found];
}

async function main() {
    const tylkoCache = process.argv.includes('--cache');
    fs.mkdirSync(CACHE_DIR, { recursive: true });

    const wynik = [];
    let bezSekcji = 0;

    for (const [miesiac, meta] of Object.entries(MIESIACE)) {
        let links;
        if (tylkoCache) {
            links = fs.readdirSync(CACHE_DIR).filter(f => f.startsWith(`${miesiac}__`))
                .map(f => `/index.php/mszal-rzymski/${meta.slug}/${f.split('__')[1].replace(/\.html$/, '')}`);
        } else {
            links = await zbierzLinki(meta.slug);
        }

        let n = 0;
        for (const href of links) {
            const id = href.split('/').pop();
            const cacheFile = path.join(CACHE_DIR, `${miesiac}__${id}.html`);

            let html;
            if (fs.existsSync(cacheFile)) {
                html = fs.readFileSync(cacheFile, 'utf8');
            } else {
                html = await fetch(BASE + href).then(r => r.text());
                fs.writeFileSync(cacheFile, html);
                await delay(DELAY_MS);
            }

            const obchod = parseObchod(html, miesiac, BASE + href);
            if (!obchod) { bezSekcji++; console.log(`  ⚠ nieczytelna strona: ${id}`); continue; }
            if (obchod.status !== 'ok') bezSekcji++;
            wynik.push(obchod);
            n++;
        }
        console.log(`${miesiac.padEnd(12)} ${n} obchodów`);
    }

    wynik.sort((a, b) => (a.dzien || '').localeCompare(b.dzien || ''));
    fs.writeFileSync(OUTPUT_FILE, JSON.stringify(wynik, null, 1), 'utf8');

    // ── Raport ──────────────────────────────────────────────────────────────────
    const formularzy = wynik.reduce((s, o) => s + o.formularze.length, 0);
    const ma = pole => wynik.filter(o => o.formularze.some(f => f.sekcje[pole])).length;
    const zLiturgia = wynik.filter(o => o.formularze.some(f => f.liturgia_slowa.length));

    console.log(`\n✓ ${wynik.length} obchodów / ${formularzy} formularzy → ${OUTPUT_FILE}`);
    console.log(`  bez tekstów na stronie („wkrótce”): ${wynik.filter(o => o.status !== 'ok').length}`);
    console.log(`  bez daty: ${wynik.filter(o => !o.dzien).length} | bez tytułu: ${wynik.filter(o => !o.tytul).length}` +
        ` | bez rangi: ${wynik.filter(o => !o.ranga).length} | stron bez formularza: ${bezSekcji}`);
    console.log(`  antyfona wejście: ${ma('antyfona_na_wejscie')} | kolekta: ${ma('kolekta')}` +
        ` | nad darami: ${ma('modlitwa_nad_darami')} | antyfona komunia: ${ma('antyfona_na_komunie')}` +
        ` | po komunii: ${ma('modlitwa_po_komunii')}`);
    console.log(`  z liturgią słowa: ${zLiturgia.length} (${zLiturgia.map(o => o.dzien).join(', ')})`);

    // Strażnik pułapki nr 2: jeśli w modlitwie siedzi formuła czytania,
    // znaczy że rozdzielenie warstw znowu się rozjechało.
    const skazone = [];
    for (const o of wynik) {
        for (const f of o.formularze) {
            for (const [pole, s] of Object.entries(f.sekcje)) {
                if (/^(Czytanie z |Słowa Ewangelii|Oto słowo Boże|Refren:)/m.test(s.tekst)) {
                    skazone.push(`${o.dzien}/${pole}`);
                }
            }
        }
    }
    console.log(skazone.length
        ? `  ⚠ MODLITWY SKAŻONE LITURGIĄ SŁOWA (${skazone.length}): ${skazone.slice(0, 10).join(', ')}`
        : '  ✓ żadna modlitwa nie zawiera formuł liturgii słowa');
}

main().catch(console.error);
