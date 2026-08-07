namespace Cantio.Services;

/// <summary>Klawisz pulpitu niewidomego — bez zależności od WPF (okno tłumaczy <c>Key</c> na to).</summary>
public enum DeskKey
{
    Other, Up, Down, Home, End, Enter, PageUp, PageDown, Escape, F1, F2, F3, F, R, B, S,
}

/// <summary>Gdzie stoi fokus klawiatury. To ONO, a nie flaga trybu, rozstrzyga los gołych liter.</summary>
public enum DeskFocus
{
    /// <summary>Lista slajdów bieżącej pieśni (stan domyślny pulpitu).</summary>
    Slides,
    /// <summary>Pole wpisywania zapytania — tu każda litera należy do pola.</summary>
    SearchBox,
    /// <summary>Lista wyników wyszukiwania.</summary>
    SearchResults,
}

/// <summary>Co pulpit ma zrobić. <see cref="None"/> = klawisz nieobsłużony, idzie dalej (np. do pola).</summary>
public enum DeskAction
{
    None,
    OpenSearch, CloseSearch, RunSearch, FocusResults, AddToSetlist, AddAndShow,
    SearchUp, SearchDown, RepeatSearchResult,
    ReadUp, ReadDown, ReadStart, ReadEnd, RepeatReading, ProjectReadingSlide,
    ProjectNextSlide, ProjectPrevSlide, NextSong, PrevSong,
    ToggleBlank, AnnounceStatus, AnnounceScreens, AnnounceHelp,
}

/// <summary>
/// Mapa klawiszy pulpitu niewidomego — JEDNO miejsce, w którym zapada decyzja, co robi klawisz.
///
/// Powód wydzielenia z okna: po dodaniu wyszukiwarki gołe litery (R powtórz, B wygaś, S stan)
/// zaczęły kolidować z PISANIEM zapytania. Reguła jest jedna i twarda — <b>gdy fokus stoi
/// w polu tekstowym, litery, Home i End należą do pola</b> (<see cref="DeskAction.None"/>),
/// a pulpit zatrzymuje sobie wyłącznie klawisze, których pole i tak nie używa: Escape, Enter,
/// funkcyjne oraz Page Up/Down (obraz dla wiernych musi dać się przewinąć nawet w środku pisania —
/// śpiew nie czeka, aż operator dokończy zapytanie).
///
/// Regułę da się tu sprawdzić testem bez uruchamiania WPF i bez symulowania klawiatury; w oknie
/// zostaje samo wykonanie akcji.
/// </summary>
public static class AccessibleKeys
{
    public static DeskAction Route(DeskKey key, bool ctrl, DeskFocus focus, bool searchOpen, bool hasResults)
    {
        bool typing = focus == DeskFocus.SearchBox;

        switch (key)
        {
            // ── działa wszędzie, także w polu tekstowym ──────────────────────
            case DeskKey.F3:                    return DeskAction.OpenSearch;
            case DeskKey.F when ctrl:           return DeskAction.OpenSearch;
            case DeskKey.Escape:                return searchOpen ? DeskAction.CloseSearch : DeskAction.None;
            case DeskKey.PageDown:              return ctrl ? DeskAction.NextSong : DeskAction.ProjectNextSlide;
            case DeskKey.PageUp:                return ctrl ? DeskAction.PrevSong : DeskAction.ProjectPrevSlide;
            case DeskKey.F1:                    return DeskAction.AnnounceHelp;
            case DeskKey.F2:                    return DeskAction.AnnounceScreens;

            case DeskKey.Enter:
                return focus switch
                {
                    DeskFocus.SearchBox     => DeskAction.RunSearch,
                    DeskFocus.SearchResults => ctrl ? DeskAction.AddAndShow : DeskAction.AddToSetlist,
                    _                       => DeskAction.ProjectReadingSlide,
                };

            // Strzałki w polu: jednowierszowe pole i tak ich nie używa, więc zamiast zginąć
            // przenoszą na wyniki (droga „wpisz i zjedź w dół” bez szukania klawisza Tab).
            case DeskKey.Up:
                return focus switch
                {
                    DeskFocus.SearchBox     => hasResults ? DeskAction.FocusResults : DeskAction.None,
                    DeskFocus.SearchResults => DeskAction.SearchUp,
                    _                       => DeskAction.ReadUp,
                };
            case DeskKey.Down:
                return focus switch
                {
                    DeskFocus.SearchBox     => hasResults ? DeskAction.FocusResults : DeskAction.None,
                    DeskFocus.SearchResults => DeskAction.SearchDown,
                    _                       => DeskAction.ReadDown,
                };

            // ── klawisze, które w polu tekstowym należą do PISANIA ───────────
            case DeskKey.Home: return typing ? DeskAction.None : DeskAction.ReadStart;
            case DeskKey.End:  return typing ? DeskAction.None : DeskAction.ReadEnd;

            case DeskKey.R:
                if (typing) return DeskAction.None;
                return focus == DeskFocus.SearchResults
                    ? DeskAction.RepeatSearchResult : DeskAction.RepeatReading;

            case DeskKey.B: return typing ? DeskAction.None : DeskAction.ToggleBlank;
            case DeskKey.S: return typing ? DeskAction.None : DeskAction.AnnounceStatus;

            default: return DeskAction.None;
        }
    }
}
