using Cantio.Models;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Xml.Linq;

namespace Cantio.Services.Import;

public class OszImporter
{
    private readonly DatabaseService _db;
    private readonly string? _group;

    public OszImporter(DatabaseService db, string? group = null)
    {
        _db = db;
        _group = group;
    }

    /// <summary>
    /// Importuje jeden lub więcej plików .osz jako zestawy.
    /// Zwraca liczbę zaimportowanych zestawów i ewentualne błędy.
    /// </summary>
    public async Task<OszImportResult> ImportFilesAsync(
        IEnumerable<string> oszPaths,
        IProgress<ImportProgress>? progress = null)
    {
        var result = new OszImportResult();
        var paths = oszPaths.ToList();

        for (int i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var setlistName = Path.GetFileNameWithoutExtension(path);

            progress?.Report(new ImportProgress
            {
                Total = paths.Count,
                Current = i + 1,
                Message = $"Importuję zestaw: {setlistName}"
            });

            try
            {
                await ImportSingleOszAsync(path, setlistName);
                result.ImportedSetlists++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{setlistName}: {ex.Message}");
                progress?.Report(new ImportProgress
                {
                    Total = paths.Count,
                    Current = i + 1,
                    Message = $"Błąd ({setlistName}): {ex.Message}",
                    IsError = true
                });
            }
        }

        return result;
    }

    private async Task ImportSingleOszAsync(string oszPath, string setlistName)
    {
        // Rozpakuj ZIP w pamięci
        using var archive = ZipFile.OpenRead(oszPath);
        var entry = archive.GetEntry("service_data.osj")
            ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".osj"))
            ?? throw new Exception("Brak pliku .osj w archiwum");

        using var stream = entry.Open();
        var json = await new StreamReader(stream).ReadToEndAsync();
        var items = ParseOsj(json);
        // Utwórz zestaw
        var setlist = new Setlist
        {
            Name = setlistName,
            Group = _group,
            CreatedAt = DateTime.Now,
            IsPinned = false
        };
        await _db.SaveSetlistAsync(setlist);

        // Dla każdej pieśni w zestawie
        var setlistItems = new List<SetlistItem>();
        int position = 1;

        foreach (var item in items)
        {
            if (item.Type != "songs") continue; // pomijamy obrazy, prezentacje itp.

            var song = await FindOrImportSongAsync(item);
            if (song == null) continue;

            setlistItems.Add(new SetlistItem
            {
                SetlistId = setlist.Id,
                SongId = song.Id,
                Position = position++,
                Type = "song"
            });
        }
        try
        {
            await _db.SaveSetlistItemsAsync(setlist.Id, setlistItems);
            MessageBox.Show($"Import OK, count={setlistItems.Count}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Błąd SaveSetlistItems: {ex.Message}\n{ex.InnerException?.Message}");
        }

    }

    private async Task<Song?> FindOrImportSongAsync(OsjServiceItem item)
    {
        // Szukaj po tytule w całej bazie
        var existing = await _db.GetSongByTitleAnyAsync(item.Title);
        if (existing != null) return existing;

        // Nie ma — importuj z xml_version
        if (string.IsNullOrEmpty(item.XmlVersion)) return null;

        try
        {
            // Znajdź lub utwórz kategorię na podstawie pola authors
            var categoryName = item.CategoryName;
            var category = await FindOrCreateCategoryAsync(categoryName);

            var song = new Song
            {
                Title = item.Title,
                Author = null,
                CategoryId = category.Id,
                Number = 0
            };

            song.Verses = ParseOpenLyricsXml(item.XmlVersion);
            await _db.SaveSongWithVersesAsync(song);
            return song;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FindOrImportSong error: {ex.Message}");
            return null;
        }
    }

    private async Task<Category> FindOrCreateCategoryAsync(string name)
    {
        var categories = await _db.GetCategoriesAsync();
        var existing = categories.FirstOrDefault(c => c.Name == name);
        if (existing != null) return existing;

        // Spróbuj wyłuskać numer z nazwy (np. "04. Wielkanoc" → 4)
        int number = 0;
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^(\d+)");
        if (match.Success) int.TryParse(match.Groups[1].Value, out number);

        var category = new Category { Name = name, Number = number };
        await _db.SaveCategoryAsync(category);
        return category;
    }

    private static List<Verse> ParseOpenLyricsXml(string xml)
    {
        var verses = new List<Verse>();
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://openlyrics.info/namespace/2009/song";
            int position = 1;

            foreach (var v in doc.Descendants(ns + "verse"))
            {
                // Zbierz tekst ze wszystkich <lines>
                var lines = v.Descendants(ns + "lines")
                    .Select(l => CleanText(l))
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (lines.Count == 0) continue;

                var text = string.Join("\n\n", lines);

                // Określ typ na podstawie atrybutu name (v1, v2, c, c1, b...)
                var nameAttr = v.Attribute("name")?.Value ?? "v1";
                var type = nameAttr.StartsWith("c") ? "c"
                         : nameAttr.StartsWith("b") ? "b"
                         : "v";

                verses.Add(new Verse
                {
                    Position = position++,
                    Type = type,
                    Text = text
                });
            }
        }
        catch
        {
            // Jeśli XML się nie parsuje — wróć pusty
        }
        return verses;
    }

    private static string CleanText(XElement linesElement)
    {
        // Zamień <br/> na \n, usuń tagi OpenLP ({tag}...{/tag})
        var sb = new System.Text.StringBuilder();

        foreach (var node in linesElement.Nodes())
        {
            if (node is XText text)
                sb.Append(text.Value);
            else if (node is XElement el && el.Name.LocalName == "br")
                sb.Append('\n');
            // tagi <tag name="..."> — bierzemy ich zawartość
            else if (node is XElement tagEl)
                sb.Append(CleanText(tagEl));
        }

        // Usuń OpenLP custom tagi {xxx} i {/xxx}
        var result = System.Text.RegularExpressions.Regex.Replace(
            sb.ToString(), @"\{/?[a-z]+\}", "");

        return result.Trim();
    }

    // ── JSON parser ───────────────────────────────────────────────────────

    private static List<OsjServiceItem> ParseOsj(string json)
    {
        var items = new List<OsjServiceItem>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Format: tablica obiektów, pierwszy to metadata, reszta to serviceitem
        foreach (var element in root.EnumerateArray())
        {
            if (!element.TryGetProperty("serviceitem", out var serviceitem)) continue;
            if (!serviceitem.TryGetProperty("header", out var header)) continue;

            var pluginName = header.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "" : "";

            if (pluginName != "songs") continue;

            var title = header.TryGetProperty("title", out var titleProp)
                ? titleProp.GetString() ?? "" : "";

            var xmlVersion = header.TryGetProperty("xml_version", out var xmlProp)
                ? xmlProp.GetString() ?? "" : "";

            // Kategoria z data.authors
            string categoryName = "";
            if (header.TryGetProperty("data", out var data)
                && data.TryGetProperty("authors", out var authorsProp))
                categoryName = authorsProp.GetString() ?? "";

            items.Add(new OsjServiceItem
            {
                Title = title,
                XmlVersion = xmlVersion,
                CategoryName = categoryName,
                Type = "songs"
            });
        }

        return items;
    }
}

public class OsjServiceItem
{
    public string Title { get; set; } = "";
    public string XmlVersion { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string Type { get; set; } = "songs";
}

public class OszImportResult
{
    public int ImportedSetlists { get; set; }
    public List<string> Errors { get; set; } = [];
}