namespace WinMac.Connect;

public enum CallCommand { Answer, Reject, Hangup, ToggleMute, RouteToPc }
public enum CommandOutcome { Done, Unavailable, Failed }
public sealed record CommandResult(CommandOutcome Outcome, string? Notice = null);

/// <summary>Estado del proveedor de telefonía del sistema (HFP de Windows vía <c>PhoneLine</c>/<c>PhoneCall</c>).</summary>
public enum ProviderState { Off, Unavailable, Ready }
public sealed record ProviderStatus(ProviderState State, string Reason)
{
    public static readonly ProviderStatus Off = new(ProviderState.Off, "Desactivado en Ajustes");
    public bool Ready => State == ProviderState.Ready;
}

/// <summary>
/// El mecanismo que de verdad contesta, rechaza, cuelga y lleva el audio al PC: el cliente HFP/telefonía de Windows (iPhone y Android por igual). La implementación de Windows está en Connect.App;
/// aquí solo el contrato, para que la coordinación se pruebe sin Bluetooth.
/// </summary>
public interface ITelephonyProvider
{
    ProviderStatus Status { get; }
    Task<bool> AnswerAsync(string callId, CancellationToken ct);
    Task<bool> RejectAsync(string callId, CancellationToken ct);
    Task<bool> HangupAsync(string callId, CancellationToken ct);
    /// <summary>Pide a Windows que el audio de la llamada salga/entre por los dispositivos de audio del PC.</summary>
    Task<bool> RouteAudioToPcAsync(string callId, CancellationToken ct);
    Task<bool> SetMuteAsync(string callId, bool mute, CancellationToken ct);
}

/// <summary>Acciones de llamada de ANCS (solo iPhone): únicamente rechazar (no necesita audio). Contestar por ANCS dejaría la llamada en el teléfono: no se usa.</summary>
public interface IAncsCallActions
{
    bool CanReject(uint uid);
    Task<bool> RejectAsync(uint uid, CancellationToken ct);
}

/// <summary>Qué botones tiene SENTIDO mostrar para una llamada, según lo que el sistema puede hacer ahora. Puro.</summary>
public static class CallControlPlanner
{
    public static CallState Apply(CallState s, ProviderStatus provider, bool ancsCanReject)
    {
        bool viaSystem = provider.Ready && s.ProviderCallId is not null;
        return s with
        {
            CanAnswer = s.Phase == CallPhase.Ringing && viaSystem,   // contestar = contestar + audio en el PC; sin llamada en el sistema no hay audio posible
            CanReject = s.Phase == CallPhase.Ringing && (viaSystem || (s.ProviderCallId is null && ancsCanReject)),
            CanHangup = s.Phase == CallPhase.Active && viaSystem,
            CanMute = s.Phase == CallPhase.Active && viaSystem && s.AudioOnPc,
            CanRoute = s.Phase == CallPhase.Active && viaSystem && !s.AudioOnPc,   // contestada en el teléfono (o el audio no pasó): «pasar al PC»
        };
    }
}

