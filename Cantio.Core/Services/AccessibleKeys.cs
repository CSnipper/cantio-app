namespace Cantio.Services;

/// <summary>Klawisz pulpitu niewidomego — bez zależności od WPF (okno tłumaczy <c>Key</c> na to).</summary>
public enum DeskKey
{
    Other, Up, Down, Home, End, Enter, PageUp, PageDown, Escape, F1, F2, F3, F, R, B, S,
    /// <summary>Usuwanie pozycji zestawu; w polu tekstowym należy do PISANIA (kasuje znak).</summary>
    Delete,
    /// <summary>Odczyt całego zestawu (litera L).</summary>
    L,
    /// <summary>Otwarcie zapisanego zestawu (Ctrl+O).</summary>
    O,
    /// <summary>
    /// Goły modyfikator (Ctrl/Shift/Alt) bez klawisza głównego. Wydzielony, bo NIE jest akcją
    /// operatora — inaczej samo przytrzymanie Ctrl gasiłoby uzbrojone potwierdzenie usunięcia.
    /// </summary>
    Modifier,
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
    /// <summary>Pole nazwy zapisywanego zestawu — jak <see cref="SearchBox"/>, litery należą do pola.</summary>
    SaveBox,
    /// <summary>Lista zapisanych zestawów w panelu otwierania.</summary>
    SetlistPicker,
    /// <summary>
    /// Pole FILTRA w panelu otwierania zestawu — jak <see cref="SearchBox"/>, litery należą do pola.
    /// Wydzielone z <see cref="SearchBox"/>, bo strzałka w dół ma tu zjechać na listę ZESTAWÓW,
    /// a nie na wyniki wyszukiwania pieśni.
    /// </summary>
    SetlistFilterBox,
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

    // ── zarządzanie zestawem (etap 2) ────────────────────────────────────────
    /// <summary>Przeczytaj cały zestaw po kolei (L).</summary>
    ReadSetlist,
    /// <summary>Delete — pierwszy raz pyta, drugi usuwa bieżącą pieśń zestawu.</summary>
    RemoveSetlistSong,
    /// <summary>Escape poza panelami — gasi uzbrojone potwierdzenie usunięcia (bez panelu milczy).</summary>
    CancelRemove,
    /// <summary>Ctrl+Shift+PageUp — bieżąca pieśń o jedno miejsce wyżej.</summary>
    MoveSongUp,
    /// <summary>Ctrl+Shift+PageDown — bieżąca pieśń o jedno miejsce niżej.</summary>
    MoveSongDown,
    /// <summary>Ctrl+S — panel zapisu zestawu (pole nazwy).</summary>
    OpenSavePanel,
    /// <summary>Enter w polu nazwy — zapisz.</summary>
    ConfirmSave,
    /// <summary>Ctrl+O — panel wyboru zapisanego zestawu.</summary>
    OpenSetlistPicker,
    PickerUp, PickerDown, RepeatPickerEntry,
    /// <summary>Enter na liście zestawów — wczytaj wybrany.</summary>
    LoadPickedSetlist,
    /// <summary>Escape w panelu zapisu/otwierania — zamknij, fokus wraca na listę slajdów.</summary>
    ClosePanel,

    // ── TRZECI kursor: pozycja w zestawie (etap 3) ───────────────────────────
    //
    // Powód istnienia: do v1.63 usuwanie i przenoszenie działały na pieśni BIEŻĄCEJ, a jedyna
    // droga do niej (Ctrl+Page Up/Down) ZMIENIA OBRAZ NA RZUTNIKU. Niewidomy operator, żeby
    // wyrzucić z zestawu czwartą pieśń, musiał przewinąć projekcję przez pół mszy na oczach
    // wiernych. Trzeci kursor chodzi po zestawie w ciszy — nie rusza ani projekcji, ani kursora
    // czytania — a jedynym mostem do rzutnika jest jawne Alt+Enter.

