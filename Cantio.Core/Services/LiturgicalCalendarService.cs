using Cantio.Models;

namespace Cantio.Services;

public static class LiturgicalCalendarService
{
    public static LiturgicalDay GetDay(DateOnly date)
    {
        int year = date.Year;
        var easter = ComputeEaster(year);

        // Obchody RUCHOME liczone od Wielkanocy (i Chrystus Król — od Adwentu). Sprawdzane jako
        // PIERWSZE, bo mają własną nazwę i rangę; wszystkie leżą w przedziale
        // [Wielkanoc+42, 1. Nd Adwentu−7], więc nie kolidują z gałęziami Adwentu/BN/Wielkiego Postu.
        var movable = GetMovable(date, easter, year);
        if (movable != null)
            return new LiturgicalDay(movable.Value.Name, movable.Value.Group,
                movable.Value.CycleDependent ? GetSundayCycle(year) : "", movable.Value.Rank);

        // Adwent
        var adventStart = GetAdventStart(year);
        // Granica jest RUCHOMA: Boże Narodzenie kończy się Chrztem Pańskim (włącznie),
        // nie sztywnym 13 stycznia — Chrzest wypada między 6 a 13 I zależnie od roku.
        // Musi się stykać bez dziury/nakładki z GetOrdinaryWeek (`date > baptism`).
        var christmasEnd = GetBaptismOfLord(year);

        if (date >= adventStart)
        {
            int week = GetAdventWeek(date, adventStart);
            string cycle = GetSundayCycle(year + 1);
            string dayAbbr = GetDayAbbr(date);
            bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string name = isSunday ? $"{week} {dayAbbr} {cycle}" : $"{week} {dayAbbr}";
            return new LiturgicalDay(name, "adwent", isSunday ? cycle : "");
        }

        if (date.Month == 12 && date.Day >= 24)
        {
            return new LiturgicalDay(GetDayAbbr(date), "boznarodzenie");
        }
        if (date.Month == 1 && date <= christmasEnd)
        {
            return new LiturgicalDay(GetDayAbbr(date), "boznarodzenie");
        }

        // Wielki Post: Środa Popielcowa = 46 dni przed Wielkanocą
        var ashWednesday = easter.AddDays(-46);
        if (date >= ashWednesday && date < easter)
        {
            bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string cycle = GetSundayCycle(year);
            return new LiturgicalDay(GetLentName(date, easter, cycle), "wielki_post", isSunday ? cycle : "");
        }

        // Wielkanoc: Niedziela Wielkanocna do Zesłania Ducha Świętego (49 dni)
        var pentecost = easter.AddDays(49);
        if (date >= easter && date <= pentecost)
        {
            int week = (date.DayNumber - easter.DayNumber) / 7 + 1;
            string dayAbbr = GetDayAbbr(date);
            bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string cycle = GetSundayCycle(year);
            string name = isSunday ? $"{week} {dayAbbr} {cycle}" : $"{week} {dayAbbr}";
            return new LiturgicalDay(name, "wielkanoc", isSunday ? cycle : "");
        }

        // Zwykły
        {
            int week = GetOrdinaryWeek(date, easter, year);
            string dayAbbr = GetDayAbbr(date);
            bool isSunday = date.DayOfWeek == DayOfWeek.Sunday;
            string name = isSunday ? $"{week} {dayAbbr} {GetSundayCycle(year)}" : $"{week} {dayAbbr}";
            // Cykl powszedni I/II wg roku kalendarzowego (okres zwykły nie przechodzi przez Nowy Rok)
            string cycle = isSunday ? GetSundayCycle(year) : (year % 2 == 1 ? "I" : "II");
            return new LiturgicalDay(name, "zwykly", cycle);
        }
    }

