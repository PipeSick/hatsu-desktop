using WinMac.Bar.Hub;
using WinMac.SystemControl;

namespace WinMac.Bar;

/// <summary>Qué volumen está seleccionado en la pestaña Discos (el pedido si existe; si no, C: o el primero).</summary>
public static class HubSelection
{
    public static string? Volume(DisksSnapshot snap, string? requested) =>
        snap.Volumes.Any(v => v.Letter == requested) ? requested : snap.Volumes.FirstOrDefault(v => v.Letter == "C:")?.Letter ?? snap.Volumes.FirstOrDefault()?.Letter;

    public static IReadOnlyList<DiskVolume> External(DisksSnapshot snap) => snap.Volumes.Where(v => v.IsRemovable || snap.DiskOf(v)?.IsExternal == true).ToList();
}

/// <summary>Panel multifunción (una sola ventana con pestañas) y panel del Tiempo. Solo datos: lo que se dibuja sale de aquí y se prueba sin Windows.</summary>
public static partial class PanelBuilder
{
    private static string Pct(double v) => $"{(int)Math.Round(v)}%";
    private static string Deg(double c) => $"{(int)Math.Round(c)} °C";
    private static string LevelColor(double fraction) => fraction < 0.6 ? "blue" : fraction < 0.85 ? "yellow" : "red";
    private static string UnitLabel(RateUnit u) => u switch { RateUnit.KBps => "KB/s", RateUnit.MBps => "MB/s", RateUnit.Mbps => "Mb/s", _ => "Auto" };
    private static Widget Cap(string id, string text) => new(WidgetKind.Caption, id, text);
    private static Widget KV(string id, string left, string right, string? glyph = null, bool dim = false) => new(WidgetKind.KeyValue, id, left, right, Enabled: !dim, Glyph: glyph);
    private static Widget Sep(string id) => new(WidgetKind.Separator, id);
    private static Widget Note(string id, string text) => new(WidgetKind.Note, id, text);

    private static string SectionTitle(HubTab t) => t switch { HubTab.Sound => "MEZCLADOR DE VOLUMEN", HubTab.System => "SISTEMA", HubTab.Network => "RED", _ => "DISCOS" };

    public static PanelModel HubPanel(PanelContext c)
    {
        var tab = c.Tab; var content = new List<Widget>();
        switch (tab)
        {
            case HubTab.Sound: SoundTab(c, content); break;
            case HubTab.System: SystemTab(c, content); break;
            case HubTab.Network: NetworkTab(c, content); break;
            default: DisksTab(c, content); break;
        }

        if (!string.IsNullOrWhiteSpace(c.Message)) content.Add(Note("msg", c.Message!));
        var w = new List<Widget>
        {
            new(WidgetKind.Header, "logo", AppIdentity.Current.Name, Glyph: "logo", Data: AppIdentity.Current.LogoKey),
            new(WidgetKind.Tabs, "tabs", "", Data: new TabsData(HubTabs.All, HubTabs.IndexOf(tab))),
            new(WidgetKind.TabHeader, "settings:" + tab, SectionTitle(tab)),
        };
        int from = w.Count; w.AddRange(content); int to = w.Count - 1;
        w.Add(new Widget(WidgetKind.Button, "hub:settings", "Ajustes", Col: 0, Glyph: "gear"));
        w.Add(new Widget(WidgetKind.Button, "hub:quit", "Salir", Col: 1, Glyph: "power"));
        return new PanelModel(PanelKind.Hub, AppIdentity.Current.Name, w, PanelLayout.HubWidth, (from, to), PinTop: from, PinBottom: 2);
    }

