using System.Globalization;
using System.IO;
using System.IO.Compression;
using Cantio.Helpers;

namespace Cantio.Services;

/// <summary>
/// Operacje konserwacyjne na plikach danych — JEDNA implementacja dla dwóch wywołujących:
/// przycisku w zakładce USTAWIENIA i komendy z tabletu (<see cref="PilotMaintenance"/>).
///
/// <para><b>Różnica jest wyłącznie w tym, SKĄD bierze się ścieżka docelowa:</b> okno pyta
/// oknem dialogowym, tablet dostaje folder wymiany i nazwę z datą. Sama operacja jest ta sama
/// co do bajtu — dwie niezależne kopie tej samej operacji to układ, który w tym projekcie już
/// raz zgubił dane (dwie listy pól przy zapisie zestawu, v1.6).</para>
///
/// <para>Klasa jest czysto plikowa i nie zna ani WPF, ani protokołu: postęp zgłasza przez
/// callback, błędy przez wyjątki. Decyzję „co zrobić z błędem" podejmuje wywołujący
/// (okno pokaże dialog, protokół wyśle <c>maintenance_progress</c> ze stanem niepowodzenia).</para>
/// </summary>
public static class MaintenanceOps
{
    /// <summary>Nazwa kopii zapasowej z datą i godziną. Sekundy są, bo tablet nie widzi kolizji nazw.</summary>
    public static string BackupFileName(DateTime now) =>
        "cantio_backup_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".db";

    /// <summary>Nazwa archiwum eksportu z datą i godziną.</summary>
    public static string ExportFileName(DateTime now) =>
        "cantio_export_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".zip";

    /// <summary>
    /// Kopia pliku bazy pod wskazaną ścieżkę. Źródło bierzemy z <see cref="AppPaths.DbPath"/>,
    /// więc harness (chodzący po kopii bazy użytkownika) kopiuje SWOJĄ bazę, nie prawdziwą.
    /// </summary>
    public static void BackupDatabase(string destPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(AppPaths.DbPath, destPath, overwrite: true);
    }

    /// <summary>
    /// Archiwum ZIP: baza jako <c>cantio.db</c> + cały katalog <c>images</c>. Dokładnie ten sam
    /// układ wpisów, co eksport z okna — inaczej archiwum z tabletu nie dałoby się zaimportować
    /// przyciskiem w oknie (i odwrotnie).
    /// </summary>
    /// <param name="progress">
    /// Postęp 0–100. Wołany po dołożeniu bazy i po każdym obrazku; <c>null</c> = nikogo to nie
    /// interesuje (przycisk w oknie).
    /// </param>
    public static void ExportZip(string destPath, Action<int>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        if (File.Exists(destPath)) File.Delete(destPath);

        var images = Directory.Exists(AppPaths.ImagesFolder)
            ? Directory.EnumerateFiles(AppPaths.ImagesFolder).ToArray()
            : [];

        using var zip = ZipFile.Open(destPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(AppPaths.DbPath, "cantio.db");
        progress?.Invoke(images.Length == 0 ? 100 : 5);

        for (int i = 0; i < images.Length; i++)
        {
            zip.CreateEntryFromFile(images[i], "images/" + Path.GetFileName(images[i]));
            // 5..100 — baza to pierwsze 5%, reszta rozłożona po obrazkach.
            progress?.Invoke(5 + (int)((i + 1) * 95L / images.Length));
        }
    }

    /// <summary>
    /// Ponowny import wbudowanych psalmów responsoryjnych — ta sama metoda, którą woła przycisk
    /// „Importuj psalmy" w oknie. <c>-1</c> = w bazie nie ma kategorii „Psalmy responsoryjne"
    /// (okno pokazuje wtedy ostrzeżenie, protokół zgłasza niepowodzenie).
    /// </summary>
    public static Task<int> ImportPsalmsAsync(DatabaseService db) => db.ImportPsalmySeedAsync();

    // ─────────────────────────────────────────────────────────────────────
    //  Etap 4B — operacje NIEODWRACALNE i importy pieśni
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Nagłówek pliku bazy SQLite: 15 znaków ASCII + bajt zerowy (pierwsze 16 bajtów pliku).</summary>
    private static readonly byte[] SqliteMagic =
        [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00];

    /// <summary>
    /// Czy <paramref name="path"/> jest naprawdę bazą SQLite (nagłówek <c>SQLite format 3</c>).
    ///
    /// <para><b>Bez tego sprawdzenia zdalne przywrócenie bazy jest bronią.</b> Podmiana pliku
    /// bazy na śmieć (pomyłkowy JPEG, ucięty transfer, plik z innego programu) daje Cantio,
    /// które nie wstaje — a przy mini PC w zakrystii nie ma klawiatury, żeby to odkręcić.
    /// Dlatego sprawdzamy PRZED nadpisaniem działającej bazy parafii, a nie po.</para>
    ///
    /// <para>Sprawdzamy nagłówek, nie całą strukturę: plik z poprawnym nagłówkiem, a uszkodzoną
    /// zawartością i tak przepadnie na migracji, ale wtedy działa już zwykła kopia zapasowa.
    /// Chodzi o odsianie pomyłki, nie o walidację formatu.</para>
    /// </summary>
    public static bool LooksLikeSqliteDatabase(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[16];
            int read = fs.ReadAtLeast(head, 16, throwOnEndOfStream: false);
            return read == 16 && head.SequenceEqual(SqliteMagic);
        }
        catch { return false; }
    }

