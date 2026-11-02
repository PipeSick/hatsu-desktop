using WinMac.SystemControl;

namespace WinMac.Bar.Notifications;

/// <summary>
/// Estado del Centro de Notificaciones. Todo el trabajo con Windows va a un hilo de trabajo (<c>bg</c>) y los resultados vuelven al hilo de la barra (<c>post</c>); el estado solo se toca en ese hilo.
/// <para>Privacidad: el contenido vive SOLO en memoria mientras WinMac corre (no se escribe en disco ni en registros); desactivar la fuente lo borra.</para>
/// <para>Actualización: con el evento de cambios de Windows (si esta app lo recibe) se relee al llegar; si no, el anfitrión relee cada pocos segundos SOLO con el panel abierto. Con el panel cerrado no se lee nada.</para>
/// <para>Dos formas de quitar: <b>ocultar</b> (solo en WinMac, durante esta sesión; sigue en el centro de Windows) y <b>eliminar</b> (se pide a Windows que la quite de su centro).</para>
/// </summary>
public sealed class NotificationStore
{
    private readonly INotificationSource? _src; private readonly Action<Action> _bg, _post; private readonly Action<string> _log; private readonly Func<DateTime> _clock;
    private IReadOnlyList<AppNotification> _items = [];
    private readonly HashSet<uint> _hidden = [], _removing = [];
    // _message: motivo del estado; _notice: fallo de la última acción del usuario (sobrevive a la relectura siguiente)
    private NcStatus _status = NcStatus.Off; private string? _message, _notice, _watchWhy; private bool _enabled, _active, _reading, _again, _watching, _live; private DateTime? _lastRead;
    private int _generation;   // sube al desactivar: los resultados de lecturas viejas se descartan

    public NotificationStore(INotificationSource? source, Action<Action> bg, Action<Action> post, Action<string> log, Func<DateTime> clockUtc)
    {
        _src = source; _bg = bg; _post = post; _log = log; _clock = clockUtc;
    }

    public event Action? Changed;

    /// <summary>Hace falta releer de vez en cuando (panel abierto, permiso concedido y SIN evento de cambios).</summary>
    public bool NeedsPolling => _enabled && _active && !_live && _status is NcStatus.Ready or NcStatus.Error;
    public bool Active => _active;

    public NcState State => new(_status, NotificationGrouping.Group(_items, _hidden.Count == 0 && _removing.Count == 0 ? null : _hidden.Concat(_removing).ToHashSet()), _notice ?? _message, _live, _watchWhy, _lastRead);

    /// <summary>Activa o desactiva la lectura (ajuste del usuario «Mostrar notificaciones de Windows»). Desactivar borra de memoria todo lo leído.</summary>
    public void SetEnabled(bool on)
    {
        if (on == _enabled) return; _enabled = on;
        if (!on) { _generation++; StopWatch(); _items = []; _hidden.Clear(); _removing.Clear(); _status = NcStatus.Off; _message = _notice = null; _lastRead = null; Raise(); return; }
        _status = NcStatus.Loading; Raise(); if (_active) Refresh();
    }

    /// <summary>El panel se abrió: se lee una vez y se escucha el evento de cambios (si existe) mientras siga abierto.</summary>
    public void Activate() { _active = true; if (!_enabled) { Raise(); return; } Refresh(); }

    /// <summary>El panel se cerró: se deja de escuchar y de releer.</summary>
    public void Deactivate() { _active = false; StopWatch(); }

