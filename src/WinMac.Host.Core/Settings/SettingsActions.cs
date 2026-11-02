using System.Text.Json;
using WinMac.Bar;

namespace WinMac.Host;

/// <summary>Lo que el usuario cambió en una fila. <c>Kind</c>: toggle · segment · slider · text · button · up · down.</summary>
public sealed record SettingsEdit(string Id, string Kind, bool On = false, int Index = -1, double Number = 0, string? Text = null);

/// <summary>Aplica un cambio de Ajustes a la configuración central (que valida, guarda de forma atómica y avisa a los módulos). Puro respecto de la interfaz: se prueba con un ConfigService real sobre un archivo temporal.</summary>
public sealed class SettingsActions(ConfigService cfg, Action openLocal, Action<bool> setAutostart, Action<string>? log = null)
{
    private static JsonElement Patch(string field, object? value) => JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [field] = value });

    /// <summary>true si el cambio se reconoció (aunque no modificara nada).</summary>
    public bool Apply(SettingsEdit e)
    {
        try { return ApplyCore(e); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { log?.Invoke("[host] ajuste «" + e.Id + "» no se pudo aplicar: " + ex.Message); return false; }
    }

    /// <summary>Extension operations, provided by the host (null when extensions are not available).</summary>
    public IExtensionOps? Extensions { get; set; }

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

        switch (id, e.Kind)
        {
            case ("mod:bar", "toggle"): cfg.SetModuleEnabled("bar", e.On); return true;
            case ("mod:dock", "toggle"): cfg.SetModuleEnabled("dock", e.On); return true;
            case ("mod:notch", "toggle"): cfg.SetModuleEnabled("notch", e.On); return true;
            case ("autostart", "toggle"): setAutostart(e.On); cfg.SetAutostartFlag(e.On); return true;
            case ("bar.bg", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.BackgroundValues.Length: cfg.ApplyBarPatch(Patch("background", SettingsBuilder.BackgroundValues[e.Index])); return true;
            case ("bar.theme", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.ThemeValues.Length: cfg.ApplyBarPatch(Patch("theme", SettingsBuilder.ThemeValues[e.Index])); return true;
            case ("bar.capfmt", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.CaptureFormatValues.Length: cfg.ApplyBarPatch(Patch("captureFormat", SettingsBuilder.CaptureFormatValues[e.Index])); return true;
            case ("bar.capdir", "text"): { var t = (e.Text ?? "").Trim().Trim('"'); cfg.ApplyBarPatch(Patch("captureFolder", t.Length == 0 ? null : t)); return true; }
            case ("bar.spot", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.SpotKeyValues.Length: cfg.ApplyBarPatch(Patch("spotlightHotkey", SettingsBuilder.SpotKeyValues[e.Index])); return true;
            case ("hub.rate", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.RateValues.Length: cfg.ApplyBarPatch(Patch("rateUnit", SettingsBuilder.RateValues[e.Index])); return true;
            case ("bar.ics", "text"): { var t = (e.Text ?? "").Trim(); cfg.ApplyBarPatch(Patch("calendarIcsPath", t.Length == 0 ? null : t)); return true; }
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
            case ("notch.clipsecrets", "toggle"): cfg.UpdateNotch(n => n with { ClipboardSkipSecrets = e.On }); return true;
            case ("notch.clippersist", "toggle"): cfg.UpdateNotch(n => n with { ClipboardPersist = e.On }); return true;
            case ("notch.cliplimit", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int cl)) cfg.UpdateNotch(n => n with { ClipboardLimit = cl }); return true;
            case ("notch.hudsec", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int hs)) cfg.UpdateNotch(n => n with { PillHudSeconds = hs }); return true;
            case ("notch.notifsec", "text"): if (int.TryParse((e.Text ?? "").Trim(), out int ns)) cfg.UpdateNotch(n => n with { PillNotifySeconds = ns }); return true;
            case ("notch.local", "button"): openLocal(); return true;
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