    // ───────────────────────────── Sonido (FOTO 2)
    private static void SoundTab(PanelContext c, List<Widget> w)
    {
        var s = c.Sys; var a = s.Audio; var mic = s.Mic; var ses = s.AppSessions;
        string outName = a.Outputs.FirstOrDefault(o => o.IsDefault)?.Name ?? (a.Available ? "Dispositivo predeterminado" : "No disponible");
        w.Add(new Widget(WidgetKind.Select, "pick:out", "Salida", Enabled: a.Available && a.Outputs.Count > 0, Reason: a.Reason, Data: new SelectData("sound", "Salida", outName)));
        w.Add(new Widget(WidgetKind.Slider, "volume", "", Sub: a.Available ? Pct(a.Volume) : null, Value: a.Volume, Enabled: a.Available, Reason: a.Reason, Glyph: a.Muted ? "sound-muted" : "sound", Data: "mute"));
        string micName = mic.Devices.FirstOrDefault(d => d.IsDefault)?.Name ?? (mic.Available ? "Micrófono predeterminado" : "No disponible");
        w.Add(new Widget(WidgetKind.Select, "pick:in", "Micrófono", Enabled: mic.Available && mic.Devices.Count > 0, Reason: mic.Reason, Data: new SelectData("mic", "Micrófono", micName)));
        w.Add(new Widget(WidgetKind.Slider, "mic", "", Sub: mic.Available && mic.VolumeControllable ? Pct(mic.Volume) : null, Value: mic.Volume, Enabled: mic.Available && mic.VolumeControllable,
            Reason: mic.Available && !mic.VolumeControllable ? mic.Reason : !mic.Available ? mic.Reason : null, Glyph: mic.Muted ? "mic-muted" : "mic", Data: "mic-mute"));
        w.Add(Sep("sep-apps"));
        if (!ses.Available) w.Add(Note("apps-none", ses.Reason ?? "No se pudieron leer las aplicaciones con audio"));
        else if (ses.Sessions.Count == 0) w.Add(Note("apps-none", "Ninguna aplicación tiene sesión de audio ahora."));
        else foreach (var app in ses.Sessions.Take(8)) w.Add(new Widget(WidgetKind.AppRow, app.Id, app.Name, Value: app.Volume, Data: new AppRowData(app.ExePath, app.Name, app.Volume, app.Muted, app.Active)));
        w.Add(Sep("sep-opts"));
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:sound", "Configuración de sonido de Windows…"));
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:apps-volume", "Salida y volumen por aplicación (Windows)…"));
    }

    // ───────────────────────────── Sistema (FOTO 3)
    private static void SystemTab(PanelContext c, List<Widget> w)
    {
        var m = c.Hub?.Monitor; if (m is null) { w.Add(Note("loading", "Leyendo el hardware…")); return; }
        var s = m.Sample;
        w.Add(Cap("cap-temp", "Temperaturas"));
        w.Add(TempTile("temp-cpu", "CPU", "cpu", s.CpuTempC) with { Col = 0 });
        w.Add(TempTile("temp-gpu", "GPU", "gpu", s.GpuTempC) with { Col = 1 });
        if (!s.CpuTempC.Available) w.Add(Note("temp-cpu-why", "CPU: " + (s.CpuTempC.Reason ?? "sin lectura") + "."));
        if (!s.GpuTempC.Available) w.Add(Note("temp-gpu-why", "GPU: " + (s.GpuTempC.Reason ?? "sin lectura") + "."));
        if (s.CpuTempNote is not null && s.CpuTempC.Available) w.Add(Note("temp-note", "CPU: " + s.CpuTempNote + ". Windows no da la temperatura real de la CPU sin un controlador con privilegios."));
        w.Add(Sep("sep-1"));
        w.Add(Cap("cap-hw", "Uso del hardware"));
        UsageBlock(w, "cpu", "CPU", s.CpuPercent, m.CpuHistory, "blue");
        UsageBlock(w, "gpu", "GPU", s.GpuPercent, m.GpuHistory, "lightblue");
        w.Add(Sep("sep-2"));
        w.Add(Cap("cap-mem", "Memoria"));
        if (s.Memory is { } mem)
        {
            string dot = mem.Pressure switch { MemoryPressureLevel.Critical => "dot-red", MemoryPressureLevel.Warning => "dot-yellow", _ => "dot-green" };
            string level = mem.Pressure switch { MemoryPressureLevel.Critical => "Crítica", MemoryPressureLevel.Warning => "Advertencia", _ => "Normal" };
            w.Add(KV("mem-pressure", "Presión: " + level, $"{Units.FormatSize(mem.UsedBytes)} / {Units.FormatSize(mem.TotalBytes, 0)}", dot));
            w.Add(KV("mem-avail", "Disponible", Units.FormatSize(mem.AvailableBytes)));
            w.Add(mem.CompressedBytes is { } cb ? KV("mem-comp", "Comprimida", Units.FormatSize(cb)) : KV("mem-comp", "Comprimida", "No disponible", dim: true));
            w.Add(KV("mem-commit", "Comprometida", $"{Units.FormatSize(mem.CommitUsedBytes)} / {Units.FormatSize(mem.CommitLimitBytes, 0)}"));
            w.Add(mem.PageFileUsedBytes is { } pu && mem.PageFileTotalBytes is { } pt ? KV("mem-page", "Archivo de paginación", $"{Units.FormatSize(pu)} / {Units.FormatSize(pt, 0)}") : KV("mem-page", "Archivo de paginación", "No disponible", dim: true));
            if (m.MemoryHistory.Count > 1) w.Add(new Widget(WidgetKind.Chart, "chart-mem", Data: new ChartData([new ChartSeries(m.MemoryHistory.Padded(HubMonitor.HistoryLength), "teal")], 100, "100%")));
        }
        else w.Add(Note("mem-none", s.MemoryReason ?? "No se pudo leer la memoria"));
        w.Add(Sep("sep-3"));
        w.Add(KV("uptime", "Activo desde hace", Units.FormatUptime(s.Uptime), "clock"));
        w.Add(s.ConnectedDevices.Available ? KV("devices", "Dispositivos USB conectados", s.ConnectedDevices.Value!.Value.ToString()) : KV("devices", "Dispositivos USB conectados", "No disponible", dim: true));
    }

