namespace Cantio.Services;

/// <summary>
/// Szablony komunikatów mówionych. Rdzeń nie zna WPF ani <c>LocalizationManager</c>, więc teksty
/// wstrzykuje warstwa okienkowa (klucze <c>Acc.*</c> w Strings.pl/en/es.xaml). Wartości domyślne
/// są polskie, żeby harness testowy nie musiał budować słownika i żeby literówka w zasobach
/// nie skończyła się niemym pulpitem.
/// </summary>
public sealed record AnnouncementText
{
    /// <summary>Rama komunikatu o projekcji, {0} = opis slajdu z tytułem pieśni.</summary>
    public string ViewersSee { get; init; } = "Wierni widzą: {0}";
    /// <summary>{0} = numer slajdu (od 1), {1} = liczba slajdów.</summary>
    public string SlideOfCount { get; init; } = "slajd {0} z {1}";
    public string Blanked { get; init; } = "Ekran wygaszony";
    public string Restored { get; init; } = "Ekran przywrócony";
    public string Nothing { get; init; } = "Nic nie jest wyświetlane";
    public string Verse { get; init; } = "zwrotka";
    public string Chorus { get; init; } = "refren";
    public string Bridge { get; init; } = "bis";
    public string Private { get; init; } = "zwrotka prywatna";
    public string Image { get; init; } = "obrazek";
}

/// <summary>
/// Czyste budowanie komunikatów dla czytnika ekranu. Dwa odbiorniki, jedna miara:
/// <see cref="DescribeProjection"/> mówi, CO WIDZĄ WIERNI (kursor projekcji),
/// <see cref="DescribeReading"/> czyta slajd spod kursora czytania (nic nie zmienia na rzutniku).
///
/// Rodzaj slajdu bierzemy z <see cref="SlideKind"/> — jednego już istniejącego miejsca zamiany
/// typu zwrotki na wartość protokołu; pulpit niewidomego nie zgaduje niczego z prefiksu tekstu.
/// </summary>
public static class ProjectionAnnouncement
{
    /// <summary>„slajd 2 z 5, zwrotka 2" — wspólna część obu komunikatów.</summary>
    public static string SlideHeader(
        int slideIndex, int slideCount, string? slideKind, string? slideLabel, AnnouncementText t)
    {
        var head = string.Format(t.SlideOfCount, slideIndex + 1, slideCount);
        var kind = KindName(slideKind, t);
        // Numer dokładamy tylko zwrotkom: „refren 3" myliłoby (etykieta refrenu to „R"),
        // a numer zwrotki jest tym, czego organista szuka najczęściej.
        if (kind.Length > 0 && slideKind == SlideKind.Verse && IsNumber(slideLabel))
            kind = $"{kind} {slideLabel}";
        return kind.Length > 0 ? $"{head}, {kind}" : head;
    }

    /// <summary>
    /// Stan projekcji na żądanie (klawisz stanu) i po każdej zmianie kursora projekcji.
    /// Wygaszenie wygrywa ze wszystkim — gdy ekran jest czarny, treść slajdu jest nieistotna
    /// i mówienie o niej wprowadzałoby w błąd.
    /// </summary>
    public static string DescribeProjection(
        int slideIndex, int slideCount, string? slideKind, string? slideLabel,
        string? songTitle, bool isBlanked, AnnouncementText t)
    {
        if (isBlanked) return t.Blanked;
        if (slideCount <= 0 || slideIndex < 0 || slideIndex >= slideCount)
            return string.Format(t.ViewersSee, t.Nothing);

        var body = SlideHeader(slideIndex, slideCount, slideKind, slideLabel, t);
        if (!string.IsNullOrWhiteSpace(songTitle)) body = $"{body}, {songTitle.Trim()}";
        return string.Format(t.ViewersSee, body);
    }

    /// <summary>
    /// Slajd spod kursora czytania: nagłówek + treść. Świadomie BEZ tytułu pieśni — przy
    /// przewijaniu zwrotka po zwrotce tytuł na każdej pozycji byłby szumem.
    /// </summary>
    public static string DescribeReading(
        int slideIndex, int slideCount, string? slideKind, string? slideLabel, string? text,
        AnnouncementText t)
    {
        if (slideCount <= 0 || slideIndex < 0 || slideIndex >= slideCount) return t.Nothing;
        var head = SlideHeader(slideIndex, slideCount, slideKind, slideLabel, t);
        var body = (text ?? string.Empty).Trim();
        return body.Length == 0 ? head : $"{head}. {body}";
    }

    private static string KindName(string? slideKind, AnnouncementText t) => slideKind switch
    {
        SlideKind.Chorus  => t.Chorus,
        SlideKind.Bridge  => t.Bridge,
        SlideKind.Private => t.Private,
        SlideKind.Image   => t.Image,
        SlideKind.Verse   => t.Verse,
        _ => string.Empty,
    };

    private static bool IsNumber(string? label)
        => !string.IsNullOrWhiteSpace(label) && label.All(char.IsDigit);
}
