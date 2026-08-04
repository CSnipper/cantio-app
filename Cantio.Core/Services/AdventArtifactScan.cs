using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Wykrywanie zestawów-artefaktów po błędzie „piątego tygodnia Adwentu”.
///
/// PO CO TO ISTNIEJE: do wersji 1.63 gałąź Adwentu wyprzedzała gałąź Bożego Narodzenia, więc
/// 25–31 XII wychodziło jako „5 Nie A”, „5 Pon”… w okresie <c>adwent</c>. „Przypnij tydzień”
/// zakładało pod takimi nazwami REALNE zestawy w bazach parafii. Piątego tygodnia Adwentu
/// w liturgii nie ma, więc każdy taki zestaw jest dowodliwie artefaktem tamtego błędu —
/// ale to praca organisty, więc nazw NIE ruszamy i nic nie kasujemy: zapisujemy ślad w logu,
/// żeby zgłoszenie „mam dziwne zestawy” dało się rozstrzygnąć jednym plikiem, bez migracji.
/// </summary>
public static class AdventArtifactScan
{
    public const string LogCategory = "Kalendarz";

    /// <summary>Czy zestaw jest artefaktem: okres „adwent” + nazwa zaczynająca się od „5 ”.</summary>
    public static bool IsArtifact(string? seasonKey, string? name)
    {
        if (!string.Equals(seasonKey, "adwent", StringComparison.OrdinalIgnoreCase)) return false;
        var n = (name ?? string.Empty).TrimStart();
        return n.Length >= 2 && n[0] == '5' && char.IsWhiteSpace(n[1]);
    }

    public static IReadOnlyList<string> Find(IEnumerable<Setlist> setlists)
        => setlists.Where(s => IsArtifact(s.SeasonKey, s.Name)).Select(s => s.Name).ToList();

    /// <summary>Treść wpisu do logu albo <c>null</c>, gdy nie ma czego zgłaszać (typowy przypadek).</summary>
    public static string? BuildLogMessage(IReadOnlyList<string> names)
        => names.Count == 0
            ? null
            : $"Zestawy z nieistniejącego 5. tygodnia Adwentu (artefakt sprzed poprawki okresu " +
              $"Bożego Narodzenia): {names.Count} — {string.Join(", ", names)}";

    /// <summary>Skan przy starcie aplikacji. Nigdy nie rzuca — to diagnostyka, nie funkcja.</summary>
    public static async Task ScanAndLogAsync(DatabaseService db)
    {
        try
        {
            var msg = BuildLogMessage(Find(await db.GetAllSetlistsAsync()));
            if (msg != null) AppLog.Write(LogCategory, msg);
        }
        catch (Exception ex)
        {
            AppLog.Write(LogCategory, $"Skan zestawów-artefaktów nie powiódł się: {ex.Message}");
        }
    }
}