    private static Widget TempTile(string id, string label, string glyph, Reading<double> r) =>
        new(WidgetKind.Tile, id, r.Available ? Deg(r.Value!.Value) : "No disponible", label, Enabled: r.Available, Reason: null, Glyph: glyph);

    private static void UsageBlock(List<Widget> w, string id, string label, Reading<double> now, History hist, string color)
    {
        if (!now.Available) { w.Add(KV("use-" + id, label, now.Reason is "Midiendo…" ? "Midiendo…" : "No disponible", dim: true)); if (now.Reason is not null and not "Midiendo…") w.Add(Note("use-" + id + "-why", now.Reason)); return; }
        double v = now.Value!.Value;
        w.Add(new Widget(WidgetKind.MeterRow, "use-" + id, label, Pct(v), Value: v / 100, Data: new MeterData(LevelColor(v / 100))));
        w.Add(new Widget(WidgetKind.Chart, "chart-" + id, Data: new ChartData([new ChartSeries(hist.Padded(HubMonitor.HistoryLength), color)], 100, "100%")));
    }

    // ───────────────────────────── Red (FOTO 4)
    private static void NetworkTab(PanelContext c, List<Widget> w)
    {
        var m = c.Hub?.Monitor; if (m is null) { w.Add(Note("loading", "Leyendo la red…")); return; }
        var t = m.Traffic; var unit = c.Cfg.RateUnit;
        if (t.Adapter is null && t.Reason is not null and not "Leyendo…") { w.Add(Note("net-none", t.Reason)); }
        w.Add(new Widget(WidgetKind.Stats, "stats-net", Data: new StatsData(new StatItem("down", Units.FormatRate(t.DownRate, unit), "Descarga", "blue"), new StatItem("up", Units.FormatRate(t.UpRate, unit), "Subida", "green"), UnitLabel(unit))));
        double max = Units.NiceCeiling(Math.Max(Math.Max(t.DownHistory.Max, t.UpHistory.Max), 100_000));
        w.Add(new Widget(WidgetKind.Chart, "chart-net", Data: new ChartData([new ChartSeries(t.DownHistory.Padded(HubMonitor.HistoryLength), "blue"), new ChartSeries(t.UpHistory.Padded(HubMonitor.HistoryLength), "green")], max, Units.FormatRate(max, unit))));
        w.Add(Sep("sep-1"));
        w.Add(Cap("cap-apps", "Apps usando red"));
        if (m.NetAppsReason is not null) w.Add(Note("apps-why", m.NetAppsReason));
        else if (m.NetApps.Count == 0) w.Add(Note("apps-none", "Ninguna app usa la red ahora."));
        else
        {
            foreach (var app in m.NetApps) w.Add(KV("app-" + app.Pid, app.Name, app.Connections == 1 ? "1 conexión" : $"{app.Connections} conexiones"));
            w.Add(Note("apps-note", "Son aplicaciones con conexiones activas: Windows no permite medir cuánto tráfico genera cada una sin ser administrador."));
        }

        w.Add(Sep("sep-2"));
        w.Add(KV("session", "En esta sesión", $"↓{Units.FormatSize(t.SessionDown)}  ↑{Units.FormatSize(t.SessionUp)}"));
        if (t.Adapter is { } ad)
        {
            w.Add(KV("adapter", "Adaptador", ad.Name));
            w.Add(KV("kind", "Tipo de conexión", ad.Kind));
            w.Add(ad.IPv4 is not null ? KV("ip", "IPv4 local", $"{ad.IPv4} ({ad.Kind})") : KV("ip", "IPv4 local", "No disponible", dim: true));
            if (ad.LinkSpeedBps is { } sp) w.Add(KV("link", "Velocidad del enlace", Units.FormatRate(sp / 8.0, RateUnit.Mbps)));
        }

        w.Add(Sep("sep-3"));
        SpeedTestSection(c, w);
    }

