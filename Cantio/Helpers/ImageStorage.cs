using System.IO;

namespace Cantio.Helpers;

/// <summary>
/// Zarządza plikami obrazków — kopiuje do lokalnego folderu images i rozwiązuje ścieżki relatywne.
/// Przechowywany format: "images\plik.jpg" (relatywny względem %LocalAppData%\Cantio\).
/// Backward-compatible z absolutnymi ścieżkami (stary format).
/// </summary>
public static class ImageStorage
{
    private static string ImagesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cantio", "images");

    /// <summary>
    /// Kopiuje plik do folderu images i zwraca ścieżkę relatywną do zapisania w bazie.
    /// Jeśli plik już jest w folderze images, zwraca ścieżkę relatywną bez kopiowania.
    /// Przy kolizji nazw dodaje sufiks czasowy.
    /// </summary>
    public static string Import(string sourcePath)
    {
        var folder = ImagesFolder;
        Directory.CreateDirectory(folder);

        var fileName = Path.GetFileName(sourcePath);
        var destPath = Path.Combine(folder, fileName);

        // Plik już jest w naszym folderze — nie kopiuj
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
                StringComparison.OrdinalIgnoreCase))
            return Path.Combine("images", fileName);

        // Kolizja nazw z innym plikiem — dodaj sufiks czasowy
        if (File.Exists(destPath))
        {
            var ext = Path.GetExtension(fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            fileName = $"{stem}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
            destPath = Path.Combine(folder, fileName);
        }

        File.Copy(sourcePath, destPath, overwrite: false);
        return Path.Combine("images", fileName);
    }

    /// <summary>
    /// Rozwiązuje ścieżkę (relatywną lub absolutną legacy) do pełnej ścieżki pliku.
    /// </summary>
    public static string Resolve(string storedPath)
    {
        if (string.IsNullOrEmpty(storedPath)) return storedPath;
        if (Path.IsPathRooted(storedPath)) return storedPath; // legacy — absolutna ścieżka

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cantio", storedPath);
    }
}
