using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Obsługa komend zestawów przychodzących z Pilota: parsowanie JSON → operacja w bazie → gotowa odpowiedź.
/// Jedno źródło prawdy dla `MainWindow` i testów protokołu (żadnej logiki w handlerach okna).
/// </summary>
public static class PilotSetlistSync
{
    /// <summary>
    /// `setlist_sync_push` → `setlist_sync_ack` albo `setlist_sync_conflict` (JSON do odesłania).
    /// Zwraca null, gdy komunikat jest niepoprawny.
    /// </summary>
    public static async Task<string?> HandlePushAsync(DatabaseService db, string rawJson)
    {
        int? desktopId;
        string name;
        long updatedAt;
        int[] songIds;
        long? baseUpdatedAt;
        bool force;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            desktopId = root.TryGetProperty("desktopId", out var dEl) && dEl.ValueKind == JsonValueKind.Number
                ? dEl.GetInt32() : null;
            name      = root.GetProperty("name").GetString() ?? "";
            updatedAt = root.GetProperty("updatedAt").GetInt64();
            songIds   = root.GetProperty("songs").EnumerateArray()
                .Select(s => s.GetProperty("id").GetInt32()).ToArray();
            // Pola opcjonalne — stary Pilot ich nie przysyła (zgodność wsteczna: nadpisanie bezwarunkowe)
            baseUpdatedAt = root.TryGetProperty("baseUpdatedAt", out var bEl) && bEl.ValueKind == JsonValueKind.Number
                ? bEl.GetInt64() : null;
            force = root.TryGetProperty("force", out var fEl) && fEl.ValueKind == JsonValueKind.True;
        }
        catch { return null; }

        var result = await db.SyncSetlistFromPilotAsync(desktopId, name, updatedAt, songIds, baseUpdatedAt, force);

        return result.Conflict
            ? JsonSerializer.Serialize(new
            {
                type      = "setlist_sync_conflict",
                desktopId = result.SetlistId,
                name      = result.Name,
                updatedAt = result.UpdatedAt,
                songs     = result.Songs.Select(s => new { id = s.SongId, title = s.Title })
            })
            : JsonSerializer.Serialize(new
            {
                type      = "setlist_sync_ack",
                desktopId = result.SetlistId,
                name      = result.Name,
                updatedAt = result.UpdatedAt
            });
    }

    /// <summary>`setlist_delete` → `setlist_delete_ack` (JSON do odesłania) + informacja, czy zestaw istniał.</summary>
    public static async Task<(string Json, bool Existed)> HandleDeleteAsync(DatabaseService db, int desktopId)
    {
        var existed = await db.DeleteSetlistAsync(desktopId);
        var json = JsonSerializer.Serialize(new
        {
            type      = "setlist_delete_ack",
            desktopId,
            existed
        });
        return (json, existed);
    }
}
