using System.Globalization;
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
                    FontSize = settings.FontSize,
                    VerseIndex = vi,
                    PartIndex = pi
                });
            }
        }
        return result;
    }

    public static List<string> SplitVerse(string text, SlideLayoutSettings settings)
    {
        var result = new List<string>();
        var availableH = settings.SlideHeight - 2 * settings.MarginV;

        // Jeśli mieści się w całości — zwróć od razu
        if (MeasureTextHeight(text, settings) <= availableH)
        {
            result.Add(text);
            return result;
        }

        // Podziel na słowa i buduj slajdy słowo po słowie
        var words = text.Split(' ');
        var chunk = new List<string>();

        foreach (var word in words)
        {
            chunk.Add(word);
            var candidate = string.Join(" ", chunk);
            if (MeasureTextHeight(candidate, settings) > availableH && chunk.Count > 1)
            {
                chunk.RemoveAt(chunk.Count - 1);
                result.Add(string.Join(" ", chunk));
                chunk.Clear();
                chunk.Add(word);
            }
        }

        if (chunk.Count > 0)
            result.Add(string.Join(" ", chunk));

        return result.Count > 0 ? result : new List<string> { text };
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
            text,
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
        => new() { Text = text, FontSize = settings.FontSize };
}