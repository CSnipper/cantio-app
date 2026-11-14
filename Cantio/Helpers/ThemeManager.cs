using System.Windows;

namespace Cantio.Helpers;

public static class ThemeManager
{
    private static readonly Dictionary<string, string> Themes = new()
    {
        ["obsidian"] = "Obsidian",
        ["monochrome"] = "Monochrome",
        ["sage"] = "Sage",
    };

    public static void SetTheme(string? id)
    {
        if (id == null || !Themes.TryGetValue(id, out var file)) file = Themes["obsidian"];

        var uri = new Uri($"pack://application:,,,/Assets/Themes/Theme.{file}.xaml", UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme.") == true);
        if (existing != null) merged.Remove(existing);
        merged.Add(dict);
    }
}
