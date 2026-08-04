using Cantio.Services.Devices;

namespace Cantio.Services;

/// <summary>
/// Decyzja, co zrobić z wynikiem odpytania urządzenia o stan zasilania.
/// Czysta logika (bez WPF i bez sieci), żeby dała się przetestować.
/// </summary>
/// <param name="State">Stan do ustawienia na urządzeniu.</param>
/// <param name="Failures">Nowy licznik kolejnych nieudanych odpytań.</param>
/// <param name="Changed">Czy stan faktycznie się zmienił (czy warto rozgłaszać pilotom).</param>
public readonly record struct DevicePollDecision(DevicePowerState State, int Failures, bool Changed);

/// <summary>
/// Reguła stosowania wyniku odpytania. Sedno: <c>Unknown</c> z odpytania W TLE nie może
/// od razu przestawić przycisku — jedna zgubiona odpowiedź w Wi-Fi zmieniałaby kolor
/// na oczach operatora w trakcie mszy. Dopiero DRUGI z rzędu brak odpowiedzi uznaje
/// urządzenie za nieosiągalne. Odpytanie jawne (przycisk, start, po akcji zasilania)
/// stosuje wynik natychmiast — użytkownik czeka na odpowiedź i ma dostać prawdę.
/// </summary>
public static class DevicePollPolicy
{
    /// <summary>Ile kolejnych braków odpowiedzi w tle przestawia urządzenie na <c>Unknown</c>.</summary>
    public const int FailuresBeforeUnknown = 2;

    public static DevicePollDecision Decide(
        DevicePowerState previous,
        DevicePowerState fresh,
        int consecutiveFailures,
        bool explicitPoll)
    {
        if (fresh != DevicePowerState.Unknown)
            return new DevicePollDecision(fresh, 0, fresh != previous);

        int failures = consecutiveFailures + 1;

        // W tle tolerujemy pierwszy brak odpowiedzi — zostaje ostatni znany stan.
        if (!explicitPoll && failures < FailuresBeforeUnknown)
            return new DevicePollDecision(previous, failures, false);

        return new DevicePollDecision(DevicePowerState.Unknown, failures,
            previous != DevicePowerState.Unknown);
    }
}
