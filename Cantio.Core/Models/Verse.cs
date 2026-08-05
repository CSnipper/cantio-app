namespace Cantio.Models;

public class Verse
{
    public int Id { get; set; }
    public int Position { get; set; }
    public string Type { get; set; } = string.Empty; // v / c / b / p / img
    public string Text { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public string? BackgroundImagePath { get; set; }

    /// <summary>
    /// Wydanie lekcjonarza, do którego należy ta zwrotka: <c>"N"</c> (nowy) lub <c>"S"</c> (stary).
    /// <c>null</c> = zwrotka wspólna dla obu wydań (wszystkie zwykłe pieśni; tak też migrują istniejące dane).
    /// Filtr projekcji zostawia zwrotki <c>null</c> oraz zgodne z ustawieniem <c>lectionary</c>.
    /// </summary>
    public string? Lekcjonarz { get; set; }

    public int SongId { get; set; }
    public Song Song { get; set; } = null!;
}