    private static void SpeedTestSection(PanelContext c, List<Widget> w)
    {
        var st = c.Hub!.Speed; string Phase(SpeedPhase p) => p switch { SpeedPhase.Latency => "Latencia", SpeedPhase.Download => "Descarga", SpeedPhase.Upload => "Subida", _ => "Listo" };
        switch (st.Status)
        {
            case SpeedTestStatus.Running:
            {
                var p = st.Progress ?? new SpeedProgress(SpeedPhase.Latency, 0, 0);
                w.Add(new Widget(WidgetKind.MeterRow, "speed-progress", "Midiendo · " + Phase(p.Phase), p.Phase is SpeedPhase.Latency ? "…" : $"{p.Mbps:0.0} Mb/s".Replace('.', ','), Value: Math.Clamp(p.Fraction, 0, 1), Data: new MeterData("blue")));
                w.Add(new Widget(WidgetKind.Button, "speedtest-cancel", "Cancelar la prueba"));
                break;
            }
            case SpeedTestStatus.Done when st.Result is { } r:
                w.Add(KV("speed-down", "Descarga", r.DownMbps is { } d ? $"{d:0.0} Mb/s".Replace('.', ',') : "No disponible", dim: r.DownMbps is null));
                w.Add(KV("speed-up", "Subida", r.UpMbps is { } u ? $"{u:0.0} Mb/s".Replace('.', ',') : "No disponible", dim: r.UpMbps is null));
                w.Add(KV("speed-lat", "Latencia", r.LatencyMs is { } l ? $"{l:0} ms" : "No disponible", dim: r.LatencyMs is null));
                w.Add(new Widget(WidgetKind.Button, "speedtest", "Repetir la prueba de velocidad", Glyph: "speed"));
                break;
            case SpeedTestStatus.Failed or SpeedTestStatus.Cancelled:
                w.Add(Note("speed-msg", st.Status == SpeedTestStatus.Cancelled ? "Prueba cancelada." : st.Result?.Error ?? "La prueba falló."));
                w.Add(new Widget(WidgetKind.Button, "speedtest", "Volver a intentar", Glyph: "speed"));
                break;
            default:
                w.Add(new Widget(WidgetKind.Button, "speedtest", "Prueba de velocidad", Enabled: st.Status != SpeedTestStatus.Running, Glyph: "speed"));
                w.Add(Note("speed-warn", "Consume datos: descarga hasta 25 MB y sube hasta 8 MB. Solo se ejecuta cuando la pulsas."));
                break;
        }
    }

