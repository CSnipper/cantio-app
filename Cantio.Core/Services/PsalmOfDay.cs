using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// „Dzień liturgiczny → tytuł psalmu responsoryjnego" (tytuł pieśni z zasobu <c>psalmy_dual.json.gz</c>).
///
/// <para>Warstwa najniższa: zwraca TYLKO tytuł, który istnieje w zasobie, albo <c>null</c>. Gdy nie
/// ma pewności (brak psalmu w danych, obchód diecezjalny, dzień z wieloma psalmami, niejasna reguła)
/// — <c>null</c>, bez zgadywania i bez dopasowań rozmytych.</para>
///
/// <para>Cykl niedzielny/powszedni bierze z <see cref="LiturgicalCalendarService"/>, a o tym, czy psalm
/// idzie z OBCHODU czy z DNIA, rozstrzyga ta sama reguła rangi co nazwa zestawu
/// (<see cref="DiocesanCalendarService.EffectiveSetlistName(DateOnly, LiturgicalDay, string)"/>, razem
/// z ręcznym wyborem formularza). Konwencja kluczy jak w Pilocie (<c>PsalmKeys</c>/<c>dayKey</c>).</para>
/// </summary>
public static class PsalmOfDay
{
    public static string? TitleFor(DateOnly date) => TitleFor(date, DiocesanCalendarService.CurrentDiocese);

    public static string? TitleFor(DateOnly date, string diocese)
    {
        var day = LiturgicalCalendarService.GetDay(date);
        var name = DiocesanCalendarService.EffectiveSetlistName(date, day, diocese);
        var celebrations = DiocesanCalendarService.ForDate(date, diocese);
        bool overridden = DiocesanCalendarService.GetOverride(date) != null;

        string? title;
        if (name != day.SetlistName)
        {
            // Obchód wypierający dzień (ranga święta/uroczystości) albo ręcznie wybrany formularz.
            var cel = celebrations.FirstOrDefault(c => string.Equals(c.Tytul, name, StringComparison.OrdinalIgnoreCase));
            if (cel == null) title = null;
            // Wspomnienie bez własnego psalmu w danych: psalm dnia (także przy ręcznym wyborze).
            else title = cel.Ranga < Ranga.Swieto && !HasOwnPsalm(cel) ? ForDay(date, day) : ForCelebration(cel, date);
        }
        else if (!overridden && date.DayOfWeek == DayOfWeek.Sunday && day.Group == "zwykly"
                 && celebrations.Any(c => c.Powszechny && SundayDisplacers.Contains(c.Data)))
        {
            // Święto Pańskie / Zaduszki w niedzielę zwykłą liturgicznie wypiera niedzielę, ale reguła nazwy
            // zestawu tego nie robi (święto nie wypiera niedzieli) — nie wybieramy za nią: brak.
            return null;
        }
        // Wspomnienie obowiązkowe w dzień powszedni to obchód dnia wg reguły „Przypnij tydzień” (ta sama rangowa
        // decyzja co nazwa zestawu); psalm własny tylko dla wspomnień, które go mają w danych.
        else if (!overridden
                 && DiocesanCalendarService.PinSetlistName(date, day, diocese).Celebration is { } memorial
                 && HasOwnPsalm(memorial))
            title = ForCelebration(memorial, date);
        else
            title = ForDay(date, day);

        // Boże Narodzenie ma kilka mszy (wigilia/w nocy/o świcie/w dzień) o różnych psalmach, a dane niosą w N i S
        // różne refreny pod jednym kluczem — bez wiedzy o porze mszy nie rozstrzygamy.
        if (title == "BN Narodzenie Pańskie") return null;
        return title != null && Titles.Value.Contains(title) ? title : null;
    }

    // Wspomnienie powszechne z własnym psalmem w danych (poza nim obowiązuje psalm dnia).
    private static bool HasOwnPsalm(Celebration c)
        => c.Tytul == LiturgicalCalendarService.ImmaculateHeartTitle || (c.Powszechny && Memorials.Contains(c.Data));

    // Niedziela zwykła wypierana przez obchód Pański (2 II, 6 VIII, 14 IX, 9 XI) lub Zaduszki (2 XI).
    private static readonly HashSet<string> SundayDisplacers = ["02-02", "08-06", "09-14", "11-09", "11-02"];

    // Daty (MM-DD) wspomnień obowiązkowych, które mają własny psalm w danych.
    private static readonly HashSet<string> Memorials =
        ["01-26", "06-11", "07-26", "07-29", "08-22", "08-29", "09-15", "10-02", "10-07", "11-02", "11-21"];

