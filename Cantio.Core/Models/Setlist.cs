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
    /// <summary>Przypięty przez „Przypnij tydzień" — także zestaw użytkownika bez SeasonKey („Dominik" dla obchodu).
    /// Zerowany przy każdym odpięciu; ręczne przypięcie go nie ustawia.</summary>
    public bool PinnedByWeek { get; set; }
    public int? PinPosition { get; set; } // 1–4, null gdy nieprzypięta

    public ICollection<SetlistItem> Items { get; set; } = [];

    /// <summary>
    /// Podpis obchodu na liście PRZYPIĘTE („wsp. św. Dominika, prezbitera") — liczony PRZY
    /// WYŚWIETLANIU przez <see cref="Cantio.Services.PinnedCelebrations"/> i celowo NIE zapisywany:
    /// zestawy wracają co roku pod tą samą nazwą, więc obchód nie może wsiąknąć w dane.
    /// </summary>
    [NotMapped]
    public string Celebration { get; set; } = "";

    /// <summary>JEDYNA definicja „zestaw tygodniowy" (lista TYDZIEŃ, pole `weekly` w WS) — także dla zapytań EF.</summary>
    public static readonly System.Linq.Expressions.Expression<Func<Setlist, bool>> IsWeeklyExpr =
        s => s.IsPinned && (s.SeasonKey != null || s.PinnedByWeek);

    private static readonly Func<Setlist, bool> IsWeeklyFunc = IsWeeklyExpr.Compile();

    [NotMapped]
    public bool IsWeekly => IsWeeklyFunc(this);

    /// <summary>Czy jest co pokazać pod nazwą (binding widoczności w XAML).</summary>
    [NotMapped]
    public bool HasCelebration => Celebration.Length > 0;
}
