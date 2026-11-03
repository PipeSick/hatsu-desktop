using WinMac.Bar.Hub;
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

    // ── panel multifunción
    /// <summary>Se eligió otra pestaña: el anfitrión empieza a leer lo de esa pestaña (y deja de leer lo de la anterior).</summary>
    void TabChanged(HubTab tab);
    /// <summary>Abre la ventana central de Ajustes de WinMac, en la sección relacionada si existe («sound», «system», «network», «disks», «weather»).</summary>
    void OpenSettings(string? section);
    /// <summary>Cierra WinMac completo (host y módulos) por el cierre ordenado existente. No es cerrar el panel.</summary>
    void QuitWinMac();
    /// <summary>Menú de opciones junto al puntero; llama a <paramref name="onPick"/> con el Id elegido (nada si se cancela).</summary>
    void ShowChoices(string title, IReadOnlyList<Choice> choices, Action<string> onPick);
    /// <summary>Cambia un ajuste central (parche JSON camelCase), que el host guarda y reparte.</summary>
    void PatchConfig(object patch);
    /// <param name="action">Label of the confirming button (WM-024); <paramref name="danger"/> paints it red. «Cancelar» is always the default.</param>
    bool Confirm(string title, string text, string action = "Continuar", bool danger = false);
}

/// <summary>
/// Traduce lo que el usuario toca en un panel (<see cref="PanelEvent"/>) en operaciones REALES sobre los servicios del sistema, siempre fuera del hilo de interfaz y con «ocupado» visible,
/// el valor mostrado al instante cuando es seguro, y el error con su motivo si algo falla. Es lógica pura: se prueba con servicios simulados, sin Windows.
/// </summary>
public sealed class PanelOperations(SystemHub hub, IPanelUi ui, Action<string> log, TimeSpan? notificationDelay = null, HubRuntime? rt = null, Notifications.NotificationStore? nc = null)
{
    /// <summary>El brillo que pidió el usuario desde la barra se aplicó de verdad (porcentaje, pantalla): la Isla lo muestra. Los monitores DDC/CI no avisan de cambios por sí solos.</summary>
    public event Action<int, string>? BrightnessApplied;

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
            if (HubAction(id)) return;
            if (NcAction(id)) return;
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

