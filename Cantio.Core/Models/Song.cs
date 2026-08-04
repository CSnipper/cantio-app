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
    public ICollection<Verse> Verses { get; set; } = [];
    public ICollection<SetlistItem> SetlistItems { get; set; } = [];
}