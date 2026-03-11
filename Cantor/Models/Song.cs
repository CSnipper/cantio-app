using System.Collections.Generic;
namespace Cantor.Models;

public class Song
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public ICollection<Verse> Verses { get; set; } = [];
    public ICollection<SetlistItem> SetlistItems { get; set; } = [];
}