/// <summary>
/// Coordina las órdenes de llamada (contestar, rechazar, colgar, silenciar) entre la interfaz (Isla), el sistema de telefonía y ANCS. Reglas:
/// una orden a la vez (el doble clic no repite nada); se comprueba el estado al EJECUTAR, no al dibujar el botón; nada espera para siempre; contestar lleva el audio al PC y, si Windows no lo consigue,
/// lo DICE (la llamada sigue activa en el teléfono: nunca se finge que el audio funciona); al terminar la llamada no queda nada fijado.
/// </summary>
public sealed class CallCoordinator
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);
    private readonly CallTracker _tracker; private readonly Func<ITelephonyProvider?> _provider; private readonly Func<IAncsCallActions?> _ancs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _answeredByUs; private int _routeTries; private string? _notice; private string? _noticeFor;

    private readonly TimeSpan _timeout;
    public CallCoordinator(CallTracker tracker, Func<ITelephonyProvider?> provider, Func<IAncsCallActions?> ancs, TimeSpan? timeout = null) { _tracker = tracker; _provider = provider; _ancs = ancs; _timeout = timeout ?? CommandTimeout; }

    /// <summary>La llamada tal como debe verla la interfaz, con los controles y el aviso vigentes.</summary>
    public CallState Present()
    {
        var s = _tracker.State; var prov = _provider(); var ancs = _ancs();
        var status = prov?.Status ?? ProviderStatus.Off;
        // Los botones son opt-in: sin proveedor (función «Contestar desde el PC» apagada) no hay ninguno, tampoco el rechazo por ANCS.
        var p = CallControlPlanner.Apply(s, status, prov is not null && ancs is not null && s.Uid != 0 && ancs.CanReject(s.Uid));
        return p with { Notice = _noticeFor == (s.ProviderCallId ?? s.Uid.ToString()) ? _notice : null };
    }

    /// <summary>Una foto de las llamadas de la línea Bluetooth del sistema (null = ninguna). Devuelve true si la presentación cambió.</summary>
    public async Task<bool> OnProviderCallAsync(ProviderCall? c, string device, CancellationToken ct = default)
    {
        bool changed = _tracker.OnProviderCall(c, device);
        if (c is null || c.Status is ProviderCallStatus.Ended) { _answeredByUs = null; _routeTries = 0; if (changed) { _notice = null; _noticeFor = null; } return changed; }
        // Contestada desde Hatsu: el audio debe quedar en el PC. Si Windows aún no lo movió (llamada recién activa), se pide hasta 2 veces.
        if (c.Status == ProviderCallStatus.Talking && _answeredByUs == c.Id && !c.AudioOnPc && _routeTries < 2 && _provider() is { Status.Ready: true } prov)
        {
            _routeTries++;
            bool ok = await Guard(prov.RouteAudioToPcAsync(c.Id, ct), ct);
            if (!ok && _routeTries >= 2) Note(c.Id, "Contestada, pero Windows no pasó el audio al PC: sigue en el teléfono");
            changed = true;
        }
        else if (c.AudioOnPc && _noticeFor == c.Id) { _notice = null; _noticeFor = null; changed = true; }
        return changed;
    }

    public async Task<CommandResult> ExecuteAsync(CallCommand cmd, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return new(CommandOutcome.Unavailable, "Hay otra orden en curso");
        try
        {
            var st = Present(); var prov = _provider();
            switch (cmd)
            {
                case CallCommand.Answer:
                    if (!st.CanAnswer || prov is null) return new(CommandOutcome.Unavailable, "No se puede contestar desde el PC ahora");
                    if (!await Guard(prov.AnswerAsync(st.ProviderCallId!, ct), ct)) { Note(st.ProviderCallId!, "Windows no pudo contestar la llamada"); return new(CommandOutcome.Failed, _notice); }
                    _answeredByUs = st.ProviderCallId; _routeTries = 0;
                    if (!await Guard(prov.RouteAudioToPcAsync(st.ProviderCallId!, ct), ct)) { _routeTries = 1; }   // se reintenta cuando el sistema la dé por activa
                    return new(CommandOutcome.Done);
                case CallCommand.Reject:
                    if (!st.CanReject) return new(CommandOutcome.Unavailable, "No se puede rechazar desde el PC ahora");
                    bool ok = st.ProviderCallId is not null && prov is { Status.Ready: true } ? await Guard(prov.RejectAsync(st.ProviderCallId, ct), ct)
                        : _ancs() is { } a && await Guard(a.RejectAsync(st.Uid, ct), ct);
                    if (!ok) { Note(st.ProviderCallId ?? st.Uid.ToString(), "No se pudo rechazar la llamada"); return new(CommandOutcome.Failed, _notice); }
                    return new(CommandOutcome.Done);
                case CallCommand.Hangup:
                    if (!st.CanHangup || prov is null) return new(CommandOutcome.Unavailable, "No hay una llamada activa que colgar");
                    if (!await Guard(prov.HangupAsync(st.ProviderCallId!, ct), ct)) { Note(st.ProviderCallId!, "No se pudo colgar desde el PC"); return new(CommandOutcome.Failed, _notice); }
                    return new(CommandOutcome.Done);
                case CallCommand.ToggleMute:
                    if (!st.CanMute || prov is null) return new(CommandOutcome.Unavailable);
                    return await Guard(prov.SetMuteAsync(st.ProviderCallId!, !st.Muted, ct), ct) ? new(CommandOutcome.Done) : new(CommandOutcome.Failed, "No se pudo cambiar el silencio");
                case CallCommand.RouteToPc:
                    if (!st.CanRoute || prov is null) return new(CommandOutcome.Unavailable, "No hay una llamada activa cuyo audio mover al PC");
                    if (!await Guard(prov.RouteAudioToPcAsync(st.ProviderCallId!, ct), ct)) { Note(st.ProviderCallId!, "Windows no pudo pasar el audio al PC: sigue en el teléfono"); return new(CommandOutcome.Failed, _notice); }
                    return new(CommandOutcome.Done);
                default: return new(CommandOutcome.Unavailable);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Se perdió la conexión o se apagó la función: no queda estado de órdenes ni avisos.</summary>
    public void Reset() { _answeredByUs = null; _routeTries = 0; _notice = null; _noticeFor = null; }

    private void Note(string key, string text) { _notice = text; _noticeFor = key; }

    private async Task<bool> Guard(Task<bool> t, CancellationToken ct)
    {
        try { return await t.WaitAsync(_timeout, ct); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { return false; }
    }
}
