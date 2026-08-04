namespace Cantio.Services;

/// <summary>
/// Katalog krojów pisma — JEDYNE wejście warstwy rdzenia do informacji o czcionkach.
///
/// Nazwy czcionek wbudowanych to zwykłe dane (te same na każdym komputerze), więc żyją tutaj.
/// Czcionek ZAINSTALOWANYCH w systemie rdzeń sam wyliczyć nie potrafi — enumerację dostarcza
/// warstwa okienkowa (<c>Cantio.Helpers.EmbeddedFonts.RegisterSystemFonts</c>, WPF
/// <c>Fonts.SystemFontFamilies</c>). Bez rejestracji (np. serwer na Linuksie) lista jest pusta
/// i liczą się wyłącznie czcionki wbudowane — nigdy nie jest to wyjątek ani null.
/// </summary>
public static class FontCatalog
{
    /// <summary>Kroje dostarczane z aplikacją (Assets/Fonts) — dostępne wszędzie tak samo.</summary>
    public static readonly IReadOnlyList<string> EmbeddedNames =
    [
        "Afacad",
        "Alatsi",
        "Barlow Semi Condensed",
        "Grenze",
        "Lato",
        "Open Sans",
        "Plus Jakarta Sans",
        "Raleway",
        "Roboto Slab",
        "Signika",
        "Sofia Sans Extra Condensed",
    ];

    private static readonly HashSet<string> _embedded =
        new(EmbeddedNames, StringComparer.OrdinalIgnoreCase);

    public static bool IsEmbedded(string name) =>
        !string.IsNullOrWhiteSpace(name) && _embedded.Contains(name);

    private static Func<IEnumerable<string>> _systemFonts = static () => [];
    private static string[]? _systemCache;
    private static HashSet<string>? _systemSet;
    private static readonly object _lock = new();

    /// <summary>
    /// Podstawia źródło czcionek systemowych (woła warstwa okienkowa przy starcie).
    /// Kasuje cache, żeby rejestracja po pierwszym odczycie nie zostawiła pustej listy.
    /// </summary>
    public static void RegisterSystemFonts(Func<IEnumerable<string>> provider)
    {
        lock (_lock)
        {
            _systemFonts = provider ?? (static () => []);
            _systemCache = null;
            _systemSet   = null;
        }
    }

    /// <summary>Posortowane, odduplikowane nazwy czcionek systemowych (pusta lista = brak źródła).</summary>
    public static IReadOnlyList<string> SystemNames => SystemArray;

    public static bool IsSystemFont(string name)
    {
        lock (_lock)
        {
            _systemSet ??= new HashSet<string>(SystemArrayNoLock, StringComparer.OrdinalIgnoreCase);
            return _systemSet.Contains(name);
        }
    }

    private static string[] SystemArray
    {
        get { lock (_lock) return SystemArrayNoLock; }
    }

    private static string[] SystemArrayNoLock
    {
        get
        {
            if (_systemCache is not null) return _systemCache;
            try
            {
                _systemCache = _systemFonts()
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { _systemCache = []; }
            return _systemCache;
        }
    }
}
