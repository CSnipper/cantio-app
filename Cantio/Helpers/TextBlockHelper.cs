using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Cantio.Helpers
{
    public static class TextBlockHelper
    {
        // ── Attached Property ──────────────────────────────────────────────
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(TextBlockHelper),
                new PropertyMetadata(null, OnFormattedTextChanged));

        public static void SetFormattedText(TextBlock tb, string value)
            => tb.SetValue(FormattedTextProperty, value);

        public static string GetFormattedText(TextBlock tb)
            => (string)tb.GetValue(FormattedTextProperty);

        // ── Predefiniowane tagi OpenLP ─────────────────────────────────────
        // Kolory (webkit-text-fill-color)
        private static readonly Dictionary<string, Action<Run>> BuiltInTags = new()
        {
            // kolory
            { "r",  r => r.Foreground = new SolidColorBrush(Colors.Red) },
            { "b",  r => r.Foreground = new SolidColorBrush(Colors.Black) },
            { "bl", r => r.Foreground = new SolidColorBrush(Colors.DodgerBlue) },
            { "y",  r => r.Foreground = new SolidColorBrush(Colors.Yellow) },
            { "g",  r => r.Foreground = new SolidColorBrush(Colors.LimeGreen) },
            { "pk", r => r.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC0CB")) },
            { "o",  r => r.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA500")) },
            { "pp", r => r.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#800080")) },
            // rozmiar
            { "big", r => r.FontSize = r.FontSize > 0 ? r.FontSize * 2.0 : 26 },
            // dodatkowe użyteczne
            { "bold",   r => r.FontWeight = FontWeights.Bold },
            { "i",      r => r.FontStyle = FontStyles.Italic },
            { "super",  r => r.BaselineAlignment = BaselineAlignment.Superscript },
            { "sub",    r => r.BaselineAlignment = BaselineAlignment.Subscript },
        };

        // ── Tagi definiowane przez użytkownika (rozszerzalne z kodu/ustawień) ──
        // Użycie: TextBlockHelper.CustomTags["mytag"] = r => r.Foreground = Brushes.Cyan;
        public static readonly Dictionary<string, Action<Run>> CustomTags = new();

        // ── Zmiana wartości ────────────────────────────────────────────────
        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;

            tb.Inlines.Clear();

            var text = e.NewValue as string;
            if (string.IsNullOrEmpty(text)) return;

            // połącz wbudowane i użytkownika
            var allTags = new Dictionary<string, Action<Run>>(BuiltInTags);
            foreach (var kv in CustomTags)
                allTags[kv.Key] = kv.Value;

            ParseInlines(tb, text, allTags);
        }

        // ── Parser ─────────────────────────────────────────────────────────
        private static void ParseInlines(TextBlock tb, string text, Dictionary<string, Action<Run>> rules)
        {
            // regex dopasowuje {tag} i {/tag} dla wszystkich znanych tagów
            var tagNames = string.Join("|", rules.Keys
                .Select(k => Regex.Escape(k)));
            var pattern = $@"(\{{/?(?:{tagNames})\}})";

            var tokens = Regex.Split(text, pattern, RegexOptions.IgnoreCase);

            // stos aktywnych formatowań (ostatni otwierający jest na górze)
            var stack = new Stack<string>();

            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token)) continue;

                // tag otwierający {tag}
                var openMatch = Regex.Match(token, @"^\{(\w+)\}$", RegexOptions.IgnoreCase);
                if (openMatch.Success)
                {
                    var tag = openMatch.Groups[1].Value.ToLower();
                    if (rules.ContainsKey(tag))
                        stack.Push(tag);
                    continue;
                }

                // tag zamykający {/tag}
                var closeMatch = Regex.Match(token, @"^\{/(\w+)\}$", RegexOptions.IgnoreCase);
                if (closeMatch.Success)
                {
                    var tag = closeMatch.Groups[1].Value.ToLower();
                    // usuń ostatnie wystąpienie tego tagu ze stosu
                    var temp = new Stack<string>();
                    var removed = false;
                    while (stack.Count > 0)
                    {
                        var t = stack.Pop();
                        if (t == tag && !removed) { removed = true; break; }
                        temp.Push(t);
                    }
                    while (temp.Count > 0) stack.Push(temp.Pop());
                    continue;
                }

                // zwykły tekst — utwórz Run i zastosuj wszystkie aktywne formaty
                var run = new Run(token)
                {
                    FontSize = tb.FontSize > 0 ? tb.FontSize : 13
                };

                // zastosuj formaty od najstarszego do najnowszego
                var activeList = stack.ToArray();
                Array.Reverse(activeList);
                foreach (var tag in activeList)
                {
                    if (rules.TryGetValue(tag, out var apply))
                        apply(run);
                }

                tb.Inlines.Add(run);
            }
        }
    }
}
