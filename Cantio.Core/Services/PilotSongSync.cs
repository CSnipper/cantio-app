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

    public static string BuildSongsDataJson(int offset, int total, IEnumerable<Song> items) =>
        JsonSerializer.Serialize(new
        {
            type = SongsDataType,
            offset,
            total,
            items = items.Select(s => new
            {
                id         = s.Id,
                title      = s.Title,
                number     = s.Number,
                author     = s.Author ?? "",
                categoryId = s.CategoryId ?? NoCategory,
                parts      = s.Verses
                    .OrderBy(v => v.Position)
                    .Select(v => new { type = v.Type, text = v.Text })
            })
        });
}
