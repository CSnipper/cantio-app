using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Cantio.Helpers;

namespace Cantio.Services;

/// <summary>
/// Ustawienia projekcji (zakładka WYGLĄD) przez protokół WS — v1.63+.
///
/// Po co: organista siedzi przy tablecie, a z ostatniej ławki „nie widać”. Zmiana czcionki,
/// rozmiaru, koloru czy tła musi być możliwa bez podchodzenia do komputera.
///
/// Kierunek prawdy jak przy kategoriach: <b>baza desktopu jest jedynym źródłem</b>. Tablet
/// wyłącznie komenduje, desktop zapisuje, przebudowuje slajdy i rozgłasza nowy stan do
/// WSZYSTKICH klientów (drugi tablet musi widzieć zmianę), a swoje UI odświeża tą samą
/// ścieżką co po kliknięciu „ZAPISZ USTAWIENIA”.
///
/// <para><b>Atomowość.</b> <c>set_display_settings</c> to aktualizacja CZĘŚCIOWA — zapisywane
/// są wyłącznie przysłane klucze. Ale pakiet jest przyjmowany albo odrzucany w CAŁOŚCI:
/// jeden nieznany klucz albo jedna absurdalna wartość i nie zapisujemy NICZEGO. Inaczej
/// tablet ze starszą/nowszą listą pól zostawiłby ustawienia w stanie w pół drogi — a to
/// wygląda dokładnie jak awaria projekcji w trakcie mszy.</para>
///
/// <para><b>Czcionki.</b> Odpowiedź niesie DWIE listy, dokładnie jak combobox w oknie Cantio:
/// <c>fonts</c> (wbudowane w Cantio — te same na każdym komputerze) i <c>systemFonts</c>
/// (zainstalowane w Windows). Systemowych nie pomijamy, bo bieżące ustawienie parafii
/// zwykle NIM właśnie jest (domyślnie „Segoe UI”) — lista bez niego nie pozwoliłaby nawet
/// wrócić do stanu wyjściowego. Koszt to kilka kB, czyli rząd wielkości mniej niż
/// <c>setlists_data</c>.</para>
///
/// Komunikaty składa WYŁĄCZNIE ta klasa (+ <see cref="PilotStatus.BuildAckJson"/>) — jedna
/// metoda na komunikat, żeby nie powstały dwie niezależne listy pól (ten sam układ zgubił
/// kiedyś notatki pozycji zestawu, v1.6).
/// </summary>
public static class PilotDisplaySettings
{
    public const string GetCommand = "get_display_settings";
    public const string SetCommand = "set_display_settings";
    public const string DataType   = "display_settings_data";

    // Powody odmowy (pole `reason` w acku)
    public const string ReasonUnknownKey   = "unknown_key";
    public const string ReasonInvalidValue = "invalid_value";
    public const string ReasonEmpty        = "empty_payload";

    /// <param name="Response">JSON do NADAWCY (ack albo dane), null = nic nie odsyłamy</param>
    /// <param name="Broadcast">JSON do WSZYSTKICH klientów po zapisie, null = nic nie zmieniono</param>
    public readonly record struct Result(string? Response, string? Broadcast)
    {
        /// <summary>Czy desktop ma przebudować slajdy i odświeżyć zakładkę WYGLĄD.</summary>
        public bool Changed => Broadcast != null;
    }

    private static readonly Result Ignored = new(null, null);

    /// <summary>Czy <paramref name="type"/> obsługuje ta klasa (routing w RemoteControlServer).</summary>
    public static bool IsCommand(string? type) => type is GetCommand or SetCommand;

    // ─── Biała lista kluczy ──────────────────────────────────────────────
    // Klucze są DOKŁADNIE te, których używa DatabaseService.GetSettings() — nie ma tu
    // drugiego słownika nazw. Czego nie ma na liście, tego przez WS zmienić nie można
    // (m.in. `projection_screen`, `language`, `app_mode`, `pilot_*` — to nie jest wygląd,
    // a zdalna zmiana ekranu projekcji albo trybu pracy potrafi odciąć operatora).

    private enum Kind { Text, Flag, Number, Count }

    private sealed record Field(
        string Key,
        Kind Kind,
        double Min = 0,
        double Max = 0,
        Func<string, bool>? TextOk = null);

