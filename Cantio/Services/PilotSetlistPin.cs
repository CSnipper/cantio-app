using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Przypinanie zestawów z Pilota (v1.63+).
///
/// Przypięcie to na desktopie panel PRZYPIĘTE (zestawy bieżącego tygodnia), czyli `Setlist.IsPinned`.
/// Stan jest SYNCHRONIZOWANY: jedna prawda w bazie desktopu, tablet wyłącznie komenduje, a desktop
/// rozgłasza skutek do WSZYSTKICH klientów (łącznie z nadawcą) i odświeża własne UI tą samą ścieżką
/// co po kliknięciu pinezki w oknie.
///
/// Rozgłaszamy MAŁY komunikat <c>setlist_pinned</c>, a nie świeże <c>setlists_data</c> — pełna lista
/// zestawów bywa duża (u użytkownika ~270 pozycji) i leci wyłącznie na żądanie.
///
/// **`UpdatedAt` zestawu MUSI zostać nietknięte** — przypięcie to flaga UI, nie zmiana treści.
/// Dlatego zapis idzie przez <see cref="DatabaseService.SetSetlistPinnedAsync"/>, nigdy przez
/// `SaveSetlistAsync`; inaczej każde przypięcie wyglądałoby dla Pilota jak edycja na komputerze
/// i generowało fałszywy konflikt synchronizacji (zob. tabela `UpdatedAt` w Services/CLAUDE.md).
/// </summary>
public static class PilotSetlistPin
{
    public const string PinCommand    = "setlist_pin";
    public const string PinnedMessage = "setlist_pinned";

    public const string ReasonNotFound = "not_found";

    /// <param name="Response">JSON do NADAWCY (ack), null = nic nie odsyłamy</param>
    /// <param name="Broadcast">JSON do WSZYSTKICH klientów po mutacji, null = nic nie zmieniono</param>
    /// <param name="DesktopId">zestaw, którego dotyczyła zmiana (0 = nic nie zmieniono)</param>
    /// <param name="Pinned">nowy stan przypięcia</param>
    public readonly record struct Result(string? Response, string? Broadcast, int DesktopId, bool Pinned)
    {
        /// <summary>Czy okno Cantio ma odświeżyć panel PRZYPIĘTE.</summary>
        public bool Changed => Broadcast != null;
    }

    private static readonly Result Ignored = new(null, null, 0, false);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) => type == PinCommand;

    /// <summary>
    /// Komunikat <c>setlist_pinned</c> — JEDYNE miejsce jego składania. Używają go obie ścieżki:
    /// komenda z Pilota i klik pinezki w oknie Cantio (przez `DisplayViewModel.SetlistPinChanged`).
    /// </summary>
    public static string BuildPinnedJson(int desktopId, bool pinned) =>
        JsonSerializer.Serialize(new { type = PinnedMessage, desktopId, pinned });

    public static async Task<Result> HandleAsync(DatabaseService db, string rawJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String ||
            !IsCommand(typeEl.GetString()))
            return Ignored;

        if (!root.TryGetProperty("desktopId", out var idEl) ||
            idEl.ValueKind != JsonValueKind.Number ||
            !idEl.TryGetInt32(out var desktopId))
            return Deny(0, ReasonNotFound);

        // brak pola `pinned` = przypnij (najczęstsza intencja), zły typ traktujemy tak samo
        bool pinned = !root.TryGetProperty("pinned", out var pEl) ||
                      pEl.ValueKind != JsonValueKind.False;

        // BEZ dotykania UpdatedAt — patrz komentarz klasy
        if (!await db.SetSetlistPinnedAsync(desktopId, pinned))
            return Deny(desktopId, ReasonNotFound);

        return new Result(
            PilotStatus.BuildAckJson(PinCommand, true, ("desktopId", desktopId), ("pinned", pinned)),
            BuildPinnedJson(desktopId, pinned),
            desktopId, pinned);
    }

    private static Result Deny(int desktopId, string reason) =>
        new(PilotStatus.BuildAckJson(PinCommand, false, ("reason", reason), ("desktopId", desktopId)),
            null, 0, false);
}
