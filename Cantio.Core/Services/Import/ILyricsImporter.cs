namespace Cantio.Services.Import;

// Wynik importu

public class ImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
}

// Postęp

public class ImportProgress
{
    public int Total { get; set; }
    public int Current { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

// Opcje

public enum OpenLpCategorySource { SongBooks, Topics }

public class ImportOptions
{
    public bool OverwriteExisting { get; set; } = false;
    public bool ImportCategories { get; set; } = true;
    public OpenLpCategorySource OpenLpCategorySource { get; set; } = OpenLpCategorySource.SongBooks;

    /// <summary>
    /// Id kategorii użytej gdy plik nie zawiera informacji o kategorii.
    /// null = użyj domyślnej nazwy importera ("OpenLP", "OpenSong" itp.)
    /// </summary>
    public int? FallbackCategoryId { get; set; } = null;
}

// Podgląd

public class ImportPreview
{
    public int Categories { get; set; }
    public int Songs { get; set; }
    public string Summary => $"Znaleziono: {Categories} kategorii, {Songs} pieśni";
}

// Format

public enum ImportFormat
{
    OpenLPSqlite,
    OpenLPXml,
    OpenSongFolder,
    OpenSongXml
}

// Fabryka

public static class ImporterFactory
{
    public static ILyricsImporter Create(ImportFormat format, string path)
        => format switch
        {
            ImportFormat.OpenLPSqlite => new OpenLpImporter(path),
            ImportFormat.OpenLPXml => new OpenLpXmlImporter(path),
            ImportFormat.OpenSongFolder => new OpenSongImporter(path),
            ImportFormat.OpenSongXml => new OpenSongImporter(path),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
}

// Interfejs

public interface ILyricsImporter
{
    string FormatName { get; }

    Task<ImportPreview> GetPreviewAsync();

    Task<ImportResult> ImportAsync(
        DatabaseService db,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null);
}
