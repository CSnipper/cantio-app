namespace Cantio.Services;

/// <summary>
/// Dopasowanie tytułu OBCHODU (np. „Św. Dominika, prezbitera") do ISTNIEJĄCEGO zestawu
/// nazwanego po swojemu (np. „Dominik", „Teresa od Jezusa", „Jana Sarkandra").
///
/// PO CO: od „Przypnij tydzień" wspomnienie obowiązkowe dostaje WŁASNY zestaw pod nazwą obchodu.
/// Bez tego kroku organista, który od lat trzyma zestaw „Dominik", dostałby drugi — „Św. Dominika,
/// prezbitera" — i jego praca rozjechałaby się na dwa rekordy.
///
/// REGUŁY (przeniesione z <c>skrypty/scal_mszal_swieci.py</c>, gdzie ta sama miara scalała
/// formularze mszalne; tu są JEDNYM źródłem prawdy dla C#):
/// <list type="number">
/// <item>porównujemy RDZENIE (pierwsze <see cref="Stem"/> znaków) — polska deklinacja psuje
///       porównanie całych słów („Dominika" vs „Dominik", „Teresy" vs „Teresa");</item>
/// <item>tolerancja JEDNEJ litery na końcu rdzenia (końcówka fleksyjna) — zob.
///       <see cref="StemsMatch"/>;</item>
/// <item>słowa funkcyjne („św.", „prezbitera", „dziewicy"…) odsiane — tytuły liturgiczne różnią
///       się nimi swobodnie;</item>
/// <item>KIERUNEK POKRYCIA: tytuł obchodu pokrywa nazwę zestawu, nie odwrotnie i nie „ile
///       procent wspólnych". Bez tego „Jan Paweł II" zlałby się ze „Św. Jana Sarkandra"
///       przez jedno wspólne imię (błąd złapany przy scalaniu mszału);</item>
/// <item>dodatkowo GŁÓWNY rdzeń obchodu (pierwsze znaczące słowo tytułu) musi być w nazwie
///       zestawu — inaczej zestaw „Jezus" przejąłby obchód „Św. Teresy od Jezusa";</item>
/// <item>składanie polskich znaków przez <see cref="DatabaseService.FoldPolish"/> —
///       <c>CompareInfo</c> z <c>IgnoreNonSpace</c> nie wystarcza, bo „ł" nie jest w Unicode
///       znakiem diakrytycznym.</item>
/// </list>
///
/// PRZY NIEJEDNOZNACZNOŚCI NIE ZGADUJEMY — <see cref="Find"/> zwraca <c>null</c> i listę
/// kandydatów; wołający zakłada nowy zestaw i loguje. Cudzy zestaw podstawiony pod obchód
/// wygląda poprawnie i jest dla użytkownika niewykrywalny, więc duplikat jest mniejszym złem.
///
/// Klasa jest CZYSTA (bez bazy, bez WPF).
/// </summary>
public static class CelebrationStems
{
    /// <summary>Ile pierwszych znaków słowa tworzy rdzeń.</summary>
    public const int Stem = 6;

