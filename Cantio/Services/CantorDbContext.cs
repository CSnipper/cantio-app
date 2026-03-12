using Cantio.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace Cantio.Services;

public class CantioDbContext : DbContext
{
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Song> Songs { get; set; } = null!;
    public DbSet<Verse> Verses { get; set; } = null!;
    public DbSet<Setlist> Setlists { get; set; } = null!;
    public DbSet<SetlistItem> SetlistItems { get; set; } = null!;
    public DbSet<AppSettings> Settings { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cantio");

        Directory.CreateDirectory(folder);

        string dbPath = Path.Combine(folder, "cantor.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Category → Songs (1:N)
        modelBuilder.Entity<Song>()
            .HasOne(s => s.Category)
            .WithMany(c => c.Songs)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Song → Verses (1:N), posortowane po Position
        modelBuilder.Entity<Verse>()
            .HasOne(v => v.Song)
            .WithMany(s => s.Verses)
            .HasForeignKey(v => v.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Verse>()
            .Property(v => v.Type)
            .HasMaxLength(1);

        // Setlist → SetlistItems (1:N)
        modelBuilder.Entity<SetlistItem>()
            .HasOne(i => i.Setlist)
            .WithMany(sl => sl.Items)
            .HasForeignKey(i => i.SetlistId)
            .OnDelete(DeleteBehavior.Cascade);

        // Song → SetlistItems (1:N)
        modelBuilder.Entity<SetlistItem>()
            .HasOne(i => i.Song)
            .WithMany(s => s.SetlistItems)
            .HasForeignKey(i => i.SongId)
            .OnDelete(DeleteBehavior.Restrict);

        // Setlist: PinPosition ograniczone do 1–4
        modelBuilder.Entity<Setlist>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_Setlist_PinPosition",
                "PinPosition IS NULL OR (PinPosition >= 1 AND PinPosition <= 4)"));

        // AppSettings: unikalny klucz
        modelBuilder.Entity<AppSettings>()
            .HasIndex(a => a.Key)
            .IsUnique();
    }
}
