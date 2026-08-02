namespace Cantio.Services;

/// <summary>
/// Mapowanie typu zwrotki z bazy (<see cref="Cantio.Models.Verse.Type"/> → <see cref="Slide.VerseType"/>)
/// na wartość wysyłaną w protokole WS do Pilota (pole <c>kind</c> / <c>slideKinds</c>).
///
/// JEDNO miejsce zamiany — Pilot nie może zgadywać typu po prefiksie tekstu.
/// Uwagi:
/// - psalm/aklamacja: bloki „Refren:" i „Aklamacja:" dostają w DisplayViewModel Type = "c",
///   więc wychodzą stąd jako <see cref="Chorus"/> bez żadnej dodatkowej reguły;
/// - tekst jednorazowy z zestawu (SetlistItem.CustomText) jest dzielony na zwrotki Type = "v"
///   → <see cref="Verse"/>;
/// - obrazek rozpoznajemy po ImagePath ALBO po Type = "img" (slajd-obrazek nie ma tekstu).
/// </summary>
public static class SlideKind
{
    public const string Verse   = "verse";
    public const string Chorus  = "chorus";
    public const string Bridge  = "bridge";
    public const string Private = "private";
    public const string Image   = "image";

    /// <summary>
    /// Czysta funkcja: typ zwrotki z bazy → wartość protokołu.
    /// <paramref name="hasImage"/> = slajd niesie obrazek (wygrywa nad typem tekstowym).
    /// Nieznany/pusty typ → <see cref="Verse"/> (bezpieczny domyślny, nigdy null).
    /// </summary>
    public static string FromVerseType(string? verseType, bool hasImage = false)
    {
        if (hasImage) return Image;
        return (verseType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "c"   => Chorus,
            "b"   => Bridge,
            "p"   => Private,
            "img" => Image,
            _     => Verse,
        };
    }

    /// <summary>Wartość protokołu dla konkretnego slajdu (null → <see cref="Verse"/>).</summary>
    public static string FromSlide(Slide? slide)
        => slide is null ? Verse : FromVerseType(slide.VerseType, slide.IsImageSlide);
}