    // ─── Obchody ruchome ──────────────────────────────────────────────────────────────
    //
    // Do wersji 1.63 rdzeń znał wyłącznie nazwy TEMPORALNE, więc Boże Ciało nazywało się
    // „13 Czw”, a Chrystus Król „34 Nie C”. Cena była trojaka: zły dzień w pasku górnym,
    // „Przypnij tydzień” zakładające zestaw pod nazwą dnia powszedniego (a więc inny zestaw
    // co roku, bo numer tygodnia się przesuwa) i podpis obchodu nieświadomy uroczystości.
    //
    // Każdy wpis niesie RANGĘ, bo o pierwszeństwie wobec kalendarza diecezji
    // (<see cref="DiocesanCalendarService.EffectiveSetlistName"/>) rozstrzyga ranga, a nie
    // kolejność sprawdzeń — inaczej święto Nawiedzenia NMP (31 V) zabierało nazwę Bożemu Ciału
    // w latach, w których oba wypadały tego samego dnia (np. 2029).
    //
    // Niepokalane Serce NMP (sobota po Sercu Jezusa) NIE jest tu obchodem dnia — to
    // WSPOMNIENIE, więc wchodzi warstwą sanktoralną przez <see cref="MovableCelebrations"/>.
    // Wpisanie go tutaj skasowałoby sobotę okresu zwykłego razem z jej czytaniami.

    /// <param name="CycleDependent">czytania zależne od cyklu A/B/C (wtedy dzień niesie literę)</param>
    private readonly record struct Movable(string Name, string Group, Ranga Rank, bool CycleDependent);

    private static Movable? GetMovable(DateOnly date, DateOnly easter, int year)
    {
        var pentecost = easter.AddDays(49);
        var corpusChristi = pentecost.AddDays(11);          // czwartek po Trójcy

        // Wniebowstąpienie: w Polsce PRZENIESIONE na 7. Niedzielę Wielkanocy (KEP 2003),
        // czyli Wielkanoc+42 — nie na czwartek Wielkanoc+39.
        if (date == easter.AddDays(42))
            return new("Wniebowstąpienie Pańskie", "wielkanoc", Ranga.Uroczystosc, true);
        if (date == pentecost.AddDays(1))                   // poniedziałek po Zesłaniu
            return new("NMP Matki Kościoła", "zwykly", Ranga.Swieto, false);
        if (date == pentecost.AddDays(4))                   // czwartek po Zesłaniu
            return new("Chrystus Najwyższy i Wieczny Kapłan", "zwykly", Ranga.Swieto, false);
        if (date == pentecost.AddDays(7))                   // niedziela po Zesłaniu
            return new("Trójca Przenajświętsza", "zwykly", Ranga.Uroczystosc, true);
        if (date == corpusChristi)
            return new("Boże Ciało", "zwykly", Ranga.Uroczystosc, true);
        if (date == corpusChristi.AddDays(8))               // piątek po Bożym Ciele
            return new("Najświętsze Serce Pana Jezusa", "zwykly", Ranga.Uroczystosc, true);
        if (date == GetAdventStart(year).AddDays(-7))       // ostatnia niedziela roku liturgicznego
            return new("Chrystus Król", "zwykly", Ranga.Uroczystosc, true);
        return null;
    }

    /// <summary>Tytuł wspomnienia ruchomego — jedno źródło dla warstwy sanktoralnej i testów.</summary>
    public const string ImmaculateHeartTitle = "Niepokalane Serce NMP";

    /// <summary>
    /// Wspomnienia RUCHOME wstrzykiwane do warstwy sanktoralnej
    /// (<see cref="DiocesanCalendarService.ForDate(DateOnly, string)"/>) tą samą ścieżką, co
    /// obchody z <c>kalendarz_diecezji.json</c>. Plik jest kluczowany przez MM-DD, więc obchodu
    /// liczonego od Wielkanocy nie da się w nim zapisać — a zaparkowany pod stałą datą wychodziłby
    /// co roku w złym dniu (regresja pilnowana przez <c>MovableCelebrationsTests</c>).
    /// </summary>
    public static IReadOnlyList<Celebration> MovableCelebrations(DateOnly date)
    {
        // Sobota po uroczystości Najświętszego Serca Pana Jezusa
        if (date != ComputeEaster(date.Year).AddDays(69)) return [];
        return
        [
            new Celebration(
                Data: $"{date.Month:D2}-{date.Day:D2}",
                Tytul: ImmaculateHeartTitle,
                Ranga: Ranga.WspObowiazkowe,
                Powszechny: true,
                Diecezje: [])
        ];
    }