    /// <summary>Lee el permiso y, si está concedido, las notificaciones. Una sola lectura a la vez; si se pide otra durante una, se hace una más al terminar.</summary>
    public void Refresh()
    {
        if (!_enabled || _src is null) { if (_src is null && _enabled) { _status = NcStatus.Unavailable; _message = "Este equipo no ofrece el lector de notificaciones de Windows."; Raise(); } return; }
        if (_reading) { _again = true; return; }
        _reading = true; int gen = _generation;
        _bg(() =>
        {
            NotificationAccess access; string? why; IReadOnlyList<AppNotification>? list = null; Exception? error = null;
            try { (access, why) = _src.GetAccess(); }
            catch (Exception e) { access = NotificationAccess.Unavailable; why = Describe(e); }
            if (access == NotificationAccess.Allowed)
            {
                try { list = _src.Read(); } catch (Exception e) { error = e; }
            }

            _post(() =>
            {
                _reading = false; if (gen != _generation) return;
                Apply(access, why, list, error);
                if (_again) { _again = false; Refresh(); }
            });
        });
    }

    private void Apply(NotificationAccess access, string? why, IReadOnlyList<AppNotification>? list, Exception? error)
    {
        switch (access)
        {
            case NotificationAccess.Unspecified: _status = NcStatus.NeedsPermission; _message = null; _items = []; break;
            case NotificationAccess.Denied: _status = NcStatus.Denied; _message = "Windows no permite que las apps lean tus notificaciones. Puedes cambiarlo en Configuración → Privacidad y seguridad → Notificaciones."; _items = []; StopWatch(); break;
            case NotificationAccess.Unavailable: _status = NcStatus.Unavailable; _message = why ?? "El lector de notificaciones de Windows no está disponible para WinMac en este equipo."; _items = []; StopWatch(); break;
            default:
                if (error is not null)
                {
                    _status = NcStatus.Error; _message = "No se pudieron leer las notificaciones de Windows (" + Describe(error) + ")."; _log("[notificaciones] lectura: " + error.GetType().Name + ": " + error.Message);   // el contenido nunca va al registro
                    break;
                }

                _items = list ?? []; _lastRead = _clock(); _status = NcStatus.Ready; _message = null;
                var present = _items.Select(i => i.Id).ToHashSet(); _hidden.IntersectWith(present); _removing.IntersectWith(present);   // lo que Windows ya no tiene deja de ocupar memoria
                if (_active && !_watching) StartWatch();
                break;
        }

        Raise();
    }

