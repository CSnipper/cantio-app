using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Zarządzanie kategoriami pieśni i grupami zestawów z Pilota (v1.63+).
///
/// JEDYNE źródło prawdy to baza desktopu: tablet WYŁĄCZNIE komenduje, desktop wykonuje
/// i rozgłasza wynik do wszystkich podłączonych klientów. Pilot nigdy nie zakłada, że
/// jego lokalna kopia listy jest aktualna — czeka na broadcast <c>categories_data</c> /
/// <c>setlist_groups_data</c>. Dzięki temu dwa tablety edytujące naraz nie rozjeżdżają się
/// ze sobą ani z oknem Cantio.
///
/// Odpowiedzi buduje WYŁĄCZNIE ta klasa (razem z <see cref="PilotStatus.BuildAckJson"/>) —
/// jedna metoda na komunikat, żeby nie powstały dwie niezależne listy pól (ten sam układ
/// zgubił kiedyś notatki pozycji zestawu, v1.6).
/// </summary>
public static class PilotCategorySync
{
    public const string CategoriesDataType = "categories_data";
    public const string GroupsDataType     = "setlist_groups_data";

    // Powody odmowy (pole `reason` w acku)
    public const string ReasonDuplicate = "duplicate";
    public const string ReasonNotFound  = "not_found";
    public const string ReasonEmptyName = "empty_name";
    public const string ReasonNotEmpty  = "not_empty";
    public const string ReasonEdge      = "edge";

    /// <summary>Co odświeżyć w UI desktopu po wykonanej komendzie.</summary>
    public enum RefreshScope { None, Categories, Groups }

    /// <param name="Response">JSON do NADAWCY (ack albo dane), null = nic nie odsyłamy</param>
    /// <param name="Broadcast">JSON do WSZYSTKICH klientów po mutacji, null = nic nie zmieniono</param>
    /// <param name="Scope">którą listę odświeżyć w oknie Cantio</param>
    public readonly record struct Result(string? Response, string? Broadcast, RefreshScope Scope);

