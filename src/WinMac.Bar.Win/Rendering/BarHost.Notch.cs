using System.Runtime.InteropServices;
using WinMac.Bar.Notch;
using WinMac.Bar.Notifications;
using WinMac.SystemControl.Win;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Barra → Isla: la barra ya tiene los servicios de audio, brillo y notificaciones, así que es quien detecta los cambios y se los cuenta a NotchWin por el host
/// (evento <see cref="NotchFeed.Topic"/>). Solo en modo integrado (<see cref="NotchEvent"/> lo pone el enlace con el host); sin él no se escucha nada.
/// <list type="bullet">
/// <item>Volumen: aviso de Core Audio (sin sondeo).</item>
/// <item>Brillo: evento WMI del panel interno, y el valor aplicado desde el deslizador de la propia barra (DDC/CI no avisa por sí solo).</item>
/// <item>Notificaciones: solo con «Mostrar notificaciones de Windows» activado en la barra y el permiso de Windows concedido; Windows no envía el evento de cambios a
/// esta app (medido), así que se relee cada <see cref="NotchNotifyEvery"/>. En «No molestar»/presentación/pantalla completa no se envían.</item>
/// </list>
/// </summary>
public sealed partial class BarHost
{
    private static readonly TimeSpan NotchNotifyEvery = TimeSpan.FromSeconds(3);

    private volatile NotchFeedOptions _notchOpts = new();
    private IDisposable? _volWatch, _brightWatch;
    private bool _notchStarted, _notchReading;
    private DateTime _nextNotchRead = DateTime.MinValue;
    private readonly NewNotificationTracker _notchTracker = new(); private string? _notchSnapshotKey;

    /// <summary>Envía un evento a la Isla (lo pone Program en modo integrado). Se llama desde cualquier hilo.</summary>
    public Action<object>? NotchEvent { get; set; }

    /// <summary>API INTERNA de actividades de la Isla (tópico notch.activity, solo por la tubería del host). docs/25.</summary>
    public Action<object>? NotchActivity { get; set; }

    /// <summary>Ajustes centrales de la Isla (tópico retenido): qué quiere recibir.</summary>
    public void ApplyNotchOptions(NotchFeedOptions o) { _notchOpts = o; _notchSnapshotKey = null; if (!o.Notifications) Post(_notchTracker.Reset); Post(SendNotchSnapshot); }   // the Island (re)connected: it gets the snapshot again

    private void Send(object payload)
    {
        try { NotchEvent?.Invoke(payload); } catch (Exception e) { _log("[isla] envío: " + e.GetType().Name + ": " + e.Message); }
    }

    private void NotchTick()
    {
        if (NotchEvent is null) return;
        if (!_notchStarted) StartNotchFeed();
        var now = DateTime.UtcNow;
        if (now < _nextNotchRead || _notchReading) return;
        _nextNotchRead = now + NotchNotifyEvery;
        // WM-025: Windows is read whenever WinMac shows Windows' notifications (also with the pill off): the Island's tab mirrors the centre; pills stay optional
        if (!_cfg.NotificationsFromWindows || _ncSrc is not { } src) { _notchTracker.Reset(); if (_notchWin.Count > 0) { _notchWin = []; SendNotchSnapshot(); } return; }
        _notchReading = true;
        Task.Run(() =>
        {
            IReadOnlyList<AppNotification>? list = null;
            try { if (src.GetAccess().Access == NotificationAccess.Allowed) list = src.Read(); }
            catch (Exception e) { _log("[isla] lectura de notificaciones: " + e.GetType().Name); }   // el contenido nunca va al registro
            Post(() =>
            {
                _notchReading = false;
                if (list is null) { _notchTracker.Reset(); return; }
                _notchWin = list; SendNotchSnapshot();   // Windows + iPhone (WinMac Connect) en la misma lista
                var fresh = _notchTracker.Observe(list, DateTimeOffset.Now);
                if (fresh.Count == 0 || !_notchOpts.Notifications) return;
                if (NotchFeed.Quiet(UserNotificationState())) { _log($"[isla] {fresh.Count} notificación(es) sin mostrar: No molestar / presentación / pantalla completa"); return; }
                foreach (var n in fresh) Send(NotchFeed.Notification(n, src.CachedIcon(n.GroupKey)));
            });
        });
    }

