namespace Cantio.Services;

/// <summary>
/// Wymiary płótna projekcji i transformata kompensująca skalę Windows (DPI).
///
/// PROBLEM, który to rozwiązuje: podział pieśni na slajdy liczył się w jednostkach WPF (DIU),
/// a te kurczą się wraz ze skalą pulpitu — ekran 1920×1080 przy 150% to płótno 1280×720,
/// przy niezmienionym rozmiarze czcionki w DIU. Ta sama pieśń wychodziła w 10 slajdach przy 100%,
/// 14 przy 125% i 21 przy 150%. Dla niewidomego organisty (laptop domyślnie 150%) podział
/// zmieniał się od ustawienia, którego nie ma jak zobaczyć.
///
/// ROZWIĄZANIE: layout liczy się w FIZYCZNYCH pikselach ekranu projekcji
/// (<see cref="LayoutWidth"/>/<see cref="LayoutHeight"/>), a zawartość okna dostaje
/// <c>ScaleTransform(<see cref="ScaleX"/>, <see cref="ScaleY"/>)</c>, żeby po przeskalowaniu
/// wypełnić okno o rozmiarze <see cref="WindowWidth"/>×<see cref="WindowHeight"/> w DIU.
/// Czcionka „64" znaczy wtedy 64 piksele ekranu przy każdej skali Windows.
/// </summary>
/// <param name="LayoutWidth">Szerokość płótna layoutu = fizyczna szerokość ekranu w pikselach.</param>
/// <param name="LayoutHeight">Wysokość płótna layoutu = fizyczna wysokość ekranu w pikselach.</param>
/// <param name="ScaleX">Mnożnik zawartości w poziomie (piksel → DIU); 1 przy skali 100%.</param>
/// <param name="ScaleY">Mnożnik zawartości w pionie.</param>
/// <param name="WindowWidth">Szerokość okna projekcji w DIU (tyle, ile ekran zajmuje w WPF).</param>
/// <param name="WindowHeight">Wysokość okna projekcji w DIU.</param>
public readonly record struct ProjectionMetrics(
    double LayoutWidth,
    double LayoutHeight,
    double ScaleX,
    double ScaleY,
    double WindowWidth,
    double WindowHeight)
{
    /// <summary>
    /// Skala 100% — zawartość okna zostaje nietknięta (bez transformaty, bez sztywnych wymiarów).
    /// Dzięki temu u istniejących parafii przy 100% nie zmienia się dosłownie nic.
    /// </summary>
    public bool IsIdentity => Near(ScaleX, 1.0) && Near(ScaleY, 1.0);

    /// <summary>
    /// Wymiary z fizycznych pikseli ekranu i transformaty <c>TransformFromDevice</c>
    /// (piksel urządzenia → DIU: 1.0 przy 100%, 0.8 przy 125%, ⅔ przy 150%).
    /// Bezsensowne wartości (0, ujemne, NaN, ∞) traktujemy jak skalę 100% — lepiej zachować
    /// się jak dotąd niż wyprodukować okno o zerowym rozmiarze.
    /// </summary>
    public static ProjectionMetrics FromScreen(double physicalWidth, double physicalHeight,
                                               double m11, double m22)
    {
        double sx = Sane(m11), sy = Sane(m22);
        double w = Sane(physicalWidth, fallback: 1920);
        double h = Sane(physicalHeight, fallback: 1080);
        return new ProjectionMetrics(w, h, sx, sy, w * sx, h * sy);
    }

    private static double Sane(double v, double fallback = 1.0)
        => double.IsFinite(v) && v > 0 ? v : fallback;

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.0001;
}
