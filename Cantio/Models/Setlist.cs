namespace Cantio.Models;

public class Setlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Group { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
    public int? PinPosition { get; set; } // 1–4, null gdy nieprzypięta

    public ICollection<SetlistItem> Items { get; set; } = [];
}
