using Cantio.Models;
using System.IO;
using System.Xml.Linq;

namespace Cantio.Services.Import;

/// <summary>
/// Importuje pieśni z plików OpenLyrics XML (eksport z OpenLP: File → Export → Song).
/// Obsługuje pojedynczy plik .xml lub folder z wieloma plikami.
/// </summary>
public class OpenLpXmlImporter : ILyricsImporter
{
    private readonly string _sourcePath;

    public OpenLpXmlImporter(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    public string FormatName => "OpenLP XML";

    public async Task<ImportPreview> GetPreviewAsync()
    {
        var files = GetXmlFiles();
        var songs = files.Select(ParseSong).Where(s => s != null).ToList();
        var categories = songs.Select(s => s!.BookName ?? "—").Distinct().Count();

        return await Task.FromResult(new ImportPreview
        {
            Categories = categories,
            Songs = songs.Count
        });
    }

    public async Task<ImportResult> ImportAsync(
        DatabaseService db,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();
        var files = GetXmlFiles();

        if (files.Count == 0)
        {
            progress?.Report(new ImportProgress { Total = 0, Current = 0, Message = "Nie znaleziono plików XML.", IsError = true });
            return result;
        }

        var existingCategories = await db.GetCategoriesAsync();
        var categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        async Task<int?> GetOrCreateCategoryAsync(string name)
        {
            if (categoryCache.TryGetValue(name, out int id)) return id;
            var existing = existingCategories.FirstOrDefault(c => c.Name == name);
            if (existing != null) { categoryCache[name] = existing.Id; return existing.Id; }
            if (!options.ImportCategories) return null;
            var newCat = new Category { Number = 0, Name = name };
            await db.SaveCategoryAsync(newCat);
            categoryCache[name] = newCat.Id;
            return newCat.Id;
        }

        int total = files.Count;
        int current = 0;

        foreach (var file in files)
        {
            current++;
            var parsed = ParseSong(file);
            if (parsed == null) { result.Skipped++; continue; }

            progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Pieśń: {parsed.Title}" });

            int categoryId;
            if (parsed.BookName == null && options.FallbackCategoryId.HasValue)
            {
                categoryId = options.FallbackCategoryId.Value;
            }
            else
            {
                categoryId = await GetOrCreateCategoryAsync(parsed.BookName ?? "OpenLP") ?? 0;
            }

            var existing = await db.GetSongByTitleAsync(parsed.Title, categoryId);
            if (existing != null && !options.OverwriteExisting)
            {
                result.Skipped++;
                progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Pominięto: {parsed.Title}" });
                continue;
            }

            try
            {
                var song = existing ?? new Song();
                song.Title = parsed.Title;
                song.Number = parsed.Number;
                song.CategoryId = categoryId;
                song.Verses = parsed.Verses;
                await db.SaveSongWithVersesAsync(song);
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Błąd ({parsed.Title}): {ex.Message}", IsError = true });
            }
        }

        return result;
    }

    private List<string> GetXmlFiles()
    {
        if (File.Exists(_sourcePath))
            return new List<string> { _sourcePath };

        if (Directory.Exists(_sourcePath))
            return Directory.GetFiles(_sourcePath, "*.xml", SearchOption.TopDirectoryOnly)
                            .OrderBy(f => f)
                            .ToList();

        return new List<string>();
    }

    // BookName == null oznacza brak tagu <songbook> w pliku — użyj FallbackCategoryId
    private record ParsedSong(string Title, int Number, string? BookName, List<Verse> Verses);

    private static ParsedSong? ParseSong(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null) return null;

            // Obsługa namespace OpenLyrics (opcjonalnie)
            var ns = root.Name.Namespace;

            var title = root.Descendants(ns + "title").FirstOrDefault()?.Value.Trim()
                        ?? Path.GetFileNameWithoutExtension(path);

            var bookEl = root.Descendants(ns + "songbook").FirstOrDefault();
            var bookName = bookEl?.Attribute("name")?.Value.Trim(); // null gdy brak <songbook>
            var entryStr = bookEl?.Attribute("entry")?.Value.Trim() ?? "0";
            int.TryParse(entryStr, out int number);

            var verses = ParseVerses(root, ns);
            if (verses.Count == 0) return null;

            return new ParsedSong(title, number, bookName, verses);
        }
        catch
        {
            return null;
        }
    }

    private static List<Verse> ParseVerses(XElement root, XNamespace ns)
    {
        var verses = new List<Verse>();
        int position = 1;
        foreach (var v in root.Descendants(ns + "verse"))
        {
            var name = v.Attribute("name")?.Value ?? "v1";
            // name: "v1", "v2", "c1", "b1" itp.
            var typeChar = name.TrimEnd('1', '2', '3', '4', '5', '6', '7', '8', '9', '0')
                              .ToLowerInvariant();
            var type = typeChar switch { "c" => "c", "b" => "b", _ => "v" };

            var lines = v.Descendants(ns + "line").Select(l => l.Value.Trim());
            var text = string.Join("\n", lines);
            if (string.IsNullOrWhiteSpace(text)) continue;

            verses.Add(new Verse { Position = position++, Type = type, Text = text });
        }
        return verses;
    }
}
