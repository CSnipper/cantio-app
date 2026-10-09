using System.Globalization;

namespace Cantio.Services;

/// <summary>
/// Powiększony refren psalmu responsoryjnego (ustawienie <c>psalm_refrain_scale</c>, w procentach
/// rozmiaru czcionki z zakładki WYGLĄD).
///
/// Po co: w trybie psalmu wierni widzą na ekranie WYŁĄCZNIE refren (i aklamację) — zwrotki czyta
/// psałterzysta, operator ma je tylko w podglądzie. Refren jest więc jedynym tekstem do śpiewania
/// i ma być czytelny z końca kościoła.
///
/// Reguła: refren dostaje <c>FontSize × skala / 100</c> jako SUFIT, a gdy w takim rozmiarze nie
/// mieści się na jednym slajdzie — czcionka maleje, aż się zmieści. Refren NIGDY nie jest dzielony
/// na kilka slajdów (wierni mieliby pół refrenu na ekranie). Pomiar tekstu robi WPF-owy
/// <c>SlideLayoutService.ComputeCappedFitFontSize</c>; tu jest wyłącznie arytmetyka.
///
/// Czysta klasa — bez WPF i bez bazy, żeby dało się ją dowieść testem na literałach.
/// </summary>
public static class PsalmRefrain
{
    public const string SettingKey = "psalm_refrain_scale";
    public const int MinScale = 100;
    public const int MaxScale = 300;
    public const int DefaultScale = 200;

    public static int Clamp(int scale) =>
        scale < MinScale ? MinScale : scale > MaxScale ? MaxScale : scale;

    /// <summary>
    /// Wartość z tabeli <c>settings</c> → skala. Zapisujemy liczbę całkowitą (kultura bez znaczenia),
    /// brak klucza albo śmieć = <see cref="DefaultScale"/>, wartość spoza zakresu przycinana.
    /// </summary>
    public static int Parse(string? raw) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? Clamp(v)
            : DefaultScale;

    /// <summary>Skala → wartość do tabeli <c>settings</c>.</summary>
    public static string ToSetting(int scale) => Clamp(scale).ToString(CultureInfo.InvariantCulture);

    /// <summary>Sufit rozmiaru czcionki refrenu: rozmiar bazowy × skala / 100.</summary>
    public static double CeilingFontSize(double baseFontSize, int scale) =>
        baseFontSize * Clamp(scale) / 100.0;

    /// <summary>
    /// Ostateczny rozmiar refrenu: sufit, chyba że tekst mieści się na jednym slajdzie dopiero
    /// w mniejszym rozmiarze (<paramref name="singleSlideFitSize"/> = największy rozmiar, w którym
    /// cały tekst wchodzi na jeden slajd).
    /// </summary>
    public static double FinalFontSize(double ceiling, double singleSlideFitSize) =>
        Math.Min(ceiling, singleSlideFitSize);
}
