using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Cantio.Helpers;

/// <summary>
/// Skalowanie obrazka do podglądu w Pilocie (<c>image_get</c>) — warstwa WPF.
///
/// <para>Rdzeń (<c>Cantio.Core</c>) jest czystym <c>net10.0</c>: nie ma tam ani
/// <c>System.Drawing</c> (Windows-only), ani kodera obrazów, a dokładać <c>SkiaSharp</c> tylko po to
/// jedno byłoby przesadą. Dlatego <c>PilotImages</c> trzyma sam kontrakt, a gospodarz wpina tę
/// metodę delegatem <see cref="Cantio.Services.PilotImages.Scaler"/>.</para>
/// </summary>
public static class PilotImageScaler
{
    /// <summary>Jakość JPEG podglądu — kompromis „czytelne na tablecie" ↔ „0,3–0,7 MB na LAN".</summary>
    public const int JpegQuality = 80;

    /// <summary>
    /// Zwraca JPEG o dłuższym boku ≤ <paramref name="maxDim"/> i jego realne wymiary;
    /// <c>null</c>, gdy pliku nie da się zdekodować (uszkodzony / nie-obraz).
    /// </summary>
    public static (byte[] Data, int Width, int Height)? Scale(string fullPath, int maxDim)
    {
        try
        {
            // Najpierw SAM rozmiar: bez tego nie wiadomo, czy w ogóle skalować, a
            // DecodePixelWidth ustawiony „w ciemno" POWIĘKSZYŁBY mały obrazek.
            int srcW, srcH;
            using (var probe = File.OpenRead(fullPath))
            {
                var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                srcW = frame.PixelWidth;
                srcH = frame.PixelHeight;
            }
            if (srcW <= 0 || srcH <= 0) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(fullPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // plik zwolniony od razu
            var longest = Math.Max(srcW, srcH);
            if (longest > maxDim)
            {
                // Podajemy TYLKO jeden wymiar — drugi WPF dolicza sam, więc proporcje zostają
                // nietknięte, a dekoder od razu pracuje w docelowej skali (12 Mpx nie wchodzi
                // do pamięci w pełnym rozmiarze).
                if (srcW >= srcH) bmp.DecodePixelWidth  = Math.Max(1, (int)Math.Round(srcW * (double)maxDim / longest));
                else              bmp.DecodePixelHeight = Math.Max(1, (int)Math.Round(srcH * (double)maxDim / longest));
            }
            bmp.EndInit();
            bmp.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return (ms.ToArray(), bmp.PixelWidth, bmp.PixelHeight);
        }
        catch { return null; }
    }
}
