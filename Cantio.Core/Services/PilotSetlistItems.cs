using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Kontrakt POZYCJI ZESTAWU na łączu z Pilotem — jedno miejsce, w którym powstaje i jest czytany
/// kształt `{id, title, type, customTitle?, customText?}`.
///
/// Dotyczy WSZYSTKICH komunikatów z listą pozycji: broadcast `setlist`, `setlist_detail`,
/// `setlist_sync_conflict` (D→P) oraz `setlist_restore` i `setlist_sync_push` (P→D).
///
/// Powód istnienia klasy: do v1.67 ten sam JSON składały DWA niezależne miejsca w `MainWindow`
/// (`BroadcastSetlistState` / `BroadcastSetlistStateToAsync`), a pozycja jednorazowa leciała jako
/// `{id:0,title:""}` — dokładnie ten układ dwóch list pól zgubił notatki pozycji zestawu w v1.6.
///
/// **Zgodność wsteczna:** `id` i `title` zostają na swoim miejscu i w swoim znaczeniu, a `type`
/// oraz pola tekstu są DOPISANE. Brak `type` = `song` (tak wygląda komunikat starego Pilota).
/// </summary>
public static class PilotSetlistItems
{
    public const string TypeSong  = "song";
    public const string TypeText  = "text";
    public const string TypeImage = "image";

    /// <summary>
    /// Pozycja zestawu w postaci protokołu. `Id` &gt; 0 wyłącznie dla pieśni; tekst i obrazek mają 0.
    /// `Title` to tytuł EFEKTYWNY (to samo, co widzi operator w oknie — <see cref="SetlistLetterJump.TitleOf"/>).
    /// </summary>
    public readonly record struct Entry(int Id, string Title, string Type, string? CustomTitle, string? CustomText)
    {
        public bool IsSong  => Type == TypeSong;
        public bool IsText  => Type == TypeText;
        public bool IsImage => Type == TypeImage;

        public static Entry Song(int id, string? title) => new(id, title ?? "", TypeSong, null, null);
        public static Entry Image(string? title)        => new(0, title ?? "", TypeImage, null, null);

        public static Entry Text(string? customTitle, string? customText)
        {
            var text = (customText ?? string.Empty).Trim();
            return new Entry(0, SetlistTextItem.ResolveTitle(customTitle, text), TypeText, customTitle, text);
        }
    }

    /// <summary>Pozycja z bazy/pamięci → pozycja protokołu. Typ rozstrzyga tak samo jak UI okna.</summary>
    public static Entry From(SetlistItem item)
    {
        var title = SetlistLetterJump.TitleOf(item) ?? "";
        if (item.IsImageItem) return Entry.Image(title);
        if (item.IsTextItem)  return new Entry(0, title, TypeText, item.CustomTitle, item.CustomText);
        return Entry.Song(item.SongId ?? 0, title);
    }

    public static List<Entry> From(IEnumerable<SetlistItem> items) => items.Select(From).ToList();

    /// <summary>
    /// Obiekt do serializacji. Pola treści niesie WYŁĄCZNIE pozycja tekstowa — obrazek zostaje
    /// przy `type` + `title`, bo plik żyje na dysku PC i telefon nic z nim nie zrobi.
    /// </summary>
    public static Dictionary<string, object?> ToJsonObject(Entry e)
    {
        var o = new Dictionary<string, object?>
        {
            ["id"]    = e.Id,
            ["title"] = e.Title,
            ["type"]  = e.Type
        };
        if (e.IsText)
        {
            o["customTitle"] = e.CustomTitle;
            o["customText"]  = e.CustomText ?? "";
        }
        return o;
    }

    public static List<Dictionary<string, object?>> ToJsonArray(IEnumerable<Entry> items)
        => items.Select(ToJsonObject).ToList();

    /// <summary>Komunikat `setlist` — JEDYNE miejsce jego składania (broadcast i wysyłka do świeżego klienta).</summary>
    public static string BuildSetlistJson(IEnumerable<Entry> items, int activeIndex)
        => JsonSerializer.Serialize(new
        {
            type        = "setlist",
            activeIndex,
            songs       = ToJsonArray(items)
        });

    /// <summary>
    /// Odczyt listy pozycji przysłanej przez Pilota (`setlist_restore`, `setlist_sync_push`).
    ///
    /// Pomijane są pozycje, których desktop nie umie odtworzyć: pieśń bez `id` (jak dotąd),
    /// tekst bez treści oraz KAŻDY obrazek (plik został na PC, telefon nie ma czego przysłać).
    /// </summary>
    public static List<Entry> Parse(JsonElement songsArray)
    {
        var result = new List<Entry>();
        if (songsArray.ValueKind != JsonValueKind.Array) return result;

        foreach (var el in songsArray.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            // brak pola `type` = pieśń (tak wygląda komunikat starego Pilota)
            var type = el.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String
                ? tEl.GetString() ?? TypeSong : TypeSong;

            if (type == TypeImage) continue;

            if (type == TypeText)
            {
                var text = Str(el, "customText");
                if (string.IsNullOrWhiteSpace(text)) continue;
                result.Add(Entry.Text(Str(el, "customTitle") ?? Str(el, "title"), text));
                continue;
            }

            var id = el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                     && idEl.TryGetInt32(out var parsed) ? parsed : 0;
            if (id <= 0) continue;
            result.Add(Entry.Song(id, Str(el, "title")));
        }
        return result;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
