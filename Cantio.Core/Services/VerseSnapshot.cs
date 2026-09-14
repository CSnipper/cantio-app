using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Zwrotka widziana przez edytor pieśni (okno lub tablet) — same pola treści, bez kluczy
/// i bez pozycji. Istnieje po to, żeby przepisywanie pól miało JEDNO miejsce
/// (<see cref="VerseSnapshot"/>), a nie dwie niezależne listy: wczytanie do edytora i zapis.
/// </summary>
public interface IVerseDraft
{
    string Type { get; set; }
    string Text { get; set; }
    string? ImagePath { get; set; }
    string? BackgroundImagePath { get; set; }
    string? Lekcjonarz { get; set; }
}

/// <summary>
/// Jedyne miejsce przepisywania pól <see cref="Verse"/> między bazą a edytorem pieśni.
///
/// POWÓD ISTNIENIA: do v1.70 edytor główny miał dwie niezależne listy pól — wczytanie
/// (<c>EditSongAsync</c>) i zapis (<c>SaveEditedSongAsync</c> budujący encję OD NOWA).
/// Pola, których nie było na OBU listach (<see cref="Verse.BackgroundImagePath"/>,
/// <see cref="Verse.Lekcjonarz"/>), ginęły po cichu przy każdym zapisie z edytora głównego.
/// Ten sam układ zgubił notatki pozycji zestawu w v1.6 — tam lekarstwem był
/// <c>SetlistSnapshot.ForSave</c> i strażnik refleksyjny w harnessie. Tu jest tak samo:
/// KAŻDA nowa kolumna <see cref="Verse"/> (poza kluczem, nawigacją i <see cref="Verse.Position"/>)
/// musi trafić do <see cref="IVerseDraft"/> i do obu metod niżej — inaczej harness się zapali.
/// </summary>
public static class VerseSnapshot
{
    /// <summary>Baza → edytor. Pozycja i klucze zostają poza edytorem (o kolejności decyduje lista).</summary>
    public static void ToDraft(Verse source, IVerseDraft draft)
    {
        draft.Type = source.Type;
        draft.Text = source.Text;
        draft.ImagePath = source.ImagePath;
        draft.BackgroundImagePath = source.BackgroundImagePath;
        draft.Lekcjonarz = source.Lekcjonarz;
    }

    /// <summary>Edytor → baza. Pozycję i właściciela nadaje wołający (kolejność w liście edytora).</summary>
    public static Verse ForSave(IVerseDraft draft, int position, int songId) => new()
    {
        Type = draft.Type,
        Text = draft.Text,
        ImagePath = draft.ImagePath,
        BackgroundImagePath = draft.BackgroundImagePath,
        Lekcjonarz = draft.Lekcjonarz,
        Position = position,
        SongId = songId
    };
}
