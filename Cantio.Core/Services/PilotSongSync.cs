using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Komunikat <c>songs_data</c> (odpowiedź na <c>get_songs</c>).
///
/// Wyniesione z inline'a w <c>MainWindow.xaml.cs</c>, żeby kształt komunikatu miał JEDNO źródło
/// i dał się objąć testem — dwie niezależne listy pól zgubiły kiedyś notatki pozycji zestawu (v1.6).
/// </summary>
public static class PilotSongSync
{
    public const string SongsDataType = "songs_data";

    /// <summary>
    /// Pieśń „bez kategorii" (<c>Song.CategoryId == null</c>) jedzie na łącze jako <c>0</c>.
    /// Stary Pilot, który jest już zainstalowany u użytkownika, ma w tym polu twardy <c>int</c>
    /// i na <c>null</c> by się wywrócił. Wartość 0 wraca potem w <c>sync_push</c> i
    /// <see cref="DatabaseService.SyncPushSongsAsync"/> czyta ją z powrotem jako brak kategorii —
    /// dzięki temu round-trip nie zakłada duplikatu w pierwszej kategorii.
    /// </summary>
    public const int NoCategory = 0;

    /// <summary>
    /// Buduje komunikat <c>songs_data</c> z IZOLACJĄ PER PIEŚŃ.
    ///
    /// Regresja produkcyjna: gdy serializacja JEDNEJ pieśni rzuci (np. uszkodzona nawigacja
    /// <c>Verses == null</c>), stary kod składał całą stronę jednym <c>Serialize</c> — wyjątek
    /// wywracał CAŁĄ stronę, a połknięty <c>catch{}</c> w gospodarzu nie wysyłał NIC. Pilot wisiał
    /// na stronie 0 → pusta biblioteka. Tu każda pieśń jest serializowana osobno; wadliwa jest
    /// POMIJANA i logowana, reszta strony leci normalnie.
    ///
    /// Paginacja znosi pominięcie: liczbą stron rządzi <c>total</c> (Count z DB, liczony niezależnie
    /// od serializacji) oraz <c>offset</c>/<c>limit</c> ustalane po stronie DB — Pilot przesuwa się
    /// o żądany <c>limit</c>, nie o liczbę zserializowanych elementów. Pominięta pieśń znika tylko
    /// z biblioteki telefonu, nie blokuje dojścia do końca stronicowania.
    /// </summary>
    public static string BuildSongsDataJson(int offset, int total, IEnumerable<Song> items)
    {
        var built = new List<JsonElement>();
        foreach (var s in items ?? Enumerable.Empty<Song>())
        {
            try
            {
                // SerializeToElement wymusza pełną ewaluację (w tym enumerację Verses) TU,
                // w obrębie try — inaczej wyjątek wypłynąłby dopiero przy leniwej serializacji
                // opakowania i znów wywrócił całą stronę.
                built.Add(JsonSerializer.SerializeToElement(new
                {
                    id         = s.Id,
                    title      = s.Title,
                    number     = s.Number,
                    author     = s.Author ?? "",
                    categoryId = s.CategoryId ?? NoCategory,
                    parts      = s.Verses
                        .OrderBy(v => v.Position)
                        // lekcjonarz: "N"/"S" na oznaczonych zwrotkach psalmu, null na zwykłych.
                        // Pilot czyta pole przez optStringOrNull → null = brak pola = pełna zgodność wsteczna.
                        .Select(v => new { type = v.Type, text = v.Text, lekcjonarz = v.Lekcjonarz })
                        .ToList()
                }));
            }
            catch (Exception ex)
            {
                // Jedna wadliwa pieśń nie może uciszyć całej strony ani jej wywrócić.
                AppLog.Write("Pilot",
                    $"songs_data: pominięto wadliwą pieśń Id={s?.Id} — {ex.GetType().Name}: {ex.Message}");
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = SongsDataType,
            offset,
            total,
            items = built
        });
    }
}
