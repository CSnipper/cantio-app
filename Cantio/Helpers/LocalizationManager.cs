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

    /// <summary>
    /// Tekst spod klucza; gdy klucza nie ma (albo nie ma jeszcze Application — tak działa harness
    /// testowy) zwraca sam klucz. Wywołujący, dla którego zwrócony klucz byłby szkodliwy
    /// (np. komunikat czytany na głos przez czytnik ekranu), rozpoznaje ten przypadek
    /// po równości z kluczem i podstawia własny tekst zapasowy.
    /// </summary>
    public static string Get(string key)
    {
        var res = Application.Current?.Resources;
        if (res != null && res.Contains(key))
            return res[key] as string ?? key;
        return key;
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
