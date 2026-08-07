namespace Cantio.Services;

/// <summary>Prostokąt ekranu w układzie pulpitu Windows (bez zależności od WPF).</summary>
public readonly record struct ScreenBox(double X, double Y, double Width, double Height);

public enum ScreenSetup
{
    /// <summary>Jeden ekran — rzutnik odłączony ALBO Windows powiela obraz (tryb „Duplikuj").</summary>
    SingleScreen,
    /// <summary>Dwa ekrany stoją w tym samym miejscu pulpitu — obraz jest powielony.</summary>
    Mirrored,
    /// <summary>Pulpit rozszerzony — projekcja ma własny ekran.</summary>
    Extended,
}

/// <summary>
/// Wykrywanie powielania ekranu (mirroring). Dla niewidomego operatora to nie kosmetyka:
/// przy powielaniu wierni widzą jego pulpit — okna, komunikaty, wszystko — a on sam nie ma
/// jak tego zauważyć. Dlatego pulpit ogłasza stan ekranów przy każdym starcie.
///
/// Windows w trybie „Duplikuj" raportuje zwykle JEDEN monitor, więc <see cref="ScreenSetup.SingleScreen"/>
/// i tak wymaga ostrzeżenia (nie da się odróżnić „rzutnik powiela" od „rzutnik odłączony",
/// a obie sytuacje kończą się brakiem projekcji tam, gdzie operator jej oczekuje).
/// Część sterowników pokazuje jednak dwa monitory nałożone na siebie — to <see cref="ScreenSetup.Mirrored"/>.
/// </summary>
public static class ScreenTopology
{
    /// <summary>Tolerancja w pikselach na porównanie położenia/rozmiaru ekranów.</summary>
    private const double Eps = 2.0;

    public static ScreenSetup Evaluate(IReadOnlyList<ScreenBox> screens)
    {
        if (screens is null || screens.Count <= 1) return ScreenSetup.SingleScreen;

        for (int i = 0; i < screens.Count; i++)
            for (int j = i + 1; j < screens.Count; j++)
                if (SameOrigin(screens[i], screens[j])) return ScreenSetup.Mirrored;

        return ScreenSetup.Extended;
    }

    /// <summary>Czy operator ma powód sądzić, że wierni widzą jego pulpit.</summary>
    public static bool IsRisky(ScreenSetup setup) => setup != ScreenSetup.Extended;

    private static bool SameOrigin(ScreenBox a, ScreenBox b)
        => Math.Abs(a.X - b.X) < Eps && Math.Abs(a.Y - b.Y) < Eps
        && Math.Abs(a.Width - b.Width) < Eps && Math.Abs(a.Height - b.Height) < Eps;
}