    private static readonly Field[] Whitelist =
    [
        new("font_family",         Kind.Text,   TextOk: IsKnownFont),
        new("font_size",           Kind.Number, 8,    400),
        new("font_bold",           Kind.Flag),
        new("font_auto_fit",       Kind.Flag),
        new("text_align",          Kind.Text,   TextOk: v => v is "left" or "center" or "right"),
        new("text_color",          Kind.Text,   TextOk: IsHexColor),
        new("line_height",         Kind.Number, 0.5,  4),
        new("shadow_enabled",      Kind.Flag),
        new("shadow_blur",         Kind.Number, 0,    100),
        new("shadow_depth",        Kind.Number, 0,    50),
        new("shadow_opacity",      Kind.Number, 0,    1),
        new("bg_color",            Kind.Text,   TextOk: IsHexColor),
        new("bg_image",            Kind.Text,   TextOk: IsClearOrExistingFile),
        new("bg_image_opacity",    Kind.Number, 0,    1),
        new("text_position",       Kind.Text,   TextOk: v => v is "top" or "center" or "bottom"),
        new("text_margin_h",       Kind.Number, 0,    1000),
        new("text_margin_v",       Kind.Number, 0,    1000),
        new("bg_gradient_enabled", Kind.Flag),
        new("bg_gradient_type",    Kind.Text,   TextOk: v => v is "linear" or "radial"),
        new("bg_gradient_color1",  Kind.Text,   TextOk: IsHexColor),
        new("bg_gradient_color2",  Kind.Text,   TextOk: IsHexColor),
        new("bg_gradient_angle",   Kind.Number, 0,    360),
        new("psalm_category_id",   Kind.Count,  0,    int.MaxValue),
    ];

    private static readonly Dictionary<string, Field> ByKey =
        Whitelist.ToDictionary(f => f.Key, StringComparer.Ordinal);

    /// <summary>Pełna biała lista kluczy w kolejności komunikatu (do testów i dokumentacji).</summary>
    public static IReadOnlyList<string> Keys { get; } = Whitelist.Select(f => f.Key).ToArray();

    // ─── Budowanie komunikatów D→P (jedyne miejsce) ──────────────────────

    /// <summary>
    /// Komunikat <c>display_settings_data</c>. Używają go OBIE ścieżki: odpowiedź na
    /// <c>get_display_settings</c>, broadcast po zapisie z tabletu i broadcast po
    /// „ZAPISZ USTAWIENIA” w oknie Cantio.
    /// </summary>
    public static string BuildDataJson(DisplaySettings s) =>
        JsonSerializer.Serialize(new
        {
            type        = DataType,
            settings    = ToWire(s),
            fonts       = EmbeddedFonts.Names,
            systemFonts = SystemFontNames.Value
        });

