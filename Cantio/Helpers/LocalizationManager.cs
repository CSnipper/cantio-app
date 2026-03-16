using System.Windows;

namespace Cantio.Helpers;

public static class LocalizationManager
{
    private static readonly string[] SupportedLanguages = ["pl", "en", "es"];

    public static string CurrentLanguage { get; private set; } = "pl";

    public static void SetLanguage(string lang)
    {
        if (!SupportedLanguages.Contains(lang)) lang = "pl";
        CurrentLanguage = lang;

        var uri = new Uri($"pack://application:,,,/Assets/Localization/Strings.{lang}.xaml", UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("Strings.") == true);
        if (existing != null) merged.Remove(existing);
        merged.Add(dict);
    }

    public static string Get(string key)
    {
        if (Application.Current.Resources.Contains(key))
            return Application.Current.Resources[key] as string ?? key;
        return key;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