    /// <summary>
    /// Podmiana pliku bazy. Wywołujący MUSI wcześniej sprawdzić <see cref="LooksLikeSqliteDatabase"/>
    /// i zrobić kopię zapasową — ta metoda tylko kopiuje.
    ///
    /// <para>Pula połączeń SQLite trzyma plik bazy otwarty także po zamknięciu kontekstu; bez
    /// <c>ClearAllPools</c> nadpisanie potrafi paść na blokadzie pliku. Po podmianie i tak
    /// następuje restart, więc czyszczenie puli niczemu nie szkodzi.</para>
    /// </summary>
    public static void RestoreDatabase(string sourcePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(sourcePath, AppPaths.DbPath, overwrite: true);
    }

    /// <summary>Wpis archiwum, który wychodzi poza katalog docelowy — import jest wtedy ODRZUCANY w całości.</summary>
    public sealed class UnsafeZipEntryException(string entryName)
        : IOException("zip_entry_outside_target: " + entryName)
    {
        public string EntryName { get; } = entryName;
    }

    /// <summary>
    /// Ścieżka, pod którą wolno rozpakować wpis archiwum, albo <c>null</c>, gdy wpis wychodzi
    /// POZA katalog docelowy.
    ///
    /// <para><b>To jest łata na „zip slip".</b> Do v1.69 import archiwum liczył
    /// <c>Path.Combine(katalog, entry.FullName)</c> i rozpakowywał wynik bez sprawdzenia —
    /// wpis nazwany <c>..\..\cokolwiek</c> zapisywał plik POZA katalogiem danych aplikacji.
    /// Dopóki archiwum wybierał człowiek w oknie, była to dziura teoretyczna; od etapu 4B
    /// archiwum pochodzi z folderu, do którego pliki wkłada ktokolwiek.</para>
    ///
    /// <para>Porównujemy ZNORMALIZOWANE ścieżki (<c>GetFullPath</c> zjada <c>..</c> i kropki),
    /// z separatorem na końcu katalogu — inaczej <c>C:\Dane\Cantio-obce</c> przeszłoby jako
    /// „zaczyna się od <c>C:\Dane\Cantio</c>". Nazwa jest ODRZUCANA, nie okrajana do samego
    /// pliku: okrojenie zapisuje cudzą zawartość pod nazwą, której nikt nie wybierał.</para>
    /// </summary>
    public static string? SafeExtractPath(string destRoot, string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(entryFullName)) return null;

