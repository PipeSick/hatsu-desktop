using System.Text.Json;
using WinMac.Bar;

namespace WinMac.Host;

/// <summary>Lo que el usuario cambió en una fila. <c>Kind</c>: toggle · segment · slider · text · button · up · down.</summary>
public sealed record SettingsEdit(string Id, string Kind, bool On = false, int Index = -1, double Number = 0, string? Text = null);

/// <summary>Aplica un cambio de Ajustes a la configuración central (que valida, guarda de forma atómica y avisa a los módulos). Puro respecto de la interfaz: se prueba con un ConfigService real sobre un archivo temporal.</summary>
public sealed class SettingsActions(ConfigService cfg, Action openLocal, Action<bool> setAutostart, Action<string>? log = null)
{
    /// <summary>Asigna al gesto <paramref name="field"/> una aplicación elegida de la lista (se guarda como acceso rápido si no estaba).</summary>
    public bool AssignApp(string field, string appId, string appName)
    {
        SettingsBuilder.GestNote = null; SettingsBuilder.GestPick = null;
        return Assign(field, WinMac.Bar.Features.GestLauncher.App, appName, appId);
    }

    /// <summary>Asigna al gesto <paramref name="field"/> una página web (la dirección se completa con https:// si hace falta).</summary>
    public bool AssignUrl(string field, string text)
    {
        SettingsBuilder.GestNote = null;
        var url = WinMac.Bar.Features.Launchers.NormalizeUrl(text);
        if (url is null) { SettingsBuilder.GestNote = $"«{text}» no parece una dirección web válida (escribe el dominio completo, con su terminación)."; return true; }
        SettingsBuilder.GestPick = null;
        return Assign(field, WinMac.Bar.Features.GestLauncher.Url, WinMac.Bar.Features.Launchers.SiteName(url), url);
    }

    private bool Assign(string field, string kind, string name, string target)
    {
        if (!SettingsBuilder.GestFields.Contains(field)) return false;
        var cur = WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers);
        var same = cur.FirstOrDefault(l => l.Kind == kind && string.Equals(l.Target, target, StringComparison.OrdinalIgnoreCase));
        if (same is null)
        {
            if (WinMac.Bar.Features.Launchers.Add(cur, name, kind, target) is not { } added) { SettingsBuilder.GestNote = cur.Count >= WinMac.Bar.Features.Launchers.Max ? $"Ya hay {WinMac.Bar.Features.Launchers.Max} accesos rápidos: quita uno para agregar otro." : "No se pudo agregar."; return true; }
            same = added.Added; cfg.ApplyBarPatch(Patch("gestLaunchers", WinMac.Bar.Features.Launchers.Serialize(added.List)));
        }