    /// <summary>Alt+↑ — kursor ZESTAWU o pozycję wyżej (projekcja bez zmian).</summary>
    SetlistCursorUp,
    /// <summary>Alt+↓ — kursor ZESTAWU o pozycję niżej (projekcja bez zmian).</summary>
    SetlistCursorDown,
    /// <summary>Alt+Home — pierwsza pozycja zestawu.</summary>
    SetlistCursorFirst,
    /// <summary>Alt+End — ostatnia pozycja zestawu.</summary>
    SetlistCursorLast,
    /// <summary>Alt+Enter — uczyń pieśń spod kursora zestawu bieżącą i wyświetl ją wiernym.</summary>
    ShowSetlistCursorSong,

    // ── panel otwierania: filtr i usuwanie zapisanych zestawów (etap 3) ──────

    /// <summary>↓ / Enter w polu filtra — zjedź na listę zestawów.</summary>
    FocusPicker,
    /// <summary>
    /// Delete na liście zapisanych zestawów — usuwa zestaw Z BAZY (dwa razy, jak przy pieśni,
    /// ale z WŁASNYM uzbrojeniem: to inna operacja i inne pytanie).
    /// </summary>
    DeleteSavedSetlist,
}

/// <summary>
/// Mapa klawiszy pulpitu niewidomego — JEDNO miejsce, w którym zapada decyzja, co robi klawisz.
///
/// Powód wydzielenia z okna: po dodaniu wyszukiwarki gołe litery (R powtórz, B wygaś, S stan)
/// zaczęły kolidować z PISANIEM zapytania. Reguła jest jedna i twarda — <b>gdy fokus stoi
/// w polu tekstowym, litery, Home, End i Delete należą do pola</b> (<see cref="DeskAction.None"/>),
/// a pulpit zatrzymuje sobie wyłącznie klawisze, których pole i tak nie używa: Escape, Enter,
/// funkcyjne oraz Page Up/Down (obraz dla wiernych musi dać się przewinąć nawet w środku pisania —
/// śpiew nie czeka, aż operator dokończy zapytanie).
///
/// Regułę da się tu sprawdzić testem bez uruchamiania WPF i bez symulowania klawiatury; w oknie
/// zostaje samo wykonanie akcji.
/// </summary>
public static class AccessibleKeys
{
    /// <summary>
    /// Stan pulpitu potrzebny do rozstrzygnięcia klawisza.
    /// <c>PanelOpen</c> = otwarty panel zapisu albo otwierania zestawu (Escape zamyka JEGO,
    /// a nie wyszukiwarkę).
    /// </summary>
    public readonly record struct DeskState(
        DeskFocus Focus,
        bool SearchOpen = false,
        bool HasResults = false,
        bool PanelOpen = false);

    public static DeskAction Route(DeskKey key, bool ctrl, DeskFocus focus, bool searchOpen, bool hasResults)
        => Route(key, ctrl, shift: false, new DeskState(focus, searchOpen, hasResults));

    public static DeskAction Route(DeskKey key, bool ctrl, bool shift, in DeskState s)
        => Route(key, ctrl, shift, alt: false, s);

