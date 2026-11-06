using WinMac.Bar;
using WinMac.Bar.Features;

namespace WinMac.Host;

/// <summary>
/// Ajustes › Módulos: una tarjeta por módulo de productividad con interruptor, descripción, su configuración y su estado de salud real (lo informa la Barra). Un módulo solo tiene tarjeta cuando está
/// implementado: no hay controles que aparenten funcionar. Puro: se prueba sin Windows.
/// </summary>
public static partial class SettingsBuilder
{
    /// <summary>Módulos con ajustes: (id del módulo, interruptor, título, descripción, filas propias). Se añaden conforme se implementan.</summary>
    internal sealed record ModuleCard(string Id, string Toggle, string Title, string Description, Func<BarConfig, IReadOnlyList<SettingsRow>> Rows, string HotkeyModule = "");

    internal static readonly List<ModuleCard> ModuleCards = [];

    /// <summary>Campos de atajo editables desde Ajustes (el patrón de la Barra solo acepta estos).</summary>
    public static readonly string[] HotkeyFields = ["mcHotkey", "ltHotkey", "snapNextHotkey", "gestKey1", "gestKey2"];

    internal static SettingsRow HotkeyRow(string field, string title, string value, string? subtitle = null) =>
        new(RowKind.Field, "mod.key:" + field, title, subtitle ?? "Ctrl o Alt + una tecla (p. ej. Ctrl+Alt+Up). Vacío = sin atajo", Text: value, Placeholder: "sin atajo");

    internal static SettingsPage? ModulesPage(BarConfig bar)
    {
        if (ModuleCards.Count == 0) return null;
        var groups = new List<SettingsGroup>
        {
            new(null, [new(RowKind.Info, "mod.intro", "Cada módulo se enciende y se apaga al momento. Apagado no deja atajos, ganchos ni tareas en marcha. Desactivar un atajo no desactiva su módulo.")]),
        };
        foreach (var c in ModuleCards)
        {
            bool on = ToggleValue(bar, c.Toggle);
            var rows = new List<SettingsRow> { new(RowKind.Toggle, "bar:" + c.Toggle, c.Title, c.Description, on) };
            var st = FeatureStatusStore.Get(c.Id);
            rows.Add(new(RowKind.Info, "mod.status:" + c.Id, StatusText(on, st, c.HotkeyModule)));
            if (on && st is { State: "Failed" }) rows.Add(new(RowKind.Button, "feature.retry:" + c.Id, "Reintentar"));
            rows.AddRange(c.Rows(bar).Select(r => on ? r : r with { Enabled = false, Reason = "Activa el módulo" }));
            groups.Add(new(null, rows));
        }

        return new SettingsPage("modules", "Módulos", "puzzle", groups);
    }

    private static bool ToggleValue(BarConfig b, string field) { foreach (var t in BarToggles) if (t.Field == field) return t.Get(b); return false; }

    /// <summary>Texto de estado: lo que de verdad ocurre (apagado, en marcha, con causa del fallo, atajo que Windows rechazó).</summary>
    public static string StatusText(bool on, FeatureStatusStore.Item? st, string hotkeyModule)
    {
        if (!on) return "Estado: apagado";
        if (st is null) return "Estado: sin informar todavía (la Barra lo comunica al arrancar)";
        string text = st.State switch
        {
            "Running" => "Estado: en marcha" + (string.IsNullOrEmpty(st.Detail) ? "" : " · " + st.Detail),
            "Starting" => "Estado: arrancando…",
            "Failed" => "Estado: ✗ " + (st.Detail ?? "falló"),
            "Disabled" => "Estado: apagado (la Barra aún no lo ha encendido)",
            _ => "Estado: " + st.State,
        };
        if (hotkeyModule.Length > 0)
            foreach (var k in HotkeyStatus.Items.Where(k => k.Module == hotkeyModule && k.Problem is not null)) text += $"\n⚠ {k.Keys}: {k.Problem}";
        return text;
    }
}
