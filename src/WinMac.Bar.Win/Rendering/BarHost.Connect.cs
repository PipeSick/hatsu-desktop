using WinMac.Bar.Notch;
using WinMac.Bar.Notifications;
using WinMac.Connect;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// WinMac Connect en la barra (docs/39, fases 1 y 2). El módulo Connect publica el estado del iPhone, sus notificaciones y los avisos nuevos por el host; aquí se
/// muestran en el indicador, en su panel, en el Centro de Notificaciones y en la Isla. Llegan desde el hilo del canal: todo se aplica en el hilo de la barra (<see cref="Post"/>).
/// Sin el módulo (o apagado) no llega nada y no se muestra nada.
/// </summary>
public sealed partial class BarHost
{
    private readonly PhoneInbox _phone = new();
    private ConnectSettings _connectCfg = new();
    private IReadOnlyList<AppNotification> _notchWin = [];

    private bool _phoneHooked;
    /// <summary>Ocultar una del iPhone en el panel también la quita de la Isla (misma lista).</summary>
    private void HookPhone() { if (_phoneHooked) return; _phoneHooked = true; _phone.Changed += () => Post(SendNotchSnapshot); }

    public void ApplyConnectConfig(ConnectSettings c) => Post(() => { HookPhone(); _connectCfg = c; SendNotchSnapshot(); RefreshPhoneUi(); });

    /// <summary>Null = el módulo se apagó: se borra todo lo del iPhone de memoria.</summary>
    public void ApplyPhoneState(PhoneState? s) => Post(() => { HookPhone(); if (s is null) _phone.Clear(); else _phone.SetState(s); SendNotchSnapshot(); RefreshPhoneUi(); });

    public void ApplyPhoneList(IReadOnlyList<ConnectWire.Item> items) => Post(() => { _phone.SetList(items); SendNotchSnapshot(); RefreshPhoneUi(); });

    /// <summary>Notificación NUEVA del iPhone (las que ya estaban se importan sin aviso en el módulo): píldora en la Isla, con las mismas reglas que las de Windows.</summary>
    public void OnPhoneAlert(ConnectWire.Item item) => Post(() =>
    {
        if (!_phone.Active || !_connectCfg.Notifications || !_connectCfg.Island || !_connectCfg.Alerts || !_notchOpts.Notifications) return;
        if (item.Category is AncsCategory.IncomingCall or AncsCategory.MissedCall) return;   // WM-038: las llamadas tienen su propia píldora (ApplyPhoneCall); aquí se duplicarían
        if (NotchFeed.Quiet(UserNotificationState())) { _log("[isla] notificación del iPhone sin mostrar: No molestar / presentación / pantalla completa"); return; }
        Send(NotchFeed.PhoneNotification(item));
    });

    private CallState _callShown = CallState.Idle;

    /// <summary>
    /// WM-038: la llamada del iPhone en la Isla. Mientras suena, una píldora persistente («Llamada de …»); perdida o terminada, unos segundos y se va.
    /// Solo avisa: no hay botones de contestar ni rechazar (sin validar) ni audio (Windows lo restringe).
    /// </summary>
    public void ApplyPhoneCall(CallState? c) => Post(() =>
    {
        var s = c ?? CallState.Idle;
        if (!_phone.Active || !_connectCfg.Notifications || !_connectCfg.Calls || !_connectCfg.Island || !_notchOpts.Notifications) s = CallState.Idle;
        if (s.Phase == _callShown.Phase && s.Caller == _callShown.Caller && s.Uid == _callShown.Uid) return;
        bool wasShowing = _callShown.Phase != CallPhase.None; _callShown = s;
        if (s.Phase == CallPhase.None && !wasShowing) return;
        Send(NotchFeed.Call(s));
    });

    private PanelContext WithPhone(PanelContext p) => p with
    {
        Phone = _phone.State,
        Nc = p.Nc is { } v ? v with { Phone = _phone.Active && _connectCfg.Notifications ? _phone.Groups : null } : null,
    };

    private void RefreshPhoneUi()
    {
        SetStateAll();
        if (_popover is { IsOpen: true } && _pkind is PanelKind.Notifications or PanelKind.Phone) RebuildPanel();
    }

    /// <summary>La pestaña Notificaciones de la Isla: lo de Windows (si está activado) y lo del iPhone (si está activado y «También en la Isla»). Solo se envía si cambió.</summary>
    private void SendNotchSnapshot()
    {
        if (NotchEvent is null) return;
        var phone = _phone.Active && _connectCfg.Notifications && _connectCfg.Island ? _phone.Visible : (IReadOnlyList<ConnectWire.Item>)[];
        var key = NotchFeed.SnapshotKey(_notchWin, phone);
        if (key == _notchSnapshotKey) return;
        _notchSnapshotKey = key; Send(NotchFeed.Snapshot(_notchWin, phone));
    }
}
