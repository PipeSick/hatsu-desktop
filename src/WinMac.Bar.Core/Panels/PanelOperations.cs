using WinMac.Calendar;
using WinMac.SystemControl;

namespace WinMac.Bar;

/// <summary>Lo que las acciones de los paneles necesitan de la interfaz (la ventana real en Windows; un doble en las pruebas).</summary>
public interface IPanelUi
{
    /// <summary>Estado de interfaz del panel: mes visible, «ocupado», último error, red desplegada… Las operaciones lo leen y lo reemplazan.</summary>
    PanelContext Ctx { get; set; }
    /// <summary>Vuelve a construir y mostrar el panel abierto con el estado actual.</summary>
    void Rebuild();
    void Navigate(PanelKind kind);
    void HidePanel();
    void ShowInfo(string title, string? text);
    /// <summary>Abre un URI de Windows (p. ej. <c>ms-settings:network-wifi</c>).</summary>
    void OpenUri(string uri);
}

/// <summary>
/// Traduce lo que el usuario toca en un panel (<see cref="PanelEvent"/>) en operaciones REALES sobre los servicios del sistema, siempre fuera del hilo de interfaz y con «ocupado» visible,
/// el valor mostrado al instante cuando es seguro, y el error con su motivo si algo falla. Es lógica pura: se prueba con servicios simulados, sin Windows.
/// </summary>
public sealed class PanelOperations(SystemHub hub, IPanelUi ui, Action<string> log, TimeSpan? notificationDelay = null)
{
    private readonly TimeSpan _notifyDelay = notificationDelay ?? TimeSpan.FromMilliseconds(250);

    private void SetMessage(string? m) => ui.Ctx = ui.Ctx with { Message = m };

    private void SetBusy(string key, string? text)
    {
        var d = new Dictionary<string, string>(ui.Ctx.Busy); if (text is null) d.Remove(key); else d[key] = text; ui.Ctx = ui.Ctx with { Busy = d };
    }

    /// <summary>Operación del sistema fuera del hilo de interfaz, con «ocupado» visible y el error (con su motivo) si falla. <paramref name="refresh"/> relee el estado real al terminar.</summary>
    private void Op(string busyKey, string busyText, Func<ServiceResult> op, Action refresh)
    {
        SetBusy(busyKey, busyText); SetMessage(null); ui.Rebuild();
        hub.Run(op, r =>
        {
            SetBusy(busyKey, null); if (!r.IsOk) SetMessage(r.Message ?? "La operación falló"); refresh(); ui.Rebuild();
        });
    }

