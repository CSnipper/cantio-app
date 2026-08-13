// Generator mapy: data → klucz liturgiczny bazy czytań.
//
// Potrzebny scraperowi lekcjonarza (scrape-lekcjonarz.js): niezbednik.niedziela.pl
// indeksuje czytania DATĄ, a baza Cantio kluczem liturgicznym („ZW 18 Nie A").
//
// Klucze liczy RDZEŃ (LiturgicalCalendarService) — nie parsujemy polskich nazw ze
// strony, bo scraper musi widzieć dokładnie ten sam dzień co aplikacja. Gdy rdzeń
// zmienia reguły kalendarza, mapę trzeba przeliczyć; sam scrape jest kluczowany datą,
// więc nie wymaga to ponownego pobierania.
//
// UWAGA: to narzędzie mieszkało wcześniej w scratchpadzie i zostało skasowane razem
// z %TEMP%. Nic trwałego nie trzymamy w Temp — stąd jego miejsce w repo.
//
// Użycie:
//   dotnet run -c Release -- 2016-11-27 2026-08-04 > ../tmp/date-keys.json

using System.Globalization;
using System.Text.Json;
using Cantio.Models;
using Cantio.Services;

if (args.Length < 2)
{
    Console.Error.WriteLine("użycie: keygen <od yyyy-MM-dd> <do yyyy-MM-dd>");
    return 1;
}

var from = DateOnly.ParseExact(args[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
var to = DateOnly.ParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);

// Group z rdzenia → prefiks okresu w bazie czytań (czytania.json.gz).
static string OkresFor(string group) => group switch
{
    "adwent" => "ADW",
    "boznarodzenie" => "BN",
    "wielki_post" => "WP",
    "wielkanoc" => "WK",
    "zwykly" => "ZW",
    _ => "US",
};

var rows = new List<object>();

for (var d = from; d <= to; d = d.AddDays(1))
{
    var day = LiturgicalCalendarService.GetDay(d);

    // Obchody RUCHOME (Boże Ciało, Chrystus Król…) mają WŁASNĄ nazwę zamiast
    // temporalnej, więc nie są dniem okresu — w bazie czytań żyją jako „US".
    bool wlasny = day.Rank != Ranga.Brak;
    string okres = wlasny ? "US" : OkresFor(day.Group);
    string dzien = wlasny ? day.SetlistName : $"{okres} {day.SetlistName}";

    rows.Add(new
    {
        data = d.ToString("yyyy-MM-dd"),
        dzien,
        okres,
        cykl = day.Cycle,
        ranga = day.Rank.ToString(),
    });
}

Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions
{
    WriteIndented = false,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
}));

return 0;
