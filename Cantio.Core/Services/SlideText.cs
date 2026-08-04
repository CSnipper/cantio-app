using System.Text.RegularExpressions;

namespace Cantio.Services;

/// <summary>
/// Operacje tekstowe na treści slajdu (tagi formatowania, bloki „tylko podgląd”).
/// Czyste funkcje — bez WPF, żeby dało się ich użyć poza aplikacją okienkową.
/// <see cref="SlideLayoutService"/> wystawia je dalej pod dotychczasowymi nazwami.
/// </summary>
public static class SlideText
{
    // Regex do usuwania tagów inline ({tag} i {/tag}) przed pomiarem szerokości
    private static readonly Regex _tagPattern = new(@"\{/?[a-zA-Z0-9]+\}", RegexOptions.Compiled);

    public static string StripFormatTags(string text) => _tagPattern.Replace(text, string.Empty);

    /// <summary>
    /// Wyciąga treść ze wszystkich bloków {tagname}...{/tagname} dla tagów preview-only.
    /// Zwraca samą treść (bez markerów), złączoną "\n" gdy bloków jest wiele.
    /// </summary>
    public static string ExtractPreviewOnlyContent(string text, IEnumerable<string> previewOnlyTagNames)
    {
        var parts = new List<string>();
        foreach (var name in previewOnlyTagNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var escaped = Regex.Escape(name);
            foreach (Match m in Regex.Matches(text,
                $@"\{{{escaped}\}}(.*?)\{{/{escaped}\}}",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var content = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add(content);
            }
        }
        return string.Join("\n", parts);
    }

    /// <summary>
    /// Usuwa bloki {tagname}...{/tagname} dla tagów oznaczonych jako "tylko podgląd".
    /// Wynik to tekst przeznaczony na projektor (bez treści tagu).
    /// </summary>
    public static string StripPreviewOnlyTags(string text, IEnumerable<string> previewOnlyTagNames)
    {
        foreach (var name in previewOnlyTagNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var escaped = Regex.Escape(name);
            text = Regex.Replace(text,
                $@"\{{{escaped}\}}.*?\{{/{escaped}\}}",
                string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        // Usuń puste linie powstałe po wystrippowaniu
        text = Regex.Replace(text, @"\n{2,}", "\n");
        return text.Trim();
    }
}