    // Meeus-Jones-Butcher
    private static DateOnly ComputeEaster(int year)
    {
        int a = year % 19;
        int b = year / 100, c = year % 100;
        int d = b / 4, e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static DateOnly GetAdventStart(int year)
    {
        // 1. Niedziela Adwentu = niedziela najbliższa 30 listopada, czyli w przedziale [27 XI, 3 XII]
        var nov27 = new DateOnly(year, 11, 27);
        int dow = (int)nov27.DayOfWeek;
        return nov27.AddDays((7 - dow) % 7);
    }

    private static int GetAdventWeek(DateOnly date, DateOnly adventStart)
        => (date.DayNumber - adventStart.DayNumber) / 7 + 1;

    // Nazwa dnia Wielkiego Postu. Tygodnie liczone od 1. Niedzieli WP (Wielkanoc − 42),
    // zgodnie z konwencją „dzień powszedni należy do tygodnia POPRZEDZAJĄCEJ niedzieli"
    // (liczenie od Środy Popielcowej przesuwało numer w środę zamiast w niedzielę).
    // Popielec, dni po Popielcu oraz Wielki Tydzień z Triduum mają nazwy własne.
    private static string GetLentName(DateOnly date, DateOnly easter, string cycle)
    {
        var firstSunday = easter.AddDays(-42);   // 1. Niedziela Wielkiego Postu
        var palmSunday = easter.AddDays(-7);     // Niedziela Palmowa

        if (date < firstSunday)
            return date.DayOfWeek switch
            {
                DayOfWeek.Wednesday => "Środa Popielcowa",
                DayOfWeek.Thursday  => "Czwartek po Popielcu",
                DayOfWeek.Friday    => "Piątek po Popielcu",
                _                   => "Sobota po Popielcu",
            };

        if (date >= palmSunday)
            return date.DayOfWeek switch
            {
                DayOfWeek.Sunday    => "Niedziela Palmowa",
                DayOfWeek.Monday    => "Wielki Poniedziałek",
                DayOfWeek.Tuesday   => "Wielki Wtorek",
                DayOfWeek.Wednesday => "Wielka Środa",
                DayOfWeek.Thursday  => "Wielki Czwartek",
                DayOfWeek.Friday    => "Wielki Piątek",
                _                   => "Wielka Sobota",
            };

        int week = GetLentWeek(date, firstSunday);
        string dayAbbr = GetDayAbbr(date);
        return date.DayOfWeek == DayOfWeek.Sunday ? $"{week} {dayAbbr} {cycle}" : $"{week} {dayAbbr}";
    }

    private static int GetLentWeek(DateOnly date, DateOnly firstSunday)
        => (date.DayNumber - firstSunday.DayNumber) / 7 + 1;

    private static int GetOrdinaryWeek(DateOnly date, DateOnly easter, int year)
    {
        var baptism = GetBaptismOfLord(year);
        var ashWednesday = easter.AddDays(-46);

        if (date > baptism && date < ashWednesday)
            return (date.DayNumber - baptism.DayNumber) / 7 + 1;

        // Po Zesłaniu — numeracja WSTECZ od Adwentu: 34. tydzień = tydzień przed 1. Nd Adwentu
        // (Chrystusa Króla); ewentualna luka w numeracji wypada tuż po Zesłaniu
        var christKing = GetAdventStart(year).AddDays(-7);
        var sundayOfWeek = date.AddDays(date.DayOfWeek == DayOfWeek.Sunday ? 0 : -(int)date.DayOfWeek);
        return 34 - (christKing.DayNumber - sundayOfWeek.DayNumber) / 7;
    }

    private static DateOnly GetBaptismOfLord(int year)
    {
        // Chrzest Pański = niedziela po Trzech Królach (6 sty)
        var epiphany = new DateOnly(year, 1, 6);
        int dow = (int)epiphany.DayOfWeek;
        int daysToSunday = dow == 0 ? 7 : 7 - dow;
        return epiphany.AddDays(daysToSunday);
    }

    private static string GetSundayCycle(int liturgicalYear)
        => (liturgicalYear % 3) switch { 1 => "A", 2 => "B", _ => "C" };

    private static string GetDayAbbr(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Sunday    => "Nie",
        DayOfWeek.Monday    => "Pon",
        DayOfWeek.Tuesday   => "Wto",
        DayOfWeek.Wednesday => "Śro",
        DayOfWeek.Thursday  => "Czw",
        DayOfWeek.Friday    => "Pią",
        DayOfWeek.Saturday  => "Sob",
        _                   => "?"
    };
}
