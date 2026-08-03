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

        bool hasBooks = await TableExistsAsync(conn, "song_books") && await TableExistsAsync(conn, "songs_songbooks");
        bool hasTopics = await TableExistsAsync(conn, "topics") && await TableExistsAsync(conn, "songs_topics");
        int catCount = hasBooks
            ? await CountAsync(conn, "SELECT COUNT(*) FROM song_books")
            : hasTopics ? await CountAsync(conn, "SELECT COUNT(*) FROM topics") : 0;
        return new ImportPreview
        {
            Categories = catCount,
            Songs = await CountAsync(conn, "SELECT COUNT(*) FROM songs")
        };
    }

    private static async Task<int> CountAsync(SqliteConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return result is long l && l > 0;
    }

    public async Task<ImportResult> ImportAsync(
        DatabaseService db,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null)
    {
        var result = new ImportResult();

        await using var conn = new SqliteConnection($"Data Source={_sourcePath};Mode=ReadOnly");
        await conn.OpenAsync();

        bool useTopics = options.OpenLpCategorySource == OpenLpCategorySource.Topics
            && await TableExistsAsync(conn, "topics")
            && await TableExistsAsync(conn, "songs_topics");
        bool hasBooks = !useTopics
            && await TableExistsAsync(conn, "song_books")
            && await TableExistsAsync(conn, "songs_songbooks");

        var existingCategories = await db.GetCategoriesAsync();
        var categoryMap = new Dictionary<int, int>();
        int fallbackCategoryId = 0;

        if (hasBooks || useTopics)
        {
            var openLpCategories = useTopics
                ? await LoadTopicsAsync(conn)
                : await LoadCategoriesAsync(conn);
            int total0 = openLpCategories.Count;
            int current0 = 0;
            foreach (var (openLpId, name) in openLpCategories)
            {
                current0++;
                progress?.Report(new ImportProgress { Total = total0, Current = current0, Message = $"Kategoria: {name}" });
                var existing = existingCategories.FirstOrDefault(c => c.Name == name);
                if (existing != null)
                    categoryMap[openLpId] = existing.Id;
                else if (options.ImportCategories)
                {
                    var newCat = new Category { Number = 0, Name = name };
                    await db.SaveCategoryAsync(newCat);
                    categoryMap[openLpId] = newCat.Id;
                }
            }
        }
        else if (options.ImportCategories)
        {
            const string fallbackName = "OpenLP";
            var existing = existingCategories.FirstOrDefault(c => c.Name == fallbackName);
            if (existing != null)
            {
                fallbackCategoryId = existing.Id;
            }
            else
            {
                var fallbackCat = new Category { Number = 0, Name = fallbackName };
                await db.SaveCategoryAsync(fallbackCat);
                fallbackCategoryId = fallbackCat.Id;
            }
        }

        var songs = await LoadSongsAsync(conn, hasBooks, useTopics);
        int total = songs.Count;
        int current = 0;

        foreach (var (openLpBookId, number, title, lyricsXml) in songs)
        {
            current++;
            progress?.Report(new ImportProgress { Total = total, Current = current, Message = $"Pieśń: {title}" });

            int categoryId;
            if (hasBooks || useTopics)
            {
                if (!categoryMap.TryGetValue(openLpBookId, out categoryId))
                {
                    result.Skipped++;
                    continue;
                }
            }
            else
            {
                categoryId = fallbackCategoryId;
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

    private static async Task<List<(int id, string name)>> LoadCategoriesAsync(SqliteConnection conn)
    {
        var list = new List<(int, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM song_books ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetString(1)));
        return list;
    }

    private static async Task<List<(int id, string name)>> LoadTopicsAsync(SqliteConnection conn)
    {
        var list = new List<(int, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM topics ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetString(1)));
        return list;
    }

    private static async Task<List<(int bookId, int number, string title, string lyrics)>> LoadSongsAsync(
        SqliteConnection conn, bool hasBooks, bool useTopics)
    {
        var list = new List<(int, int, string, string)>();
        await using var cmd = conn.CreateCommand();
        if (hasBooks)
        {
            cmd.CommandText = @"
                SELECT ssb.songbook_id, ssb.entry, s.title, s.lyrics
                FROM songs s
                JOIN songs_songbooks ssb ON ssb.song_id = s.id
                ORDER BY ssb.songbook_id, ssb.entry";
        }
        else if (useTopics)
        {
            // Pierwszy temat alfabetycznie per pieśń
            cmd.CommandText = @"
                SELECT st.topic_id, 0, s.title, s.lyrics
                FROM songs s
                JOIN songs_topics st ON st.song_id = s.id
                JOIN topics t ON t.id = st.topic_id
                WHERE t.name = (
                    SELECT MIN(t2.name)
                    FROM songs_topics st2
                    JOIN topics t2 ON t2.id = st2.topic_id
                    WHERE st2.song_id = s.id
                )
                ORDER BY t.name, s.title";
        }
        else
        {
            cmd.CommandText = "SELECT id, 0, title, lyrics FROM songs ORDER BY title";
        }
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int bookId = reader.GetInt32(0);
            string entryRaw = reader.IsDBNull(1) ? "0" : reader.GetValue(1).ToString() ?? "0";
            int number = int.TryParse(entryRaw, out var n) ? n : 0;
            list.Add((bookId, number, reader.GetString(2), reader.GetString(3)));
        }
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