    // ───────────────────────────── Discos (FOTO 5)
    private static void DisksTab(PanelContext c, List<Widget> w)
    {
        var m = c.Hub?.Monitor; if (m is null) { w.Add(Note("loading", "Leyendo los discos…")); return; }
        var snap = m.Disks;
        if (snap.Volumes.Count == 0) { w.Add(Note("disks-none", snap.Reason ?? "No se encontraron unidades.")); return; }
        string sel = HubSelection.Volume(snap, c.Hub!.SelectedVolume)!; var vol = snap.Volumes.First(v => v.Letter == sel); var disk = snap.DiskOf(vol);
        w.Add(Cap("cap-pick", "Seleccionar disco"));
        int vi = 0; foreach (var v in snap.Volumes.Take(6)) w.Add(new Widget(WidgetKind.Tile, "disk:" + v.Letter, v.Display, $"{Pct(v.UsedFraction * 100)} usado", On: v.Letter == sel, Glyph: "disk", Col: vi++ % 2));   // de dos en dos, como las tarjetas de la referencia
        w.Add(Sep("sep-1"));
        w.Add(Cap("cap-use", "Uso del disco"));
        string media = disk is null ? "" : disk.Media switch { DiskMedia.SSD => " · SSD", DiskMedia.HDD => " · HDD", _ => "" };
        w.Add(KV("vol-name", vol.Display, $"{(string.IsNullOrEmpty(vol.FileSystem) ? "—" : vol.FileSystem)}{media} · {(disk?.IsExternal == true || vol.IsRemovable ? "Externo" : "Interno")}", "disk"));
        w.Add(new Widget(WidgetKind.MeterRow, "vol-usage", "", $"{Pct(vol.UsedFraction * 100)} usado", Value: vol.UsedFraction, Data: new MeterData(vol.UsedFraction < 0.75 ? "blue" : vol.UsedFraction < 0.9 ? "yellow" : "red")));
        w.Add(KV("vol-free", "Disponible", Units.FormatSize(vol.FreeBytes)));
        w.Add(KV("vol-cap", "Capacidad", $"{Units.FormatSize(vol.UsedBytes)} / {Units.FormatSize(vol.TotalBytes, 0)}"));
        w.Add(Sep("sep-2"));
        w.Add(Cap("cap-io", "Actividad en directo"));
        var io = disk is null ? null : m.MetersOf(disk.Number); var unit = c.Cfg.RateUnit;
        if (disk is null) w.Add(Note("io-none", "No se pudo asociar este volumen a un disco físico: sin actividad ni SMART." + (snap.Reason is null ? "" : " Motivo: " + snap.Reason + ".")));
        else if (io is null) w.Add(Note("io-wait", "Midiendo la actividad…"));
        else
        {
            w.Add(new Widget(WidgetKind.Stats, "stats-io", Data: new StatsData(new StatItem("down", Units.FormatRate(io.Read.Rate, unit), "Lectura", "blue"), new StatItem("up", Units.FormatRate(io.Write.Rate, unit), "Escritura", "red"), null)));
            double max = Units.NiceCeiling(Math.Max(Math.Max(io.ReadHistory.Max, io.WriteHistory.Max), 1_000_000));
            w.Add(new Widget(WidgetKind.Chart, "chart-io", Data: new ChartData([new ChartSeries(io.ReadHistory.Padded(HubMonitor.HistoryLength), "blue"), new ChartSeries(io.WriteHistory.Padded(HubMonitor.HistoryLength), "red")], max, Units.FormatRate(max, unit))));
            w.Add(KV("io-session", "En esta sesión", $"↓{Units.FormatSize(io.Read.Total)}  ↑{Units.FormatSize(io.Write.Total)}"));
        }

        w.Add(Sep("sep-3"));
        w.Add(Cap("cap-smart", "SMART"));
        if (disk is null) w.Add(Note("smart-none", "Este volumen no está asociado a un disco físico: no hay datos de salud que mostrar."));
        else
        {
            var sm = disk.Smart; w.Add(KV("smart-model", disk.Model, disk.SizeBytes > 0 ? Units.FormatSize(disk.SizeBytes, 0) : "", "disk"));
            w.Add(KV("smart-state", "Estado", sm.Health ?? "No disponible", dim: sm.Health is null));
            w.Add(KV("smart-temp", "Temperatura", sm.TemperatureC is { } tc ? Deg(tc) : "No disponible", dim: sm.TemperatureC is null));
            w.Add(KV("smart-wear", "Desgaste", sm.WearPercent is { } wp ? $"{Pct(wp)} usado" : "No disponible", dim: sm.WearPercent is null));
            w.Add(KV("smart-hours", "Horas encendido", sm.PowerOnHours is { } h ? h.ToString("N0", new System.Globalization.CultureInfo("es-CL")) : "No disponible", dim: sm.PowerOnHours is null));
            if (sm.TotalWrittenBytes is { } tw) w.Add(KV("smart-written", "Total escrito", Units.FormatSize(tw)));
            if (sm.TotalReadBytes is { } tr) w.Add(KV("smart-read", "Total leído", Units.FormatSize(tr)));
            if (sm.Reason is not null) w.Add(Note("smart-why", sm.Reason));
            if (sm.Incidents is { Total: > 0 } inc)
            {
                w.Add(KV("smart-events", "Errores (7 días)", inc.Summary(), "dot-red"));
                w.Add(Note("smart-events-why", "Windows registró estos errores de este disco en el registro del sistema, aunque su estado diga «Saludable». Haz una copia de lo importante antes de nada."));
            }
        }

        w.Add(Sep("sep-4"));
        w.Add(Cap("cap-ext", "Protección externa"));
        var ext = HubSelection.External(snap); bool selExt = ext.Any(v => v.Letter == sel);
        w.Add(new Widget(WidgetKind.Button, "eject:" + sel, "Expulsar", Enabled: selExt, Col: 0, Glyph: "eject"));
        w.Add(new Widget(WidgetKind.Button, "eject-all", "Expulsar todos", Enabled: ext.Count > 0, Col: 1, Glyph: "eject"));
        w.Add(Note("ext-note", ext.Count == 0 ? "No hay discos externos listos para expulsar." : selExt ? "La expulsión usa el mecanismo seguro de Windows: si la unidad está en uso, no se fuerza." : "El disco seleccionado es interno: no se puede expulsar."));
        w.Add(Sep("sep-5"));
        w.Add(Cap("cap-tools", "Herramientas"));
        w.Add(new Widget(WidgetKind.Button, "explorer:" + sel, "Explorador", Col: 0, Glyph: "folder"));
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:storagesense", "Almacenamiento", Col: 1, Glyph: "gear"));
    }

