namespace WinMac.Connect;

/// <summary>Fase de la llamada del iPhone tal como la deja ver ANCS (solo avisos: WinMac no controla la llamada).</summary>
public enum CallPhase { None, Ringing, Missed, Ended, Active }   // Active/Ended solo con una fuente de telefonía fiable (nunca desde ANCS)

/// <param name="Caller">Nombre o número que da el iPhone (título de la notificación de llamada); null si aún no llegó o por privacidad.</param>
public sealed record CallState(CallPhase Phase, string? Caller, uint Uid, long Session, DateTimeOffset Since)
{
    public static readonly CallState Idle = new(CallPhase.None, null, 0, 0, DateTimeOffset.MinValue);
}

/// <summary>
/// WM-038: estado de la llamada entrante del iPhone a partir de ANCS, con reconciliación. ANCS no informa del estado telefónico: solo publica una notificación
/// «Llamada entrante» mientras suena y la retira al contestar, rechazar o colgar quien llama; si nadie contesta, aparece «Llamada perdida». Por eso:
/// <list type="bullet">
/// <item>Sonando: desde que llega la notificación de llamada entrante.</item>
/// <item>Al retirarse: si en <see cref="MissedGrace"/> aparece una perdida → «perdida»; si no → se CIERRA EN NEUTRO (vuelve a «ninguna»). Retirar la notificación es lo que hace iOS al
/// contestar, rechazar o colgar quien llama, y ANCS no distingue entre ellos: <b>nunca</b> se muestra «terminada» (la llamada contestada sigue activa en el teléfono; evidencia real, docs/56).</item>
/// <item>Seguridad: más de <see cref="RingTimeout"/> sonando sin noticias → cierre neutro; al perder la conexión Bluetooth → se borra (nunca queda fijada).</item>
/// <item>Perdida se muestra <see cref="EndedShow"/> y desaparece. <see cref="CallPhase.Ended"/> se conserva solo por compatibilidad del cable: ya no se produce (solo una fuente fiable, p. ej. HFP, podrá darlo).</item>
/// </list>
/// Puro: el módulo le pasa eventos y lo hace avanzar con <see cref="Tick"/>.
/// </summary>
public sealed class CallTracker(Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(90), MissedGrace = TimeSpan.FromSeconds(4), EndedShow = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    public static readonly TimeSpan LateMissed = TimeSpan.FromSeconds(10);
    private DateTimeOffset? _removedAt, _closedAt; private long _closedSession; private string? _closedCaller;
    public CallState State { get; private set; } = CallState.Idle;

    /// <summary>Aplica un aviso de ANCS. Devuelve true si el estado visible cambió.</summary>
    public bool OnEvent(AncsEvent e, long session)
    {
        var t = _now();
        switch (e.Event, e.Category)
        {
            case (AncsEventId.Added, AncsCategory.IncomingCall) when State.Phase == CallPhase.Ringing && e.Uid == State.Uid && session == State.Session:
                _removedAt = null; return false;   // duplicado de la misma llamada: conserva a quien llama, no reinicia nada
            case (AncsEventId.Added, AncsCategory.IncomingCall) when State.Phase == CallPhase.Active: return false;   // llamada activa confirmada: un aviso ANCS no la sustituye
            case (AncsEventId.Added, AncsCategory.IncomingCall):   // también si ya sonaba al conectar (PreExisting): está sonando ahora
                _removedAt = null; _closedAt = null; return Set(new CallState(CallPhase.Ringing, null, e.Uid, session, t));
            case (AncsEventId.Removed, _) when State.Phase == CallPhase.Ringing && e.Uid == State.Uid && session == State.Session:
                _removedAt = t; return false;   // se decide en Tick: ¿llega una perdida?
            case (AncsEventId.Added, AncsCategory.MissedCall) when !e.PreExisting && State.Phase == CallPhase.Ringing:
                _removedAt = null; return Set(State with { Phase = CallPhase.Missed, Since = t });
            case (AncsEventId.Added, AncsCategory.MissedCall) when !e.PreExisting && State.Phase == CallPhase.None && _closedAt is { } c && t - c <= LateMissed && session == _closedSession:
                var who = _closedCaller; _closedAt = null; return Set(new CallState(CallPhase.Missed, who, e.Uid, session, t));   // la perdida llegó justo después del cierre neutro
        }
        return false;
    }

    /// <summary>
    /// Fuente de telefonía FIABLE (p. ej. el estado de llamada del sistema, no ANCS): hay (o ya no hay) una llamada en curso.
    /// Activa → fase <see cref="CallPhase.Active"/> solo si este iPhone sonaba o acababa de cerrarse el aviso (correlación) (conserva a quien llama; <see cref="CallState.Since"/> = inicio, para la duración).
    /// Deja de estar activa → <see cref="CallPhase.Ended"/>, y solo si veníamos de Active (única forma legítima de decir «terminada»).
    /// </summary>
    public bool OnTelephony(bool active, uint uid = 0)
    {
        var t = _now();
        if (active)
        {
            if (State.Phase == CallPhase.Active) return false;
            // Correlación: el estado del sistema no dice de qué teléfono es (también cuenta Teams, etc.). Solo se acepta si ESTE iPhone acaba de sonar.
            bool correlated = State.Phase == CallPhase.Ringing || _closedAt is { } cc && t - cc <= LateMissed;
            if (!correlated) return false;
            _removedAt = null; _closedAt = null;
            return Set(new CallState(CallPhase.Active, State.Phase == CallPhase.Ringing ? State.Caller : (_closedAt is { } ca && t - ca <= LateMissed ? _closedCaller : null), State.Phase == CallPhase.Ringing ? State.Uid : uid, State.Session, t));
        }
        return State.Phase == CallPhase.Active && Set(State with { Phase = CallPhase.Ended, Since = t });
    }

    /// <summary>Duración de una llamada activa confirmada; null si no lo está.</summary>
    public TimeSpan? ActiveDuration => State.Phase == CallPhase.Active ? _now() - State.Since : null;

    /// <summary>Llegó el título de una notificación: si es la llamada que suena, es quien llama.</summary>
    public bool OnCaller(uint uid, long session, string? title)
    {
        if (State.Phase != CallPhase.Ringing || State.Uid != uid || State.Session != session || string.IsNullOrWhiteSpace(title) || title == State.Caller) return false;
        return Set(State with { Caller = title.Trim() });
    }

    /// <summary>Avanza el tiempo. Devuelve true si el estado visible cambió.</summary>
    public bool Tick()
    {
        var t = _now();
        switch (State.Phase)
        {
            case CallPhase.Ringing when _removedAt is { } r && t - r >= MissedGrace: return CloseNeutral(t);
            case CallPhase.Ringing when t - State.Since >= RingTimeout: return CloseNeutral(t);
            case CallPhase.Missed or CallPhase.Ended when t - State.Since >= EndedShow: return Set(CallState.Idle);
        }
        return false;
    }

    /// <summary>Se perdió la conexión o se apagó la función: nada de llamadas «fantasma».</summary>
    public bool Reset() { _removedAt = null; _closedAt = null; return Set(CallState.Idle); }

    /// <summary>Cierre sin afirmar nada (ni contestada, ni rechazada, ni terminada).</summary>
    private bool CloseNeutral(DateTimeOffset t)
    {
        _removedAt = null; _closedAt = t; _closedSession = State.Session; _closedCaller = State.Caller;
        return Set(CallState.Idle);
    }

    public bool NeedsTicks => State.Phase is CallPhase.Ringing or CallPhase.Missed or CallPhase.Ended;   // Active no caduca: la duración la pinta la presentación a partir de Since

    private bool Set(CallState s) { if (s == State) return false; State = s; return true; }
}