        var root = Path.GetFullPath(destRoot);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        string full;
        try
        {
            var relative = entryFullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative)) return null;   // `\cokolwiek`, `C:\cokolwiek`
            full = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch { return null; }

        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Rozpakowanie archiwum do katalogu danych aplikacji — ta sama operacja, co przycisk
    /// „Importuj archiwum" w oknie i komenda <c>maintenance_run {op:"import_zip"}</c> z tabletu.
    ///
    /// <para><b>Dwa przebiegi, nie jeden.</b> Najpierw SPRAWDZAMY wszystkie wpisy, dopiero potem
    /// rozpakowujemy. Pojedynczy wpis wychodzący poza katalog wywraca CAŁY import
    /// (<see cref="UnsafeZipEntryException"/>) i nic nie zostaje zapisane — archiwum ze śmieciem
    /// w środku nie może wejść w połowie, bo połowa importu bazy parafii to stan gorszy niż brak
    /// importu.</para>
    /// </summary>
    public static void ImportZip(string sourcePath, Action<int>? progress = null)
    {
        var root = AppPaths.Root;
        Directory.CreateDirectory(root);

        using var zip = ZipFile.OpenRead(sourcePath);

        // Przebieg 1 — kontrola. Katalogi (wpis z pustą nazwą pliku) też sprawdzamy: `..\` jako
        // wpis katalogowy jest tak samo podejrzany, choć sam nic nie zapisuje.
        var planned = new List<(ZipArchiveEntry Entry, string Path)>();
        foreach (var entry in zip.Entries)
        {
            var dest = SafeExtractPath(root, entry.FullName);
            if (dest is null) throw new UnsafeZipEntryException(entry.FullName);
            if (entry.Name.Length == 0) continue;           // sam katalog — nie ma czego zapisywać
            planned.Add((entry, dest));
        }

        // Przebieg 2 — zapis.
        for (int i = 0; i < planned.Count; i++)
        {
            var (entry, dest) = planned[i];
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            progress?.Invoke((int)((i + 1) * 100L / planned.Count));
        }
        if (planned.Count == 0) progress?.Invoke(100);
    }

    /// <summary>
    /// Wyczyszczenie danych — ta sama metoda, którą woła przycisk „Wyczyść bazę" w oknie.
    /// Kopię zapasową robi WYWOŁUJĄCY (zob. <see cref="PilotMaintenance"/>), bo to on wie,
    /// dokąd ma trafić.
    /// </summary>
    public static Task ClearDatabaseAsync(DatabaseService db) => db.ClearAllDataAsync();

    // ─── Import pieśni ───────────────────────────────────────────────────

    /// <summary>Formaty importu pieśni dostępne Z TABLETU (plikowe; import KATALOGU zostaje przy oknie).</summary>
    public enum SongImportFormat { OpenLpSqlite, OpenLpXml, OpenSong, Osz }

    /// <summary>Nazwa formatu z protokołu → format. <c>null</c> = nazwa nieznana (odmowa w acku).</summary>
    public static SongImportFormat? ParseSongFormat(string? name) => name switch
    {
        "openlp_sqlite" => SongImportFormat.OpenLpSqlite,
        "openlp_xml"    => SongImportFormat.OpenLpXml,
        "opensong"      => SongImportFormat.OpenSong,
        "osz"           => SongImportFormat.Osz,
        _               => null
    };

    /// <summary>
    /// Wynik importu w JEDNYM kształcie dla wszystkich formatów. <paramref name="Setlists"/>
    /// dotyczy wyłącznie OSZ (tam jednostką importu jest ZESTAW, nie pieśń).
    /// </summary>
    public readonly record struct SongImportOutcome(int Imported, int Skipped, int Errors, int Setlists);

    /// <summary>
    /// Import pieśni z pliku — jedno wejście dla czterech formatów.
    ///
    /// <para>Importery istnieją od v1.0 i mają własne raportowanie postępu
    /// (<c>ImportAsync(db, options, progress)</c>); tu je tylko przeliczamy na procenty dla
    /// <c>maintenance_progress</c>. Drugi mechanizm postępu byłby dokładnie tym, czego etap 4A
    /// miał NIE powtarzać.</para>
    ///
    /// <para>OSZ idzie osobną ścieżką, bo <see cref="Import.OszImporter"/> importuje ZESTAWY
    /// (i przy okazji brakujące pieśni), a nie bibliotekę — ma inny interfejs i inny wynik.</para>
    /// </summary>
    public static async Task<SongImportOutcome> ImportSongsAsync(
        DatabaseService db, string path, SongImportFormat format,
        Import.ImportOptions options, Action<int>? progress = null)
    {
        var reporter = progress is null ? null : new Progress<Import.ImportProgress>(p =>
        {
            if (p.Total > 0) progress(Math.Clamp((int)(p.Current * 100L / p.Total), 0, 100));
        });

        if (format == SongImportFormat.Osz)
        {
            var osz = await new Import.OszImporter(db).ImportFilesAsync([path], reporter);
            return new SongImportOutcome(0, 0, osz.Errors.Count, osz.ImportedSetlists);
        }

        var importer = Import.ImporterFactory.Create(format switch
        {
            SongImportFormat.OpenLpSqlite => Import.ImportFormat.OpenLPSqlite,
            SongImportFormat.OpenLpXml    => Import.ImportFormat.OpenLPXml,
            _                             => Import.ImportFormat.OpenSongXml
        }, path);

        var result = await importer.ImportAsync(db, options, reporter);
        return new SongImportOutcome(result.Imported, result.Skipped, result.Errors, 0);
    }
}