            if (id.StartsWith("in:")) { var mic = id[3..]; Op(id, "Cambiando…", () => svc.Audio.SetDefaultInput(mic), () => { hub.RefreshInput(); }); return; }
            if (id.StartsWith("out:")) { var dev = id[4..]; Op(id, "Cambiando…", () => svc.Audio.SetDefaultOutput(dev), () => { hub.RefreshAudio(); hub.RefreshSlow(); }); return; }
            if (id.StartsWith("power:") && Enum.TryParse<PowerMode>(id[6..], out var pm)) { hub.Patch(x => x with { Power = new PowerModeState(true, pm) }); Op(id, "Cambiando…", () => svc.Power.SetMode(pm), hub.RefreshFast); return; }
            if (id.StartsWith("topology:") && Enum.TryParse<DisplayTopology>(id[9..], out var topo)) { ui.HidePanel(); hub.Run(() => svc.Display.SetTopology(topo), r => { if (!r.IsOk) ui.ShowInfo("Pantallas", r.Message); }); return; }
        }
        catch (Exception ex) { log("[bar] evento de panel falló: " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Acciones del Centro de Notificaciones. Devuelve true si el Id era de ellas.</summary>
    private bool NcAction(string id)
    {
        bool win = ui.Ctx.Cfg.NotifRemoveFromWindows;
        switch (id)
        {
            case "nc:open": ui.Navigate(PanelKind.Notifications); return true;   // mosaico del Control Center: el anfitrión lo reabre como panel lateral
            case "nc:close": ui.HidePanel(); return true;
            case "nc:settings": ui.HidePanel(); ui.OpenSettings("notifications"); return true;
            case "nc:enable":   // consentimiento explícito en WinMac; después, el de Windows
                ui.Ctx = ui.Ctx with { Cfg = ui.Ctx.Cfg with { NotificationsFromWindows = true } }; ui.PatchConfig(new { notificationsFromWindows = true });
                nc?.SetEnabled(true); if (nc is { Active: false }) nc.Activate(); ui.Rebuild(); return true;
            case "nc:request": nc?.RequestAccess(); ui.Rebuild(); return true;
            case "nc:retry": nc?.Refresh(); return true;
            case "nc:clearall":
                if (nc is null) return true;
                if (win && !ui.Confirm("¿Eliminar todas las notificaciones?", "Cuando Windows lo permita, también se eliminarán de su centro de notificaciones. No se puede deshacer.", "Eliminar notificaciones", danger: true)) return true;
                nc.ClearAll(win); return true;
        }

        if (id.StartsWith("ngtoggle:"))
        {
            var key = id[9..]; var view = ui.Ctx.Nc; if (view is null) return true;
            var set = new HashSet<string>(view.Expanded, StringComparer.OrdinalIgnoreCase); if (!set.Remove(key)) set.Add(key);
            ui.Ctx = ui.Ctx with { Nc = view with { Expanded = set } }; ui.Rebuild(); return true;
        }

        if (id.StartsWith("ngclear:")) { nc?.ClearGroup(id[8..], win); return true; }
        if (id.StartsWith("ndismiss:") && uint.TryParse(id[9..], out var did)) { nc?.Dismiss(did, win); return true; }
        if (id.StartsWith("nopen:") && uint.TryParse(id[6..], out var oid))
        {
            nc?.OpenApp(oid, r => { if (r.IsOk) ui.HidePanel(); else { SetMessage(r.Message ?? "No se pudo abrir la app"); ui.Rebuild(); } }); return true;
        }

        return false;
    }

    private void ToggleSelectedVolume(string letter) => ui.Ctx = ui.Ctx with { Hub = ui.Ctx.Hub is { } h ? h with { SelectedVolume = letter } : null };

    /// <summary>Acciones del panel multifunción y del Tiempo. Devuelve true si el Id era de ellas.</summary>
    private bool HubAction(string id)
    {
        var svc = hub.Services; var s = hub.Snapshot;
        if (id.StartsWith("tab:") && HubTabs.TryParse(id[4..], out var tab))
        {
            ui.Ctx = ui.Ctx with { Tab = tab, Message = null }; ui.PatchConfig(new { hubTab = tab.ToString() }); ui.TabChanged(tab); ui.Rebuild(); return true;
        }

        if (id.StartsWith("settings:") && HubTabs.TryParse(id[9..], out var st)) { ui.OpenSettings(st.ToString().ToLowerInvariant()); return true; }
        if (id == "hub:settings") { ui.OpenSettings(ui.Ctx.Tab.ToString().ToLowerInvariant()); return true; }
        if (id == "hub:settings-weather") { ui.HidePanel(); ui.OpenSettings("weather"); return true; }
        if (id == "hub:quit") { ui.QuitWinMac(); return true; }

        switch (id)
        {
            case "pick:out":
                ui.ShowChoices("Salida", s.Audio.Outputs.Select(d => new Choice("out:" + d.Id, d.Name, d.IsDefault)).ToList(), pick => Handle(new PanelEvent(PanelEventKind.Click, pick))); return true;
            case "pick:in":
                ui.ShowChoices("Micrófono", s.Mic.Devices.Select(d => new Choice("in:" + d.Id, d.Name, d.IsDefault)).ToList(), pick => Handle(new PanelEvent(PanelEventKind.Click, pick))); return true;
            case "mic-mute": { bool m = !s.Mic.Muted; Op("mic-mute", "…", () => svc.Audio.SetInputMuted(m), hub.RefreshInput); return true; }
            case "unit":
            {
                var next = (RateUnit)(((int)ui.Ctx.Cfg.RateUnit + 1) % 4);
                ui.Ctx = ui.Ctx with { Cfg = ui.Ctx.Cfg with { RateUnit = next } }; ui.PatchConfig(new { rateUnit = next.ToString() }); ui.Rebuild(); return true;   // se ve al instante; el host lo guarda
            }

            case "speedtest":
                if (rt is null || !rt.Speed.Available) { SetMessage("La prueba de velocidad no está disponible en este equipo"); ui.Rebuild(); return true; }
                if (!ui.Confirm("Prueba de velocidad", "La prueba consume datos: descarga hasta 25 MB y sube hasta 8 MB con tu conexión.", "Ejecutar la prueba")) return true;   // nunca arranca sola ni sin confirmar
                rt.Speed.Start(); ui.Rebuild(); return true;
            case "speedtest-cancel": rt?.Speed.Cancel(); return true;
            case "weather:refresh": rt?.Weather.Refresh(force: true); ui.Rebuild(); return true;
            case "weather:open": ui.HidePanel(); ui.OpenUri("bingweather:"); return true;
            case "eject-all":
            {
                if (rt is null) return true;
                foreach (var v in HubSelection.External(rt.Monitor.Disks)) { var letter = v.Letter; rt.Monitor.Eject(letter, r => { if (!r.IsOk) { SetMessage($"{letter} {r.Message}"); ui.Rebuild(); } }); }
                return true;
            }
        }

        if (id.StartsWith("disk:")) { ToggleSelectedVolume(id[5..]); ui.Rebuild(); return true; }
        if (id.StartsWith("explorer:")) { ui.OpenUri(id[9..] + "\\"); return true; }
        if (id.StartsWith("eject:")) { var letter = id[6..]; rt?.Monitor.Eject(letter, r => { SetMessage(r.IsOk ? null : r.Message ?? "No se pudo expulsar"); ui.Rebuild(); }); return true; }
        if (id.StartsWith("appmute:"))
        {
            var sid = id[8..]; var app = s.AppSessions.Sessions.FirstOrDefault(a => a.Id == sid); if (app is null) return true; bool m = !app.Muted;
            hub.Patch(x => x with { Sessions = x.AppSessions with { Sessions = x.AppSessions.Sessions.Select(a => a.Id == sid ? a with { Muted = m } : a).ToList() } });
            Op(id, "…", () => svc.Audio.SetSessionMuted(sid, m), hub.RefreshSessions); return true;
        }

        return false;
    }

    private void OnSlider(PanelEvent e)
    {
        int v = (int)Math.Round(e.Value); var svc = hub.Services; bool commit = e.Kind == PanelEventKind.SliderCommit;
        if (e.Id == "mic")
        {
            hub.Patch(s => s with { Input = s.Mic with { Volume = v } });
            hub.SetLatest("mic", v, svc.Audio.SetInputVolume, r => { if (!r.IsOk) SetMessage(r.Message); if (commit || !r.IsOk) hub.RefreshInput(); ui.Rebuild(); });
            ui.Rebuild(); return;
        }

        if (e.Id.StartsWith("appvol:"))
        {
            var sid = e.Id[7..];
            hub.Patch(s => s with { Sessions = s.AppSessions with { Sessions = s.AppSessions.Sessions.Select(a => a.Id == sid ? a with { Volume = v } : a).ToList() } });
            hub.SetLatest("app:" + sid, v, p => svc.Audio.SetSessionVolume(sid, p), r => { if (!r.IsOk) SetMessage(r.Message); if (commit || !r.IsOk) hub.RefreshSessions(); ui.Rebuild(); });
            ui.Rebuild(); return;
        }

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
                r => { if (!r.IsOk) SetMessage(r.Message); else BrightnessApplied?.Invoke(v, mon.Count == 1 ? mon[0].Name : "Pantallas"); if (commit || !r.IsOk) hub.RefreshBrightness(true); ui.Rebuild(); });
        }

        ui.Rebuild();
    }
}