        cfg.ApplyBarPatch(Patch(field, same.ActionId)); return true;
    }

    private static JsonElement Patch(string field, object? value) => JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [field] = value });

    /// <summary>true si el cambio se reconoció (aunque no modificara nada).</summary>
    public bool Apply(SettingsEdit e)
    {
        try { return ApplyCore(e); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { log?.Invoke("[host] ajuste «" + e.Id + "» no se pudo aplicar: " + ex.Message); return false; }
    }

    /// <summary>Busca aplicaciones instaladas por nombre (la implementa el host con el índice de Launchpad/Spotlight). Devuelve (Id, nombre) ordenadas de mejor a peor coincidencia.</summary>
    public Func<string, IReadOnlyList<(string Id, string Name)>>? FindApps { get; set; }

    /// <summary>Clears the Island's clipboard history (pinned included); provided by the host.</summary>
    public Action? ClearClipboard { get; set; }

    /// <summary>Hatsu Desktop: adopción EXPLÍCITA del inicio automático (el host pide la confirmación y escribe el valor). Devuelve true si cambió.</summary>
    public Func<bool>? AdoptAutostart { get; set; }
    /// <summary>Reintenta UN módulo que no arrancó (la ventana llama a Supervisor.Restart).</summary>
    public Func<string, bool>? RetryModule { get; set; }
    /// <summary>Genera el informe de diagnóstico local (solo lectura) y lo abre.</summary>
    public Action? RunDiagnostics { get; set; }
    /// <summary>Reabre el asistente de primera ejecución (no borra nada: solo lo muestra con las elecciones actuales).</summary>
    public Action? RunOnboarding { get; set; }

    /// <summary>WM-019: lectura y guardado de dock.json (el Dock lo vuelve a leer al cambiar). Los da el host; null = sin página del Dock.</summary>
    public Func<WinMac.Dock.DockConfig?>? LoadDock { get; set; }
    public Action<WinMac.Dock.DockConfig>? SaveDock { get; set; }

    private bool Dock(Func<WinMac.Dock.DockConfig, WinMac.Dock.DockConfig> change)
    {
        if (LoadDock?.Invoke() is not { } d || SaveDock is null) return false;
        var n = WinMac.Dock.DockSettings.Sanitize(change(d));
        if (n != d) SaveDock(n);
        return true;
    }

    /// <summary>Diagnóstico de Bluetooth y llamadas (solo lectura), lo da el host. Observar: interruptor TEMPORAL del observador de telefonía del sistema.</summary>
    public Action? DiagRun { get; set; }
    public Action? DiagOpen { get; set; }
    public Action? DiagCopy { get; set; }
    public Action? DiagSave { get; set; }
    public Action<bool>? DiagObserve { get; set; }
    /// <summary>Experimentos HFP con confirmación: «hfp», «audio» o «restore».</summary>
    public Action<string>? DiagExperiment { get; set; }

    /// <summary>Abre una URI o una app (WinMac Connect). La da el host; null = no hace nada.</summary>
    public Action<string>? OpenUri { get; set; }

    /// <summary>Extension operations, provided by the host (null when extensions are not available).</summary>
    public IExtensionOps? Extensions { get; set; }

    /// <summary>Lo que muestra la lista desplegable de una asignación de Gestures.</summary>
    public IReadOnlyList<(string Id, string Name, bool Current)>? GestureChoices(string field) => SettingsBuilder.GestureChoices(cfg.Bar, field);

    private bool ApplyCore(SettingsEdit e)
    {
        string id = e.Id;
        if (Extensions is { } x)
        {
            if (id.StartsWith("ext:", StringComparison.Ordinal) && e.Kind == "toggle") { x.SetEnabled(id[4..], e.On); return true; }
            if (id.StartsWith("ext.uninstall:", StringComparison.Ordinal)) { x.Uninstall(id[14..]); return true; }
            if (id == "ext.install" && e.Kind == "text" && !string.IsNullOrWhiteSpace(e.Text)) { var err = x.Install(e.Text.Trim().Trim('"')); log?.Invoke(err is null ? "[extensiones] instalada (desactivada)" : "[extensiones] no se pudo instalar: " + err); return true; }
        }
        if (id.StartsWith("bar:", StringComparison.Ordinal) && e.Kind == "toggle") { string f = id[4..]; if (!SettingsBuilder.BarToggles.Any(t => t.Field == f)) return false; cfg.ApplyBarPatch(Patch(f, e.On)); return true; }
        if (id.StartsWith("notch.sec:", StringComparison.Ordinal) && int.TryParse(id[10..], out int si))
        {
            switch (e.Kind) { case "toggle": cfg.UpdateNotch(n => n.ToggleSection(si)); return true; case "up": cfg.UpdateNotch(n => n.MoveSection(si, -1)); return true; case "down": cfg.UpdateNotch(n => n.MoveSection(si, 1)); return true; }
            return false;
        }

        if (id.StartsWith("nc.w:", StringComparison.Ordinal) && Enum.TryParse<WinMac.Bar.Notifications.NcWidget>(id[5..], true, out var wk))
        {
            var list = WinMac.Bar.Notifications.NcWidgets.Parse((cfg.Current.Bar ?? new BarConfig()).NcWidgets).ToList(); int at = list.IndexOf(wk);
            switch (e.Kind)
            {
                case "toggle": if (e.On && at < 0) list.Add(wk); else if (!e.On && at >= 0) list.RemoveAt(at); break;
                case "up" when at > 0: (list[at - 1], list[at]) = (list[at], list[at - 1]); break;
                case "down" when at >= 0 && at < list.Count - 1: (list[at + 1], list[at]) = (list[at], list[at + 1]); break;
                default: return true;
            }

            cfg.ApplyBarPatch(Patch("ncWidgets", WinMac.Bar.Notifications.NcWidgets.Serialize(list))); return true;
        }

        if (id.StartsWith("baritem:", StringComparison.Ordinal) || id.StartsWith("baritem.add:", StringComparison.Ordinal))
        {   // Personalizar los iconos de la barra: encender/apagar y subir/bajar. Para los de Rendimiento, Red y Discos también se enciende el interruptor general.
            bool add = id.StartsWith("baritem.add:", StringComparison.Ordinal); var def = SettingsBuilder.BarIcons.FirstOrDefault(d => d.Id == id[(add ? 12 : 8)..]); if (def is null) return false;
            if (add) e = e with { Kind = "toggle", On = true };
            var bar = cfg.Current.Bar ?? new BarConfig(); var order = SettingsBuilder.BarIconsInOrder(bar).Select(d => d.Id).ToList(); int at = order.IndexOf(def.Id);
            switch (e.Kind)
            {
                case "toggle":
                {
                    var patch = new Dictionary<string, object?> { [def.Field] = e.On };
                    if (e.On) foreach (var f in def.AlsoSet ?? []) patch[f] = true;
                    if (e.On && at < 0) order.Add(def.Id); else if (!e.On && at >= 0) order.RemoveAt(at);
                    patch["barOrder"] = WinMac.Bar.BarOrdering.Serialize(order);
                    cfg.ApplyBarPatch(JsonSerializer.SerializeToElement(patch)); return true;
                }
                case "up" when at > 0: (order[at - 1], order[at]) = (order[at], order[at - 1]); break;
                case "down" when at >= 0 && at < order.Count - 1: (order[at + 1], order[at]) = (order[at], order[at + 1]); break;
                default: return true;
            }

            cfg.ApplyBarPatch(Patch("barOrder", WinMac.Bar.BarOrdering.Serialize(order))); return true;
        }

        if (id.StartsWith("wx.fav:", StringComparison.Ordinal) && int.TryParse(id[7..], out int fi))
        {
            var favs = WinMac.Bar.Hub.GeoPoint.ParseList((cfg.Current.Bar ?? new BarConfig()).WeatherFavorites).ToList();
            if (fi < 0 || fi >= favs.Count) return true;
            switch (e.Kind)
            {
                case "toggle" when !e.On: favs.RemoveAt(fi); break;
                case "up" when fi > 0: (favs[fi - 1], favs[fi]) = (favs[fi], favs[fi - 1]); break;
                case "down" when fi < favs.Count - 1: (favs[fi + 1], favs[fi]) = (favs[fi], favs[fi + 1]); break;
                default: return true;
            }

            cfg.ApplyBarPatch(Patch("weatherFavorites", favs.Count == 0 ? null : WinMac.Bar.Hub.GeoPoint.SerializeList(favs))); return true;
        }

        if (id.StartsWith("feature.retry:", StringComparison.Ordinal)) { cfg.UpdateBar(b => b with { FeatureRetryToken = b.FeatureRetryToken + 1 }); return true; }
        if (id.StartsWith("gest.set:", StringComparison.Ordinal) && e.Kind == "text")
        {
            string f = id[9..]; if (SettingsBuilder.GestureChoices(cfg.Bar, f) is not { } choices || !choices.Any(c => c.Id == e.Text)) return false;   // solo acciones de la lista (módulo encendido)
            cfg.ApplyBarPatch(Patch(f, e.Text)); return true;
        }

        if (id.StartsWith("gest.rm:", StringComparison.Ordinal) && e.Kind == "button")
        {
            var cur = WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers); string lid = id[8..]; if (!cur.Any(l => l.Id == lid)) return false;
            SettingsBuilder.GestNote = null; cfg.ApplyBarPatch(Patch("gestLaunchers", WinMac.Bar.Features.Launchers.Serialize(WinMac.Bar.Features.Launchers.Remove(cur, lid)))); return true;   // Sanitize deja en «Nada» las asignaciones que apuntaban a él al validarse
        }

        if (id == "gest.pick.cancel" && e.Kind == "button") { SettingsBuilder.GestPick = null; SettingsBuilder.GestNote = null; return true; }
        if (id.StartsWith("mod.key:", StringComparison.Ordinal) && e.Kind == "text")
        {
            string f = id[8..]; if (!SettingsBuilder.HotkeyFields.Contains(f)) return false;
            string t = (e.Text ?? "").Trim();
            if (t.Length > 0 && !WinMac.Bar.Features.HotkeySpec.TryParse(t, out _)) { log?.Invoke("[módulos] atajo no válido (se necesita Ctrl o Alt y una tecla): " + t); return true; }   // la fila se repinta con el valor vigente
            if (t.Length > 0 && WinMac.Bar.Features.HotkeySpec.TryParse(t, out var wanted))
            {
                if (wanted.ReservedBy() is { } reserved) { SettingsBuilder.HotkeyNote = $"{wanted.Display()} no se guardó: Windows la reserva ({reserved})."; return true; }
                if (SettingsBuilder.HotkeyConflict(f, wanted) is { } other) { SettingsBuilder.HotkeyNote = $"{wanted.Display()} no se guardó: ya la usa {other}. Elige otra combinación."; return true; }
            }

            SettingsBuilder.HotkeyNote = null;
            cfg.ApplyBarPatch(Patch(f, t.Length == 0 ? "" : WinMac.Bar.Features.HotkeySpec.TryParse(t, out var hs) ? hs.Format() : t)); return true;
        }

        if (id.StartsWith("module.retry:", StringComparison.Ordinal)) return RetryModule?.Invoke(id[13..]) ?? false;
        switch (id, e.Kind)
        {
            case ("diagnose.run", _): if (RunDiagnostics is null) return false; RunDiagnostics(); return true;
            case ("dock.size", "segment") when e.Index >= 0 && e.Index < WinMac.Dock.DockSettings.Presets.Length: return Dock(d => WinMac.Dock.DockSettings.WithIconSize(d, WinMac.Dock.DockSettings.Presets[e.Index].Size));
            case ("dock.fine", "slider"): return Dock(d => WinMac.Dock.DockSettings.WithIconSize(d, SettingsBuilder.DockFineSize(e.Number)));
            case ("dock.magnify", "toggle"): return Dock(d => d with { MagnificationEnabled = e.On });
            case ("dock.autohide", "toggle"): return Dock(d => d with { AutoHide = e.On });
            case ("dock.reserve", "toggle"): return Dock(d => d with { ReserveSpace = e.On });
            case ("dock.previews", "toggle"): return Dock(d => d with { PreviewsEnabled = e.On });
            case ("dock.minimize", "toggle"): return Dock(d => d with { ClickActiveMinimizes = e.On });
            case ("bar.capdelay", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.CaptureDelayValues.Length: cfg.ApplyBarPatch(Patch("captureDelaySeconds", SettingsBuilder.CaptureDelayValues[e.Index])); return true;
            case ("wx.src", "segment") when e.Index is 0 or 1: cfg.ApplyBarPatch(Patch("weatherAutoLocation", e.Index == 0)); return true;
            case ("mod:bar", "toggle"): cfg.SetModuleEnabled("bar", e.On); return true;
            case ("mod:dock", "toggle"): cfg.SetModuleEnabled("dock", e.On); return true;
            case ("mod:notch", "toggle"): cfg.SetModuleEnabled("notch", e.On); return true;
            case ("host.hidetaskbar", "toggle"): cfg.SetHideTaskbar(e.On); return true;
            case ("autostart", "toggle"): setAutostart(e.On); cfg.SetAutostartFlag(e.On); return true;
            case ("bar.bg", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.BackgroundValues.Length: cfg.ApplyBarPatch(Patch("background", SettingsBuilder.BackgroundValues[e.Index])); return true;
            case ("bar.orderreset", _): cfg.ApplyBarPatch(Patch("barOrder", "")); return true;
            case ("bar.capmods", "segment") when e.Index >= 0 && e.Index < WinMac.Bar.Launcher.HotkeyAudit.CaptureModifierIds.Length: cfg.ApplyBarPatch(Patch("captureModifiers", WinMac.Bar.Launcher.HotkeyAudit.CaptureModifierIds[e.Index])); return true;
            case ("snap.mod", "segment") when e.Index >= 0 && e.Index < WinMac.Bar.Features.FeatureConfig.SnapModifiers.Length: cfg.ApplyBarPatch(Patch("snapModifier", WinMac.Bar.Features.FeatureConfig.SnapModifiers[e.Index])); return true;
            case ("snap.layout", "segment"): { var names = SettingsBuilder.SnapLayoutNames(); if (e.Index < 0 || e.Index >= names.Length) return false; cfg.ApplyBarPatch(Patch("snapLayout", names[e.Index])); return true; }
            case ("snap.gap", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int gap)) cfg.ApplyBarPatch(Patch("snapGap", gap)); return true;
            case ("mod.lang", "text"): { var t = (e.Text ?? "").Trim(); if (t.Length > 16) return true; cfg.ApplyBarPatch(Patch("ltLanguage", t)); return true; }
            case ("gest.delay", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int gd)) cfg.ApplyBarPatch(Patch("gestCornerDelayMs", gd)); return true;
            case ("shelf.enabled", "toggle"): cfg.UpdateNotch(n => { int i = n.Sections.ToList().FindIndex(s => s.Id == "shelf"); return i >= 0 && n.Sections[i].Enabled != e.On ? n.ToggleSection(i) : n; }); return true;
            case ("shelf.max", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int shm)) cfg.UpdateNotch(n => n with { ShelfMaxItems = shm }); return true;
            case ("snap.edit", _): cfg.UpdateBar(b => b with { SnapEditToken = b.SnapEditToken + 1 }); return true;
            case ("bar.keysreset", _): cfg.ApplyBarPatch(JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["captureModifiers"] = "CtrlShift", ["captureHotkeys"] = true, ["spotlightHotkey"] = "AltSpace" })); return true;
            case ("bar.capfmt", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.CaptureFormatValues.Length: cfg.ApplyBarPatch(Patch("captureFormat", SettingsBuilder.CaptureFormatValues[e.Index])); return true;
            case ("bar.capdir", "text"): { var t = (e.Text ?? "").Trim().Trim('"'); cfg.ApplyBarPatch(Patch("captureFolder", t.Length == 0 ? null : t)); return true; }
            case ("bar.spot", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.SpotKeyValues.Length: cfg.ApplyBarPatch(Patch("spotlightHotkey", SettingsBuilder.SpotKeyValues[e.Index])); return true;
            case ("hub.rate", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.RateValues.Length: cfg.ApplyBarPatch(Patch("rateUnit", SettingsBuilder.RateValues[e.Index])); return true;
            case ("bar.ics", "text"): { var t = (e.Text ?? "").Trim(); cfg.ApplyBarPatch(Patch("calendarIcsPath", t.Length == 0 ? null : t)); return true; }
            case ("periph.asus.kb", "text"): cfg.ApplyBarPatch(Patch("peripheralAsusKeyboardName", (e.Text ?? "").Trim())); return true;
            case ("periph.asus.mouse", "text"): cfg.ApplyBarPatch(Patch("peripheralAsusMouseName", (e.Text ?? "").Trim())); return true;
            case ("periph.thr", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int thr)) cfg.ApplyBarPatch(Patch("peripheralAlertThreshold", thr)); return true;
            case ("periph.unhide", _): cfg.ApplyBarPatch(Patch("peripheralHidden", "")); return true;
            case ("onboarding.rerun", _): if (RunOnboarding is null) return false; RunOnboarding(); return true;
            case ("autostart.adopt", _): return AdoptAutostart?.Invoke() ?? false;
            case ("hub.ms", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int ms)) cfg.ApplyBarPatch(Patch("hubRefreshMs", ms)); return true;   // un valor no numérico se ignora (la ventana vuelve a mostrar el vigente)
            case ("notch.pos", "segment") when e.Index >= 0 && e.Index < NotchSettings.Positions.Length: cfg.UpdateNotch(n => n with { Position = NotchSettings.Positions[e.Index] }); return true;
            case ("notch.player", "segment") when e.Index >= 0 && e.Index < NotchSettings.Players.Length: cfg.UpdateNotch(n => n with { MusicPlayer = NotchSettings.Players[e.Index] }); return true;
            case ("notch.glass", "slider"): cfg.UpdateNotch(n => n with { GlassLevel = (int)Math.Clamp(Math.Round(e.Number), 0, 100) }); return true;
            case ("notch.path", "text"): { var t = e.Text ?? ""; cfg.UpdateNotch(n => n with { CustomPlayerPath = t }); return true; }
            case ("notch.poll", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int s)) cfg.UpdateNotch(n => n with { PollIntervalSeconds = s }); return true;
            case ("notch.music", "toggle"): cfg.UpdateNotch(n => n with { MusicEnabled = e.On }); return true;
            case ("notch.agents", "toggle"): cfg.UpdateNotch(n => n with { AgentsEnabled = e.On }); return true;
            case ("notch.lyrics", "toggle"): cfg.UpdateNotch(n => n with { LyricsEnabled = e.On, LyricsInPill = e.On && n.LyricsInPill }); return true;
            case ("notch.pill", "toggle"): cfg.UpdateNotch(n => n with { LyricsInPill = e.On }); return true;
            case ("notch.pillvol", "toggle"): cfg.UpdateNotch(n => n with { PillVolume = e.On }); return true;
            case ("notch.pillbright", "toggle"): cfg.UpdateNotch(n => n with { PillBrightness = e.On }); return true;
            case ("notch.pillnotif", "toggle"): cfg.UpdateNotch(n => n with { PillNotifications = e.On }); return true;
            case ("notch.pillanim", "toggle"): cfg.UpdateNotch(n => n with { PillAnimations = e.On }); return true;
            case ("notch.clipexcl", "toggle"): cfg.UpdateNotch(n => n with { ClipboardExclusionsOn = e.On }); return true;
            case ("notch.clipapps", "text"): { var t = (e.Text ?? "").Trim(); cfg.UpdateNotch(n => n with { ClipboardExcludedApps = t.Length == 0 ? null : string.Join(", ", NotchSettings.ParseApps(t)) }); return true; }
            case ("notch.clipclear", _): ClearClipboard?.Invoke(); return true;
            case ("notch.clipsecrets", "toggle"): cfg.UpdateNotch(n => n with { ClipboardSkipSecrets = e.On }); return true;
            case ("notch.clippersist", "toggle"): cfg.UpdateNotch(n => n with { ClipboardPersist = e.On }); return true;
            case ("notch.cliplimit", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int cl)) cfg.UpdateNotch(n => n with { ClipboardLimit = cl }); return true;
            case ("notch.hudsec", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int hs)) cfg.UpdateNotch(n => n with { PillHudSeconds = hs }); return true;
            case ("notch.notifsec", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int ns)) cfg.UpdateNotch(n => n with { PillNotifySeconds = ns }); return true;
            case ("notch.local", "button"): openLocal(); return true;
            case ("mod:connect", "toggle") or ("connect.module", "toggle"): cfg.SetModuleEnabled("connect", e.On); return true;
            case ("connect.battery", "toggle"): return cfg.UpdateConnect(c => c with { Battery = e.On }) || true;
            case ("connect.notifs", "toggle"): return cfg.UpdateConnect(c => c with { Notifications = e.On }) || true;
            case ("connect.calls", "toggle"): return cfg.UpdateConnect(c => c with { Calls = e.On }) || true;
            case ("connect.callcontrol", "toggle"): return cfg.UpdateConnect(c => c with { CallControl = e.On }) || true;
            case ("connect.island", "toggle"): return cfg.UpdateConnect(c => c with { Island = e.On }) || true;
            case ("connect.alerts", "toggle"): return cfg.UpdateConnect(c => c with { Alerts = e.On }) || true;
            case ("connect.content", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.ContentNames.Length: return cfg.UpdateConnect(c => c with { Content = (WinMac.Connect.ContentLevel)e.Index }) || true;
            case ("connect.device", "text"): { var t = (e.Text ?? "").Trim(); return cfg.UpdateConnect(c => c with { DeviceName = t.Length == 0 ? null : t }) || true; }
            case ("connect.diag.run", _): if (DiagRun is null) return false; DiagRun(); return true;
            case ("connect.diag.open", _): if (DiagOpen is null) return false; DiagOpen(); return true;
            case ("connect.diag.copy", _): if (DiagCopy is null) return false; DiagCopy(); return true;
            case ("connect.diag.save", _): if (DiagSave is null) return false; DiagSave(); return true;
            case ("connect.diag.exp:hfp" or "connect.diag.exp:audio" or "connect.diag.exp:restore", _): if (DiagExperiment is null) return false; DiagExperiment(id[(id.IndexOf(':') + 1)..]); return true;
            case ("connect.diag.observe", "toggle"): if (DiagObserve is null) return false; DiagObserve(e.On); return true;
            case ("connect.bt", "button"): if (OpenUri is null) return false; OpenUri("ms-settings:bluetooth"); return true;
        }

        return false;
    }
}

/// <summary>What the Settings window can do with extensions (the host implements it over <c>ExtensionCatalog</c> + <c>ExtensionHost</c>).</summary>
public interface IExtensionOps
{
    void SetEnabled(string id, bool on);
    void Uninstall(string id);
    /// <summary>Null on success, else the reason.</summary>
    string? Install(string folder);
}
