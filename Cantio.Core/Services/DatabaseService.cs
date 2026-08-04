using Cantio.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cantio.Services;

public class DatabaseService
{
    // Kategorie

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

    public async Task DeleteCategoryAsync(int id)
    {
        await using var db = new CantioDbContext();
        var cat = await db.Categories.FindAsync(id);
        if (cat != null) { db.Categories.Remove(cat); await db.SaveChangesAsync(); }
        await ClearPsalmCategoryIfDeletedAsync(id);
    }

    /// <summary>
    /// Ustawienie <c>psalm_category_id</c> wskazujące na SKASOWANĄ kategorię cicho gasi tryb
    /// psalm: <c>LoadVersesAsync</c> porównuje je z kategorią pieśni i nigdy już nie trafia.
    /// Wołane z KAŻDEJ ścieżki kasowania kategorii (okno i protokół WS mają wspólne dno w
    /// <c>DatabaseService</c>), więc nie da się dodać czwartej, która o tym zapomni.
    /// </summary>
    private async Task ClearPsalmCategoryIfDeletedAsync(int deletedCategoryId)
    {
        if (deletedCategoryId <= 0) return;
        var current = await GetSettingAsync("psalm_category_id");
        if (int.TryParse(current, out var id) && id == deletedCategoryId)
            await SaveSettingAsync("psalm_category_id", "0");
    }

    /// <summary>
    /// Usuwa kategorię, ale ZOSTAWIA pieśni — dostają <c>CategoryId = NULL</c> i widać je
    /// w oknie Cantio pod wirtualną pozycją „Bez kategorii". Zwraca liczbę odczepionych pieśni.
    ///
    /// Odczepienie robimy JAWNIE, nie licząc na <c>ON DELETE SET NULL</c> w schemacie: dzięki temu
    /// znamy liczbę pieśni do acka, a wynik nie zależy od tego, czy połączenie ma
    /// <c>PRAGMA foreign_keys</c> włączone. Pozycje zestawów zostają nietknięte — pieśń dalej
    /// istnieje, więc nie ma czego sprzątać (na tym wywracała się poprzednia implementacja,
    /// która kasowała pieśń i uderzała w RESTRICT na SetlistItems).
    /// </summary>
    public async Task<int> DeleteCategoryKeepSongsAsync(int id)
    {
        await using var db = new CantioDbContext();
        var songs = await db.Songs.Where(s => s.CategoryId == id).ToListAsync();
        foreach (var s in songs) s.CategoryId = null;
        var cat = await db.Categories.FindAsync(id);
        if (cat != null) db.Categories.Remove(cat);
        await db.SaveChangesAsync();
        await ClearPsalmCategoryIfDeletedAsync(id);
        return songs.Count;
    }

    public async Task DeleteCategoryWithSongsAsync(int id)
    {
        await using var db = new CantioDbContext();
        var songIds = await db.Songs.Where(s => s.CategoryId == id).Select(s => s.Id).ToListAsync();
        if (songIds.Count > 0)
        {
            db.SetlistItems.RemoveRange(db.SetlistItems.Where(i => i.SongId.HasValue && songIds.Contains(i.SongId.Value)));
            db.Songs.RemoveRange(db.Songs.Where(s => songIds.Contains(s.Id)));
        }
        var cat = await db.Categories.FindAsync(id);
        if (cat != null) db.Categories.Remove(cat);
        await db.SaveChangesAsync();
        await ClearPsalmCategoryIfDeletedAsync(id);
    }

