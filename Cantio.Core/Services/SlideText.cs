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
        return StripSlideBreaks(string.Join("\n", parts));
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

    /// <summary>
    /// Linia pusta pochodząca z „|” (OpenSong). Zero-width space nie jest białym znakiem,
    /// więc przeżywa Trim()/IsNullOrWhiteSpace w układaniu slajdów, a na ekranie jest niewidoczna.
    /// </summary>
    public const string BlankLine = "​";

    // Pojedyncze „|” = „|” niebędące częścią „||”.
    private static readonly Regex _singleBar = new(@"(?<!\|)\|(?!\|)", RegexOptions.Compiled);

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// Zamienia znaczniki OpenSong na tekst do wyświetlenia: „||” na początku/końcu linii (po trim) lub samo
    /// w linii = podział slajdu (null w wyniku), „||” w środku zostaje; „|” w środku linii = łamanie linii,
    /// na końcu znika, samo w linii = pusta linia (<see cref="BlankLine"/>).
    /// </summary>
    private static IEnumerable<string?> Tokens(string text)
    {
        foreach (var raw in Lines(text))
        {
            var t = raw.Trim();
            // Linie bez „|” zostają bajt w bajt (wcięcia, odstępy) — tylko linie z kreską są przepisywane.
            if (!t.Contains('|')) { yield return raw; continue; }
            bool endBreak = false;
            if (t.StartsWith("||")) { yield return null; t = t[2..].Trim(); }
            if (t.EndsWith("||")) { endBreak = true; t = t[..^2].Trim(); }
            if (t.Length > 0 && _singleBar.IsMatch(t))
            {
                var parts = _singleBar.Split(t).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (parts.Count == 0) yield return BlankLine;
                else foreach (var part in parts) yield return part;
            }
            else if (t.Length > 0) yield return t;
            if (endBreak) yield return null;
        }
    }

    /// <summary>
    /// Dzieli tekst zwrotki na segmenty po podziałach „||”; puste segmenty odpadają,
    /// ale segment złożony wyłącznie z „|” daje pusty slajd (""). Zob. <see cref="Tokens"/>.
    /// </summary>
    public static List<string> SplitOnSlideBreaks(string text)
    {
        var result = new List<string>();
        var cur = new List<string>();
        void Flush()
        {
            var seg = string.Join('\n', cur).Trim();
            if (seg.Length > 0) result.Add(seg.Replace(BlankLine, "").Trim().Length == 0 ? "" : seg);
            cur.Clear();
        }
        foreach (var tok in Tokens(text))
        {
            if (tok is null) Flush(); else cur.Add(tok);
        }
        Flush();
        return result;
    }

    /// <summary>Tekst bez podziałów „||” i znaczników „|” (gdy podział jest wyłączony lub tekst jest pokazywany wprost).</summary>
    public static string StripSlideBreaks(string text)
    {
        var s = string.Join('\n', Tokens(text).Where(t => t is not null)).Trim();
        return s.Replace(BlankLine, "").Trim().Length == 0 && s.Contains(BlankLine) ? "" : s; // same „|” = pusty slajd
    }

    /// <summary>Usuwa znaczniki pustych linii — dla odbiorców, którym znak niewidoczny nie jest potrzebny (Pilot, czytnik ekranu).</summary>
    public static string StripBlankMarks(string text) => text.Replace(BlankLine, "");
}