    private static readonly Result Ignored = new(null, null, RefreshScope.None);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) => type switch
    {
        "get_categories" or "category_add" or "category_rename" or
        "category_delete" or "category_move" or
        "get_setlist_groups" or "setlist_group_add" or
        "setlist_group_rename" or "setlist_group_delete" => true,
        _ => false
    };

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>
    /// Komunikat <c>categories_data</c>. Kształt bez zmian względem v1.56 (na `ClientConnected`) —
    /// od v1.63 leci też jako broadcast po każdej mutacji kategorii.
    /// </summary>
    public static string BuildCategoriesJson(IEnumerable<Category> categories) =>
        JsonSerializer.Serialize(new
        {
            type       = CategoriesDataType,
            categories = categories.Select(c => new { id = c.Id, name = c.Name, number = c.Number })
        });

    /// <summary>Komunikat <c>setlist_groups_data</c> — kolejność dokładnie jak w CSV ustawienia.</summary>
    public static string BuildGroupsJson(IEnumerable<string> groups) =>
        JsonSerializer.Serialize(new { type = GroupsDataType, groups });

    // ─── Wejście ─────────────────────────────────────────────────────────

    public static async Task<Result> HandleAsync(DatabaseService db, string rawJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
            return Ignored;

        return typeEl.GetString() switch
        {
            "get_categories"       => new Result(BuildCategoriesJson(await db.GetCategoriesAsync()), null, RefreshScope.None),
            "category_add"         => await AddCategoryAsync(db, root),
            "category_rename"      => await RenameCategoryAsync(db, root),
            "category_delete"      => await DeleteCategoryAsync(db, root),
            "category_move"        => await MoveCategoryAsync(db, root),
            "get_setlist_groups"   => new Result(BuildGroupsJson(await db.GetSetlistGroupsAsync()), null, RefreshScope.None),
            "setlist_group_add"    => await AddGroupAsync(db, root),
            "setlist_group_rename" => await RenameGroupAsync(db, root),
            "setlist_group_delete" => await DeleteGroupAsync(db, root),
            _ => Ignored
        };
    }

    // ─── Kategorie ───────────────────────────────────────────────────────

    private static async Task<Result> AddCategoryAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "category_add";
        var name = Str(root, "name");
        if (name.Length == 0) return Deny(cmd, ReasonEmptyName);

        var existing = await db.FindCategoryByNameAsync(name);
        if (existing != null)
            return Deny(cmd, ReasonDuplicate, ("id", existing.Id), ("name", existing.Name));

        var cats = await db.GetCategoriesAsync();
        var cat = new Category
        {
            Name   = name,
            Number = cats.Count > 0 ? cats.Max(c => c.Number) + 1 : 1
        };
        await db.SaveCategoryAsync(cat);   // Id nadany przez EF
        return await OkCategoriesAsync(db, cmd, ("id", cat.Id), ("name", cat.Name), ("number", cat.Number));
    }

    private static async Task<Result> RenameCategoryAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "category_rename";
        if (!TryInt(root, "id", out var id)) return Deny(cmd, ReasonNotFound);
        var name = Str(root, "name");
        if (name.Length == 0) return Deny(cmd, ReasonEmptyName, ("id", id));

        var cat = await db.GetCategoryAsync(id);
        if (cat == null) return Deny(cmd, ReasonNotFound, ("id", id));

        // Zmiana samej wielkości liter („adwent" → „Adwent") to nadal ta sama kategoria,
        // więc duplikatem jest wyłącznie INNY rekord o zgodnej nazwie.
        var clash = await db.FindCategoryByNameAsync(name);
        if (clash != null && clash.Id != id)
            return Deny(cmd, ReasonDuplicate, ("id", id), ("name", clash.Name));

        cat.Name = name;
        await db.SaveCategoryAsync(cat);
        return await OkCategoriesAsync(db, cmd, ("id", cat.Id), ("name", cat.Name), ("number", cat.Number));
    }

    /// <summary>
    /// Kasowanie kategorii. Pusta — normalnie; NIEPUSTA wyłącznie z jawnym <c>withSongs:true</c>,
    /// bo w schemacie `Songs.CategoryId` jest `NOT NULL` z `ON DELETE CASCADE`: usunięcie samej
    /// kategorii zabrałoby ze sobą pieśni (a przy pieśni w zapisanym zestawie wywróciłoby się
    /// na `RESTRICT`). Bez flagi odsyłamy `not_empty` + liczbę pieśni, żeby tablet mógł zapytać.
    /// </summary>
    private static async Task<Result> DeleteCategoryAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "category_delete";
        if (!TryInt(root, "id", out var id)) return Deny(cmd, ReasonNotFound);

        var cat = await db.GetCategoryAsync(id);
        if (cat == null) return Deny(cmd, ReasonNotFound, ("id", id));

        int songs = await db.CountSongsInCategoryAsync(id);
        bool withSongs = root.TryGetProperty("withSongs", out var wsEl) &&
                         wsEl.ValueKind == JsonValueKind.True;

        if (songs > 0 && !withSongs)
            return Deny(cmd, ReasonNotEmpty, ("id", id), ("name", cat.Name), ("songs", songs));

        if (songs > 0) await db.DeleteCategoryWithSongsAsync(id);
        else           await db.DeleteCategoryAsync(id);

        return await OkCategoriesAsync(db, cmd, ("id", id), ("name", cat.Name), ("songs", songs));
    }

    private static async Task<Result> MoveCategoryAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "category_move";
        if (!TryInt(root, "id", out var id)) return Deny(cmd, ReasonNotFound);
        var dir = Str(root, "direction");
        int delta = dir switch { "up" => -1, "down" => +1, _ => 0 };
        if (delta == 0) return Deny(cmd, ReasonEdge, ("id", id));

        var moved = await db.MoveCategoryAsync(id, delta);
        if (moved is null)
        {
            // brak kategorii vs. ruch poza zakres — rozróżniamy, bo tablet inaczej reaguje
            var exists = await db.GetCategoryAsync(id) != null;
            return Deny(cmd, exists ? ReasonEdge : ReasonNotFound, ("id", id));
        }
        return await OkCategoriesAsync(db, cmd, ("id", id), ("number", moved.Value));
    }

    // ─── Grupy zestawów ──────────────────────────────────────────────────
    // Parytet z UI Cantio: grupa to wyłącznie wpis w CSV ustawienia `setlist_groups`.
    // Zestawy trzymają nazwę grupy luźnym stringiem i NIE są ruszane (tak samo jak
    // `SaveGroupAsync`/`DeleteGroupAsync` w DisplayViewModel) — do acka trafia
    // `setlists:N` jako ostrzeżenie, ile zestawów zostaje przy starej nazwie.

    private static async Task<Result> AddGroupAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "setlist_group_add";
        var name = Str(root, "name");
        if (name.Length == 0) return Deny(cmd, ReasonEmptyName);

        var groups = await db.GetSetlistGroupsAsync();
        var existing = groups.FirstOrDefault(g => DatabaseService.NameEquals(g, name));
        if (existing != null) return Deny(cmd, ReasonDuplicate, ("name", existing));

        groups.Add(name);
        await db.SaveSetlistGroupsAsync(groups);
        return Ok(cmd, BuildGroupsJson(groups), RefreshScope.Groups, ("name", name));
    }

    private static async Task<Result> RenameGroupAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "setlist_group_rename";
        var name    = Str(root, "name");
        var newName = Str(root, "newName");
        if (name.Length == 0)    return Deny(cmd, ReasonNotFound);
        if (newName.Length == 0) return Deny(cmd, ReasonEmptyName, ("name", name));

        var groups = await db.GetSetlistGroupsAsync();
        int idx = groups.FindIndex(g => DatabaseService.NameEquals(g, name));
        if (idx < 0) return Deny(cmd, ReasonNotFound, ("name", name));

        var clash = groups.FindIndex(g => DatabaseService.NameEquals(g, newName));
        if (clash >= 0 && clash != idx)
            return Deny(cmd, ReasonDuplicate, ("name", name), ("newName", groups[clash]));

        var old = groups[idx];
        groups[idx] = newName;
        await db.SaveSetlistGroupsAsync(groups);
        int setlists = await db.CountSetlistsInGroupAsync(old);
        return Ok(cmd, BuildGroupsJson(groups), RefreshScope.Groups,
                  ("name", old), ("newName", newName), ("setlists", setlists));
    }

    private static async Task<Result> DeleteGroupAsync(DatabaseService db, JsonElement root)
    {
        const string cmd = "setlist_group_delete";
        var name = Str(root, "name");
        if (name.Length == 0) return Deny(cmd, ReasonNotFound);

        var groups = await db.GetSetlistGroupsAsync();
        int idx = groups.FindIndex(g => DatabaseService.NameEquals(g, name));
        if (idx < 0) return Deny(cmd, ReasonNotFound, ("name", name));

        var old = groups[idx];
        groups.RemoveAt(idx);
        await db.SaveSetlistGroupsAsync(groups);
        int setlists = await db.CountSetlistsInGroupAsync(old);
        return Ok(cmd, BuildGroupsJson(groups), RefreshScope.Groups,
                  ("name", old), ("setlists", setlists));
    }

    // ─── Pomocnicze ──────────────────────────────────────────────────────

    private static async Task<Result> OkCategoriesAsync(
        DatabaseService db, string cmd, params (string Key, object? Value)[] extra) =>
        Ok(cmd, BuildCategoriesJson(await db.GetCategoriesAsync()), RefreshScope.Categories, extra);

    private static Result Ok(string cmd, string broadcast, RefreshScope scope,
                             params (string Key, object? Value)[] extra) =>
        new(PilotStatus.BuildAckJson(cmd, true, extra), broadcast, scope);

    private static Result Deny(string cmd, string reason, params (string Key, object? Value)[] extra) =>
        new(PilotStatus.BuildAckJson(cmd, false,
                [("reason", reason), .. extra]), null, RefreshScope.None);

    private static string Str(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? "").Trim() : "";

    private static bool TryInt(JsonElement root, string prop, out int value)
    {
        value = 0;
        return root.TryGetProperty(prop, out var el) &&
               el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }
}