    public async Task<int> CountSongsInCategoryAsync(int id)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.CountAsync(s => s.CategoryId == id);
    }

    public async Task<Category?> GetCategoryAsync(int id)
    {
        await using var db = new CantioDbContext();
        return await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>
    /// Kategoria o zgodnej nazwie (pl-PL, ignoreCase) albo null. Porównanie PO STRONIE KLIENTA —
    /// SQLite <c>lower()</c> obsługuje tylko ASCII, więc „MARYJNE" i „Maryjne" nie zrównałyby się
    /// w zapytaniu EF.
    /// </summary>
    public async Task<Category?> FindCategoryByNameAsync(string name)
    {
        await using var db = new CantioDbContext();
        var all = await db.Categories.AsNoTracking().ToListAsync();
        return all.FirstOrDefault(c => NameEquals(c.Name, name));
    }

    /// <summary>
    /// Przesuwa kategorię o <paramref name="delta"/> pozycji w kolejności i PRZENUMEROWUJE całą
    /// listę na 1..N — dokładnie to samo, co strzałki ▲▼ w oknie Cantio
    /// (<c>DisplayViewModel.SaveCategoryOrderAsync</c>). Zwraca nowy <c>Number</c>
    /// albo null, gdy kategorii nie ma lub ruch wychodzi poza zakres.
    /// </summary>
    public async Task<int?> MoveCategoryAsync(int id, int delta)
    {
        await using var db = new CantioDbContext();
        var ordered = await db.Categories.OrderBy(c => c.Number).ThenBy(c => c.Id).ToListAsync();
        int idx = ordered.FindIndex(c => c.Id == id);
        if (idx < 0) return null;
        int target = idx + delta;
        if (target < 0 || target >= ordered.Count) return null;

        (ordered[idx], ordered[target]) = (ordered[target], ordered[idx]);
        for (int i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
        await db.SaveChangesAsync();
        return target + 1;
    }

    // Pieśni

    public async Task<List<Song>> GetSongsByCategoryAsync(int categoryId)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Where(s => s.CategoryId == categoryId)
            .OrderBy(s => s.Number)
            .ToListAsync();
    }

    /// <summary>
    /// Pieśni bez kategorii (<c>CategoryId IS NULL</c>) — zawartość wirtualnej pozycji
    /// „Bez kategorii" na liście KATEGORIE. Powstają wyłącznie przez
    /// <see cref="DeleteCategoryKeepSongsAsync"/>.
    /// </summary>
    public async Task<List<Song>> GetUncategorizedSongsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Where(s => s.CategoryId == null)
            .OrderBy(s => s.Number)
            .ToListAsync();
    }

    /// <summary>Ile pieśni jest bez kategorii — 0 chowa wirtualną pozycję z listy.</summary>
    public async Task<int> CountUncategorizedSongsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Songs.CountAsync(s => s.CategoryId == null);
    }

    /// <summary>Szuka po tytule i/lub numerze ze śpiewnika — logika w <see cref="SongSearch"/>.</summary>
    public async Task<List<Song>> SearchSongsAsync(string query, CancellationToken ct = default)
    {
        await using var db = new CantioDbContext();
        return await SongSearch.SearchAsync(db.Songs.AsNoTracking(), query, ct);
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

    /** Odnotowuje wyświetlenie pieśni na projektorze (lista „Ostatnie"). */
    public async Task TouchSongUsageAsync(int songId)
    {
        await using var db = new CantioDbContext();
        var song = await db.Songs.FirstOrDefaultAsync(s => s.Id == songId);
        if (song == null) return;
        song.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<Song>> GetRecentSongsAsync(int limit = 10)
    {
        await using var db = new CantioDbContext();
        return await db.Songs.AsNoTracking()
            .Where(s => s.LastUsedAt != null)
            .OrderByDescending(s => s.LastUsedAt)
            .Take(limit)
            .ToListAsync();
    }

    // Przyjmuje pieśni z Android pilota i wstawia je do właściwych kategorii.
    // Zwraca mapowanie localId → assignedId.
    public async Task<List<(int localId, int assignedId)>> SyncPushSongsAsync(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var songsEl = doc.RootElement.GetProperty("songs");
        var mapping = new List<(int localId, int assignedId)>();

        await using var db = new CantioDbContext();

        foreach (var songEl in songsEl.EnumerateArray())
        {
            var localId    = songEl.GetProperty("localId").GetInt32();
            var title      = songEl.GetProperty("title").GetString() ?? "";
            var author     = songEl.GetProperty("author").GetString() ?? "";
            var rawCategoryId = songEl.TryGetProperty("categoryId", out var catEl) ? catEl.GetInt32() : 0;

            // 0 (albo brak pola) = „bez kategorii" — dokładnie to, co desktop wysyła w `songs_data`
            // dla pieśni z CategoryId == NULL. Bez tego pieśń bez kategorii wracałaby z telefonu
            // jako DUPLIKAT w pierwszej kategorii (upsert szuka po tytule W TEJ SAMEJ kategorii).
            // Niezerowe, ale nieistniejące ID = przestarzała lista kategorii w Pilocie → jak dotąd
            // fallback do pierwszej kategorii.
            int? categoryId = rawCategoryId > 0 ? rawCategoryId : null;
            if (categoryId is int cid && !await db.Categories.AnyAsync(c => c.Id == cid))
            {
                var firstCat = await db.Categories.OrderBy(c => c.Number).FirstOrDefaultAsync();
                categoryId = firstCat?.Id;
            }

            // Upsert: szukaj po tytule w tej samej kategorii (null == null też jest dopasowaniem)
            var song = categoryId is null
                ? await db.Songs.Include(s => s.Verses)
                      .FirstOrDefaultAsync(s => s.Title == title && s.CategoryId == null)
                : await db.Songs.Include(s => s.Verses)
                      .FirstOrDefaultAsync(s => s.Title == title && s.CategoryId == categoryId);

            if (song == null)
            {
                song = new Song { Title = title, Author = author, CategoryId = categoryId, Number = 0 };
                db.Songs.Add(song);
                await db.SaveChangesAsync();
            }
            else
            {
                song.Author = author;
                var oldVerses = await db.Verses.Where(v => v.SongId == song.Id).ToListAsync();
                db.Verses.RemoveRange(oldVerses);
                await db.SaveChangesAsync();
            }

            var partsEl = songEl.GetProperty("parts");
            int pos = 0;
            foreach (var partEl in partsEl.EnumerateArray())
            {
                db.Verses.Add(new Verse
                {
                    SongId   = song.Id,
                    Position = pos++,
                    Type     = partEl.GetProperty("type").GetString() ?? "v",
                    Text     = partEl.GetProperty("text").GetString() ?? ""
                });
            }
            await db.SaveChangesAsync();

            mapping.Add((localId, song.Id));
        }

        return mapping;
    }

    public async Task<(int total, List<Song> items)> GetSongsForSyncAsync(int offset, int limit)
    {
        await using var db = new CantioDbContext();
        var total = await db.Songs.CountAsync();
        var items = await db.Songs.AsNoTracking()
            .Include(s => s.Verses)
            .OrderBy(s => s.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
        return (total, items);
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
            var existing = await db.Songs.FindAsync(song.Id)
                ?? throw new InvalidOperationException($"Pieśń {song.Id} nie istnieje w bazie");
            existing.Title = song.Title;
            existing.Number = song.Number;
            existing.Author = song.Author;
            existing.CategoryId = song.CategoryId;
            existing.PlayOrderJson = song.PlayOrderJson;

            var oldVerses = await db.Verses.Where(v => v.SongId == song.Id).ToListAsync();
            db.Verses.RemoveRange(oldVerses);

            foreach (var v in song.Verses)
                db.Verses.Add(new Verse
                {
                    SongId = song.Id,
                    Position = v.Position,
                    Type = v.Type,
                    Text = v.Text,
                    ImagePath = v.ImagePath,
                    BackgroundImagePath = v.BackgroundImagePath
                });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ile ZAPISANYCH zestawów zawiera tę pieśń. Okno Cantio kasuje pieśń razem z jej pozycjami
    /// w zestawach (<see cref="DeleteSongAsync"/>), a protokół WS używa tej liczby, żeby najpierw
    /// odmówić kasowania z tabletu (<c>reason:"in_setlists"</c>) — na tablecie nie ma nikogo,
    /// kto by zobaczył, ile zestawów przy okazji straci pozycję.
    /// </summary>
    public async Task<int> CountSetlistsWithSongAsync(int songId)
    {
        await using var db = new CantioDbContext();
        return await db.SetlistItems.AsNoTracking()
            .Where(i => i.SongId == songId)
            .Select(i => i.SetlistId)
            .Distinct()
            .CountAsync();
    }

    /// <summary>
    /// Ile ZAPISANYCH zestawów straciłoby pozycje, gdyby skasować kategorię RAZEM z pieśniami
    /// (<see cref="DeleteCategoryWithSongsAsync"/>). Odpowiednik
    /// <see cref="CountSetlistsWithSongAsync"/> dla całej kategorii — protokół WS używa tej
    /// liczby, żeby odmówić kasowania z tabletu bez jawnego <c>force</c>.
    /// </summary>
    public async Task<int> CountSetlistsWithCategorySongsAsync(int categoryId)
    {
        await using var db = new CantioDbContext();
        return await db.SetlistItems.AsNoTracking()
            .Where(i => i.SongId.HasValue &&
                        db.Songs.Any(s => s.Id == i.SongId!.Value && s.CategoryId == categoryId))
            .Select(i => i.SetlistId)
            .Distinct()
            .CountAsync();
    }

    public async Task DeleteSongAsync(int songId)
    {
        await using var db = new CantioDbContext();
        var items = db.SetlistItems.Where(i => i.SongId == songId);
        db.SetlistItems.RemoveRange(items);
        var song = await db.Songs.FindAsync(songId);
        if (song is not null) db.Songs.Remove(song);
        await db.SaveChangesAsync();
    }

    public async Task SaveVerseTextAsync(int verseId, string newText, string? imagePath = null)
    {
        await using var db = new CantioDbContext();
        var verse = await db.Verses.FindAsync(verseId);
        if (verse != null)
        {
            verse.Text = newText;
            verse.ImagePath = imagePath;
            await db.SaveChangesAsync();
        }
    }

    public async Task SaveVerseTextsAsync(
        IEnumerable<(int Id, string Text, string? ImagePath, string? BackgroundImagePath)> updates)
    {
        var list = updates.ToList();
        if (list.Count == 0) return;
        var ids = list.Select(u => u.Id).ToList();
        await using var db = new CantioDbContext();
        var verses = await db.Verses.Where(v => ids.Contains(v.Id)).ToListAsync();
        foreach (var verse in verses)
        {
            var (_, text, imagePath, bgImagePath) = list.First(u => u.Id == verse.Id);
            verse.Text = text;
            verse.ImagePath = imagePath;
            verse.BackgroundImagePath = bgImagePath;
        }
        await db.SaveChangesAsync();
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

    public async Task SaveVerseOrderAsync(IEnumerable<(int id, int position)> order)
    {
        await using var db = new CantioDbContext();
        var posMap = order.ToDictionary(o => o.id, o => o.position);
        var verses = await db.Verses
            .Where(v => posMap.Keys.Contains(v.Id))
            .ToListAsync();
        foreach (var v in verses)
            if (posMap.TryGetValue(v.Id, out var pos)) v.Position = pos;
        await db.SaveChangesAsync();
    }

    // Zestawy

    public async Task<List<Setlist>> GetSetlistsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .OrderByDescending(sl => sl.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<(int Id, string Name, string? Group, int SongCount, long UpdatedAt, bool IsPinned)>> GetSetlistSummariesAsync()
    {
        await using var db = new CantioDbContext();
        var rows = await db.Setlists.AsNoTracking()
            .OrderByDescending(sl => sl.UpdatedAt)
            .Select(sl => new { sl.Id, sl.Name, sl.Group, SongCount = sl.Items.Count(i => i.SongId != null), sl.UpdatedAt, sl.IsPinned })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.Name, r.Group, r.SongCount,
            new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(), r.IsPinned)).ToList();
    }

    public async Task<(int Id, string Name, string? Group, long UpdatedAt, List<(int SongId, string Title)> Songs)?> GetSetlistDetailAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        var sl = await db.Setlists.AsNoTracking()
            .Include(s => s.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.Song)
            .FirstOrDefaultAsync(s => s.Id == setlistId);
        if (sl == null) return null;
        var songs = sl.Items
            .Where(i => i.SongId.HasValue && i.Song != null)
            .Select(i => (i.SongId!.Value, i.Song!.Title))
            .ToList();
        var updatedAt = new DateTimeOffset(sl.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
        return (sl.Id, sl.Name, sl.Group, updatedAt, songs);
    }

    /// <summary>
    /// Wynik synchronizacji zestawu z Pilota.
    /// <paramref name="Conflict"/> = zestaw zmieniono po obu stronach; <paramref name="Songs"/> niesie wtedy wersję desktopową.
    /// </summary>
    public readonly record struct PilotSetlistSyncResult(
        bool Conflict, int SetlistId, string Name, long UpdatedAt, List<(int SongId, string Title)> Songs);

    /// <summary>
    /// Zapis zestawu przysłanego przez Pilota z wykrywaniem konfliktu.
    /// <para><paramref name="baseUpdatedAtMs"/> = `UpdatedAt` z chwili ostatniej synchronizacji Pilota.
    /// Gdy zestaw na desktopie jest nowszy, NIE nadpisujemy — zwracamy wersję desktopową do rozstrzygnięcia
    /// przez użytkownika. Brak `baseUpdatedAtMs` (stary Pilot) lub <paramref name="force"/> = nadpisz bezwarunkowo.</para>
    /// </summary>
    public async Task<PilotSetlistSyncResult> SyncSetlistFromPilotAsync(
        int? desktopId, string name, long updatedAtMs, int[] songIds,
        long? baseUpdatedAtMs = null, bool force = false)
    {
        if (!force && baseUpdatedAtMs.HasValue && desktopId is int existingId && existingId > 0)
        {
            var current = await GetSetlistDetailAsync(existingId);
            if (current != null && current.Value.UpdatedAt > baseUpdatedAtMs.Value)
                return new PilotSetlistSyncResult(
                    true, current.Value.Id, current.Value.Name, current.Value.UpdatedAt, current.Value.Songs);
        }

        var assignedId = await CreateOrUpdateSetlistFromPilotAsync(desktopId, name, updatedAtMs, songIds);
        return new PilotSetlistSyncResult(false, assignedId, name, updatedAtMs, []);
    }

    public async Task<int> CreateOrUpdateSetlistFromPilotAsync(int? desktopId, string name, long updatedAtMs, int[] songIds)
    {
        await using var db = new CantioDbContext();
        Setlist setlist;
        if (desktopId.HasValue)
        {
            setlist = await db.Setlists.Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == desktopId.Value)
                ?? new Setlist { Name = name };
            if (setlist.Id != 0)
            {
                db.SetlistItems.RemoveRange(setlist.Items);
                setlist.Name      = name;
                setlist.UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs).UtcDateTime;
            }
        }
        else
        {
            setlist = new Setlist
            {
                Name      = name,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs).UtcDateTime
            };
        }
        if (setlist.Id == 0) db.Setlists.Add(setlist);
        await db.SaveChangesAsync();

        var songs = await db.Songs.Where(s => songIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var items = songIds
            .Select((id, pos) => songs.TryGetValue(id, out var s)
                ? new SetlistItem { SetlistId = setlist.Id, SongId = id, Position = pos }
                : null)
            .Where(i => i != null)
            .Cast<SetlistItem>()
            .ToList();
        db.SetlistItems.AddRange(items);
        await db.SaveChangesAsync();
        return setlist.Id;
    }

    public async Task<Setlist?> GetSetlistAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == setlistId);
    }

    public async Task<List<Setlist>> GetPinnedSetlistsAsync()
    {
        await using var db = new CantioDbContext();
        return await db.Setlists.AsNoTracking()
            .Where(sl => sl.IsPinned)
            .OrderBy(sl => sl.Name)
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

    /// <summary>
    /// Zapis zestawu = ZMIANA TREŚCI, więc ZAWSZE odświeża <see cref="Setlist.UpdatedAt"/>.
    /// Pilot porównuje ten znacznik z zapamiętanym przy ostatniej synchronizacji — bez odświeżenia
    /// zmiana zrobiona w Cantio jest dla niego niewidoczna i zostaje po cichu nadpisana wersją z telefonu.
    /// <para>Operacje, które NIE zmieniają treści (przypięcie), mają własne metody
    /// (<see cref="SetSetlistPinnedAsync"/>) — inaczej przypinanie zgłaszałoby Pilotowi fałszywy konflikt.</para>
    /// <para>Zapis z Pilota idzie osobną ścieżką (<see cref="CreateOrUpdateSetlistFromPilotAsync"/>)
    /// i zachowuje znacznik przysłany przez telefon — to warunek działania round-tripu synchronizacji.</para>
    /// </summary>
    public async Task SaveSetlistAsync(Setlist setlist)
    {
        await using var db = new CantioDbContext();
        setlist.UpdatedAt = DateTime.UtcNow;
        if (setlist.Id == 0) db.Setlists.Add(setlist);
        else db.Setlists.Update(setlist);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Przypięcie/odpięcie — flaga UI, nie treść zestawu, więc BEZ dotykania `UpdatedAt`.
    /// Zwraca `false`, gdy zestawu nie ma (protokół WS odsyła wtedy `reason:"not_found"`).
    /// </summary>
    public async Task<bool> SetSetlistPinnedAsync(int setlistId, bool pinned)
    {
        await using var db = new CantioDbContext();
        var setlist = await db.Setlists.FindAsync(setlistId);
        if (setlist == null) return false;
        setlist.IsPinned = pinned;
        await db.SaveChangesAsync();
        return true;
    }

    // Porównanie po stronie klienta: SQLite lower() obsługuje tylko ASCII,
    // więc polskie znaki w nazwach dni ("11 Śro") nie były dopasowywane.
    private static readonly System.Globalization.CompareInfo _plCompare =
        new System.Globalization.CultureInfo("pl-PL").CompareInfo;

    public static bool NameEquals(string? a, string? b) =>
        _plCompare.Compare((a ?? "").Trim(), (b ?? "").Trim(),
            System.Globalization.CompareOptions.IgnoreCase) == 0;

    // Normalizacja klucza grupy: trim, lowercase (pl-PL), '_'→spacja, pl→ASCII.
    // "zwykly" ≡ "Zwykły", "wielki_post" ≡ "Wielki Post".
    public static string NormalizeGroupKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var lower = s.Trim().ToLower(new System.Globalization.CultureInfo("pl-PL")).Replace('_', ' ');
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            sb.Append(ch switch
            {
                'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n',
                'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z',
                _ => ch
            });
        }
        var norm = sb.ToString();
        // Aliasy: klucz okresu → alternatywne pisownie grup
        return norm switch
        {
            "boze narodzenie" => "boznarodzenie",
            _ => norm
        };
    }

    // Aliasy znormalizowanych nazw grup (przez NormalizeGroupKey) → klucz okresu.
    // Klucze okresów pochodzą z LiturgicalCalendarService.GetDay (day.Group).
    // Uwzględnia lokalizacje pl/en/es z Season.* oraz warianty pisowni.
    private static readonly Dictionary<string, string> _seasonAliases = new()
    {
        // zwykly — pl "Okres Zwykły", en "Ordinary Time", es "Tiempo Ordinario"
        ["okres zwykly"] = "zwykly",
        ["ordinary time"] = "zwykly",
        ["tiempo ordinario"] = "zwykly",
        // adwent — pl "Adwent", en "Advent", es "Adviento"
        ["advent"] = "adwent",
        ["adviento"] = "adwent",
        // wielki_post — pl "Wielki Post", en "Lent", es "Cuaresma"
        ["lent"] = "wielki_post",
        ["cuaresma"] = "wielki_post",
        // wielkanoc — pl "Wielkanoc", en "Easter", es "Pascua"
        ["easter"] = "wielkanoc",
        ["pascua"] = "wielkanoc",
        // boznarodzenie — pl "Boże Narodzenie", en "Christmas", es "Navidad"
        ["christmas"] = "boznarodzenie",
        ["navidad"] = "boznarodzenie",
    };

    // Zbiór znormalizowanych postaci wszystkich kluczy okresów (self-mapping).
    // NormalizeGroupKey("wielki_post") == "wielki post" itd.
    private static readonly HashSet<string> _seasonKeyNorms = new()
    {
        NormalizeGroupKey("zwykly"),
        NormalizeGroupKey("adwent"),
        NormalizeGroupKey("wielki_post"),
        NormalizeGroupKey("wielkanoc"),
        NormalizeGroupKey("boznarodzenie"),
    };

    // Zwraca klucz okresu dla nazwy grupy, gdy jej znormalizowana postać jest
    // samym kluczem lub aliasem; inaczej null. NormalizeGroupKey już mapuje
    // "boze narodzenie" → "boznarodzenie", więc self-mapping obejmuje ten wariant.
    public static string? SeasonKeyFromGroupName(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return null;
        var norm = NormalizeGroupKey(groupName);
        if (_seasonKeyNorms.Contains(norm))
            return norm switch
            {
                "wielki post" => "wielki_post",
                _ => norm
            };
        return _seasonAliases.TryGetValue(norm, out var key) ? key : null;
    }

    // Zwraca istniejącą pisownię grupy pasującej po normalizacji do klucza,
    // albo klucz bez zmian gdy żadna nie pasuje.
    public async Task<string> ResolveGroupNameAsync(string key)
    {
        var target = NormalizeGroupKey(key);

        var candidates = new List<string>();
        var csv = await GetSettingAsync("setlist_groups") ?? string.Empty;
        candidates.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Select(g => g.Trim())
                               .Where(g => !string.IsNullOrEmpty(g)));

        await using (var db = new CantioDbContext())
        {
            var dbGroups = await db.Setlists.AsNoTracking()
                .Select(s => s.Group)
                .Distinct()
                .ToListAsync();
            candidates.AddRange(dbGroups
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!));
        }

        foreach (var c in candidates)
            if (NormalizeGroupKey(c) == target || SeasonKeyFromGroupName(c) == key)
                return c;

        return key;
    }

    // Lookup dla „Przypnij tydzień": dopasowanie po nazwie + kluczu okresu.
    // 1) nazwa == oraz SeasonKey == seasonKey; 2) fallback: nazwa == oraz
    // SeasonKeyFromGroupName(Group) == seasonKey (stare zestawy) + backfill SeasonKey.
    public async Task<Setlist?> GetSetlistForPinAsync(string name, string seasonKey)
    {
        await using var db = new CantioDbContext();
        var candidates = await db.Setlists.AsNoTracking().ToListAsync();

        var match = candidates.FirstOrDefault(s =>
            NameEquals(s.Name, name) && s.SeasonKey == seasonKey);
        if (match != null) return match;

        var fallback = candidates.FirstOrDefault(s =>
            NameEquals(s.Name, name) && SeasonKeyFromGroupName(s.Group) == seasonKey);
        if (fallback != null)
        {
            fallback.SeasonKey = seasonKey;
            db.Setlists.Update(fallback);
            await db.SaveChangesAsync();
        }
        return fallback;
    }

    public async Task<Setlist?> GetSetlistByNameAndGroupAsync(string name, string group)
    {
        await using var db = new CantioDbContext();
        var candidates = await db.Setlists.AsNoTracking().ToListAsync();
        var groupKey = NormalizeGroupKey(group);
        return candidates.FirstOrDefault(s =>
            NameEquals(s.Name, name) &&
            NormalizeGroupKey(s.Group) == groupKey);
    }

    /// <summary>
    /// Grupy zestawów z ustawienia <c>setlist_groups</c> — kolejność DOKŁADNIE jak w CSV
    /// (UI sortuje dopiero przy wyświetlaniu). Grupy nie są encją; ich jedynym nośnikiem
    /// jest ten CSV, a <c>Setlist.Group</c> to luźny string.
    /// </summary>
    public async Task<List<string>> GetSetlistGroupsAsync()
    {
        var csv = await GetSettingAsync("setlist_groups") ?? string.Empty;
        return [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(g => g.Trim())
                      .Where(g => !string.IsNullOrEmpty(g))];
    }

    public Task SaveSetlistGroupsAsync(IEnumerable<string> groups) =>
        SaveSettingAsync("setlist_groups", string.Join(",", groups
            .Select(g => (g ?? "").Trim())
            .Where(g => g.Length > 0)));

    /// <summary>Ile zestawów wskazuje na grupę o tej nazwie (porównanie pl-PL po stronie klienta).</summary>
    public async Task<int> CountSetlistsInGroupAsync(string group)
    {
        await using var db = new CantioDbContext();
        var groups = await db.Setlists.AsNoTracking().Select(s => s.Group).ToListAsync();
        return groups.Count(g => NameEquals(g, group));
    }

    public async Task EnsureGroupAsync(string group)
    {
        var csv = await GetSettingAsync("setlist_groups") ?? string.Empty;
        var groups = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(g => g.Trim())
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
        if (!groups.Contains(group, StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(group);
            await SaveSettingAsync("setlist_groups", string.Join(",", groups));
        }
    }

    public async Task<Setlist> SaveSetlistWithItemsAsync(Setlist setlist, List<SetlistItem> items)
    {
        await using var db = new CantioDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            db.Setlists.Add(setlist);
            await db.SaveChangesAsync(); // uzyskaj Id

            var newItems = items.Select(item => new SetlistItem
            {
                SetlistId = setlist.Id,
                SongId = item.SongId,
                Position = item.Position,
                Type = item.Type,
                SelectedVerses = item.SelectedVerses,
                ImagePath = item.ImagePath,
                Notes = item.Notes
            }).ToList();

            db.SetlistItems.AddRange(newItems);
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            return setlist;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Usuwa zestaw wraz z jego pozycjami. Zwraca false, gdy zestawu już nie było.</summary>
    public async Task<bool> DeleteSetlistAsync(int setlistId)
    {
        await using var db = new CantioDbContext();
        var setlist = await db.Setlists
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == setlistId);
        if (setlist is null) return false;
        db.SetlistItems.RemoveRange(setlist.Items);
        db.Setlists.Remove(setlist);
        await db.SaveChangesAsync();
        return true;
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

        db.SetlistItems.AddRange(SetlistSnapshot.ForSave(items, setlistId));

        // Zmiana pozycji (dodanie/usunięcie/przeniesienie pieśni) to zmiana treści zestawu —
        // bez odświeżenia znacznika Pilot nie wykryje jej i nadpisze swoją wersją.
        var setlist = await db.Setlists.FindAsync(setlistId);
        if (setlist != null) setlist.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task SaveSongFontSizeOverrideAsync(int songId, double? fontSize)
    {
        await using var db = new CantioDbContext();
        var song = await db.Songs.FindAsync(songId);
        if (song == null) return;
        song.FontSizeOverride = fontSize;
        await db.SaveChangesAsync();
    }

    public async Task SaveSetlistItemNotesAsync(int itemId, string? notes)
    {
        await using var db = new CantioDbContext();
        var item = await db.SetlistItems.FindAsync(itemId);
        if (item == null) return;
        item.Notes = notes;
        await db.SaveChangesAsync();
    }

    public async Task ClearAllDataAsync()
    {
        await using var db = new CantioDbContext();
        db.SetlistItems.RemoveRange(db.SetlistItems);
        db.Setlists.RemoveRange(db.Setlists);
        db.Verses.RemoveRange(db.Verses);
        db.Songs.RemoveRange(db.Songs);
        db.Categories.RemoveRange(db.Categories);
        await db.SaveChangesAsync();
    }

    // Ustawienia

    public string? GetSettingSync(string key)
    {
        using var db = new CantioDbContext();
        return db.Settings.AsNoTracking().FirstOrDefault(a => a.Key == key)?.Value;
    }

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

    // Tagi formatowania

    private static List<TextFormatTag> GetDefaultTags() =>
    [
        new TextFormatTag { Name = "z",  Color = "#c9a84c" },
        new TextFormatTag { Name = "cz", Color = "#e05555" },
        new TextFormatTag { Name = "bb", Bold = true },
        new TextFormatTag { Name = "kk", Italic = true },
    ];

    public List<TextFormatTag> GetTextTags()
    {
        using var db = new CantioDbContext();
        var setting = db.Settings.AsNoTracking().FirstOrDefault(s => s.Key == "text_tags");
        if (setting == null || string.IsNullOrEmpty(setting.Value)) return GetDefaultTags();
        try { return JsonSerializer.Deserialize<List<TextFormatTag>>(setting.Value) ?? GetDefaultTags(); }
        catch { return GetDefaultTags(); }
    }

    public async Task SaveTextTagsAsync(List<TextFormatTag> tags)
    {
        var json = JsonSerializer.Serialize(tags);
        await SaveSettingAsync("text_tags", json);
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
            GradientEnabled = Get("bg_gradient_enabled", false, v => v == "true"),
            GradientType = Get("bg_gradient_type", "linear", v => v),
            GradientColor1 = Get("bg_gradient_color1", "#000000", v => v),
            GradientColor2 = Get("bg_gradient_color2", "#1a1a2e", v => v),
            GradientAngle = Get("bg_gradient_angle", 180.0, v => double.TryParse(v, out var d) ? d : 180),
            TextTags = GetTextTags(),
            FontAutoFit = Get("font_auto_fit", true, v => v == "true"),
            PsalmCategoryId = Get("psalm_category_id", 0, v => int.TryParse(v, out var id) ? id : 0),
        };
    }

    // ── Import psalmów responsoryjnych ───────────────────────────────────────

    private record PsalmSeedRecord(
        [property: JsonPropertyName("dzien")]                string Dzien,
        [property: JsonPropertyName("cykl")]                 string Cykl,
        [property: JsonPropertyName("psalm_responsoryjny")]  string PsalmResponsoryjny,
        [property: JsonPropertyName("aklamacja")]            string? Aklamacja
    );

    /// <summary>
    /// Importuje psalmy responsoryjne z wbudowanego zasobu psalmy.json.gz
    /// do kategorii "Psalmy responsoryjne". Pomija tytuły już istniejące.
    /// Zwraca liczbę dodanych pieśni lub -1 gdy kategoria nie istnieje.
    /// </summary>
    public async Task<int> ImportPsalmySeedAsync()
    {
        // Odczyt i dekompresja zasobu
        var asm = Assembly.GetExecutingAssembly();
        await using var gz = asm.GetManifestResourceStream("Cantio.Assets.Data.psalmy.json.gz")!;
        await using var deflated = new GZipStream(gz, CompressionMode.Decompress);
        using var reader = new StreamReader(deflated, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var records = JsonSerializer.Deserialize<List<PsalmSeedRecord>>(json) ?? [];

        await using var db = new CantioDbContext();

        var category = await db.Categories
            .FirstOrDefaultAsync(c => c.Name == "Psalmy responsoryjne");
        if (category == null) return -1;

        // Usuń stare psalmy (mogą mieć inny format klucza) — importujemy od nowa
        var oldSongIds = await db.Songs
            .Where(s => s.CategoryId == category.Id)
            .Select(s => s.Id)
            .ToListAsync();
        if (oldSongIds.Count > 0)
        {
            await db.SetlistItems.Where(i => i.SongId.HasValue && oldSongIds.Contains(i.SongId.Value)).ExecuteDeleteAsync();
            await db.Verses.Where(v => oldSongIds.Contains(v.SongId)).ExecuteDeleteAsync();
            await db.Songs.Where(s => oldSongIds.Contains(s.Id)).ExecuteDeleteAsync();
        }                               

        var existing = new HashSet<string>(); // po wyczyszczeniu nic nie istnieje
        int maxNumber = 0;

        // Faza 1: wstaw wszystkie nowe pieśni
        var toInsert = new List<(Song Song, string Psalm, string Akl)>();
        foreach (var rec in records)
        {
            // Cykl jest już zawarty w polu dzien (np. "ZW 12 Nie A") — używamy dzien bezpośrednio
            string title = rec.Dzien.Trim();
            if (existing.Contains(title)) continue;
            maxNumber++;
            var song = new Song { Number = maxNumber, Title = title, CategoryId = category.Id };
            db.Songs.Add(song);
            toInsert.Add((song, rec.PsalmResponsoryjny, rec.Aklamacja ?? ""));
        }
        await db.SaveChangesAsync(); // Pieśni otrzymują Id

        // Faza 2: wstaw wszystkie zwrotki
        foreach (var (song, psalm, akl) in toInsert)
        {
            int pos = 0;
            foreach (var (type, text) in ParsePsalmBlocks(psalm))
                db.Verses.Add(new Verse { SongId = song.Id, Type = type, Text = text, Position = pos++ });
            foreach (var (type, text) in ParsePsalmBlocks(akl))
                db.Verses.Add(new Verse { SongId = song.Id, Type = type, Text = text, Position = pos++ });
        }
        await db.SaveChangesAsync();

        return toInsert.Count;
    }

    private static List<(string Type, string Text)> ParsePsalmBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var blocks = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                         .Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
        if (blocks.Count == 0) return [];

        var result = new List<(string, string)>();
        int i = 0;

        string[] prefixes = ["Refren:", "Aklamacja:"];
        string? prefix = prefixes.FirstOrDefault(p => blocks[0].StartsWith(p));
        if (prefix != null)
        {
            string refren = blocks[0][prefix.Length..].Trim();
            i = 1;
            if (i < blocks.Count && blocks[i].StartsWith("Albo:"))
            {
                refren = $"{refren}\n\nAlbo:\n{blocks[i]["Albo:".Length..].Trim()}";
                i++;
            }
            result.Add(("c", refren));
        }
        else
        {
            result.Add(("v", blocks[0]));
            i = 1;
        }

        for (; i < blocks.Count; i++)
            result.Add(("v", blocks[i]));

        return result;
    }
}