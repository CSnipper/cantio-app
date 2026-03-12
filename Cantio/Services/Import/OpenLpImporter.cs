using Cantio.Models;
using Microsoft.Data.Sqlite;
using System.Xml.Linq;

namespace Cantio.Services.Import;

public class OpenLpImporter : ILyricsImporter
{
    private readonly string _sourcePath;

    public OpenLpImporter(string sourcePath)
    {
        _sourcePath = sourcePath;
    }

    public string FormatName => "OpenLP";

    public async Task<ImportPreview> GetPreviewAsync()
    {
        await using var conn = new SqliteConnection($"Data Source={_sourcePath};Mode=ReadOnly");
        await conn.OpenAsync();

        return new ImportPreview
        {
            Categories = await CountAsync(conn, "SELECT COUNT(*) FROM songs_book"),
            Songs = await CountAsync(conn, "SELECT COUNT(*) FROM songs_song")
        };
    }

    private static async Task<int> CountAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<ImportResult> ImportAsync(
        DatabaseService db,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();

        await using var conn = new SqliteConnection($"Data Source={_sourcePath};Mode=ReadOnly");
        await conn.OpenAsync();

        var openLpCategories = await LoadCategoriesAsync(conn);
        var existingCategories = await db.GetCategoriesAsync();
        var categoryMap = new Dictionary<int, int>();

        int total = openLpCategories.Count;
        int current = 0;

        foreach (var (openLpId, number, name) in openLpCategories)
        {
            current++;
            progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Kategoria: {name}" });

            var existing = existingCategories.FirstOrDefault(c => c.Name == name);
            if (existing != null)
                categoryMap[openLpId] = existing.Id;
            else if (options.ImportCategories)
            {
                var newCat = new Category { Number = number, Name = name };
                await db.SaveCategoryAsync(newCat);
                categoryMap[openLpId] = newCat.Id;
            }
        }

        var songs = await LoadSongsAsync(conn);
        total = songs.Count;
        current = 0;

        foreach (var (openLpBookId, number, title, lyricsXml) in songs)
        {
            current++;
            progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Pieśń: {title}" });

            if (!categoryMap.TryGetValue(openLpBookId, out int categoryId))
            {
                result.Skipped++;
                continue;
            }

            var existing = await db.GetSongByTitleAsync(title, categoryId);
            if (existing != null && !options.OverwriteExisting)
            {
                result.Skipped++;
                progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Pominięto: {title}" });
                continue;
            }

            try
            {
                var song = existing ?? new Song();
                song.Title = title; song.Number = number; song.CategoryId = categoryId;
                song.Verses = ParseVerses(lyricsXml);
                await db.SaveSongWithVersesAsync(song);
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Błąd ({title}): {ex.Message}", IsError = true });
            }
        }

        return result;
    }

    private static async Task<List<(int id, int number, string name)>> LoadCategoriesAsync(SqliteConnection conn)
    {
        var list = new List<(int, int, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, book_number, name FROM songs_book ORDER BY book_number";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        return list;
    }

    private static async Task<List<(int bookId, int number, string title, string lyrics)>> LoadSongsAsync(SqliteConnection conn)
    {
        var list = new List<(int, int, string, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ssb.book_id, ssb.entry, ss.title, ss.lyrics
            FROM songs_song ss
            JOIN songs_song_books ssb ON ssb.song_id = ss.id
            ORDER BY ssb.book_id, ssb.entry";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        return list;
    }

    private static List<Verse> ParseVerses(string lyricsXml)
    {
        var verses = new List<Verse>();
        try
        {
            var doc = XDocument.Parse(lyricsXml);
            int position = 1;
            foreach (var v in doc.Descendants("verse"))
            {
                var typeAttr = v.Attribute("type")?.Value ?? "v";
                var text = string.Join("\n", v.Descendants("line").Select(l => l.Value.Trim()));
                if (string.IsNullOrWhiteSpace(text)) continue;
                verses.Add(new Verse
                {
                    Position = position++,
                    Type = typeAttr switch { "chorus" => "c", "bridge" => "b", _ => "v" },
                    Text = text
                });
            }
        }
        catch
        {
            verses.Add(new Verse { Position = 1, Type = "v", Text = lyricsXml });
        }
        return verses;
    }
}