    private void StartNotchFeed()
    {
        _notchStarted = true;
        _ops.BrightnessApplied += (pct, name) => { if (_notchOpts.Brightness) Send(NotchFeed.Brightness(pct, name)); };
        Task.Run(() =>   // Core Audio y WMI se crean en un hilo MTA, fuera del hilo de la barra
        {
            _volWatch = AudioService.WatchVolume((lvl, muted, dev) => { if (_notchOpts.Volume) Send(NotchFeed.Volume(lvl, muted, dev)); }, _log, out var whyV);
            _log(_volWatch is null ? "[isla] volumen: sin aviso de Windows (" + whyV + ")" : "[isla] volumen: escuchando el aviso de Core Audio");
            _brightWatch = BrightnessService.WatchInternal((pct, name) => { if (_notchOpts.Brightness) Send(NotchFeed.Brightness(pct, name)); }, _log, out var whyB);
            _log(_brightWatch is null ? "[isla] brillo: " + whyB : "[isla] brillo: escuchando el panel interno (WMI)");
        });
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WINMAC_PILL_SELFTEST"))) _ = SelfTestFeedAsync();
    }

    /// <summary>
    /// SOLO CI (variable WINMAC_PILL_SELFTEST): el runner no tiene salida de audio, panel interno ni permiso de notificaciones, así que la detección real no puede dispararse.
    /// Se envía una secuencia de muestra por el MISMO canal (barra → host → Isla) para comprobar el recorrido y el dibujo. Sin la variable no existe.
    /// </summary>
    private async Task SelfTestFeedAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12)).ConfigureAwait(false);   // la Isla arranca y se enlaza
            // WinMac 2.0: música de PRUEBA por la API interna (el runner no tiene reproductor). Marcada como prueba: la Isla lo dice y no muestra controles.
            try
            {
                NotchActivity?.Invoke(new { op = "upsert", activity = new { id = "prueba-musica", kind = "media", title = "Canción de prueba", subtitle = "Artista de prueba · datos del CI", progress = 0.32, test = true } });
                _log("[isla] prueba: música de prueba enviada por la API de actividades");
            }
            catch (Exception e) { _log("[isla] prueba: actividad: " + e.GetType().Name); }
            void S(string what, object o) { _log("[isla] prueba: " + what); Send(o); }
            S("volumen 40→65", NotchFeed.Volume(40, false, "Altavoces de prueba")); await Task.Delay(250).ConfigureAwait(false);
            Send(NotchFeed.Volume(45, false, "Altavoces de prueba")); await Task.Delay(250).ConfigureAwait(false);
            Send(NotchFeed.Volume(65, false, "Altavoces de prueba")); await Task.Delay(3500).ConfigureAwait(false);
            S("silencio", NotchFeed.Volume(65, true, "Altavoces de prueba")); await Task.Delay(3500).ConfigureAwait(false);
            S("brillo 80", NotchFeed.Brightness(80, "Pantalla integrada")); await Task.Delay(3500).ConfigureAwait(false);
            var icon = new byte[32 * 32 * 4];
            for (int i = 0; i < icon.Length; i += 4) { icon[i] = 0xF5; icon[i + 1] = 0x84; icon[i + 2] = 0x0A; icon[i + 3] = 0xFF; }   // cuadrado azul opaco (BGRA)
            var n = new AppNotification(1, "WinMac.Prueba", "Hatsu Desktop", "Prueba de Hatsu Desktop", "Notificación de muestra del CI", DateTimeOffset.Now);
            S("notificación", NotchFeed.Notification(n, icon)); await Task.Delay(1500).ConfigureAwait(false);
            S("volumen 30 durante la notificación", NotchFeed.Volume(30, false, "Altavoces de prueba"));
        }
        catch (Exception e) { _log("[isla] prueba: " + e.GetType().Name); }
    }

    private void StopNotchFeed()
    {
        try { _volWatch?.Dispose(); } catch (Exception) { /* cerrando */ }
        try { _brightWatch?.Dispose(); } catch (Exception) { /* cerrando */ }
        _volWatch = _brightWatch = null;
    }

    private static int? UserNotificationState()
    {
        try { return SHQueryUserNotificationState(out int s) == 0 ? s : null; } catch (Exception) { return null; }
    }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
