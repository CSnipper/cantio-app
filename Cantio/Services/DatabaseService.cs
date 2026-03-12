using Cantio.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantio.Services;

public class DatabaseService
{
    // ── Kategorie ─────────────────────────────────────────────────────────

    public async Task<List<Category>> GetCategoriesAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Categories.AsNoTracking().OrderBy(c => c.Number).ToListAsync();
    }

    public async Task SaveCategoryAsync(Category category)
    {
        await using var db = new CantioDbContext();
        if (category.Id == 0) db.Categories.Add(category);
        else db.Categories.Update(category);
        await db.SaveChangesAsync();
    }

    // ── Pieśni ────────────────────────────────────────────────────────────

    public async Task<List<Song>> GetSongsByCategoryAsync(int categoryId)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Where(s => s.CategoryId == categoryId)
            .OrderBy(s => s.Number)
            .ToListAsync();
    }

    public async Task<List<Song>> SearchSongsAsync(string query)
    {
        await using var db = new CantioDbContext();
        var q = query.ToLower();
        return await db.Songs.AsNoTracking()
            .Where(s => s.Title.ToLower().Contains(q))
            .OrderBy(s => s.Title)
            .Take(100)
            .ToListAsync();
    }

    public async Task<Song?> GetSongWithVersesAsync(int songId)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Include(s => s.Verses.OrderBy(v => v.Position))
            .FirstOrDefaultAsync(s => s.Id == songId);
    }

    public async Task<Song?> GetSongByNumberAsync(int categoryNumber, int songNumber)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Include(s => s.Verses.OrderBy(v => v.Position))
            .FirstOrDefaultAsync(s => s.Number == songNumber && s.Category.Number == categoryNumber);
    }

    public async Task<Song?> GetSongByTitleAsync(string title, int categoryId)
    {
        await using var db = new CantioDbContext();
        return await db.Songs
            .FirstOrDefaultAsync(s => s.Title == title && s.CategoryId == categoryId);
    }

    public async Task<Song?> GetSongByTitleAnyAsync(string title)
    {
        await using var db = new CantioDbContext();
        return await db.Songs
            .FirstOrDefaultAsync(s => s.Title == title);
    }

    public async Task<List<Song>> GetAllSongsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Include(s => s.Category)
            .OrderBy(s => s.Title)
            .ToListAsync();
    }

    public async Task SaveSongAsync(Song song)
    {
        await using var db = new CantioDbContext();
        if (song.Id == 0)
        {
            db.Songs.Add(song);
        }
        else
        {
            // Usuń stare zwrotki
            var oldVerses = await db.Verses.Where(v => v.SongId == song.Id).ToListAsync();
            db.Verses.RemoveRange(oldVerses);

            // Przypisz SongId do nowych
            foreach (var v in song.Verses)
                v.SongId = song.Id;

            db.Songs.Update(song);
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteSongAsync(int songId)
    {
        await using var db = new CantioDbContext();
        var song = await db.Songs.FindAsync(songId);
        if (song is not null) { db.Songs.Remove(song); await db.SaveChangesAsync(); }
    }

    public async Task SaveSongWithVersesAsync(Song song)
    {
        await using var db = new CantioDbContext();
        if (song.Id == 0)
        {
            db.Songs.Add(song);
        }
        else
        {
            db.Songs.Update(song);
            var old = db.Verses.Where(v => v.SongId == song.Id);
            db.Verses.RemoveRange(old);
            db.Verses.AddRange(song.Verses);
        }
        await db.SaveChangesAsync();
    }

    // ── Zestawy ───────────────────────────────────────────────────────────

    public async Task<List<Setlist>> GetSetlistsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .OrderByDescending(sl => sl.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Setlist>> GetPinnedSetlistsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .Where(sl => sl.IsPinned && sl.PinPosition != null)
            .OrderBy(sl => sl.PinPosition)
            .ToListAsync();
    }

    public async Task<Setlist?> GetSetlistWithItemsAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .Include(sl => sl.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.Song)
                    .ThenInclude(s => s!.Verses.OrderBy(v => v.Position))
            .FirstOrDefaultAsync(sl => sl.Id == setlistId);
    }

    public async Task SaveSetlistAsync(Setlist setlist)
    {
        await using var db = new CantioDbContext();
        if (setlist.Id == 0) db.Setlists.Add(setlist);
        else db.Setlists.Update(setlist);
        await db.SaveChangesAsync();
    }

    public async Task DeleteSetlistAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        var setlist = await db.Setlists.FindAsync(setlistId);
        if (setlist is not null) { db.Setlists.Remove(setlist); await db.SaveChangesAsync(); }
    }

    public async Task<List<Setlist>> GetAllSetlistsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<List<SetlistItem>> GetSetlistItemsAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        return await db.SetlistItems.AsNoTracking()
            .Where(i => i.SetlistId == setlistId)
            .Include(i => i.Song)
            .OrderBy(i => i.Position)
            .ToListAsync();
    }

    public async Task SaveSetlistItemsAsync(int setlistId, List<SetlistItem> items)
    {
        await using var db = new CantioDbContext();
        var old = db.SetlistItems.Where(i => i.SetlistId == setlistId);
        db.SetlistItems.RemoveRange(old);
        foreach (var item in items)
        {
            item.SetlistId = setlistId;
            item.Id = 0;
        }
        db.SetlistItems.AddRange(items);
        await db.SaveChangesAsync();
    }

    // ── Ustawienia ────────────────────────────────────────────────────────

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var db = new CantioDbContext();
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(a => a.Key == key);
        return setting?.Value;
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        await using var db = new CantioDbContext();
        var setting = await db.Settings.FirstOrDefaultAsync(a => a.Key == key);
        if (setting is null) db.Settings.Add(new AppSettings { Key = key, Value = value });
        else setting.Value = value;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ładuje wszystkie ustawienia wyświetlania z bazy jako DisplaySettings DTO.
    /// </summary>
    public DisplaySettings GetSettings()
    {
        using var db = new CantioDbContext();
        var pairs = db.Settings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);

        T Get<T>(string key, T def, Func<string, T> parse)
            => pairs.TryGetValue(key, out var v) ? parse(v) : def;

        return new DisplaySettings
        {
            FontFamily = Get("font_family", "Segoe UI", v => v),
            FontSize = Get("font_size", 60.0, v => double.TryParse(v, out var d) ? d : 60),
            FontBold = Get("font_bold", false, v => v == "true"),
            TextColor = Get("text_color", "#FFFFFF", v => v),
            TextAlign = Get("text_align", "center", v => v),
            LineHeightMultiplier = Get("line_height", 1.35, v => double.TryParse(v, out var d) ? d : 1.35),
            ShadowEnabled = Get("shadow_enabled", true, v => v == "true"),
            ShadowBlur = Get("shadow_blur", 8.0, v => double.TryParse(v, out var d) ? d : 8),
            ShadowDepth = Get("shadow_depth", 2.0, v => double.TryParse(v, out var d) ? d : 2),
            ShadowOpacity = Get("shadow_opacity", 0.8, v => double.TryParse(v, out var d) ? d : 0.8),
            BackgroundColor = Get("bg_color", "#000000", v => v),
            BackgroundImagePath = Get("bg_image", (string?)null, v => string.IsNullOrEmpty(v) ? null : v),
            BackgroundImageOpacity = Get("bg_image_opacity", 1.0, v => double.TryParse(v, out var d) ? d : 1.0),
            TextPosition = Get("text_position", "center", v => v),
            TextMarginH = Get("text_margin_h", 40.0, v => double.TryParse(v, out var d) ? d : 40),
            TextMarginV = Get("text_margin_v", 20.0, v => double.TryParse(v, out var d) ? d : 20),
        };
    }
}