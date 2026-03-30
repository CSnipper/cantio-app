namespace Cantio.Models;

public class SetlistItem
{
    public int Id { get; set; }
    public int Position { get; set; }
    public string? Type { get; set; }
    public string? SelectedVerses { get; set; } // null = wszystkie zwrotki

    public int SetlistId { get; set; }
    public Setlist Setlist { get; set; } = null!;

    public int? SongId { get; set; }
    public Song? Song { get; set; }

    public string? ImagePath { get; set; }
    public bool IsImageItem => !string.IsNullOrEmpty(ImagePath);
}
