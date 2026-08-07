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
    {
        var focus = s.Focus;
        bool typing = focus is DeskFocus.SearchBox or DeskFocus.SaveBox;

        switch (key)
        {
            // ── działa wszędzie, także w polu tekstowym ──────────────────────
            case DeskKey.F3:                    return DeskAction.OpenSearch;
            case DeskKey.F when ctrl:           return DeskAction.OpenSearch;
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
            case DeskKey.PageDown when ctrl && shift: return DeskAction.MoveSongDown;
            case DeskKey.PageUp   when ctrl && shift: return DeskAction.MoveSongUp;
            case DeskKey.PageDown:              return ctrl ? DeskAction.NextSong : DeskAction.ProjectNextSlide;
            case DeskKey.PageUp:                return ctrl ? DeskAction.PrevSong : DeskAction.ProjectPrevSlide;

            // Panele zestawu. W polu NAZWY zapisu Ctrl+S nie robi nic (panel już jest otwarty,
            // a Enter go zatwierdza) — reszta pulpitu otwiera je z każdego miejsca.
            case DeskKey.S when ctrl: return focus == DeskFocus.SaveBox ? DeskAction.None : DeskAction.OpenSavePanel;
            case DeskKey.O when ctrl: return focus == DeskFocus.SaveBox ? DeskAction.None : DeskAction.OpenSetlistPicker;

            case DeskKey.Enter:
                return focus switch
                {
                    DeskFocus.SearchBox      => DeskAction.RunSearch,
                    DeskFocus.SearchResults  => ctrl ? DeskAction.AddAndShow : DeskAction.AddToSetlist,
                    DeskFocus.SaveBox        => DeskAction.ConfirmSave,
                    DeskFocus.SetlistPicker  => DeskAction.LoadPickedSetlist,
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
                    DeskFocus.SetlistPicker => DeskAction.PickerUp,
                    _                       => DeskAction.ReadUp,
                };
            case DeskKey.Down:
                return focus switch
                {
                    DeskFocus.SearchBox     => s.HasResults ? DeskAction.FocusResults : DeskAction.None,
                    DeskFocus.SearchResults => DeskAction.SearchDown,
                    DeskFocus.SaveBox       => DeskAction.None,
                    DeskFocus.SetlistPicker => DeskAction.PickerDown,
                    _                       => DeskAction.ReadDown,
                };

            // ── klawisze, które w polu tekstowym należą do PISANIA ───────────
            case DeskKey.Home: return typing ? DeskAction.None : DeskAction.ReadStart;
            case DeskKey.End:  return typing ? DeskAction.None : DeskAction.ReadEnd;

            // Delete w polu tekstowym kasuje ZNAK — pulpit nie ma prawa go przechwycić.
            // Poza polem usuwa pieśń wyłącznie z listy slajdów: na liście wyników i na liście
            // zapisanych zestawów ten sam klawisz znaczyłby coś innego, a niewidomy operator
            // nie ma jak sprawdzić, gdzie stoi fokus.
            case DeskKey.Delete:
                return focus == DeskFocus.Slides ? DeskAction.RemoveSetlistSong : DeskAction.None;

            case DeskKey.L: return typing ? DeskAction.None : DeskAction.ReadSetlist;

            case DeskKey.R:
                if (typing) return DeskAction.None;
                return focus switch
                {
                    DeskFocus.SearchResults => DeskAction.RepeatSearchResult,
                    DeskFocus.SetlistPicker => DeskAction.RepeatPickerEntry,
                    _                       => DeskAction.RepeatReading,
                };

            case DeskKey.B: return typing ? DeskAction.None : DeskAction.ToggleBlank;
            case DeskKey.S: return typing ? DeskAction.None : DeskAction.AnnounceStatus;

            default: return DeskAction.None;
        }
    }
}