    public static DeskAction Route(DeskKey key, bool ctrl, bool shift, bool alt, in DeskState s)
    {
        var focus = s.Focus;
        bool typing = focus is DeskFocus.SearchBox or DeskFocus.SaveBox or DeskFocus.SetlistFilterBox;

        // Prawy Alt (AltGr) = Ctrl+Alt jednocześnie — tak Windows generuje polskie znaki
        // (AltGr+o="ó", AltGr+s="ś" itd.). Bez tego rozróżnienia "Ctrl+O" łapało też AltGr+o
        // i pisanie "ó" w nazwie zestawu otwierało panel zamiast wstawić literę. Każdy skrót
        // wymagający Ctrl musi więc wymagać Ctrl BEZ Alt.
        bool ctrlOnly = ctrl && !alt;

        // ALT = kursor ZESTAWU, i to NIEZALEŻNIE od fokusu — dokładnie jak Page Up/Down.
        // Powód jest ten sam: pozycja w zestawie to druga rzecz (obok obrazu na rzutniku), którą
        // operator musi umieć ruszyć w środku pisania, nie szukając wprzódy drogi powrotnej z pola.
        // Klawisze są dobrane tak, żeby NIE kolidowały z pisaniem: jednowierszowe pole nie używa
        // ani strzałek, ani Home/End z Altem, ani Alt+Enter.
        if (alt)
        {
            switch (key)
            {
                case DeskKey.Up:    return DeskAction.SetlistCursorUp;
                case DeskKey.Down:  return DeskAction.SetlistCursorDown;
                case DeskKey.Home:  return DeskAction.SetlistCursorFirst;
                case DeskKey.End:   return DeskAction.SetlistCursorLast;
                case DeskKey.Enter: return DeskAction.ShowSetlistCursorSong;
                // Każdy inny klawisz z Altem leci dalej zwykłą drogą (nie ma tu menu okna,
                // więc Alt+litera niczego nie otwiera).
            }
        }

        switch (key)
        {
            // ── działa wszędzie, także w polu tekstowym ──────────────────────
            case DeskKey.F3:                    return DeskAction.OpenSearch;
            case DeskKey.F when ctrlOnly:       return DeskAction.OpenSearch;
            case DeskKey.F1:                    return DeskAction.AnnounceHelp;
            case DeskKey.F2:                    return DeskAction.AnnounceScreens;

            // Escape ma JEDNĄ kolejność pierwszeństwa: najpierw zamyka to, co zasłania pulpit
            // (panel zapisu/otwierania), potem wyszukiwarkę, a gdy nic nie jest otwarte — gasi
            // uzbrojone potwierdzenie usunięcia (gdy nie ma czego gasić, pulpit milczy).
            case DeskKey.Escape:
                return s.PanelOpen  ? DeskAction.ClosePanel
                     : s.SearchOpen ? DeskAction.CloseSearch
                     : DeskAction.CancelRemove;

            // Ctrl+Shift+Page = przenoszenie pieśni w zestawie. MUSI być sprawdzone przed
            // samym Ctrl+Page (zmiana pieśni), inaczej przenoszenie nigdy by nie zadziałało.
            case DeskKey.PageDown when ctrlOnly && shift: return DeskAction.MoveSongDown;
            case DeskKey.PageUp   when ctrlOnly && shift: return DeskAction.MoveSongUp;
            case DeskKey.PageDown:              return ctrlOnly ? DeskAction.NextSong : DeskAction.ProjectNextSlide;
            case DeskKey.PageUp:                return ctrlOnly ? DeskAction.PrevSong : DeskAction.ProjectPrevSlide;

            // Panele zestawu. W polu NAZWY zapisu Ctrl+S nie robi nic (panel już jest otwarty,
            // a Enter go zatwierdza) — reszta pulpitu otwiera je z każdego miejsca.
            case DeskKey.S when ctrlOnly: return focus == DeskFocus.SaveBox ? DeskAction.None : DeskAction.OpenSavePanel;
            // Ctrl+O w polu FILTRA nic nie robi — panel otwierania już jest otwarty, a ponowne
            // otwarcie skasowałoby wpisany filtr (czego niewidomy operator nie miałby jak zauważyć).
            case DeskKey.O when ctrlOnly:
                return focus is DeskFocus.SaveBox or DeskFocus.SetlistFilterBox
                    ? DeskAction.None : DeskAction.OpenSetlistPicker;

            case DeskKey.Enter:
                return focus switch
                {
                    DeskFocus.SearchBox      => DeskAction.RunSearch,
                    DeskFocus.SearchResults  => ctrlOnly ? DeskAction.AddAndShow : DeskAction.AddToSetlist,
                    DeskFocus.SaveBox        => DeskAction.ConfirmSave,
                    DeskFocus.SetlistPicker  => DeskAction.LoadPickedSetlist,
                    // Enter w polu filtra ZJEŻDŻA NA LISTĘ, a nie wczytuje pierwszego trafienia.
                    // Filtrowanie jest na bieżąco i nic go nie ogłasza pozycja po pozycji, więc
                    // operator w chwili naciśnięcia Enter zna tylko LICZBĘ trafień — wczytanie
                    // „czegoś pierwszego z listy” byłoby wtedy zgadywaniem na oczach wiernych.
                    DeskFocus.SetlistFilterBox => DeskAction.FocusPicker,
                    _                        => DeskAction.ProjectReadingSlide,
                };

            // Strzałki w polu: jednowierszowe pole i tak ich nie używa, więc zamiast zginąć
            // przenoszą na wyniki (droga „wpisz i zjedź w dół” bez szukania klawisza Tab).
            case DeskKey.Up:
                return focus switch
                {
                    DeskFocus.SearchBox     => s.HasResults ? DeskAction.FocusResults : DeskAction.None,
                    DeskFocus.SearchResults => DeskAction.SearchUp,
                    DeskFocus.SaveBox       => DeskAction.None,
                    DeskFocus.SetlistFilterBox => DeskAction.None, // nad polem filtra nic nie ma
                    DeskFocus.SetlistPicker => DeskAction.PickerUp,
                    _                       => DeskAction.ReadUp,
                };
            case DeskKey.Down:
                return focus switch
                {
                    DeskFocus.SearchBox     => s.HasResults ? DeskAction.FocusResults : DeskAction.None,
                    DeskFocus.SearchResults => DeskAction.SearchDown,
                    DeskFocus.SaveBox       => DeskAction.None,
                    DeskFocus.SetlistFilterBox => DeskAction.FocusPicker,
                    DeskFocus.SetlistPicker => DeskAction.PickerDown,
                    _                       => DeskAction.ReadDown,
                };

            // ── klawisze, które w polu tekstowym należą do PISANIA ───────────
            case DeskKey.Home: return typing ? DeskAction.None : DeskAction.ReadStart;
            case DeskKey.End:  return typing ? DeskAction.None : DeskAction.ReadEnd;

            // Delete w polu tekstowym kasuje ZNAK — pulpit nie ma prawa go przechwycić.
            // Poza polem Delete znaczy „usuń to, na czym stoisz”, i są to DWIE RÓŻNE operacje:
            // na liście slajdów — pieśń spod kursora ZESTAWU, na liście zapisanych zestawów —
            // cały zestaw z bazy. Rozróżnia je wyłącznie fokus, więc każda ma własne pytanie
            // i własne uzbrojenie (zob. DeleteConfirmation). Na liście WYNIKÓW wyszukiwania
            // Delete dalej nie znaczy nic — nie ma tam czego usuwać.
            case DeskKey.Delete:
                return focus switch
                {
                    DeskFocus.Slides        => DeskAction.RemoveSetlistSong,
                    DeskFocus.SetlistPicker => DeskAction.DeleteSavedSetlist,
                    _                       => DeskAction.None,
                };

            // Gołe litery L/R/B/S są akcją WYŁĄCZNIE bez żadnego modyfikatora — z Ctrl, Alt
            // lub AltGr (Ctrl+Alt) mają milczeć, żeby AltGr+s/AltGr+l (polskie "ś"/"ł") trafiały
            // do pola jako znak, a nie odpalały akcję pulpitu.
            case DeskKey.L: return (typing || ctrl || alt) ? DeskAction.None : DeskAction.ReadSetlist;

            case DeskKey.R:
                if (typing || ctrl || alt) return DeskAction.None;
                return focus switch
                {
                    DeskFocus.SearchResults => DeskAction.RepeatSearchResult,
                    DeskFocus.SetlistPicker => DeskAction.RepeatPickerEntry,
                    _                       => DeskAction.RepeatReading,
                };

            case DeskKey.B: return (typing || ctrl || alt) ? DeskAction.None : DeskAction.ToggleBlank;
            case DeskKey.S: return (typing || ctrl || alt) ? DeskAction.None : DeskAction.AnnounceStatus;

            default: return DeskAction.None;
        }
    }
}
