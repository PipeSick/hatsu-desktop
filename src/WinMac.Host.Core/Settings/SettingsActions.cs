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

    private bool ApplyCore(SettingsEdit e)
    {
        string id = e.Id;
        if (id.StartsWith("bar:", StringComparison.Ordinal) && e.Kind == "toggle") { string f = id[4..]; if (!SettingsBuilder.BarToggles.Any(t => t.Field == f)) return false; cfg.ApplyBarPatch(Patch(f, e.On)); return true; }
        if (id.StartsWith("notch.sec:", StringComparison.Ordinal) && int.TryParse(id[10..], out int si))
        {
            switch (e.Kind) { case "toggle": cfg.UpdateNotch(n => n.ToggleSection(si)); return true; case "up": cfg.UpdateNotch(n => n.MoveSection(si, -1)); return true; case "down": cfg.UpdateNotch(n => n.MoveSection(si, 1)); return true; }
            return false;
        }

        switch (id, e.Kind)
        {
            case ("mod:bar", "toggle"): cfg.SetModuleEnabled("bar", e.On); return true;
            case ("mod:dock", "toggle"): cfg.SetModuleEnabled("dock", e.On); return true;
            case ("mod:notch", "toggle"): cfg.SetModuleEnabled("notch", e.On); return true;
            case ("autostart", "toggle"): setAutostart(e.On); cfg.SetAutostartFlag(e.On); return true;
            case ("bar.bg", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.BackgroundValues.Length: cfg.ApplyBarPatch(Patch("background", SettingsBuilder.BackgroundValues[e.Index])); return true;
            case ("bar.theme", "segment") when e.Index >= 0 && e.Index < SettingsBuilder.ThemeValues.Length: cfg.ApplyBarPatch(Patch("theme", SettingsBuilder.ThemeValues[e.Index])); return true;
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
            case ("notch.local", "button"): openLocal(); return true;
        }

        return false;
    }
}