    /// <summary>
    /// Słowa funkcyjne tytułów liturgicznych (po złożeniu polskich znaków, małymi literami).
    /// Lista pochodzi ze skryptu scalającego mszał; człony maryjne są tu z tego samego powodu
    /// co tam: „NMP Tuchowskiej" i „NMP Kodeńskiej" to RÓŻNE obchody, a wspólny człon zrównałby je.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "sw", "swietego", "swietej", "swietych", "bl", "blogoslawionego", "blogoslawionej",
        "blogoslawionych", "biskup", "biskupa", "biskupow", "prezbiter", "prezbitera",
        "prezbiterow", "dziewica", "dziewicy", "dziewic", "meczennik", "meczennika",
        "meczennicy", "meczennikow", "meczennica", "meczennicy", "zakonnik", "zakonnika",
        "zakonnicy", "zakonnica", "opat", "opata", "papiez", "papieza", "doktor", "doktora",
        "kosciol", "kosciola", "wspomnienie", "dowolne", "obowiazkowe", "swieto", "uroczystosc",
        "diakon", "diakona", "wdowa", "wdowy", "krol", "krola", "krolowa", "krolowej",
        "apostol", "apostola", "apostolow", "ewangelista", "ewangelisty", "pustelnik",
        "pustelnika", "misjonarz", "misjonarza", "zakonodawca", "zakonodawcy", "prorok",
        "proroka", "matka", "matki", "towarzysze", "towarzyszy", "towarzyszow",
        "oraz", "ze", "we", "na", "od", "jego", "jej", "ich", "pierwszych", "patrona",
        "patronki", "glownego", "glownej", "rodziny", "zakonu",
        // człony maryjne — różnicuje wyłącznie przydomek
        "najswietszej", "najswietszy", "najswietsza", "najswietszego", "najsw", "nmp",
        "maryi", "maryja", "maryjnej", "panny", "panna", "bozej", "boza", "bozego",
    };

    /// <summary>
    /// Rdzenie znaczących słów tytułu, w kolejności występowania, bez powtórzeń.
    /// Fallback do rdzeni WSZYSTKICH słów, gdy po odsianiu nie zostaje nic — pusty zbiór
    /// dopasowałby się do wszystkiego.
    /// </summary>
    public static List<string> Stems(string? title)
    {
        var all = new List<string>();
        var significant = new List<string>();
        foreach (var w in Words(title))
        {
            var s = w.Length <= Stem ? w : w[..Stem];
            if (!all.Contains(s)) all.Add(s);
            if (!Stopwords.Contains(w) && !significant.Contains(s)) significant.Add(s);
        }
        return significant.Count > 0 ? significant : all;
    }

    /// <summary>Słowa tytułu: polskie znaki złożone, wszystko poza [a-z0-9] jako separator, 1-znakowe odrzucone.</summary>
    private static IEnumerable<string> Words(string? title)
    {
        var folded = DatabaseService.FoldPolish(title);
        var word = new System.Text.StringBuilder();
        foreach (var ch in folded + " ")
        {
            if (char.IsAsciiLetterOrDigit(ch)) { word.Append(ch); continue; }
            if (word.Length > 1) yield return word.ToString();
            word.Clear();
        }
    }

    /// <summary>
    /// Czy dwa rdzenie to ten sam wyraz. Wspólny prefiks musi mieć co najmniej 3 znaki
    /// ORAZ sięgać krótszego rdzenia z tolerancją jednej litery — tyle właśnie zmienia
    /// deklinacja („domini"≡„domini", „teresa"≡„teresy", „regina"≡„reginy", „jan"≡„jana").
    /// <para>Świadomie NIE tolerujemy dwóch liter: „maria"/„marek" mają wspólne trzy i przy
    /// luźniejszym progu zlewałyby się w jeden obchód.</para>
    /// </summary>
    public static bool StemsMatch(string a, string b)
    {
        int common = 0;
        while (common < a.Length && common < b.Length && a[common] == b[common]) common++;
        int shorter = Math.Min(a.Length, b.Length);
        return common >= 3 && common >= shorter - 1;
    }

    /// <summary>
    /// Czy zestaw o nazwie <paramref name="setlistName"/> jest zestawem obchodu
    /// <paramref name="celebrationTitle"/> — pełne pokrycie rdzeni nazwy zestawu przez rdzenie
    /// tytułu obchodu PLUS obecność głównego rdzenia obchodu w nazwie zestawu.
    /// </summary>
    public static bool Covers(string? celebrationTitle, string? setlistName)
    {
        var cel = Stems(celebrationTitle);
        var name = Stems(setlistName);
        if (cel.Count == 0 || name.Count == 0) return false;
        if (!name.All(n => cel.Any(c => StemsMatch(c, n)))) return false;
        return name.Any(n => StemsMatch(cel[0], n));   // główne imię obchodu musi być w nazwie
    }

    /// <summary>Zestaw widziany przez tę klasę — wyłącznie identyfikator i nazwa.</summary>
    public readonly record struct Candidate(int Id, string Name);

    /// <param name="Match">jednoznacznie dopasowany zestaw albo <c>null</c></param>
    /// <param name="Candidates">wszystko, co pasowało (do logu przy niejednoznaczności)</param>
    public readonly record struct Result(Candidate? Match, IReadOnlyList<Candidate> Candidates)
    {
        public bool Ambiguous => Match == null && Candidates.Count > 1;
    }

    /// <summary>
    /// Szuka istniejącego zestawu obchodu. Nazwa DOKŁADNIE równa tytułowi obchodu (pl-PL,
    /// ignoreCase) wygrywa i nie jest niejednoznacznością — inaczej użytkownik mający „Dominik"
    /// obok „Św. Dominika, prezbitera" dostawałby przy każdym przypinaniu trzeci zestaw.
    /// </summary>
    public static Result Find(string celebrationTitle, IEnumerable<Candidate> candidates)
    {
        var list = candidates as IList<Candidate> ?? candidates.ToList();

        var exact = list.Where(c => DatabaseService.NameEquals(c.Name, celebrationTitle)).ToList();
        if (exact.Count > 0) return new Result(exact[0], exact);

        var hits = list.Where(c => Covers(celebrationTitle, c.Name)).ToList();
        return new Result(hits.Count == 1 ? hits[0] : null, hits);
    }
}
