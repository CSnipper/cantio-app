namespace Cantio.Services;

/// <summary>
/// Zmiana „na próbę" — wzorzec ze zmiany rozdzielczości w Windows. Dwa przedmioty próby:
/// <b>ekran projekcji</b> i <b>port serwera pilota</b>.
///
/// Po co: zdalne przestawienie projekcji na inne wyjście HDMI może trafić w monitor,
/// którego nikt nie ogląda (albo w nieistniejące w praktyce wyjście). Na mini PC w zakrystii
/// nie ma komu tego cofnąć, więc zmiana wchodzi od razu, ale sama się wycofuje, jeśli tablet
/// nie potwierdzi, że obraz jest tam, gdzie miał być.
///
/// <para>Klasa jest CZYSTA: nie zna zegara systemowego ani bazy — czas przychodzi z zewnątrz,
/// a przywrócenie wykonuje gospodarz. Dzięki temu cała reguła („potwierdzenie zatrzymuje
/// cofnięcie, brak potwierdzenia przywraca poprzednią wartość") jest testowalna bez czekania
/// 20 sekund.</para>
///
/// <para><b>Druga zmiana w trakcie odliczania</b> nie nadpisuje zapamiętanej wartości —
/// zostaje ta SPRZED pierwszej zdalnej zmiany, odświeżany jest tylko termin. Inaczej
/// dwa kliknięcia na tablecie („nie ten ekran… ten też nie") zostawiłyby parafię na ekranie
/// pośrednim, do którego nikt nie chciał wrócić.</para>
///
/// <para><b>Etap 3 (2026-09-14): port serwera pilota.</b> Ten sam bezpiecznik, ale z dwiema
/// różnicami. (1) Termin jest DŁUŻSZY (<see cref="PortSeconds"/>), bo zmiana portu rozłącza
/// wszystkie tablety i potwierdzenie przyjdzie dopiero po ponownym połączeniu i uwierzytelnieniu.
/// (2) Potwierdzenie przychodzi INNYM GNIAZDEM niż komenda — dlatego stan próby żyje w jednym
/// obiekcie po stronie serwera i <b>nie wolno go czyścić przy rozłączeniu klienta</b>; gdyby
/// sprzątanie po rozłączeniu (jak przy porzuconych wysyłkach obrazków) objęło ten stan,
/// bezpiecznik portu rozbroiłby się dokładnie w chwili, w której jest potrzebny.</para>
///
/// <para>Oba przedmioty mają WŁASNY termin i własną zapamiętaną wartość, ale JEDNĄ regułę
/// (<see cref="Subject"/>) — druga kopia maszyny stanu to układ, który w tym projekcie już
/// raz gubił dane.</para>
/// </summary>
public sealed class SystemSettingsTrial
{
    /// <summary>Ile sekund desktop czeka na potwierdzenie zmiany EKRANU (pole `trialSeconds`).</summary>
    public const int DefaultSeconds = 20;

    /// <summary>
    /// Ile sekund desktop czeka na potwierdzenie zmiany PORTU. Dłużej niż przy ekranie, bo
    /// tablet musi zdążyć zauważyć zerwane połączenie, znaleźć serwer na nowym porcie,
    /// uwierzytelnić się i dopiero wtedy wysłać potwierdzenie.
    /// </summary>
    public const int PortSeconds = 60;

    /// <summary>Jeden przedmiot próby: zapamiętana wartość + termin. Reguła jest tu RAZ.</summary>
    private sealed class Subject
    {
        private int _previous;
        private DateTime _deadlineUtc;
        private bool _armed;

        public int? Previous => _armed ? _previous : null;

        public bool IsPending(DateTime nowUtc) => _armed && nowUtc < _deadlineUtc;

        public void Start(int previous, DateTime nowUtc, int seconds)
        {
            if (!_armed) _previous = previous;   // liczy się stan SPRZED pierwszej zdalnej zmiany
            _deadlineUtc = nowUtc.AddSeconds(seconds);
            _armed = true;
        }

        public bool Confirm(DateTime nowUtc)
        {
            if (!IsPending(nowUtc)) return false;
            _armed = false;
            return true;
        }

        public int? Revert()
        {
            if (!_armed) return null;
            _armed = false;
            return _previous;
        }

