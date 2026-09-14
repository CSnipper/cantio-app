using System.IO;
using Cantio.Helpers;

namespace Cantio.Services;

/// <summary>
/// FOLDER WYMIANY — jedyny katalog dysku komputera, który widzi tablet.
///
/// <para>Po co: mini PC w zakrystii pracuje bez klawiatury i z ukrytym oknem, więc operacje
/// wymagające pliku (kopia zapasowa, eksport, import śpiewnika) nie mają jak wskazać ścieżki.
/// Tablet dysku NIE przegląda i wielkich plików NIE przesyła — Cantio pilnuje jednego
/// katalogu, a pliki wkłada tam człowiek (pendrivem, udziałem sieciowym, czymkolwiek).</para>
///
/// <para><b>Tablet nigdy nie podaje ścieżki.</b> Gdyby podawał, miałby wpływ na to, gdzie
/// komputer zapisuje i skąd czyta pliki — a to jest dokładnie ta władza, której folder wymiany
/// ma NIE dawać. Zdalne operacje dostają nazwę pliku i nic więcej.</para>
/// </summary>
public static class ExchangeFolder
{
    /// <summary>Pełna ścieżka katalogu (bez tworzenia go).</summary>
    public static string Path => AppPaths.ExchangeFolder;

    /// <summary>Tworzy katalog przy pierwszym użyciu i zwraca jego ścieżkę.</summary>
    public static string EnsureCreated()
    {
        Directory.CreateDirectory(Path);
        return Path;
    }

    /// <summary>
    /// Czy <paramref name="name"/> to GOŁA nazwa pliku z folderu wymiany. Ta sama zasada co
    /// <see cref="PilotImages.IsSafeRef"/>, tylko OSTRZEJ: tam ścieżki absolutne przechodzą
    /// (legacy obrazków w bazach parafii), tu nie ma żadnego legacy, więc wszystko poza gołą
    /// nazwą odpada — separator, dwukropek dysku, <c>..</c>, kropki-katalogi.
    /// </summary>
    public static bool IsSafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name is "." or "..") return false;
        // Separatory (oba, także na Linuksie — protokół jest ten sam), dwukropek dysku
        // i znaki, których Windows nie dopuszcza w nazwie pliku.
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':')) return false;
        if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return false;
        // `Path.GetFileName` na gołej nazwie zwraca ją samą; cokolwiek innego = to nie była
        // goła nazwa (ostatni bezpiecznik, gdyby doszła jakaś egzotyka platformy).
        return System.IO.Path.GetFileName(name) == name;
    }

    /// <summary>
    /// JEDYNE miejsce, w którym nazwa z protokołu zamienia się w ścieżkę na dysku.
    /// <c>null</c> = nazwa wychodzi poza folder wymiany i nie wolno jej dotknąć.
    ///
    /// <para>Dwa niezależne warunki, bo normalizacja ścieżek na Windows potrafi zaskoczyć
    /// (końcowe kropki i spacje są ucinane): najpierw kształt nazwy, potem sprawdzenie, że
    /// ZNORMALIZOWANY wynik nadal leży WPROST w katalogu wymiany.</para>
    /// </summary>
    public static string? Resolve(string? name)
    {
        if (!IsSafeName(name)) return null;

        var folder = System.IO.Path.GetFullPath(Path);
        string full;
        try { full = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, name!)); }
        catch { return null; }

        var parent = System.IO.Path.GetDirectoryName(full);
        if (parent is null) return null;
        return string.Equals(
                   parent.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                   folder.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase)
               ? full
               : null;
    }

    /// <summary>Plik widziany przez tablet. <paramref name="Modified"/> w milisekundach epoki UTC (format Pilota).</summary>
    public readonly record struct FileInfoEntry(string Name, long Size, long Modified, string Kind);

    /// <summary>
    /// Zawartość folderu wymiany — WYŁĄCZNIE pliki leżące w nim WPROST. Bez rekurencji
    /// (podkatalog nie jest częścią umowy) i bez pozycji, których <see cref="Resolve"/>
    /// nie potwierdzi — dowiązanie albo junction wskazujące poza katalog nie ma prawa
    /// wypłynąć na łącze.
    ///
    /// <para>Pusty katalog daje PUSTĄ LISTĘ, nie błąd: „nic nie wrzuciłem jeszcze" to
    /// normalny stan, a nie awaria.</para>
    /// </summary>
    public static IReadOnlyList<FileInfoEntry> List()
    {
        var folder = EnsureCreated();
        var result = new List<FileInfoEntry>();

        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var name = System.IO.Path.GetFileName(path);
            var safe = Resolve(name);
            if (safe is null) continue;

            FileInfo info;
            try { info = new FileInfo(safe); if (!info.Exists) continue; }
            catch { continue; }   // plik zniknął albo jest zablokowany — pomiń, nie wywracaj listy

            result.Add(new FileInfoEntry(
                name,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                KindOf(name)));
        }

        // Porządek alfabetyczny — katalog systemowy zwraca pliki w kolejności, której nikt
        // nie obiecuje, a tablet pokazuje listę człowiekowi.
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// Rodzaj pliku po ROZSZERZENIU — tablet nie ma czym zajrzeć do środka, a i tak wybór
    /// operacji należy do człowieka. <c>other</c> = nie wiemy i nie zgadujemy.
    /// </summary>
    public static string KindOf(string name) =>
        System.IO.Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".db"                                   => "db",
            ".zip"                                  => "zip",
            ".osz"                                  => "osz",
            ".sqlite" or ".sqlite3"                 => "sqlite",
            ".xml"                                  => "xml",
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" => "image",
            _                                       => "other"
        };
}
