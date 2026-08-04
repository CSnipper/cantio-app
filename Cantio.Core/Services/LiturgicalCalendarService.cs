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

        // Boże Narodzenie SPRAWDZANE PRZED Adwentem — Adwent trwa do 24 XII włącznie, więc
        // warunek „date >= adventStart" obejmuje też koniec grudnia i przy odwrotnej kolejności
        // zjadał cały okres BN (25–31 XII wychodziło jako „5 Nie A"/„5 Pon" w grupie „adwent”,
        // a piątego tygodnia Adwentu w liturgii nie ma — gałąź BN była martwym kodem).
        if (date >= new DateOnly(year, 12, 25))
            return ChristmasDay(date, year);

        if (date >= adventStart)
            return AdventDay(date, adventStart, GetSundayCycle(year + 1));

        // Okres BN z POPRZEDNIEGO roku kalendarzowego: 1 I – Chrzest Pański (włącznie).
        if (date.Month == 1 && date <= christmasEnd)
            return ChristmasDay(date, year - 1);

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

    /// <summary>
    /// Początek bezpośredniego przygotowania do Narodzenia Pańskiego (17 XII, antyfony „O”).
    /// Od tego dnia liturgię dnia POWSZEDNIEGO Adwentu wyznacza DATA, a nie numer tygodnia.
    /// </summary>
    public const int AdventDatedFromDay = 17;

    private static LiturgicalDay AdventDay(DateOnly date, DateOnly adventStart, string cycle)
    {
        string dayAbbr = GetDayAbbr(date);

        // NIEDZIELE mają klucz tygodniowy ZAWSZE — 3. Niedziela Adwentu wypada 11–17 XII,
        // a 4. aż 18–24 XII, więc obie wchodzą w zakres dat poniżej i nie wolno im go zabrać.
        if (date.DayOfWeek == DayOfWeek.Sunday)
            return new LiturgicalDay($"{GetAdventWeek(date, adventStart)} {dayAbbr} {cycle}", "adwent", cycle);

        // Dni powszednie 17–24 XII: klucz DATOWY, zgodny co do znaku z Pilotem
        // („ADW 17 Grudnia” … „ADW 24 Grudnia - Wigilia” po doklejeniu prefiksu okresu).
        // Klucz tygodniowy był tu gorszy niż brak klucza: „ADW 3 Wto” (17 XII 2024) ISTNIEJE
        // w bazie czytań i opisuje INNY dzień, więc dzień wyglądał poprawnie, a miał cudze
        // czytania; „ADW 4 Pon” nie istnieje w ogóle, bo te dni zawsze wypadają po 16 XII.
        if (date.Month == 12 && date.Day >= AdventDatedFromDay)
            return new LiturgicalDay(AdventDatedName(date.Day), "adwent");

        return new LiturgicalDay($"{GetAdventWeek(date, adventStart)} {dayAbbr}", "adwent");
    }

    /// <summary>Nazwa dnia powszedniego Adwentu 17–24 XII (24 XII jako jedyny z dopiskiem Wigilii).</summary>
    public static string AdventDatedName(int dayOfMonth)
        => dayOfMonth == 24 ? "24 Grudnia - Wigilia" : $"{dayOfMonth} Grudnia";

    /// <summary>Niedziela Świętej Rodziny — pierwsza niedziela po 25 XII; gdy 25 XII wypada
    /// w niedzielę, obchodzi się ją 30 XII (wtedy pierwsza niedziela to samo Boże Narodzenie).</summary>
    private static DateOnly GetHolyFamilySunday(int christmasYear)
    {
        var dec25 = new DateOnly(christmasYear, 12, 25);
        if (dec25.DayOfWeek == DayOfWeek.Sunday) return new DateOnly(christmasYear, 12, 30);
        return dec25.AddDays(7 - (int)dec25.DayOfWeek);
    }

    /// <summary>
    /// Dzień okresu Bożego Narodzenia: 25–31 XII roku <paramref name="christmasYear"/> oraz
    /// 1 I – Chrzest Pański roku następnego.
    /// </summary>
    /// <remarks>
    /// Nazwy odwzorowują klucze, pod którymi te dni żyją w bazie czytań i psalmów — po doklejeniu
    /// prefiksu okresu („BN ”) wychodzą dokładnie klucze, których szuka Pilot. Dlatego są to
    /// nazwy własne, a nie skrót dnia tygodnia: „Śro” w każdą środę okresu byłoby tą samą nazwą
    /// zestawu i nie odpowiadałoby żadnemu wpisowi w danych.
    /// </remarks>
    private static LiturgicalDay ChristmasDay(DateOnly date, int christmasYear)
    {
        // Rok liturgiczny KOŃCZY SIĘ w roku kalendarzowym następującym po 25 XII — ta sama
        // konwencja co w gałęzi Adwentu (GetSundayCycle dostaje rok zakończenia).
        string cycle = GetSundayCycle(christmasYear + 1);

        // Święta Rodzina jest świętem PAŃSKIM, więc na niedzielę wypiera święta świętych
        // (Szczepana 26 XII, Jana 27 XII, Młodzianków 28 XII) — sprawdzana jako pierwsza.
        if (date == GetHolyFamilySunday(christmasYear))
            return new LiturgicalDay("Świętej Rodziny Jezusa, Maryi i Józefa - Święto", "boznarodzenie", cycle);

        // Chrzest Pański zamyka okres (granica z GetOrdinaryWeek: okres zwykły zaczyna się
        // NAZAJUTRZ, `date > baptism`). Czytania zależą od cyklu, stąd litera w Cycle.
        if (date == GetBaptismOfLord(christmasYear + 1))
            return new LiturgicalDay("Chrzest Pański", "boznarodzenie", cycle);

        if (date.Month == 12)
            return new LiturgicalDay(date.Day switch
            {
                25 => "Narodzenie Pańskie",
                26 => "Świętego Szczepana, Pierwszego Męczennika - Święto",
                27 => "Świętego Jana, Apostoła i Ewangelisty - Święto",
                28 => "Świętych Młodzianków, Męczenników - Święto",
                29 => "Piąty Dzień Oktawy Narodzenia Pańskiego",
                30 => "Szósty Dzień Oktawy Narodzenia Pańskiego",
                _  => "Siódmy Dzień Oktawy Narodzenia Pańskiego",
            }, "boznarodzenie");

        if (date.Day == 1) return new LiturgicalDay("Świętej Bożej Rodzicielki Maryi, Uroczystość", "boznarodzenie");
        if (date.Day == 6) return new LiturgicalDay("Objawienie Pańskie, Uroczystość", "boznarodzenie");

        // 2. Niedziela po Narodzeniu Pańskim — jedyna niedziela, która może wypaść 2–5 I
        // (niedziela 7–12 I jest już Chrztem Pańskim, obsłużonym wyżej).
        if (date.DayOfWeek == DayOfWeek.Sunday && date.Day <= 5)
            return new LiturgicalDay($"2 Nie {cycle}", "boznarodzenie", cycle);

        return new LiturgicalDay($"{date.Day} Stycznia", "boznarodzenie");
    }

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
