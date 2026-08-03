namespace Cantio.Models;

public class Category
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName => $"{Number:D2}. {Name}";

    public ICollection<Song> Songs { get; set; } = [];
}