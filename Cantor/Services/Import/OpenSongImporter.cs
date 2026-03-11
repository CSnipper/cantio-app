using Cantor.Models;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cantor.Services.Import;

public class OpenSongImporter : ILyricsImporter
{
    private readonly string _sourcePath; // plik .xml lub folder

    public OpenSongImporter(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    public string FormatName => "OpenSong";

    // ── Podgląd ───────────────────────────────────────────────────────────

    public async Task<ImportPreview> GetPreviewAsync()
    {
        var files = GetSongFiles();
        var categories = GetCategoryNames(files);

        return await Task.FromResult(new ImportPreview
        {
            Categories = categories.Count,
            Songs      = files.Count
        });
    }

    // ── Import ────────────────────────────────────────────────────────────

    public async Task<ImportResult> ImportAsync(
        DatabaseService db,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();
        var files = GetSongFiles();

        if (files.Count == 0)
        {
            progress?.Report(new ImportProgress { Total = 0, Current = 0, Message = "Nie znaleziono plików pieśni.", IsError = true });
            return result;
        }

        // kategoria = nazwa folderu nadrzędnego (lub "OpenSong" jeśli plik bezpośrednio)
        var existingCategories = await db.GetCategoriesAsync();
        var categoryCache = new Dictionary<string, int>(); // nazwa → id

        async Task<int?> GetOrCreateCategoryAsync(string name)
        {
            if (categoryCache.TryGetValue(name, out int id)) return id;

            var existing = existingCategories.FirstOrDefault(c => c.Name == name);
            if (existing != null)
            {
                categoryCache[name] = existing.Id;
                return existing.Id;
            }

            if (!options.ImportCategories) return null;

            var newCat = new Category
            {
                Number = existingCategories.Count + categoryCache.Count + 1,
                Name   = name
            };
            await db.SaveCategoryAsync(newCat);
            categoryCache[name] = newCat.Id;
            return newCat.Id;
        }

        int total = files.Count;
        int current = 0;

        foreach (var file in files)
        {
            current++;
            var fileName = Path.GetFileNameWithoutExtension(file);

            progress?.Report(new ImportProgress
            {
                Total = total, Current = current,
                Message = $"Pieśń: {fileName}"
            });

            try
            {
                var song = ParseSongFile(file);
                if (song == null)
                {
                    result.Skipped++;
                    progress?.Report(new ImportProgress
                    {
                        Total = total, Current = current,
                        Message = $"Pominięto (błąd parsowania): {fileName}", IsError = true
                    });
                    continue;
                }

                // kategoria = folder nadrzędny
                var categoryName = GetCategoryForFile(file);
                var categoryId = await GetOrCreateCategoryAsync(categoryName);

                if (categoryId == null)
                {
                    result.Skipped++;
                    continue;
                }

                song.CategoryId = categoryId.Value;

                var existing = await db.GetSongByTitleAsync(song.Title, categoryId.Value);
                if (existing != null && !options.OverwriteExisting)
                {
                    result.Skipped++;
                    progress?.Report(new ImportProgress
                    {
                        Total = total, Current = current,
                        Message = $"Pominięto (już istnieje): {song.Title}"
                    });
                    continue;
                }

                if (existing != null)
                {
                    song.Id = existing.Id;
                }

                await db.SaveSongWithVersesAsync(song);
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                progress?.Report(new ImportProgress
                {
                    Total = total, Current = current,
                    Message = $"Błąd ({fileName}): {ex.Message}", IsError = true
                });
            }
        }

        return result;
    }

    // ── Parsowanie pliku OpenSong ─────────────────────────────────────────

    private static Song? ParseSongFile(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null || root.Name != "song") return null;

            var title = root.Element("title")?.Value?.Trim()
                        ?? Path.GetFileNameWithoutExtension(path);

            var numberStr = root.Element("hymn_number")?.Value?.Trim() ?? "0";
            int.TryParse(numberStr, out int number);

            var lyricsRaw = root.Element("lyrics")?.Value ?? string.Empty;
            var presentation = root.Element("presentation")?.Value?.Trim() ?? string.Empty;

            var verses = ParseOpenSongLyrics(lyricsRaw, presentation);

            return new Song
            {
                Title  = title,
                Number = number,
                Verses = verses
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parsuje tekst lyrics z OpenSong.
    /// Format:
    ///   [V1]           — znacznik zwrotki
    ///   [C]            — refren
    ///   [B]            — bridge
    ///    linia tekstu  — zaczyna się od spacji
    ///   .akordy        — zaczyna się od kropki (pomijamy)
    ///   ; komentarz    — pomijamy
    /// </summary>
    private static List<Verse> ParseOpenSongLyrics(string raw, string presentation)
    {
        var blocks = new Dictionary<string, (string type, List<string> lines)>(StringComparer.OrdinalIgnoreCase);

        string? currentKey = null;
        string currentType = "v";

        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd();

            // komentarz — pomiń
            if (trimmed.StartsWith(";")) continue;

            // znacznik bloku [V1], [C], [B], [V2] itp.
            var tagMatch = Regex.Match(trimmed, @"^\[([A-Za-z]+)(\d*)\]");
            if (tagMatch.Success)
            {
                currentKey = tagMatch.Value; // np. [V1], [C]
                var typeChar = tagMatch.Groups[1].Value.ToUpper();
                currentType = typeChar switch
                {
                    "C"  => "c",
                    "B"  => "b",
                    _    => "v"
                };

                if (!blocks.ContainsKey(currentKey))
                    blocks[currentKey] = (currentType, new List<string>());

                continue;
            }

            if (currentKey == null) continue;

            // akordy — pomiń
            if (trimmed.StartsWith(".")) continue;

            // pusta linia — pomiń
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // tekst — zaczyna się od spacji w OpenSong, ale trim i tak
            blocks[currentKey].lines.Add(trimmed.TrimStart());
        }

        // określ kolejność z <presentation> lub domyślnie po kolei
        List<string> order;
        if (!string.IsNullOrWhiteSpace(presentation))
        {
            order = presentation.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => $"[{p}]")
                                .ToList();
        }
        else
        {
            order = blocks.Keys.ToList();
        }

        var verses = new List<Verse>();
        int position = 1;

        foreach (var key in order)
        {
            if (!blocks.TryGetValue(key, out var block)) continue;

            var text = string.Join("\n", block.lines.Where(l => !string.IsNullOrWhiteSpace(l)));
            if (string.IsNullOrWhiteSpace(text)) continue;

            verses.Add(new Verse
            {
                Position = position++,
                Type     = block.type,
                Text     = text
            });
        }

        return verses;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private List<string> GetSongFiles()
    {
        if (File.Exists(_sourcePath))
            return new List<string> { _sourcePath };

        if (Directory.Exists(_sourcePath))
        {
            // pobierz wszystkie pliki — OpenSong nie ma rozszerzenia lub .xml
            return Directory.GetFiles(_sourcePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".svn") && !f.Contains(".git"))
                .Where(f => string.IsNullOrEmpty(Path.GetExtension(f))
                         || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new List<string>();
    }

    private string GetCategoryForFile(string filePath)
    {
        // jeśli plik jest bezpośrednio w wybranym folderze → "OpenSong"
        // jeśli jest w podfolderze → nazwa podfolderu = kategoria
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var baseName = Path.GetFileName(dir);

        if (string.IsNullOrEmpty(baseName)
            || dir.Equals(_sourcePath, StringComparison.OrdinalIgnoreCase))
            return "OpenSong";

        return baseName;
    }

    private HashSet<string> GetCategoryNames(List<string> files)
        => files.Select(GetCategoryForFile).ToHashSet();
}