    // ───────────────────────────── Tiempo (FOTO 1)
    public static PanelModel WeatherPanel(PanelContext c)
    {
        var w = new List<Widget>(); var st = c.Hub?.Weather ?? WeatherState.NoCity; bool f = c.Cfg.WeatherFahrenheit, h24 = c.Hub?.Use24h ?? c.Cfg.Use24Hour;
        if (st.Status == WeatherStatus.NoCity)
        {
            w.Add(new Widget(WidgetKind.Header, "title", "Tiempo")); w.Add(Note("nocity", "Elige una ciudad en los ajustes de Hatsu Desktop para ver el tiempo."));
            w.Add(new Widget(WidgetKind.Button, "hub:settings-weather", "Abrir ajustes del Tiempo", Glyph: "gear"));
        }
        else if (st.Snapshot is null)
        {
            w.Add(new Widget(WidgetKind.Header, "title", "Tiempo")); w.Add(Note("wx-msg", st.Message ?? "Cargando el tiempo…"));
            if (st.Status is WeatherStatus.Offline or WeatherStatus.Error) w.Add(new Widget(WidgetKind.Button, "weather:refresh", "Reintentar", Glyph: "refresh"));
        }
        else
        {
            var cw = st.Snapshot.Main;
            string? hl = cw.HighC is { } hi && cw.LowC is { } lo ? $"Máx. {TempFormat.Degrees(hi, f)} Mín. {TempFormat.Degrees(lo, f)}" : null;
            w.Add(new Widget(WidgetKind.WeatherHead, "head", Data: new WeatherHeadData(cw.Place.Name, c.Cfg.WeatherAutoLocation, TempFormat.Degrees(cw.TempC, f), WeatherCodes.Icon(cw.Code, cw.IsDay), WeatherCodes.Describe(cw.Code), hl)));
            w.Add(Sep("sep-1"));
            var strip = WeatherStrip.Build(cw, f, h24); if (strip.Count > 0) w.Add(new Widget(WidgetKind.Strip, "strip", Data: new StripData(strip)));
            if (st.Snapshot.Others.Count > 0) w.Add(Sep("sep-2"));
            foreach (var o in st.Snapshot.Others) w.Add(new Widget(WidgetKind.CityRow, "city:" + o.Place.Key, o.Place.Name, TempFormat.Degrees(o.TempC, f), Glyph: WeatherCodes.Icon(o.Code, o.IsDay)));
            if (st.Status is WeatherStatus.Offline or WeatherStatus.Stale or WeatherStatus.Error)
            {
                string age = st.FetchedAtUtc is { } fa ? AgeText(DateTime.UtcNow - fa) : "";
                w.Add(Note("wx-stale", (st.Status == WeatherStatus.Offline ? "Sin conexión" : st.Status == WeatherStatus.Error ? "Error del proveedor" : "Datos desactualizados") + (age.Length > 0 ? " · última actualización " + age : "") + (st.Message is not null && st.Status != WeatherStatus.Stale ? ". " + st.Message : "")));
                w.Add(new Widget(WidgetKind.Button, "weather:refresh", "Actualizar ahora", Glyph: "refresh"));
            }

            w.Add(Sep("sep-3"));
            w.Add(new Widget(WidgetKind.Row, "weather:open", "Abrir la app Tiempo", Glyph: "chevron"));
        }

        return new PanelModel(PanelKind.Weather, "Tiempo", w, PanelLayout.WeatherWidth);
    }

    private static string AgeText(TimeSpan t) => t.TotalMinutes < 1 ? "hace un momento" : t.TotalHours < 1 ? $"hace {(int)t.TotalMinutes} min" : t.TotalDays < 1 ? $"hace {(int)t.TotalHours} h" : $"hace {(int)t.TotalDays} d";
}