    /// <summary>Obchody powszechne: data (MM-DD) → tytuł psalmu. Tytuły dokładnie jak w zasobie.</summary>
    private static readonly Dictionary<string, string> Celebrations = new()
    {
        ["01-01"] = "BN Świętej Bożej Rodzicielki Maryi, Uroczystość",
        ["01-06"] = "BN Objawienie Pańskie, Uroczystość",
        ["01-25"] = "Nawrócenie Świętego Pawła Apostoła",
        ["01-26"] = "Świętych Tymoteusza i Tytusa, Biskupów",
        ["02-02"] = "Ofiarowanie Pańskie",
        ["02-14"] = "Świętych Cyryla i Metodego",
        ["03-04"] = "ŚW. Kazimierza Królewicza",
        ["03-19"] = "ŚW. Józefa Oblubieńca NMP",
        ["03-25"] = "Zwiastowanie Pańskie",
        ["04-23"] = "ŚW. Wojciecha, Biskupa i Męczennika",
        ["04-25"] = "ŚW. Marka, Ewangelisty",
        ["04-29"] = "ŚW. Katarzyny Sieneńskiej, Dziewicy i Doktora Kościoła, Patronki Europy",
        ["05-03"] = "NMP Królowej Polski",
        ["05-06"] = "ŚW. Apostołów Filipa i Jakuba",
        ["05-08"] = "ŚW. Stanisława, Biskupa i Męczennika, Patrona Polski",
        ["05-14"] = "ŚW. Macieja Apostoła",
        ["05-16"] = "ŚW. Andrzeja Boboli, Prezbitera i Męczennika, Patrona Polski",
        ["05-31"] = "Nawiedzenie Najświętszej Maryi Panny",
        ["06-11"] = "ŚW. Barnaby, Apostoła",
        // Zasób ma dwa wpisy z różnym psalmem; "…JANA Chrzciciela" to psalm WIGILII (Ps 71), dzień ma Ps 139.
        ["06-24"] = "Narodzenie ŚW. Jana Chrzciciela",
        ["06-29"] = "Świętych Apostołów Piotra i Pawła",
        ["07-03"] = "ŚW. Tomasza Apostoła",
        ["07-11"] = "ŚW. Benedykta, Opata",
        ["07-22"] = "ŚW. Marii Magdaleny",
        ["07-23"] = "ŚW. Brygidy, Zakonnicy, Patronki Europy",
        ["07-25"] = "ŚW. Jakuba Apostoła",
        // Zasób ma dwa wpisy; "…i ANNY" niesie psalm cudzego dnia (Ps 78), "…i Anny" właściwy (Ps 132).
        ["07-26"] = "ŚW. Joachima i Anny",
        ["07-29"] = "Świętych Marty, Marii i Łazarza",
        ["08-09"] = "ŚW. Teresy Benedykty OD Krzyża (edyty Stein), Dziewicy i Męczennicy, Patronki Europy",
        ["08-10"] = "ŚW. Wawrzyńca, Diakona i Męczennika",
        // Klucz „Wniebowzięcia Najświętszej Maryi Panny” ma Ps 132 (psalm WIGILII); psalm mszy w dzień (N: Ps 45) jest pod krótkim.
        ["08-15"] = "Wniebowzięcia NMP",
        ["08-22"] = "NMP Królowej",
        ["08-24"] = "ŚW. Bartłomieja Apostoła",
        ["08-26"] = "Najświętszej Maryi Panny Częstochowskiej",
        ["08-29"] = "Męczeństwo ŚW. JANA Chrzciciela",
        ["09-08"] = "Narodzenie NMP",
        ["09-14"] = "Podwyższenie Krzyża",
        ["09-15"] = "Najświętszej Maryi Panny Bolesnej",
        ["09-18"] = "Świętego Stanisława Kostki",
        ["09-21"] = "ŚW. Mateusza Apostoła i Ewangelisty",
        ["09-29"] = "ŚW. Archaniołów Michała, Gabriela i Rafała",
        ["10-02"] = "Świętych Aniołów Stróżów",
        ["10-07"] = "Najświętszej Maryi Panny Różańcowej",
        ["10-18"] = "ŚW. Łukasza Ewangelisty",
        ["10-28"] = "Świętych Apostołów Szymona i JUDY Tadeusza",
        ["11-01"] = "Wszystkich Świętych",
        ["11-02"] = "Wszystkich Wiernych Zmarłych",
        ["11-09"] = "Rocznica Poświęcenia Bazyliki Laterańskiej",
        ["11-21"] = "Ofiarowanie Najświętszej Maryi Panny",
        ["11-30"] = "ŚW. Andrzeja Apostoła",
        ["12-08"] = "Niepokalanego Poczęcia NMP",
        ["12-26"] = "BN Świętego Szczepana, Pierwszego Męczennika - Święto",
        ["12-27"] = "BN Świętego Jana, Apostoła i Ewangelisty - Święto",
        ["12-28"] = "BN Świętych Młodzianków, Męczenników - Święto",
    };

