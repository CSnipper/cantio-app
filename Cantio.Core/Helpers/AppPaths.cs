using System.IO;

namespace Cantio.Helpers;

/// <summary>
/// Katalog danych aplikacji (<c>%LocalAppData%\Cantio</c>) i to, co w nim leży: baza, obrazki
/// i FOLDER WYMIANY. Jedno miejsce, bo od etapu 4 te same ścieżki liczy okno Cantio
/// (przyciski kopii/eksportu) i protokół pilota (operacje z tabletu) — dwie niezależne kopie
/// tej samej arytmetyki to układ, który w tym projekcie gubił już dane.
///
/// <para><b>Ścieżka bazy honoruje <see cref="Cantio.Services.CantioDbContext.DbPathOverride"/></b>
/// — inaczej harness protokołu, chodzący po KOPII bazy użytkownika, robiłby kopię zapasową
/// bazy PRAWDZIWEJ.</para>
/// </summary>
public static class AppPaths
{
    /// <summary>Nazwa folderu wymiany (po polsku — zagląda do niego człowiek, nie program).</summary>
    public const string ExchangeFolderName = "wymiana";

    /// <summary>
    /// Katalog danych inny niż domyślny. Ustawia WYŁĄCZNIE harness — testy tworzą realne pliki
    /// i nie wolno im zaśmiecić katalogu użytkownika (ani skasować z niego czegokolwiek).
    /// </summary>
    public static string? RootOverride { get; set; }

    /// <summary>Katalog danych aplikacji: <c>%LocalAppData%\Cantio</c>.</summary>
    public static string Root => RootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cantio");

    /// <summary>Plik bazy — z uwzględnieniem podmiany na kopię testową.</summary>
    public static string DbPath =>
        string.IsNullOrEmpty(Services.CantioDbContext.DbPathOverride)
            ? Path.Combine(Root, "cantio.db")
            : Services.CantioDbContext.DbPathOverride!;

    /// <summary>Magazyn obrazków (ten sam, do którego kopiuje <see cref="ImageStorage"/>).</summary>
    public static string ImagesFolder => Path.Combine(Root, "images");

    /// <summary>Folder wymiany z tabletem — <c>%LocalAppData%\Cantio\wymiana</c>.</summary>
    public static string ExchangeFolder => Path.Combine(Root, ExchangeFolderName);
}
