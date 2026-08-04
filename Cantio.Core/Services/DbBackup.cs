using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Cantio.Services;

/// <summary>
/// Kopia bazy robiona AUTOMATYCZNIE tuż przed zastosowaniem migracji EF Core.
///
/// Powód: migracja <c>ZmienCategoryIdNaNullableWSong</c> (v1.63) to w SQLite przebudowa tabeli
/// (nowa tabela → przepisanie wierszy → DROP starej). Odpala się u KAŻDEGO użytkownika przy
/// pierwszym starcie po aktualizacji, a zanik zasilania albo pełny dysk w jej trakcie zostawia
/// organistę bez śpiewnika. Kopia kosztuje ~2 MB i sekundę raz na wydanie.
///
/// Podział: wszystkie decyzje (nazwa pliku, co skasować przy rotacji) są czystymi funkcjami —
/// testowalnymi bez dotykania dysku. I/O siedzi w jednej cienkiej otoczce
/// <see cref="CreateBeforeMigration"/>, która NIGDY nie rzuca: nieudana kopia zapasowa nie może
/// zablokować startu aplikacji.
/// </summary>
public static partial class DbBackup
{
    /// <summary>Prefiks nazw kopii AUTOMATYCZNYCH (ręczne mają inne nazwy i rotacja ich nie tyka).</summary>
    public const string Prefix = "cantio.backup-przed-migracja-";

    /// <summary>
    /// Ile kopii migracyjnych zostaje po rotacji.
    ///
    /// Trzy, bo: (1) najważniejsza jest ostatnia — awarię śpiewnika widać przy najbliższej mszy,
    /// nie po roku; (2) trzy sięgają trzech kolejnych wydań wstecz, czyli obejmują sytuację
    /// „zaktualizowałem, coś było nie tak, zaktualizowałem jeszcze raz" bez utraty stanu
    /// sprzed pierwszej aktualizacji; (3) sufit miejsca to ~3× rozmiar bazy (przy 2 MB to 6 MB) —
    /// bez tego katalog rósłby o kopię na każde wydanie już zawsze.
    /// </summary>
    public const int KeepCount = 3;

