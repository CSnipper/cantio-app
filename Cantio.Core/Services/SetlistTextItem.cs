using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Tekst jednorazowy w zestawie (v1.6) — reguły wspólne dla edytora w oknie Cantio i komend
/// Pilota (`setlist_add_text` / `setlist_update_text`, `setlist_restore`, `setlist_sync_push`).
///
/// Treść żyje wyłącznie w <see cref="SetlistItem"/> (`CustomTitle`/`CustomText`) — nie zakłada
/// pieśni w bazie. Tytuł liczy JEDNA funkcja, żeby pozycja dodana z telefonu nazywała się
/// dokładnie tak samo jak dodana w oknie (dwie kopie tej reguły rozjechałyby się przy
/// pierwszej zmianie limitu długości).
/// </summary>
public static class SetlistTextItem
{
    /// <summary>Maksymalna długość tytułu wyprowadzonego z pierwszej linii treści.</summary>
    public const int TitleMaxLength = 40;

    /// <summary>Tytuł wpisany ręcznie, a gdy pusty — pierwsza niepusta linia treści (max 40 znaków).</summary>
    public static string ResolveTitle(string? title, string? content)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length > 0) return trimmed;

        var firstLine = (content ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        return firstLine.Length > TitleMaxLength
            ? firstLine[..TitleMaxLength].TrimEnd() + "…"
            : firstLine;
    }

    /// <summary>Nowa pozycja tekstowa zestawu (bez pozycji w kolejności — nadaje ją wołający).</summary>
    public static SetlistItem Create(string? title, string? content) => new()
    {
        Type       = PilotSetlistItems.TypeText,
        CustomTitle = ResolveTitle(title, content),
        CustomText  = (content ?? string.Empty).Trim()
    };
}
