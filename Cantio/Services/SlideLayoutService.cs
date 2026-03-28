using Cantio.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
    public bool AutoFit { get; set; } = true;
    public bool ForceSingleSlide { get; set; } = false; // psalm mode: nigdy nie dziel, auto-fit bez minimum
}

public class Slide
{
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public int VerseIndex { get; set; }
    public int PartIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public string VerseType { get; set; } = string.Empty; // "v", "c", "b"
    public bool IsChorusSlide => VerseType == "c";
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
        if (settings.ForceSingleSlide)
            return [text.Trim()];

        // 10% bufor bezpieczeństwa — off-tree TextBlock może mierzyć niżej niż renderuje (zawijanie, zaokrąglenie pikseli)
        var availableH = (settings.SlideHeight - 2 * settings.MarginV) * 0.90;

        text = text.Trim();
        if (MeasureTextHeight(text, settings) <= availableH)
            return [text];

        var result = new List<string>();
        SplitRecursive(text, settings, availableH, result);
        return result.Count > 0 ? result : [text];
    }

    private static void SplitRecursive(string text, SlideLayoutSettings settings, double availableH, List<string> output)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();

        if (MeasureTextHeight(text, settings) <= availableH)
        {
            output.Add(text);
            return;
        }

        // Krok 1: zbalansowany podział po \n
        // Szukamy minimalnego k takiego, że ceil(lineCount/k) linii mieści się na slajdzie.
        // Daje równomierne fragmenty (np. 7+6 zamiast 11+2).
        var lines = text.Split('\n');
        if (lines.Length > 1)
        {
            for (int k = 2; k <= lines.Length; k++)
            {
                int perSlide = (int)Math.Ceiling((double)lines.Length / k);
                var sample = string.Join("\n", lines.Take(perSlide));
                if (MeasureTextHeight(sample, settings) <= availableH)
                {
                    for (int s = 0; s < k; s++)
                    {
                        int start = s * perSlide;
                        int count = Math.Min(perSlide, lines.Length - start);
                        if (count <= 0) break;
                        var chunk = string.Join("\n",
                            lines.Skip(start).Take(count)
                                 .SkipWhile(string.IsNullOrWhiteSpace)
                                 .Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse());
                        if (!string.IsNullOrWhiteSpace(chunk))
                            SplitRecursive(chunk, settings, availableH, output);
                    }
                    return;
                }
            }
        }

        // Krok 2: podział wewnątrz tekstu po . > , > spacja
        // Używany gdy brak \n lub gdy nawet jedna linia nie mieści się sama.
        foreach (char breakChar in new[] { '.', ',', ';', ':', ' ' })
        {
            bool includeChar = breakChar is '.' or ',' or ';' or ':';
            for (int pos = text.Length - 1; pos > 0; pos--)
            {
                if (text[pos] != breakChar) continue;
                var prefix = includeChar ? text[..(pos + 1)].TrimEnd() : text[..pos].TrimEnd();
                var suffix = text[(pos + 1)..].TrimStart();
                if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(suffix)) continue;
                if (MeasureTextHeight(prefix, settings) <= availableH)
                {
                    output.Add(prefix);
                    SplitRecursive(suffix, settings, availableH, output);
                    return;
                }
            }
        }

        // Fallback: nie da się podzielić — pokaż w całości
        output.Add(text);
    }

    // Statyczny TextBlock wielokrotnego użytku — identyczny engine co widoczny TextBlock
    [System.ThreadStatic] private static TextBlock? _measureTb;
    private static TextBlock MeasureTb => _measureTb ??= new TextBlock();

    public static double MeasureTextHeight(string text, SlideLayoutSettings settings)
    {
        var availableWidth = settings.SlideWidth - 2 * settings.MarginH;
        var tb = MeasureTb;

        tb.FontFamily   = new FontFamily(settings.FontFamily);
        tb.FontSize     = settings.FontSize;
        tb.FontWeight   = settings.FontBold ? FontWeights.Bold : FontWeights.Normal;
        tb.TextWrapping = TextWrapping.Wrap;
        tb.Text         = StripTags(text);

        double lh = settings.FontSize * settings.LineHeightMultiplier;
        tb.LineHeight = lh >= 1 ? lh : double.NaN;

        tb.Measure(new Size(availableWidth, double.PositiveInfinity));
        return tb.DesiredSize.Height;
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
        if (!settings.AutoFit && !settings.ForceSingleSlide)
            return settings.FontSize;

        double availableH = (settings.SlideHeight - 2 * settings.MarginV) * 0.85;
        double availableW = settings.SlideWidth - 2 * settings.MarginH;
        double minFs = settings.ForceSingleSlide ? 1.0 : settings.FontSize;
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

        double availableWidth = settings.SlideWidth - 2 * settings.MarginH;
        double maxW = 0;
        foreach (var line in StripTags(text).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var ft = new FormattedText(line.Trim(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, settings.FontSize, Brushes.White, 1.0);
            // Linie szersze niż dostępna szerokość i tak będą zawijane przez TextBlock —
            // nie constrainujemy ich fontu (to by spowodowało minimalny font dla długich tekstów bez \n).
            if (ft.Width > availableWidth) continue;
            if (ft.Width > maxW) maxW = ft.Width;
        }
        return maxW;
    }

    private static SlideLayoutSettings CloneWithFontSize(SlideLayoutSettings s, double fontSize) => new()
    {
        FontFamily = s.FontFamily, FontBold = s.FontBold,
        FontSize = fontSize, LineHeightMultiplier = s.LineHeightMultiplier,
        SlideWidth = s.SlideWidth, SlideHeight = s.SlideHeight,
        MarginH = s.MarginH, MarginV = s.MarginV,
        AutoFit = s.AutoFit
    };

}