    public void Handle(PanelEvent e)
    {
        try
        {
            if (e.Kind is PanelEventKind.SliderChange or PanelEventKind.SliderCommit) { OnSlider(e); return; }
            string id = e.Id;
            if (id == "back") { ui.Navigate(PanelKind.ControlCenter); return; }
            if (id.StartsWith("open:ms-settings:") || id.StartsWith("open:ms-windows-store:")) { ui.HidePanel(); ui.OpenUri(id[5..]); return; }
            if (id.StartsWith("open:")) { var k = id[5..] switch { "wifi" => PanelKind.Wifi, "bluetooth" => PanelKind.Bluetooth, "sound" => PanelKind.Sound, "battery" => PanelKind.Battery, "display" => PanelKind.Display, _ => PanelKind.ControlCenter }; ui.Navigate(k); return; }
            var s = hub.Snapshot; var svc = hub.Services;
            switch (id)
            {
                case "notifications": ui.HidePanel(); hub.Run(() => { if (_notifyDelay > TimeSpan.Zero) Thread.Sleep(_notifyDelay); return svc.Session.OpenNotificationCenter(); }, r => { if (!r.IsOk) ui.ShowInfo("Notificaciones", r.Message); }); return;
                case "appearance": { bool dark = !s.Appearance.Dark; hub.Patch(x => x with { Appearance = x.Appearance with { Dark = dark } }); Op("appearance", "Cambiando…", () => svc.Appearance.SetDark(dark), hub.RefreshFast); return; }
                case "wifi-radio": { bool on = s.Wifi.RadioOn != true; Op("wifi-radio", on ? "Activando…" : "Desactivando…", () => svc.Wifi.SetRadio(on), () => { hub.RefreshWifi(true); }); return; }
                case "bt-radio": { bool on = !s.Bluetooth.RadioOn; Op("bt-radio", on ? "Activando…" : "Desactivando…", () => svc.Bluetooth.SetRadio(on), () => hub.RefreshBluetooth(true)); return; }
                case "mute": { bool m = !s.Audio.Muted; Op("mute", "…", () => svc.Audio.SetMuted(m), hub.RefreshAudio); return; }
                case "audio-restore": { var prev = hub.PreviousOutputId; if (prev is null) return; Op("audio-restore", "Restaurando…", () => svc.Audio.SetDefaultOutput(prev), () => { hub.RefreshAudio(); hub.RefreshSlow(); }); return; }
                case "prev": case "next": { var (y, m) = CalendarGrid.Shift(ui.Ctx.ViewYear, ui.Ctx.ViewMonth, id == "next" ? 1 : -1); ui.Ctx = ui.Ctx with { ViewYear = y, ViewMonth = m }; ui.Rebuild(); return; }
            }

            if (id.StartsWith("day:") && DateOnly.TryParseExact(id[4..], "yyyy-MM-dd", out var day)) { ui.Ctx = ui.Ctx with { SelectedDay = day }; ui.Rebuild(); return; }
            if (id.StartsWith("net:") && ui.Ctx.Cfg.WifiDetails)   // con detalles activados la fila se despliega (la acción está dentro)
            { ui.Ctx = ui.Ctx with { ExpandedNet = ui.Ctx.ExpandedNet == id[4..] ? null : id[4..] }; ui.Rebuild(); return; }
            if (id.StartsWith("net:") || id.StartsWith("netact:"))
            {
                string ssid = id[(id.IndexOf(':') + 1)..], busy = "net:" + ssid;
                var net = s.Wifi.Networks.FirstOrDefault(n => n.Ssid == ssid); if (net is null) return;
                if (net.Connected) Op(busy, "Desconectando…", svc.Wifi.Disconnect, () => hub.RefreshWifi(true));
                else Op(busy, "Conectando…", () => svc.Wifi.Connect(net.ProfileName ?? "", net.Ssid), () => { hub.RefreshWifi(true); });
                return;
            }

            if (id.StartsWith("out:")) { var dev = id[4..]; Op(id, "Cambiando…", () => svc.Audio.SetDefaultOutput(dev), () => { hub.RefreshAudio(); hub.RefreshSlow(); }); return; }
            if (id.StartsWith("power:") && Enum.TryParse<PowerMode>(id[6..], out var pm)) { hub.Patch(x => x with { Power = new PowerModeState(true, pm) }); Op(id, "Cambiando…", () => svc.Power.SetMode(pm), hub.RefreshFast); return; }
            if (id.StartsWith("topology:") && Enum.TryParse<DisplayTopology>(id[9..], out var topo)) { ui.HidePanel(); hub.Run(() => svc.Display.SetTopology(topo), r => { if (!r.IsOk) ui.ShowInfo("Pantallas", r.Message); }); return; }
        }
        catch (Exception ex) { log("[bar] evento de panel falló: " + ex.GetType().Name + ": " + ex.Message); }
    }

    private void OnSlider(PanelEvent e)
    {
        int v = (int)Math.Round(e.Value); var svc = hub.Services; bool commit = e.Kind == PanelEventKind.SliderCommit;
        if (e.Id == "volume")
        {
            hub.Patch(s => s with { Audio = s.Audio with { Volume = v } });   // la posición del deslizador sigue al puntero; al soltar se relee el valor REAL
            hub.SetLatest("volume", v, svc.Audio.SetVolume, r => { if (!r.IsOk) { SetMessage(r.Message); } if (commit || !r.IsOk) hub.RefreshAudio(); ui.Rebuild(); });
        }
        else if (e.Id == "brightness")
        {
            var mon = hub.Snapshot.Brightness.Monitors.ToList(); if (mon.Count == 0) return;
            hub.Patch(s => s with { Brightness = s.Brightness with { Monitors = s.Brightness.Monitors.Select(m => m with { Percent = v }).ToList() } });
            hub.SetLatest("brightness", v, p => { ServiceResult last = ServiceResult.Ok; foreach (var m in mon) { last = svc.Brightness.Set(m.Id, p); if (!last.IsOk) break; } return last; },
                r => { if (!r.IsOk) SetMessage(r.Message); if (commit || !r.IsOk) hub.RefreshBrightness(true); ui.Rebuild(); });
        }

        ui.Rebuild();
    }
}
