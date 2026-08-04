namespace Cantio.Services;

/// <summary>
/// Parametry układu slajdu (rozmiar płótna, marginesy, krój/rozmiar czcionki).
/// Czysty typ danych — bez zależności od WPF; pomiar tekstu robi <see cref="SlideLayoutService"/>.
/// </summary>
public class SlideLayoutSettings
{
    public string FontFamily { get; set; } = "Segoe UI";
    public bool FontBold { get; set; } = false;
    public double FontSize { get; set; } = 60;
    public double LineHeightMultiplier { get; set; } = 1.35;
    public double SlideWidth { get; set; } = 1920;
    public double SlideHeight { get; set; } = 1080;
    public double MarginH { get; set; } = 80;
    public double MarginV { get; set; } = 60;
    public bool AutoFit { get; set; } = true;
    public bool ForceSingleSlide { get; set; } = false; // psalm mode: nigdy nie dziel, auto-fit bez minimum
}

/// <summary>
/// Pojedynczy slajd projekcji. Typ PROTOKOŁU — niosą go komunikaty WS
/// (<c>RemoteControlServer.BuildSlideJson</c>, mapowanie typu przez <c>SlideKind</c>),
/// dlatego musi być wolny od zależności okienkowych.
/// </summary>
public class Slide
{
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public int VerseIndex { get; set; }
    public int PartIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public string VerseType { get; set; } = string.Empty; // "v", "c", "b"
    public string? ImagePath { get; set; }
    public bool IsImageSlide => !string.IsNullOrEmpty(ImagePath);
    public string? BackgroundImagePath { get; set; }
    public bool IsChorusSlide => VerseType == "c";
    public bool IsPrivateSlide => VerseType == "p";
    // Gdy tag "tylko podgląd" jest obecny: pełny tekst dla operatora, Text ma go wystrippowany
    public string OperatorText { get; set; } = string.Empty;
    public double OperatorFontSize { get; set; }
    public bool HasPreviewOnlyContent => !string.IsNullOrEmpty(OperatorText);
    // Cała zwrotka jest preview-only (cały tekst w {tag}...{/tag}): projektor trzyma poprzedni slajd
    public bool IsPreviewOnlySlide { get; set; }
}