    /// <summary>Pide el permiso a Windows (botón «Permitir»). Si Windows ya tiene una decisión, la devuelve sin preguntar.</summary>
    public void RequestAccess()
    {
        if (_src is null || !_enabled) return;
        _status = NcStatus.Requesting; _message = null; Raise(); int gen = _generation;
        Task<NotificationAccess> t;
        try { t = _src.RequestAccessAsync(); }
        catch (Exception e) { _status = NcStatus.Unavailable; _message = "Windows no pudo pedir el permiso (" + Describe(e) + ")."; Raise(); return; }
        t.ContinueWith(r => _post(() =>
        {
            if (gen != _generation) return;
            if (r.IsFaulted) { _status = NcStatus.Unavailable; _message = "Windows no pudo pedir el permiso (" + Describe(r.Exception!.GetBaseException()) + ")."; Raise(); return; }
            Refresh();
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);   // la continuación solo publica al hilo de la barra
    }

    // ───────────────────────────── acciones
    /// <summary>Quita una notificación. <paramref name="fromWindows"/>: también del centro de Windows (si Windows lo rechaza, vuelve a aparecer con el motivo); si no, solo se oculta en WinMac.</summary>
    public void Dismiss(uint id, bool fromWindows)
    {
        if (!_items.Any(i => i.Id == id)) return;
        _notice = null; if (!fromWindows) { _hidden.Add(id); Raise(); return; }
        RemoveFromWindows([id]);
    }

    /// <summary>Quita todas las de una app (mismo criterio que <see cref="Dismiss"/>).</summary>
    public void ClearGroup(string groupKey, bool fromWindows)
    {
        var ids = _items.Where(i => string.Equals(i.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase) && !_hidden.Contains(i.Id)).Select(i => i.Id).ToList();
        if (ids.Count == 0) return;
        _notice = null; if (!fromWindows) { _hidden.UnionWith(ids); Raise(); return; }
        RemoveFromWindows(ids);
    }

    /// <summary>Quita todas. Desde Windows se usa su operación de borrado total (borra también las que WinMac tuviera ocultas).</summary>
    public void ClearAll(bool fromWindows)
    {
        if (_items.Count == 0) return;
        _notice = null; if (!fromWindows) { _hidden.UnionWith(_items.Select(i => i.Id)); Raise(); return; }
        if (_src is null) return;
        var ids = _items.Select(i => i.Id).ToList(); _removing.UnionWith(ids); Raise();
        _bg(() =>
        {
            ServiceResult r; try { r = _src.ClearAll(); } catch (Exception e) { r = ServiceResult.Failed(Describe(e)); }
            _post(() => { _removing.ExceptWith(ids); if (r.IsOk) _items = _items.Where(i => !ids.Contains(i.Id)).ToList(); else _notice = "Windows no pudo borrar las notificaciones: " + (r.Message ?? "motivo desconocido"); Raise(); Refresh(); });
        });
    }

    private void RemoveFromWindows(IReadOnlyList<uint> ids)
    {
        if (_src is null) return;
        _removing.UnionWith(ids); Raise();   // se ve al instante; si Windows lo rechaza, vuelve
        _bg(() =>
        {
            var failed = new List<(uint Id, string Why)>();
            foreach (var id in ids)
            {
                ServiceResult r; try { r = _src.Remove(id); } catch (Exception e) { r = ServiceResult.Failed(Describe(e)); }
                if (!r.IsOk) failed.Add((id, r.Message ?? "motivo desconocido"));
            }

            _post(() =>
            {
                _removing.ExceptWith(ids); var gone = ids.Except(failed.Select(f => f.Id)).ToHashSet();
                _items = _items.Where(i => !gone.Contains(i.Id)).ToList();
                if (failed.Count > 0) _notice = "Windows no pudo eliminar " + (failed.Count == 1 ? "la notificación" : failed.Count + " notificaciones") + ": " + failed[0].Why;
                Raise();
            });
        });
    }

    /// <summary>Abre la app de una notificación. El resultado (o el motivo del fallo) vuelve por <paramref name="done"/> en el hilo de la barra.</summary>
    public void OpenApp(uint id, Action<ServiceResult> done)
    {
        var n = _items.FirstOrDefault(i => i.Id == id); if (n is null || _src is null) return;
        if (string.IsNullOrWhiteSpace(n.AppId)) { done(ServiceResult.Unavailable("Windows no indica qué app envió esta notificación.")); return; }
        _bg(() => { ServiceResult r; try { r = _src.OpenApp(n.AppId); } catch (Exception e) { r = ServiceResult.Failed(Describe(e)); } _post(() => done(r)); });
    }

    // ───────────────────────────── evento de cambios
    private void StartWatch()
    {
        if (_src is null || _watching) return;
        _watching = true; int gen = _generation; string? why = null; bool ok;
        try { ok = _src.StartWatching(() => _post(() => { if (gen == _generation && _active && _enabled) Refresh(); }), out why); }
        catch (Exception e) { ok = false; why = Describe(e); }
        _live = ok; _watchWhy = ok ? null : why ?? "Windows no envía avisos de cambios a esta app";
        if (!ok) _log("[notificaciones] sin evento de cambios: " + _watchWhy + " (se relee solo con el panel abierto)");
    }

    private void StopWatch()
    {
        if (!_watching) return; _watching = false; _live = false;
        try { _src?.StopWatching(); } catch (Exception e) { _log("[notificaciones] al dejar de escuchar: " + e.GetType().Name + ": " + e.Message); }
    }

    private void Raise() => Changed?.Invoke();

    private static string Describe(Exception e) => e.GetType().Name + (e.HResult != 0 ? $", 0x{e.HResult:X8}" : "") + (string.IsNullOrWhiteSpace(e.Message) ? "" : ": " + Short(e.Message));
    private static string Short(string m) { m = m.Replace('\r', ' ').Replace('\n', ' ').Trim(); return m.Length > 120 ? m[..120] + "…" : m; }
}
