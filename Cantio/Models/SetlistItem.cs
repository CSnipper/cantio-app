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

    // Tekst jednorazowy — treść żyje wyłącznie w zestawie, nie zakłada pieśni w bazie
    private string? _customTitle;
    public string? CustomTitle
    {
        get => _customTitle;
        set { _customTitle = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomTitle))); }
    }

    private string? _customText;
    public string? CustomText
    {
        get => _customText;
        set
        {
            _customText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTextItem)));
        }
    }

    public bool IsTextItem => !string.IsNullOrEmpty(CustomText);

    /// <summary>Element „zwykły" (pieśń z bazy) — nie obrazek i nie tekst jednorazowy.</summary>
    public bool IsSongItem => !IsImageItem && !IsTextItem;

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set { _notes = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