    /// <summary>
    /// Ustawienia w postaci klucz→wartość, kluczami z tabeli <c>settings</c> (nie nazwami
    /// właściwości DTO) — tablet odsyła DOKŁADNIE te klucze w <c>set_display_settings</c>.
    /// </summary>
    private static Dictionary<string, object?> ToWire(DisplaySettings s) => new()
    {
        ["font_family"]         = s.FontFamily,
        ["font_size"]           = s.FontSize,
        ["font_bold"]           = s.FontBold,
        ["font_auto_fit"]       = s.FontAutoFit,
        ["text_align"]          = s.TextAlign,
        ["text_color"]          = s.TextColor,
        ["line_height"]         = s.LineHeightMultiplier,
        ["shadow_enabled"]      = s.ShadowEnabled,
        ["shadow_blur"]         = s.ShadowBlur,
        ["shadow_depth"]        = s.ShadowDepth,
        ["shadow_opacity"]      = s.ShadowOpacity,
        ["bg_color"]            = s.BackgroundColor,
        // pusty string, nigdy null — tablet ma tam prosty `String`, a „brak tła" i „null"
        // to dla niego ta sama sytuacja
        ["bg_image"]            = s.BackgroundImagePath ?? "",
        ["bg_image_opacity"]    = s.BackgroundImageOpacity,
        ["text_position"]       = s.TextPosition,
        ["text_margin_h"]       = s.TextMarginH,
        ["text_margin_v"]       = s.TextMarginV,
        ["bg_gradient_enabled"] = s.GradientEnabled,
        ["bg_gradient_type"]    = s.GradientType,
        ["bg_gradient_color1"]  = s.GradientColor1,
        ["bg_gradient_color2"]  = s.GradientColor2,
        ["bg_gradient_angle"]   = s.GradientAngle,
        ["psalm_category_id"]   = s.PsalmCategoryId,
    };

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
            GetCommand => new Result(BuildDataJson(db.GetSettings()), null),
            SetCommand => await SetAsync(db, root),
            _          => Ignored
        };
    }

    private static async Task<Result> SetAsync(DatabaseService db, JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settings) ||
            settings.ValueKind != JsonValueKind.Object)
            return Deny(ReasonEmpty);

        // FAZA 1 — walidacja CAŁEGO pakietu. Nic nie dotyka bazy, dopóki nie wiadomo,
        // że przejdzie w całości (zob. „Atomowość" w opisie klasy).
        var pending = new List<(string Key, string Value)>();
        foreach (var prop in settings.EnumerateObject())
        {
            if (!ByKey.TryGetValue(prop.Name, out var field))
                return Deny(ReasonUnknownKey, prop.Name);
            if (!TryNormalize(field, prop.Value, out var value))
                return Deny(ReasonInvalidValue, prop.Name);
            pending.Add((field.Key, value));
        }

        if (pending.Count == 0) return Deny(ReasonEmpty);

        // FAZA 2 — zapis
        foreach (var (key, value) in pending)
            await db.SaveSettingAsync(key, value);

        return new Result(
            PilotStatus.BuildAckJson(SetCommand, true, ("keys", pending.Count)),
            BuildDataJson(db.GetSettings()));
    }

    /// <summary>
    /// Wartość z JSON → string do tabeli <c>settings</c>. Format MUSI się zgadzać z tym,
    /// co zapisuje `SzablonViewModel.SaveAsync` i czyta `DatabaseService.GetSettings`
    /// (flagi jako <c>true</c>/<c>false</c>, liczby w bieżącej kulturze — pl-PL używa
    /// przecinka i inaczej zapisana wartość ułamkowa wróciłaby jako śmieć).
    /// </summary>
    private static bool TryNormalize(Field field, JsonElement value, out string result)
    {
        result = "";
        switch (field.Kind)
        {
            case Kind.Flag:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                result = value.GetBoolean() ? "true" : "false";
                return true;

            case Kind.Number:
            {
                // Wyłącznie liczba JSON — „abc" i "60" (string) są odrzucane, bo cichy
                // fallback do wartości domyślnej wyglądałby jak samowolna zmiana wyglądu.
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var d)) return false;
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                if (d < field.Min || d > field.Max) return false;
                result = d.ToString(CultureInfo.CurrentCulture);
                return true;
            }

            case Kind.Count:
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var i)) return false;
                if (i < field.Min || i > field.Max) return false;
                result = i.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            default:
            {
                if (value.ValueKind != JsonValueKind.String) return false;
                var s = (value.GetString() ?? "").Trim();
                if (field.TextOk != null && !field.TextOk(s)) return false;
                result = s;
                return true;
            }
        }
    }

    private static Result Deny(string reason, string? key = null) =>
        new(PilotStatus.BuildAckJson(SetCommand, false, ("reason", reason), ("key", key)), null);

    // ─── Walidatory wartości tekstowych ──────────────────────────────────

    /// <summary>#RRGGBB albo #AARRGGBB — dokładnie to, co potrafi `ColorConverter` w projekcji.</summary>
    public static bool IsHexColor(string v)
    {
        if (v.Length is not (7 or 9) || v[0] != '#') return false;
        for (int i = 1; i < v.Length; i++)
            if (!Uri.IsHexDigit(v[i])) return false;
        return true;
    }

    /// <summary>
    /// Obraz tła: pusty string = wyłącz (jedyna sensowna zmiana z tabletu, bo telefon nie
    /// widzi dysku komputera). Niepusta ścieżka musi istnieć — inaczej projekcja zostałaby
    /// z czarnym tłem i nikt by nie wiedział dlaczego.
    /// </summary>
    public static bool IsClearOrExistingFile(string v) => v.Length == 0 || File.Exists(v);

    /// <summary>
    /// Czcionka musi być wbudowana albo zainstalowana w systemie. Literówka z tabletu
    /// oznaczałaby fallback WPF na domyślny krój w środku mszy.
    /// </summary>
    public static bool IsKnownFont(string v) =>
        v.Length > 0 && (EmbeddedFonts.IsEmbedded(v) || SystemFontSet.Value.Contains(v));

    private static readonly Lazy<string[]> SystemFontNames = new(() =>
    {
        try
        {
            return Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return []; }
    });

    private static readonly Lazy<HashSet<string>> SystemFontSet =
        new(() => new HashSet<string>(SystemFontNames.Value, StringComparer.OrdinalIgnoreCase));
}
