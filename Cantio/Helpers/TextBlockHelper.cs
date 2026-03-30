using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Cantio.Models;

namespace Cantio.Helpers
{
    public static class TextBlockHelper
    {
        // FormattedText Attached Property
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

        // LineHeightMultiplier Attached Property
        // Zamiast bindować TextBlock.LineHeight bezpośrednio, TextBlockHelper
        // ustawia go po każdym Rebuild na: maxRunFontSize * lhm.
        // Dzięki temu LineHeight jest zawsze spójne z faktycznym rozmiarem runów,
        // niezależnie od kolejności aktualizacji bindingów w ViewModel.
        public static readonly DependencyProperty LineHeightMultiplierProperty =
            DependencyProperty.RegisterAttached(
                "LineHeightMultiplier",
                typeof(double),
                typeof(TextBlockHelper),
                new PropertyMetadata(1.35, OnLineHeightMultiplierChanged));

        public static void SetLineHeightMultiplier(TextBlock tb, double value)
            => tb.SetValue(LineHeightMultiplierProperty, value);

        public static double GetLineHeightMultiplier(TextBlock tb)
            => (double)tb.GetValue(LineHeightMultiplierProperty);

        // TagDefinitions Attached Property
        public static readonly DependencyProperty TagDefinitionsProperty =
            DependencyProperty.RegisterAttached(
                "TagDefinitions",
                typeof(IEnumerable<TextFormatTag>),
                typeof(TextBlockHelper),
                new PropertyMetadata(null, OnTagDefinitionsChanged));

        public static void SetTagDefinitions(TextBlock tb, IEnumerable<TextFormatTag> value)
            => tb.SetValue(TagDefinitionsProperty, value);

        public static IEnumerable<TextFormatTag> GetTagDefinitions(TextBlock tb)
            => (IEnumerable<TextFormatTag>)tb.GetValue(TagDefinitionsProperty);

        // Predefiniowane tagi OpenLP
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
            // dodatkowe użyteczne
            { "bold",   r => r.FontWeight = FontWeights.Bold },
            { "i",      r => r.FontStyle = FontStyles.Italic },
            { "super",  r => r.BaselineAlignment = BaselineAlignment.Superscript },
            { "sub",    r => r.BaselineAlignment = BaselineAlignment.Subscript },
        };

        // Tagi definiowane przez użytkownika (rozszerzalne z kodu/ustawień)
        // Użycie: TextBlockHelper.CustomTags["mytag"] = r => r.Foreground = Brushes.Cyan;
        public static readonly Dictionary<string, Action<Run>> CustomTags = new();

        // Helpers do budowania akcji z TextFormatTag
        public static Action<Run> BuildTagAction(TextFormatTag tag)
        {
            return run =>
            {
                if (!string.IsNullOrEmpty(tag.Color))
                {
                    try { run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tag.Color)); }
                    catch { }
                }
                if (tag.Bold) run.FontWeight = FontWeights.Bold;
                if (tag.Italic) run.FontStyle = FontStyles.Italic;
                if (tag.Underline) run.TextDecorations = TextDecorations.Underline;
            };
        }

        public static void SetCustomTagsFromDefinitions(IEnumerable<TextFormatTag> tags)
        {
            CustomTags.Clear();
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag.Name)) continue;
                CustomTags[tag.Name.ToLower()] = BuildTagAction(tag);
            }
        }

        // Zmiana wartości
        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            Rebuild(tb);
        }

        private static void OnLineHeightMultiplierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            Rebuild(tb);
        }

        private static void OnTagDefinitionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            Rebuild(tb);
        }

        private static void Rebuild(TextBlock tb)
        {
            tb.Inlines.Clear();
            var text = GetFormattedText(tb);
            if (string.IsNullOrEmpty(text)) return;

            var allTags = new Dictionary<string, Action<Run>>(BuiltInTags);
            foreach (var kv in CustomTags)
                allTags[kv.Key] = kv.Value;

            var tagDefs = GetTagDefinitions(tb);
            if (tagDefs != null)
            {
                foreach (var tag in tagDefs)
                {
                    if (string.IsNullOrEmpty(tag.Name)) continue;
                    allTags[tag.Name.ToLower()] = BuildTagAction(tag);
                }
            }

            ParseInlines(tb, text, allTags);

            // Ustaw LineHeight = maxRunFontSize * lhm.
            // Musi być ≥ naturalnej wysokości runów (MaxHeight strategy WPF),
            // żeby tekst wypełniał ekran zgodnie z obliczeniami SlideLayoutService.
            double lhm = GetLineHeightMultiplier(tb);
            double maxRunFs = tb.FontSize > 0 ? tb.FontSize : 13;
            foreach (var run in tb.Inlines.OfType<Run>())
            {
                if (run.FontSize > maxRunFs)
                    maxRunFs = run.FontSize;
            }
            tb.LineHeight = Math.Max(1, maxRunFs * lhm);
        }

        // Parser
        private static void ParseInlines(TextBlock tb, string text, Dictionary<string, Action<Run>> rules)
        {
            // regex dopasowuje {tag} i {/tag} dla wszystkich znanych tagów
            var tagNames = string.Join("|", rules.Keys
                .Select(k => Regex.Escape(k)));
            var pattern = $@"(\{{/?(?:{tagNames})\}})";

            var tokens = Regex.Split(text, pattern, RegexOptions.IgnoreCase);

            // stos aktywnych formatowań (ostatni otwierający jest na górze)
            var stack = new Stack<string>();
            // czy poprzedni token tekstowy kończył się \n (przed kolejnym tokenem, np. tagiem)
            bool trailingNewline = false;

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

                // zwykły tekst — podziel po \n i wstaw LineBreak między częściami
                // Normalizacja \r\n → \n (WPF TextBox wstawia \r\n, Run renderuje \r jako dodatkowy separator)
                var normalizedToken = token.Replace("\r\n", "\n").Replace("\r", "\n");
                var parts = normalizedToken.Split('\n');
                // dziedzicz \n z końca poprzedniego tokenu (np. tekst kończył się \n, a następny token to {tag})
                bool needBreak = trailingNewline;
                trailingNewline = false;

                foreach (var part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue;

                    if (needBreak)
                        tb.Inlines.Add(new LineBreak());
                    needBreak = true;

                    var run = new Run(part)
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

                // jeśli token kończył się \n (ostatnia część jest pusta), zapamiętaj dla kolejnego tokenu
                if (parts.Length > 1 && string.IsNullOrEmpty(parts[^1]) && tb.Inlines.Count > 0)
                    trailingNewline = true;
            }
        }
    }
}
