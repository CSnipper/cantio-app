using Cantio.Services;

namespace Cantio.Models;

/// <param name="SetlistName">nazwa dnia = nazwa zestawu z „Przypnij tydzień”</param>
/// <param name="Group">klucz okresu ("zwykly", "adwent"…) → SeasonKey zestawu</param>
/// <param name="Cycle">"A"/"B"/"C" (niedziele), "I"/"II" (dni powszednie okresu zwykłego) lub ""</param>
/// <param name="Rank">
/// Ranga WŁASNA dnia: <see cref="Ranga.Brak"/> dla dnia temporalnego, uroczystość/święto dla
/// obchodów ruchomych (Boże Ciało, Chrystus Król…). Po to istnieje, żeby pierwszeństwo wobec
/// sanktorału (<see cref="DiocesanCalendarService.EffectiveSetlistName"/>) rozstrzygała RANGA,
/// a nie kolejność sprawdzeń — inaczej wspomnienie z kalendarza diecezji zabiera nazwę dnia
/// uroczystości.
/// </param>
public record LiturgicalDay(
    string SetlistName,
    string Group,
    string Cycle = "",
    Ranga Rank = Ranga.Brak
);