    // Celowo WĄSKI wzorzec: data (opcjonalnie licznik w obrębie dnia) i koniec.
    // Kopia ręczna „cantio.backup-przed-migracja-nullable-2026-08-02.db" ma po prefiksie słowo,
    // nie datę — NIE pasuje, więc rotacja jej nie skasuje. To jest cała ochrona kopii ręcznych.
    [GeneratedRegex(@"^cantio\.backup-przed-migracja-(\d{4})-(\d{2})-(\d{2})(?:-(\d+))?\.db$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// Czy nazwa to kopia zrobiona przez ten mechanizm (a nie ręczna kopia użytkownika).
    /// Wprost przez <see cref="OrderKey"/>, żeby „rozpoznaję" i „kasuję" opierały się na
    /// DOKŁADNIE tym samym warunku — inaczej data spoza kalendarza („2026-13-40") byłaby
    /// uznana za naszą, ale nie dałaby się uporządkować.
    /// </summary>
    public static bool IsAutomaticBackup(string fileName)
        => fileName is not null && OrderKey(fileName) is not null;

    /// <summary>
    /// Nazwa dla nowej kopii: <c>cantio.backup-przed-migracja-RRRR-MM-DD.db</c>, a gdy taka
    /// z dziś już jest — z licznikiem (<c>…-2.db</c>). Bez licznika druga migracja tego samego
    /// dnia (dwa wydania pod rząd) nadpisałaby kopię sprzed pierwszej.
    /// </summary>
    public static string BuildName(DateTime now, IEnumerable<string>? existingNames = null)
    {
        var taken = new HashSet<string>(
            existingNames ?? [], StringComparer.OrdinalIgnoreCase);
        var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var name = $"{Prefix}{day}.db";
        for (int n = 2; taken.Contains(name); n++)
            name = $"{Prefix}{day}-{n}.db";
        return name;
    }

    /// <summary>Klucz porządkowy kopii: data + licznik w obrębie dnia. Null = nie nasza kopia.</summary>
    private static (DateTime Day, int Index)? OrderKey(string fileName)
    {
        var m = NamePattern().Match(fileName);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact($"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}",
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return null; // np. „2026-13-40" — wzorzec przepuści, kalendarz nie
        var idx = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 1;
        return (day, idx);
    }

    /// <summary>
    /// Które kopie usunąć, żeby zostało <paramref name="keep"/> najnowszych.
    /// Zwraca NAJSTARSZE najpierw; pliki spoza konwencji (kopie ręczne, baza, cokolwiek innego)
    /// nigdy nie trafiają do wyniku.
    /// </summary>
    public static IReadOnlyList<string> SelectObsolete(IEnumerable<string> fileNames, int keep = KeepCount)
    {
        if (keep < 0) keep = 0;
        var ours = fileNames
            .Where(n => n is not null)
            .Select(n => (Name: n, Key: OrderKey(n)))
            .Where(x => x.Key is not null)
            .OrderByDescending(x => x.Key!.Value.Day)
            .ThenByDescending(x => x.Key!.Value.Index)
            .ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ours.Skip(keep).Select(x => x.Name).Reverse().ToList();
    }

    /// <summary>Pliki towarzyszące bazie SQLite. Sama <c>.db</c> bez nich bywa NIEPEŁNA (WAL).</summary>
    private static readonly string[] Sidecars = ["-wal", "-shm"];

    /// <summary>
    /// Robi kopię bazy, jeśli <paramref name="pendingMigrations"/> nie jest puste, i przycina
    /// stare kopie do <see cref="KeepCount"/>. Zwraca ścieżkę kopii albo null (brak migracji
    /// LUB nieudane kopiowanie — jedno i drugie ma pozwolić aplikacji wstać).
    ///
    /// Bez pending migracji kopii NIE MA: inaczej każdy start aplikacji przepisywałby bazę
    /// i katalog puchłby przy zerowym pożytku.
    /// </summary>
    public static string? CreateBeforeMigration(
        string dbPath, IEnumerable<string> pendingMigrations, DateTime? now = null)
    {
        var pending = pendingMigrations as IReadOnlyCollection<string> ?? pendingMigrations.ToList();
        if (pending.Count == 0) return null;

        try
        {
            if (!File.Exists(dbPath)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;

            Checkpoint(dbPath);

            var name = BuildName(now ?? DateTime.Now,
                Directory.EnumerateFiles(dir, "*.db").Select(Path.GetFileName)!);
            var target = Path.Combine(dir, name);

            File.Copy(dbPath, target, overwrite: false);
            // Gdyby checkpoint się nie udał (inne połączenie trzyma WAL), kopiujemy też pliki
            // towarzyszące — bez nich kopia .db może nie zawierać ostatnich transakcji.
            foreach (var s in Sidecars)
                if (File.Exists(dbPath + s))
                    File.Copy(dbPath + s, target + s, overwrite: true);

            AppLog.Write("DbBackup",
                $"Kopia przed migracją ({pending.Count} do zastosowania: {string.Join(", ", pending)}) → {target}");

            Rotate(dir);
            return target;
        }
        catch (Exception ex)
        {
            // Świadomie łykamy: brak miejsca na dysku czy zablokowany plik nie może skończyć się
            // tym, że użytkownik nie uruchomi programu. Ślad zostaje w logu.
            AppLog.Write("DbBackup", $"NIE UDAŁO SIĘ zrobić kopii przed migracją: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>PRAGMA wal_checkpoint(TRUNCATE)</c> — przepisuje zawartość dziennika WAL do pliku bazy.
    /// Robimy to, bo przywracanie w UI (<c>SzablonViewModel.RestoreDatabase</c>) kopiuje JEDEN plik
    /// <c>.db</c>; kopia musi więc być samowystarczalna, inaczej użytkownik odzyskałby bazę bez
    /// ostatnich zmian. Na dziś Cantio nie włącza WAL (SQLite domyślnie ma dziennik rollback,
    /// pliki <c>-wal</c>/<c>-shm</c> nie powstają) — to jest zabezpieczenie na wypadek zmiany
    /// trybu dziennika i na bazy przyniesione z innej maszyny. Nieudany checkpoint niczego nie
    /// przerywa: kopiujemy wtedy komplet plików.
    /// </summary>
    private static void Checkpoint(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            conn.Close();
            SqliteConnection.ClearPool(conn); // pula trzymałaby uchwyt do pliku
        }
        catch (Exception ex)
        {
            AppLog.Write("DbBackup", $"checkpoint WAL nieudany (kopiuję z plikami -wal/-shm): {ex.Message}");
        }
    }

    /// <summary>Kasuje nadmiarowe kopie automatyczne wraz z ich plikami towarzyszącymi.</summary>
    private static void Rotate(string dir)
    {
        try
        {
            var names = Directory.EnumerateFiles(dir, "*.db").Select(Path.GetFileName).OfType<string>();
            foreach (var old in SelectObsolete(names))
            {
                var full = Path.Combine(dir, old);
                File.Delete(full);
                foreach (var s in Sidecars)
                    if (File.Exists(full + s)) File.Delete(full + s);
                AppLog.Write("DbBackup", $"Usunięto starą kopię migracyjną: {old}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("DbBackup", $"rotacja kopii nieudana: {ex.Message}");
        }
    }
}
