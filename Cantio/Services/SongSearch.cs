using Cantio.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace Cantio.Services;

/// <summary>
/// Wyszukiwanie pieśni po tytule i/lub numerze ze śpiewnika.
/// Logika wydzielona z <see cref="DatabaseService"/>, żeby dała się testować
/// na dowolnym <see cref="IQueryable{Song}"/> (także na kopii bazy).
/// </summary>
public static class SongSearch
{
    public const int Limit = 100;

    // Ile wyników pobrać z bazy przed sortowaniem po stronie klienta.
    private const int FetchLimit = Limit * 3;

    private static readonly StringComparer PlComparer =
        StringComparer.Create(new CultureInfo("pl-PL"), ignoreCase: true);

    /// <param name="NumberPrefix">Prefiks numeru (same cyfry) albo null, gdy zapytanie nie ma członu liczbowego.</param>
    /// <param name="TitleWords">Słowa, które muszą wystąpić w tytule w gałęzi tytułowej.</param>
    /// <param name="ExtraWords">Słowa nieliczbowe — dokładane do gałęzi numerycznej.</param>
    /// <param name="NumberOnly">Zapytanie to same cyfry → szukamy wyłącznie po numerze.</param>
    public readonly record struct ParsedQuery(
        string? NumberPrefix,
        string[] TitleWords,
        string[] ExtraWords,
        bool NumberOnly)
    {
        public bool IsEmpty => NumberPrefix is null && TitleWords.Length == 0;
    }

    /// <summary>Rozpoznaje intencję zapytania: numer, tytuł albo mieszane.</summary>
    public static ParsedQuery Parse(string? query)
    {
        var sb = new StringBuilder();
        foreach (var c in query ?? string.Empty)
            if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);

        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new ParsedQuery(null, [], [], false);

        // Pierwszy człon złożony z samych cyfr traktujemy jako numer.
        var numberPrefix = words.FirstOrDefault(w => w.All(char.IsDigit));
        // Wiodące zera nie zmieniają numeru („012" == „12"), ale „0" samo w sobie to nie numer.
        numberPrefix = numberPrefix?.TrimStart('0');
        if (string.IsNullOrEmpty(numberPrefix)) numberPrefix = null;

        var extra = words.Where(w => !w.All(char.IsDigit)).ToArray();
        var numberOnly = numberPrefix != null && extra.Length == 0;

        return new ParsedQuery(numberPrefix, words, extra, numberOnly);
    }

    /// <summary>
    /// Zwraca pieśni pasujące do zapytania. Same cyfry → dopasowanie po prefiksie numeru
    /// (dokładne trafienie pierwsze). Litery → dopasowanie po tytule (wszystkie słowa).
    /// Mieszane → suma obu dopasowań, bez duplikatów.
    /// </summary>
    public static async Task<List<Song>> SearchAsync(
        IQueryable<Song> songs, string? query, CancellationToken ct = default)
    {
        var parsed = Parse(query);
        if (parsed.IsEmpty) return [];

        // Gałąź numeryczna: Number > 0 (0 = „bez numeru"), prefiks po tekstowej reprezentacji.
        List<Song> numberHits = [];
        if (parsed.NumberPrefix is { } prefix)
        {
            var nq = songs.Where(s => s.Number > 0 &&
                                      EF.Functions.Like(s.Number.ToString(), prefix + "%"));
            foreach (var word in parsed.ExtraWords)
                nq = nq.Where(s => EF.Functions.Like(s.Title, $"%{word}%"));

            numberHits = await nq.OrderBy(s => s.Number).Take(FetchLimit).ToListAsync(ct);
        }

        // Gałąź tytułowa: pomijana, gdy zapytanie to same cyfry.
        List<Song> titleHits = [];
        if (!parsed.NumberOnly)
        {
            var tq = songs;
            foreach (var word in parsed.TitleWords)
                tq = tq.Where(s => EF.Functions.Like(s.Title, $"%{word}%"));

            titleHits = await tq.Take(FetchLimit).ToListAsync(ct);
        }

        return Merge(numberHits, titleHits, parsed);
    }

    /// <summary>Scala i porządkuje wyniki obu gałęzi (czysta funkcja — testowalna bez bazy).</summary>
    public static List<Song> Merge(
        IEnumerable<Song> numberHits, IEnumerable<Song> titleHits, ParsedQuery parsed)
    {
        var exact = parsed.NumberPrefix != null &&
                    int.TryParse(parsed.NumberPrefix, out var n) ? n : (int?)null;

        var ordered = numberHits
            .OrderBy(s => s.Number == exact ? 0 : 1)
            .ThenBy(s => s.Number)
            .ThenBy(s => s.Title, PlComparer)
            .ToList();

        var seen = ordered.Select(s => s.Id).ToHashSet();
        ordered.AddRange(titleHits
            .Where(s => seen.Add(s.Id))
            .OrderBy(s => s.Title, PlComparer));

        return ordered.Count > Limit ? ordered.GetRange(0, Limit) : ordered;
    }
}
