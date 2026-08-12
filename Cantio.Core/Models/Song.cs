namespace Cantio.Models;

public class Song
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    /// <summary>
    /// Kategoria pieśni. <c>null</c> = pieśń „bez kategorii" — powstaje przy usunięciu kategorii
    /// z zachowaniem pieśni (FK ma <c>ON DELETE SET NULL</c>). Takie pieśni widać w oknie Cantio
    /// pod wirtualną pozycją „Bez kategorii" na liście KATEGORIE oraz w wyszukiwarce.
    /// W protokole WS null jest wysyłany jako <c>0</c> (stary Pilot ma tam twardy int).
    /// </summary>
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? PlayOrderJson { get; set; }
    public double? FontSizeOverride { get; set; }
    public DateTime? LastUsedAt { get; set; }
    /// <summary>
    /// Znacznik ostatniej zmiany TREŚCI pieśni (UTC) — podstawa wykrywania konfliktów przy
    /// edycji offline z Pilota (<c>song_update {baseUpdatedAt}</c> → <c>song_update_conflict</c>).
    /// Podbija go każda zmiana widoczna dla Pilota (tytuł, numer, autor, kategoria, zwrotki,
    /// kolejność odtwarzania); NIE podbija <see cref="LastUsedAt"/> ani
    /// <see cref="FontSizeOverride"/> — pełna tabela w <c>Cantio/Services/CLAUDE.md</c>.
    /// Inicjalizowany na „teraz", żeby pieśń utworzona jakąkolwiek ścieżką (import, seed psalmów,
    /// edytor) nigdy nie miała wartości zerowej wyglądającej jak „nigdy nie zmieniona".
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Verse> Verses { get; set; } = [];
    public ICollection<SetlistItem> SetlistItems { get; set; } = [];
}