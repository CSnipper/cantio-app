using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Cantio.Helpers;

public static class EmbeddedFonts
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "Afacad",
        "Alatsi",
        "Barlow Semi Condensed",
        "Grenze",
        "Lato",
        "Open Sans",
        "Plus Jakarta Sans",
        "Raleway",
        "Roboto Slab",
        "Signika",
        "Sofia Sans Extra Condensed",
    };

    private static readonly HashSet<string> _set = new(Names, StringComparer.OrdinalIgnoreCase);

    private static readonly Uri _baseUri = new("pack://application:,,,/", UriKind.Absolute);

    public static bool IsEmbedded(string name) => !string.IsNullOrWhiteSpace(name) && _set.Contains(name);

    public static FontFamily Resolve(string name)
    {
        if (IsEmbedded(name))
            return new FontFamily(_baseUri, "./Assets/Fonts/#" + name);
        return new FontFamily(name);
    }
}
