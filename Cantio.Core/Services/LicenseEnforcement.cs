namespace Cantio.Services;

/// <summary>
/// JEDNO miejsce decyzji: czy brak licencji ma cokolwiek zablokować.
///
/// Dziś <see cref="RequireLicense"/> = <c>false</c> — pulpit działa bez klucza, ale przy każdym
/// starcie mówi „Wersja niezarejestrowana". Powód: testerzy dostają program ZANIM ustalona
/// zostanie cena, a pulpit, który zamyka się niewidomemu organiście w niedzielę rano, byłby
/// gorszy niż brak zabezpieczenia.
///
/// Żeby włączyć blokadę, wystarczy zmienić tę JEDNĄ stałą. Reguła jest tu wydzielona jako czysta
/// funkcja <see cref="Decide"/>, żeby zachowanie po włączeniu dało się sprawdzić testem już dziś —
/// inaczej pierwszym sprawdzeniem blokady byłby dzień, w którym zostanie sprzedana.
/// </summary>
public static class LicenseEnforcement
{
    /// <summary>Przełącznik autora. <c>true</c> = brak ważnego klucza blokuje pulpit.</summary>
    public const bool RequireLicense = false;

    /// <summary>Czysta reguła — testowalna niezależnie od stanu przełącznika.</summary>
    public static bool Decide(bool requireLicense, bool hasValidLicense)
        => requireLicense && !hasValidLicense;

    /// <summary>Czy pulpit ma być zablokowany przy obecnym ustawieniu autora.</summary>
    public static bool IsBlocked(bool hasValidLicense) => Decide(RequireLicense, hasValidLicense);
}
