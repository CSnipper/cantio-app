namespace Cantio.Services;

/// <summary>
/// Szablony komunikatów LICENCJI na pulpicie niewidomego — ten sam wzorzec co
/// <see cref="SetlistText"/>: rdzeń nie zna <c>LocalizationManager</c>, więc teksty wstrzykuje
/// warstwa okienkowa (klucze <c>Acc.License*</c>), a wartości domyślne są polskie.
/// </summary>
public sealed record LicenseText
{
    /// <summary>{0} = imię i nazwisko z klucza. Ogłaszane przy KAŻDYM starcie pulpitu.</summary>
    public string Registered { get; init; } = "Licencja: {0}";

    /// <summary>Brak klucza albo klucz odrzucony — pulpit przedstawia się przy każdym starcie.</summary>
    public string Unregistered { get; init; } = "Wersja niezarejestrowana";

    public string PanelOpened { get; init; } =
        "Klucz licencyjny. Wklej klucz skrótem Ctrl plus V i naciśnij Enter. Escape anuluje.";
    public string PanelClosed { get; init; } = "Wpisywanie klucza anulowane";

    /// <summary>{0} = imię i nazwisko. Potwierdzenie udanej rejestracji.</summary>
    public string Accepted { get; init; } = "Licencja zarejestrowana na: {0}";

    public string Invalid { get; init; } = "Klucz nieprawidłowy";
    public string Empty { get; init; } = "Nie wklejono klucza";

    /// <summary>Klucz poprawnie podpisany, ale wystawiony na INNY produkt.</summary>
    public string WrongProduct { get; init; } = "Klucz nie jest przeznaczony dla tego programu";
}

/// <summary>
/// Reguły licencji pulpitu niewidomego — czysta warstwa nad <see cref="LicenseKey"/>.
///
/// Wydzielone z ViewModelu, bo to JEDYNE miejsce, w którym rozstrzyga się, co usłyszy operator
/// po wklejeniu klucza. Test może tu sprawdzić każdą odpowiedź bez uruchamiania WPF, a okno
/// zostaje z samym wykonaniem (zapis do bazy, fokus).
/// </summary>
public static class AccessibleLicense
{
    /// <summary>Wynik próby zarejestrowania klucza. Nazwisko jest wypełnione tylko przy <see cref="Ok"/>.</summary>
    public enum Result { Ok, Empty, Invalid, WrongProduct }

    /// <summary>
    /// Sprawdza wklejony tekst. Klucz podpisany, ale na inny produkt, dostaje WŁASNĄ odpowiedź:
    /// „nieprawidłowy" kazałoby użytkownikowi szukać błędu w przepisaniu, a błąd jest w tym,
    /// że dostał nie ten klucz.
    /// </summary>
    public static Result Validate(string? text, out LicenseInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(text)) return Result.Empty;
        if (!LicenseKey.TryParse(text, out info)) return Result.Invalid;
        return info.Product == LicenseKey.ProductAccessibleDesk ? Result.Ok : Result.WrongProduct;
    }

    /// <summary>Komunikat po próbie rejestracji.</summary>
    public static string DescribeResult(Result result, LicenseInfo info, LicenseText t) => result switch
    {
        Result.Ok           => string.Format(t.Accepted, info.Name),
        Result.Empty        => t.Empty,
        Result.WrongProduct => t.WrongProduct,
        _                   => t.Invalid,
    };

    /// <summary>
    /// Jak pulpit przedstawia się przy starcie (i w pomocy F1). Klucz zapisany w bazie, który
    /// przestał przechodzić weryfikację (uszkodzony wiersz, klucz z innej pary), daje dokładnie
    /// to samo co brak klucza — „niezarejestrowana" jest stanem, nie zarzutem.
    /// </summary>
    public static string DescribeStored(string? storedKey, LicenseText t)
        => Validate(storedKey, out var info) == Result.Ok
            ? string.Format(t.Registered, info.Name)
            : t.Unregistered;

    /// <summary>Czy zapisany klucz jest ważny dla TEGO produktu (wejście dla <see cref="LicenseEnforcement"/>).</summary>
    public static bool IsRegistered(string? storedKey) => Validate(storedKey, out _) == Result.Ok;
}
