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
    /// <para><c>ImageRef</c> (v1.69) to WZGLĘDNA ścieżka pliku (`images\foo.jpg`, taka jak w
    /// <see cref="SetlistItem.ImagePath"/>) — niesie ją wyłącznie pozycja typu `image`.</para>
    /// </summary>
    public readonly record struct Entry(
        int Id, string Title, string Type, string? CustomTitle, string? CustomText, string? ImageRef = null)
    {
        public bool IsSong  => Type == TypeSong;
        public bool IsText  => Type == TypeText;
        public bool IsImage => Type == TypeImage;

        public static Entry Song(int id, string? title) => new(id, title ?? "", TypeSong, null, null);

        public static Entry Image(string? title, string? imageRef = null)
            => new(0, title ?? "", TypeImage, null, null, imageRef);

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
        if (item.IsImageItem) return Entry.Image(title, item.ImagePath);
        if (item.IsTextItem)  return new Entry(0, title, TypeText, item.CustomTitle, item.CustomText);
        return Entry.Song(item.SongId ?? 0, title);
    }

    public static List<Entry> From(IEnumerable<SetlistItem> items) => items.Select(From).ToList();

    /// <summary>
    /// Obiekt do serializacji. Pola treści niesie WYŁĄCZNIE pozycja tekstowa; obrazek dostaje
    /// `imageRef` (v1.69) — samą ścieżkę, nie zawartość. Po niej telefon prosi o podgląd
    /// (`image_get`) i po niej odtwarza pozycję przy `setlist_restore`/`setlist_sync_push`.
    /// <para>Pole jest DOPISANE: pozycja bez ścieżki (obrazek z bazy sprzed v1.69 albo pozycja
    /// pieśni/tekstu) wygląda dokładnie tak, jak przed zmianą.</para>
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
        if (e.IsImage && !string.IsNullOrWhiteSpace(e.ImageRef))
            o["imageRef"] = e.ImageRef;
        return o;
    }

    public static List<Dictionary<string, object?>> ToJsonArray(IEnumerable<Entry> items)
        => items.Select(ToJsonObject).ToList();

    /// <summary>
    /// Komunikat `setlist` — JEDYNE miejsce jego składania (broadcast i wysyłka do świeżego klienta).
    ///
    /// `setlistId`/`name` to TOŻSAMOŚĆ bieżącej listy: rekord zestawu w bazie desktopu, z którego
    /// została wczytana albo pod którym ostatnio zapisana. Bez niej Pilot po odebraniu broadcastu
    /// nie wie, co ma zaproponować do nadpisania przy „Zapisz".
    ///
    /// Lista, która NIE pochodzi z zapisanego zestawu (operator zebrał pieśni ręcznie, wyczyścił
    /// zestaw, skasowano rekord), nie niesie tych pól w ogóle — brak pola znaczy „nie ma czego
    /// nadpisywać", i to jest to samo, co widzi stary Pilot (nieznane pola ignoruje).
    /// </summary>
    public static string BuildSetlistJson(
        IEnumerable<Entry> items, int activeIndex, int setlistId = 0, string? name = null)
    {
        var o = new Dictionary<string, object?>
        {
            ["type"]        = "setlist",
            ["activeIndex"] = activeIndex,
            ["songs"]       = ToJsonArray(items)
        };
        if (setlistId > 0) o["setlistId"] = setlistId;
        if (!string.IsNullOrWhiteSpace(name)) o["name"] = name;
        return JsonSerializer.Serialize(o);
    }

    /// <summary>
    /// Odczyt listy pozycji przysłanej przez Pilota (`setlist_restore`, `setlist_sync_push`).
    ///
    /// Pomijane są pozycje, których desktop nie umie odtworzyć: pieśń bez `id` (jak dotąd),
    /// tekst bez treści oraz obrazek bez `imageRef` (tak wygląda pozycja ze starego Pilota —
    /// plik został na PC i telefon nie ma czego przysłać).
    /// <para><b>Obrazek z `imageRef` PRZECHODZI</b> (v1.69) — to koniec ograniczenia „obrazki giną
    /// przy wczytaniu zestawu z telefonu". Parser NIE sprawdza, czy plik istnieje: to pytanie
    /// o dysk, a ta klasa ma zostać czysta. Istnienie weryfikuje warstwa WYKONUJĄCA
    /// (<see cref="PilotImages.RefExists"/> w handlerze restore i w zapisie zestawu), bo tylko ona
    /// wie, co zrobić z pozycją bez pliku — pominąć i zalogować.</para>
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

            if (type == TypeImage)
            {
                var imageRef = Str(el, "imageRef");
                if (string.IsNullOrWhiteSpace(imageRef)) continue;   // stary Pilot: sam `title`
                result.Add(Entry.Image(Str(el, "title"), imageRef));
                continue;
            }

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
