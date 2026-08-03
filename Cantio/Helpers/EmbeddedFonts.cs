using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Cantio.Helpers;

public static class EmbeddedFonts
{
    /// <summary>
    /// Jedno źródło prawdy o liście wbudowanych czcionek to rdzeń (`FontCatalog.EmbeddedNames`)
    /// — protokół pilota wysyła ją tabletowi, więc lokalna kopia tutaj rozjechałaby się cicho.
    /// Ta klasa dokłada wyłącznie część WPF: mapowanie nazwy na pack:// URI.
    /// </summary>
    public static IReadOnlyList<string> Names => Cantio.Services.FontCatalog.EmbeddedNames;

    private static readonly Uri _baseUri = new("pack://application:,,,/", UriKind.Absolute);

    public static bool IsEmbedded(string name) => Cantio.Services.FontCatalog.IsEmbedded(name);

    public static FontFamily Resolve(string name)
    {
        if (IsEmbedded(name))
            return new FontFamily(_baseUri, "./Assets/Fonts/#" + name);
        return new FontFamily(name);
    }
}
