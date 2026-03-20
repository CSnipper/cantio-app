using Cantio.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Cantio.Services;

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
}

public class Slide
{
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public int VerseIndex { get; set; }
    public int PartIndex { get; set; }
}

public static class SlideLayoutService
{
    // Regex do usuwania tagów inline ({tag} i {/tag}) przed pomiarem szerokości
    private static readonly Regex _tagPattern = new(@"\{/?[a-zA-Z0-9]+\}", RegexOptions.Compiled);
    private static string StripTags(string text) => _tagPattern.Replace(text, string.Empty);

    public static List<Slide> BuildSlides(IList<string> verseTexts, SlideLayoutSettings settings)
    {
        var result = new List<Slide>();
        for (int vi = 0; vi < verseTexts.Count; vi++)
        {
            var parts = SplitVerse(verseTexts[vi], settings);
            for (int pi = 0; pi < parts.Count; pi++)
            {
                result.Add(new Slide
                {
                    Text = parts[pi],
                    FontSize = ComputeFitFontSize(parts[pi], settings),
                    VerseIndex = vi,
                    PartIndex = pi
                });
            }
        }
        return result;
    }

    public static List<string> SplitVerse(string text, SlideLayoutSettings settings)
    {
        // 8% bufor bezpieczeństwa — FormattedText może zaniżać wysokość vs TextBlock
        var availableH = (settings.SlideHeight - 2 * settings.MarginV) * 0.92;

        text = text.Trim();
        if (MeasureTextHeight(text, settings) <= availableH)
            return [text];

        var slides = new List<string>();
        SplitRecursive(text, settings, availableH, slides);
        return slides.Count > 0 ? slides : [text];
    }

    /// <summary>
    /// Rekurencyjnie dzieli tekst, zachłannie maksymalizując każdy fragment.
    /// </summary>
    private static void SplitRecursive(string text, SlideLayoutSettings settings, double availableH, List<string> slides)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();

        if (MeasureTextHeight(text, settings) <= availableH)
        {
            slides.Add(text);
            return;
        }

        var (first, rest) = FindBestSplitPoint(text, settings, availableH);
        if (first != null && rest != null)
        {
            slides.Add(first);
            SplitRecursive(rest, settings, availableH, slides);
        }
        else
        {
            slides.Add(text); // nie da się podzielić — pokaż w całości
        }
    }

    /// <summary>
    /// Szuka najpóźniejszego miejsca podziału w tekście przy zachowaniu priorytetu:
    /// enter (\n) > kropka (.) > przecinek (,) > spacja.
    /// Zwraca najdłuższy możliwy prefix mieszczący się na jednym slajdzie.
    /// </summary>
    private static (string? first, string? rest) FindBestSplitPoint(
        string text, SlideLayoutSettings settings, double availableH)
    {
        foreach (char breakChar in new[] { '\n', '.', ',', ' ' })
        {
            // Kropka i przecinek zostają przy pierwszym fragmencie
            bool includeChar = breakChar is '.' or ',';

            // Skanuj od końca — szukamy najpóźniejszego pasującego miejsca
            for (int pos = text.Length - 1; pos > 0; pos--)
            {
                if (text[pos] != breakChar) continue;

                var prefix = includeChar
                    ? text[..(pos + 1)].TrimEnd()
                    : text[..pos].TrimEnd();
                var suffix = text[(pos + 1)..].TrimStart();

                if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(suffix))
                    continue;

                if (MeasureTextHeight(prefix, settings) <= availableH)
                    return (prefix, suffix);
            }
        }

        return (null, null);
    }

    public static double MeasureTextHeight(string text, SlideLayoutSettings settings)
    {
        var availableWidth = settings.SlideWidth - 2 * settings.MarginH;
        var typeface = new Typeface(
            new FontFamily(settings.FontFamily),
            FontStyles.Normal,
            settings.FontBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var ft = new FormattedText(
            StripTags(text),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            settings.FontSize,
            Brushes.White,
            96);

        ft.MaxTextWidth = availableWidth;
        ft.LineHeight = settings.FontSize * settings.LineHeightMultiplier;
        return ft.Height;
    }

    public static Slide BuildSingle(string text, SlideLayoutSettings settings)
        => new() { Text = text, FontSize = ComputeFitFontSize(text, settings) };

    /// <summary>
    /// Oblicza optymalny rozmiar czcionki bazowej dla slajdu.
    /// Używa binarnego przeszukiwania efektywnego rozmiaru (base × multiplier),
    /// mierząc wysokość z zawijaniem (MeasureTextHeight) — dokładnie tak jak renderuje TextBlock.
    /// Dodatkowo ogranicza rozmiar tak, by najszersze nieprzenoszalne słowo mieściło się w szerokości.
    /// Wynik nigdy nie jest mniejszy niż settings.FontSize (minimum z ustawień).
    /// </summary>
    public static double ComputeFitFontSize(string slideText, SlideLayoutSettings settings)
    {
        double availableH = (settings.SlideHeight - 2 * settings.MarginV) * 0.92;
        double availableW = settings.SlideWidth - 2 * settings.MarginH;
        double minFs = settings.FontSize;
        double lo = minFs;
        double hi = availableH / settings.LineHeightMultiplier;
        if (hi < lo) hi = lo;

        if (MeasureTextHeight(slideText, CloneWithFontSize(settings, lo)) > availableH)
            return minFs;

        for (int i = 0; i < 20; i++)
        {
            double mid = (lo + hi) / 2;
            if (MeasureTextHeight(slideText, CloneWithFontSize(settings, mid)) <= availableH)
                lo = mid;
            else
                hi = mid;
        }

        // Constraint szerokości: każda jawna linia tekstu musi mieścić się bez zawijania.
        // Mierzymy naturalną szerokość (bez MaxTextWidth), bo WPF może zawijać wewnątrz
        // linii przy znakach interpunkcyjnych i diakrytycznych (Unicode break opportunities).
        double maxLineW = MeasureMaxLineWidth(slideText, CloneWithFontSize(settings, lo));
        if (maxLineW > availableW && maxLineW > 0)
            lo = lo * availableW / maxLineW;

        return Math.Max(minFs, Math.Round(lo, 1));
    }

    /// <summary>
    /// Mierzy naturalną szerokość najszerszej jawnej linii tekstu (split po \n, bez MaxTextWidth).
    /// Zapobiega zawijaniu WPF wewnątrz linii przy znakach interpunkcyjnych i diakrytycznych.
    /// </summary>
    private static double MeasureMaxLineWidth(string text, SlideLayoutSettings settings)
    {
        var typeface = new Typeface(
            new FontFamily(settings.FontFamily),
            FontStyles.Normal,
            settings.FontBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double maxW = 0;
        foreach (var line in StripTags(text).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var ft = new FormattedText(line.Trim(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, settings.FontSize, Brushes.White, 96);
            if (ft.Width > maxW) maxW = ft.Width;
        }
        return maxW;
    }

    private static SlideLayoutSettings CloneWithFontSize(SlideLayoutSettings s, double fontSize) => new()
    {
        FontFamily = s.FontFamily, FontBold = s.FontBold,
        FontSize = fontSize, LineHeightMultiplier = s.LineHeightMultiplier,
        SlideWidth = s.SlideWidth, SlideHeight = s.SlideHeight,
        MarginH = s.MarginH, MarginV = s.MarginV
    };

}