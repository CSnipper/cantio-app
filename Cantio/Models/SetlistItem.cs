using System.ComponentModel;

namespace Cantio.Models;

public class SetlistItem : INotifyPropertyChanged
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

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set { _notes = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
