using System.ComponentModel.DataAnnotations.Schema;

namespace Cantio.Models;

public class Setlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string? SeasonKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
    public int? PinPosition { get; set; } // 1–4, null gdy nieprzypięta

    public ICollection<SetlistItem> Items { get; set; } = [];

    /// <summary>
    /// Podpis obchodu na liście PRZYPIĘTE („wsp. św. Dominika, prezbitera") — liczony PRZY
    /// WYŚWIETLANIU przez <see cref="Cantio.Services.PinnedCelebrations"/> i celowo NIE zapisywany:
    /// zestawy wracają co roku pod tą samą nazwą, więc obchód nie może wsiąknąć w dane.
    /// </summary>
    [NotMapped]
    public string Celebration { get; set; } = "";

    /// <summary>Czy jest co pokazać pod nazwą (binding widoczności w XAML).</summary>
    [NotMapped]
    public bool HasCelebration => Celebration.Length > 0;
}
