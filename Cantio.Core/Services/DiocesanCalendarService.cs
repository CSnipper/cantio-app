using System.IO;
using System.Reflection;
using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

public enum Ranga
{
    WspDowolne = 30,
    WspObowiazkowe = 40,
    Swieto = 60,
    Uroczystosc = 80,
}

public record Celebration(
    string Data,          // "MM-DD"
    string Tytul,
    Ranga Ranga,
    bool Powszechny,
    List<string> Diecezje
)
{
    public string RangaLabel => Ranga switch
    {
        Ranga.Uroczystosc => "uroczystość",
        Ranga.Swieto => "święto",
        Ranga.WspObowiazkowe => "wsp. obow.",
        _ => "wsp. dow.",
    };
}

/// <summary>
/// Kalendarz diecezji polskich (wbudowany zasób kalendarz_diecezji.json) — warstwa sanktoralna
/// z rangą i wymiarem diecezjalnym. Port repozytorium z CantioPilot; snapshot danych: 7.10.2023.
/// </summary>
public static class DiocesanCalendarService
{
    private static Dictionary<string, List<Celebration>>? _byDate;
    private static readonly Lock _lock = new();

    /// <summary>Wybrana diecezja (mianownik, np. "krakowska"); "" = kalendarz ogólny.</summary>
    public static string CurrentDiocese { get; set; } = "";

    /// <summary>Ręczny wybór formularza per data (z dropdownu przy zegarze) — tylko na czas sesji.</summary>
    private static readonly Dictionary<DateOnly, string> _overrides = [];

    public static IReadOnlyList<string> Diecezje { get; } = new[]
    {
        "białostocka", "bielsko-żywiecka", "bydgoska", "częstochowska", "drohiczyńska",
        "elbląska", "ełcka", "gdańska", "gliwicka", "gnieźnieńska", "kaliska", "katowicka",
        "kielecka", "koszalińsko-kołobrzeska", "krakowska", "legnicka", "lubelska", "łomżyńska",
        "łowicka", "łódzka", "opolska", "pelplińska", "płocka", "poznańska", "przemyska",
        "radomska", "rzeszowska", "sandomierska", "siedlecka", "sosnowiecka",
        "szczecińsko-kamieńska", "świdnicka", "tarnowska", "toruńska", "warmińska", "warszawska",
        "warszawsko-praska", "włocławska", "wrocławska", "zamojsko-lubaczowska",
        "zielonogórsko-gorzowska", "Ordynariat Polowy Wojska Polskiego",
    };

    private static void Load()
    {
        if (_byDate != null) return;
        lock (_lock)
        {
            if (_byDate != null) return;
            var map = new Dictionary<string, List<Celebration>>();
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("Cantio.Assets.Data.kalendarz_diecezji.json")!;
                using var reader = new StreamReader(stream);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var data = el.GetProperty("data").GetString() ?? "";
                    if (data.Length == 0) continue;
                    var diec = new List<string>();
                    if (el.TryGetProperty("diecezje", out var dArr))
                        foreach (var d in dArr.EnumerateArray())
                            diec.Add(d.GetString() ?? "");
                    var c = new Celebration(
                        Data: data,
                        Tytul: el.GetProperty("tytul").GetString()?.Trim() ?? "",
                        Ranga: el.GetProperty("ranga").GetString() switch
                        {
                            "uroczystosc" => Ranga.Uroczystosc,
                            "swieto" => Ranga.Swieto,
                            "wsp_obowiazkowe" => Ranga.WspObowiazkowe,
                            _ => Ranga.WspDowolne,
                        },
                        Powszechny: el.GetProperty("zakres").GetString() == "powszechny",
                        Diecezje: diec);
                    if (!map.TryGetValue(data, out var list)) map[data] = list = [];
                    list.Add(c);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiocesanCalendar] load failed: {ex}");
            }
            _byDate = map;
        }
    }

    /// <summary>
    /// Obchody na dzień: powszechne + diecezjalne wybranej diecezji, dedup po tytule
    /// (wyższa ranga wygrywa), sort malejąco wg rangi.
    /// </summary>
    public static List<Celebration> ForDate(DateOnly date) => ForDate(date, CurrentDiocese);

    /// <summary>
    /// Jak <see cref="ForDate(DateOnly)"/>, ale z jawnie podaną diecezją — do kodu bezgłowego
    /// (obsługa komend z Pilota, testy), który nie może polegać na statyku ustawianym przez okno.
    /// </summary>
    public static List<Celebration> ForDate(DateOnly date, string diocese)
    {
        Load();
        var mmdd = $"{date.Month:D2}-{date.Day:D2}";
        if (_byDate == null || !_byDate.TryGetValue(mmdd, out var all)) return [];
        diocese ??= "";
        var applicable = all.Where(c => c.Powszechny ||
            (diocese.Length > 0 && c.Diecezje.Contains(diocese)));
        var byTitle = new Dictionary<string, Celebration>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in applicable.OrderByDescending(c => (int)c.Ranga))
            byTitle.TryAdd(c.Tytul, c);
        return byTitle.Values.OrderByDescending(c => (int)c.Ranga).ToList();
    }

    public static void SetOverride(DateOnly date, string? title)
    {
        if (string.IsNullOrEmpty(title)) _overrides.Remove(date);
        else _overrides[date] = title;
    }

    public static string? GetOverride(DateOnly date) =>
        _overrides.TryGetValue(date, out var t) ? t : null;

    /// <summary>
    /// Efektywna nazwa zestawu dla dnia — do „Przypnij tydzień" i wyświetlania:
    /// ręczny wybór > uroczystość/święto wypierające dzień powszedni (na niedzielę tylko
    /// uroczystość) > temporalna nazwa dnia.
    /// </summary>
    public static string EffectiveSetlistName(DateOnly date, LiturgicalDay day)
        => EffectiveSetlistName(date, day, CurrentDiocese);

    /// <summary>Jak wyżej, ale z jawnie podaną diecezją (kod bezgłowy — Pilot, testy).</summary>
    public static string EffectiveSetlistName(DateOnly date, LiturgicalDay day, string diocese)
    {
        var ovr = GetOverride(date);
        if (ovr != null) return ovr;
        var top = ForDate(date, diocese).FirstOrDefault();
        if (top == null) return day.SetlistName;
        bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
        bool displaces = isSunday
            ? top.Ranga == Ranga.Uroczystosc && day.Group == "zwykly"
            : top.Ranga >= Ranga.Swieto;
        return displaces ? top.Tytul : day.SetlistName;
    }
}
