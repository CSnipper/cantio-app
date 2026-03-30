namespace Cantio.Models;

public class Verse
{
    public int Id { get; set; }
    public int Position { get; set; }
    public string Type { get; set; } = string.Empty; // v / c / b / p / img
    public string Text { get; set; } = string.Empty;
    public string? ImagePath { get; set; }

    public int SongId { get; set; }
    public Song Song { get; set; } = null!;
}