        public int? Expire(DateTime nowUtc)
        {
            if (!_armed || nowUtc < _deadlineUtc) return null;
            _armed = false;
            return _previous;
        }
    }

    private readonly Subject _screen = new();
    private readonly Subject _port   = new();

    /// <summary>Ekran, na który wrócimy przy braku potwierdzenia (tylko gdy odliczanie trwa).</summary>
    public int? PreviousScreen => _screen.Previous;

    /// <summary>Port, na który wrócimy przy braku potwierdzenia (tylko gdy odliczanie trwa).</summary>
    public int? PreviousPort => _port.Previous;

    /// <summary>Czy odliczanie KTÓREJKOLWIEK próby trwa w chwili <paramref name="nowUtc"/>.</summary>
    public bool IsPending(DateTime nowUtc) => _screen.IsPending(nowUtc) || _port.IsPending(nowUtc);

    /// <summary>Czy trwa odliczanie próby EKRANU.</summary>
    public bool IsScreenPending(DateTime nowUtc) => _screen.IsPending(nowUtc);

    /// <summary>Czy trwa odliczanie próby PORTU.</summary>
    public bool IsPortPending(DateTime nowUtc) => _port.IsPending(nowUtc);

    /// <summary>Uruchamia (albo przedłuża) próbę EKRANU; <paramref name="previousScreen"/> liczy się tylko przy pierwszej.</summary>
    public void Start(int previousScreen, DateTime nowUtc, int seconds = DefaultSeconds)
        => _screen.Start(previousScreen, nowUtc, seconds);

    /// <summary>Uruchamia (albo przedłuża) próbę PORTU; <paramref name="previousPort"/> liczy się tylko przy pierwszej.</summary>
    public void StartPort(int previousPort, DateTime nowUtc, int seconds = PortSeconds)
        => _port.Start(previousPort, nowUtc, seconds);

    /// <summary>
    /// Potwierdzenie z tabletu — dotyczy WSZYSTKICH trwających prób (tablet, który przysłał
    /// potwierdzenie, właśnie udowodnił i że widzi obraz, i że dobił się na nowy port).
    /// <c>true</c> = było co potwierdzać i zdążyło; <c>false</c> = odliczania nie było albo
    /// już minęło (wtedy cofnięcie ZOSTAJE uzbrojone — rozstrzyga <see cref="Expire"/>, żeby
    /// spóźnione „potwierdzam" nie zabetonowało ekranu, którego i tak nikt nie widzi).
    /// </summary>
    public bool Confirm(DateTime nowUtc)
    {
        // Bez skrótu ||: obie próby muszą zostać rozbrojone, nie tylko pierwsza pasująca.
        bool screen = _screen.Confirm(nowUtc);
        bool port   = _port.Confirm(nowUtc);
        return screen || port;
    }

    /// <summary>
    /// Natychmiastowe cofnięcie EKRANU na żądanie tabletu („cofnij" w oknie próbnym).
    /// W odróżnieniu od <see cref="Expire"/> NIE czeka na termin — operator patrzy na ekran,
    /// którego nie widzi, i nie ma powodu trzymać go tam do końca odliczania.
    /// Zwraca ekran do przywrócenia; <c>null</c> = nie było trwającej próby. Czasu świadomie
    /// nie przyjmuje — spóźniony „cofnij" (po terminie, zanim zadziałał budzik) ma cofnąć
    /// dokładnie tak samo.
    /// </summary>
    public int? Revert() => _screen.Revert();

    /// <summary>Natychmiastowe cofnięcie PORTU — jak <see cref="Revert"/>, tylko drugi przedmiot próby.</summary>
    public int? RevertPort() => _port.Revert();

    /// <summary>
    /// Wygaszenie odliczania EKRANU. Zwraca ekran DO PRZYWRÓCENIA, gdy termin minął bez
    /// potwierdzenia; <c>null</c> = nie ma czego cofać (potwierdzono albo termin jeszcze trwa —
    /// tak wygląda spóźniony budzik po przedłużeniu odliczania drugą zmianą).
    /// </summary>
    public int? Expire(DateTime nowUtc) => _screen.Expire(nowUtc);

    /// <summary>Wygaszenie odliczania PORTU — jak <see cref="Expire"/>, tylko drugi przedmiot próby.</summary>
    public int? ExpirePort(DateTime nowUtc) => _port.Expire(nowUtc);
}
