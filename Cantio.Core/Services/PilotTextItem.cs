using System.Text.Json;

namespace Cantio.Services;

/// <summary>
/// Tekst jednorazowy sterowany z Pilota: `setlist_add_text` i `setlist_update_text`.
///
/// Inaczej niż pozostałe komendy zestawów, te NIE dotykają bazy — mutują BIEŻĄCY zestaw, który
/// żyje w pamięci `DisplayViewModel` (dokładnie jak przycisk 📝 w oknie Cantio; do bazy trafia
/// dopiero przy „ZAPISZ ZESTAW"). Dlatego ta klasa robi wyłącznie rzeczy czyste — parsuje JSON,
/// rozstrzyga odmowy i składa acki — a samą mutację wykonuje `MainWindow` na Dispatcherze
/// przez wspólną ścieżkę `DisplayViewModel.ApplyTextItem` (tę samą, co edytor w oknie).
///
/// **Edycja w miejscu nie odpala `CollectionChanged`**, więc po `setlist_update_text` broadcast
/// `setlist` trzeba wywołać JAWNIE — inaczej tablet obok zostałby ze starą treścią.
/// </summary>
public static class PilotTextItem
{
    public const string AddCommand    = "setlist_add_text";
    public const string UpdateCommand = "setlist_update_text";

    public const string ReasonEmptyText = "empty_text";
    public const string ReasonBadIndex  = "bad_index";
    public const string ReasonNotText   = "not_text";

    public enum Kind { None, Add, Update }

    /// <param name="Operation">rozpoznana komenda (<see cref="Kind.None"/> = nie nasza / niepoprawny JSON)</param>
    /// <param name="Title">tytuł podany przez użytkownika (pusty = wyprowadzimy z treści)</param>
    /// <param name="Text">treść pozycji (już przycięta)</param>
    /// <param name="Index">pozycja do edycji (tylko <see cref="Kind.Update"/>)</param>
    /// <param name="Reason">powód odmowy rozstrzygalny bez patrzenia na zestaw; null = można wykonać</param>
    public readonly record struct Request(Kind Operation, string? Title, string Text, int Index, string? Reason)
    {
        public bool IsAdd    => Operation == Kind.Add;
        public bool IsUpdate => Operation == Kind.Update;
        public bool Denied   => Reason != null;
        public string Command => CommandName(Operation);
    }

    private static readonly Request Ignored = new(Kind.None, null, "", -1, null);

    public static bool IsCommand(string? type) => type == AddCommand || type == UpdateCommand;

    public static string CommandName(Kind kind) => kind switch
    {
        Kind.Add    => AddCommand,
        Kind.Update => UpdateCommand,
        _           => ""
    };

    /// <summary>Ack do NADAWCY — jedyne miejsce składania odpowiedzi obu komend.</summary>
    public static string BuildAck(Kind kind, bool ok, string? reason = null) =>
        reason == null
            ? PilotStatus.BuildAckJson(CommandName(kind), ok)
            : PilotStatus.BuildAckJson(CommandName(kind), ok, ("reason", reason));

    /// <summary>Ack odmowy dla gotowego żądania.</summary>
    public static string BuildDenial(Request request, string reason) =>
        BuildAck(request.Operation, false, reason);

    /// <summary>
    /// Parsowanie + walidacja tego, co da się rozstrzygnąć bez zestawu: pusta treść (`empty_text`)
    /// i brak/ujemny `index` przy edycji (`bad_index`).
    /// </summary>
    public static Request Parse(string rawJson)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return Ignored; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String)
            return Ignored;

        var kind = typeEl.GetString() switch
        {
            AddCommand    => Kind.Add,
            UpdateCommand => Kind.Update,
            _             => Kind.None
        };
        if (kind == Kind.None) return Ignored;

        var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
            ? titleEl.GetString() : null;
        var text = root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString() ?? "" : "";

        var index = -1;
        if (kind == Kind.Update)
        {
            if (!root.TryGetProperty("index", out var idxEl) || idxEl.ValueKind != JsonValueKind.Number ||
                !idxEl.TryGetInt32(out index))
                return new Request(kind, title, text.Trim(), -1, ReasonBadIndex);
        }

        if (string.IsNullOrWhiteSpace(text))
            return new Request(kind, title, "", index, ReasonEmptyText);

        return new Request(kind, title, text.Trim(), index, null);
    }

    /// <summary>
    /// Odmowa zależna od stanu zestawu (indeks poza zakresem / pozycja nie jest tekstem)
    /// albo null, gdy edycję można wykonać. Fakty podaje wołający — reguła zostaje tutaj.
    /// </summary>
    public static string? ValidateTarget(int index, int itemCount, bool isTextItem)
    {
        if (index < 0 || index >= itemCount) return ReasonBadIndex;
        return isTextItem ? null : ReasonNotText;
    }
}