    private static string? ForCelebration(Celebration c, DateOnly date)
    {
        if (c.Tytul == LiturgicalCalendarService.ImmaculateHeartTitle) return c.Tytul;
        if (!c.Powszechny) return null;   // obchody diecezjalne: w zasobie ich nie ma
        if (c.Data == "12-08" && date.DayOfWeek == DayOfWeek.Sunday)
            return "Niepokalanego Poczęcia NMP - (w Niedzielę)";
        if (c.Data == "08-06") return $"Przemienienie Pańskie - {SundayLetter(date)}";
        return Celebrations.GetValueOrDefault(c.Data);
    }

    /// <summary>Litera cyklu A/B/C dnia = cykl niedzieli otwierającej tydzień (rok liturgiczny zmienia się w niedzielę).</summary>
    private static string SundayLetter(DateOnly date)
    {
        var sunday = date.AddDays(-(int)date.DayOfWeek);
        // niedziele bez litery (25 XII, 1 I, 6 I w niedzielę) — cofamy się o tydzień
        for (int i = 0; i < 4; i++, sunday = sunday.AddDays(-7))
        {
            var c = LiturgicalCalendarService.GetDay(sunday).Cycle;
            if (c is "A" or "B" or "C") return c;
        }
        return "";
    }

    private static string? ForDay(DateOnly date, LiturgicalDay day)
    {
        var n = day.SetlistName;
        var c = day.Cycle;
        bool sunday = date.DayOfWeek == DayOfWeek.Sunday;
        switch (day.Group)
        {
            case "zwykly":
                return n switch
                {
                    "NMP Matki Kościoła" => n,
                    // święto niesie cykl (czytania zależne od rocznika), ale kalendarz nie wpisuje go do dnia
                    "Chrystus Najwyższy i Wieczny Kapłan" => $"Jezusa Chrystusa Najwyższego i Wiecznego Kapłana - {SundayLetter(date)}",
                    "Trójca Przenajświętsza" => $"Najświętszej Trójcy - {c}",
                    "Boże Ciało" => $"Najświętszego Ciała i KRWI Chrystusa - {c}",
                    "Najświętsze Serce Pana Jezusa" => $"Najświętszego Serca PANA Jezusa - {c}",
                    "Chrystus Król" => $"ZW 34 Nie {c}",
                    _ => sunday ? $"ZW {n}" : $"ZW {n} {(c == "I" ? "1" : c == "II" ? "2" : "?")}",
                };
            case "adwent":
                return $"ADW {n}";
            case "boznarodzenie":
                if (n.StartsWith("Świętej Rodziny")) return $"Świętej Rodziny Jezusa, Maryi i Józefa - {c}";
                if (n == "Chrzest Pański") return $"Chrzest Pański - {c}";
                return $"BN {n}";
            case "wielki_post":
                return n switch
                {
                    "Wielka Sobota" => null,   // dzień Wigilii Paschalnej (wiele psalmów, Msza dopiero wieczorem)
                    "Czwartek po Popielcu" => "WP Czwartek PO Popielcu",
                    "Piątek po Popielcu" => "WP Piątek PO Popielcu",
                    "Sobota po Popielcu" => "WP Sobota PO Popielcu",
                    _ => $"WP {n}",
                };
            case "wielkanoc":
                if (n == "Wniebowstąpienie Pańskie") return $"Wniebowstąpienie Pańskie - {c}";
                if (sunday && n.StartsWith("1 ")) return "WK Zmartwychwstanie Pańskie – MSZA W Dzień";
                if (sunday && n.StartsWith("8 ")) return $"Zesłanie Ducha Świętego - {c}";
                return $"WK {n}";
            default:
                return null;
        }
    }

    // Zbiór tytułów z zasobu (jak ImportPsalmySeedAsync: Trim, rekordy bez psalmu pomijane).
    private static readonly Lazy<HashSet<string>> Titles = new(() =>
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var gz = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cantio.Assets.Data.psalmy_dual.json.gz")!;
        using var json = new GZipStream(gz, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(json);
        foreach (var rec in doc.RootElement.EnumerateArray())
            if (rec.TryGetProperty("psalm", out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0
                && rec.GetProperty("dzien").GetString()?.Trim() is { Length: > 0 } t)
                set.Add(t);
        return set;
    });
}
