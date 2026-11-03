namespace WinMac.Connect;

/// <summary>Fase de la llamada del iPhone tal como la deja ver ANCS (solo avisos: WinMac no controla la llamada).</summary>
public enum CallPhase { None, Ringing, Missed, Ended }

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
/// <item>Al retirarse: si en <see cref="MissedGrace"/> aparece una perdida → «perdida»; si no → «terminada» (contestada o rechazada: ANCS no los distingue y no se inventa).</item>
/// <item>Seguridad: más de <see cref="RingTimeout"/> sonando sin noticias → se da por terminada; al perder la conexión Bluetooth → se borra (nunca queda fijada).</item>
/// <item>Perdida y terminada se muestran <see cref="EndedShow"/> y desaparecen.</item>
/// </list>
/// Puro: el módulo le pasa eventos y lo hace avanzar con <see cref="Tick"/>.
/// </summary>
public sealed class CallTracker(Func<DateTimeOffset>? now = null)
{
    public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(90), MissedGrace = TimeSpan.FromSeconds(4), EndedShow = TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);
    private DateTimeOffset? _removedAt;
    public CallState State { get; private set; } = CallState.Idle;

    /// <summary>Aplica un aviso de ANCS. Devuelve true si el estado visible cambió.</summary>
    public bool OnEvent(AncsEvent e, long session)
    {
        var t = _now();
        switch (e.Event, e.Category)
        {
            case (AncsEventId.Added, AncsCategory.IncomingCall):   // también si ya sonaba al conectar (PreExisting): está sonando ahora
                _removedAt = null; return Set(new CallState(CallPhase.Ringing, null, e.Uid, session, t));
            case (AncsEventId.Removed, _) when State.Phase == CallPhase.Ringing && e.Uid == State.Uid && session == State.Session:
                _removedAt = t; return false;   // se decide en Tick: ¿llega una perdida?
            case (AncsEventId.Added, AncsCategory.MissedCall) when !e.PreExisting && State.Phase == CallPhase.Ringing:
                _removedAt = null; return Set(State with { Phase = CallPhase.Missed, Since = t });
        }
        return false;
    }

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
            case CallPhase.Ringing when _removedAt is { } r && t - r >= MissedGrace: _removedAt = null; return Set(State with { Phase = CallPhase.Ended, Since = t });
            case CallPhase.Ringing when t - State.Since >= RingTimeout: _removedAt = null; return Set(State with { Phase = CallPhase.Ended, Since = t });
            case CallPhase.Missed or CallPhase.Ended when t - State.Since >= EndedShow: return Set(CallState.Idle);
        }
        return false;
    }

    /// <summary>Se perdió la conexión o se apagó la función: nada de llamadas «fantasma».</summary>
    public bool Reset() { _removedAt = null; return Set(CallState.Idle); }

    public bool NeedsTicks => State.Phase != CallPhase.None;

    private bool Set(CallState s) { if (s == State) return false; State = s; return true; }
}
