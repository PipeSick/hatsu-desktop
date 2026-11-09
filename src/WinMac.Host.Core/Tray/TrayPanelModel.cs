namespace WinMac.Host;

/// <summary>
/// WM-011: the panel of WinMac's own tray icon (it replaces the white Win32 menu): overall status, one row per module with its real state and the action
/// that makes sense for it (Reiniciar / Abrir / Activar), Ajustes, «Reiniciar los módulos» and «Salir de WinMac» (orderly shutdown that restores the desktop).
/// Pure: layout, hit-testing and keyboard are tested without Windows.
/// </summary>
public static class TrayPanelModel
{
    public enum Kind { Header, Module, Separator, Command }

    /// <param name="Action">Event id when activated (null = not clickable). Module rows: «restart:id», «open:id», «enable:id».</param>
    public sealed record Item(Kind Kind, string Text, string? Sub = null, string? Action = null, string? ActionLabel = null, ModuleState? State = null, bool Danger = false);

    public const double Width = 300, Pad = 8;

    public static string Label(string id) => id switch { "bar" => "Barra superior", "dock" => "Dock", "notch" => "Hatsu Notch", "connect" => "Hatsu Connect", _ => id };

    public static string StateText(ModuleState s) => s switch
    {
        ModuleState.Running => "En ejecución", ModuleState.Starting => "Iniciando…", ModuleState.Stopping => "Cerrando…", ModuleState.Stopped => "Detenido", ModuleState.Crashed => "Reiniciando tras un fallo…",
        ModuleState.Failed => "Falló varias veces: no se reintenta", ModuleState.NotInstalled => "No instalada", _ => "Desactivado",
    };

    /// <summary>Colour of the state dot: green running, amber in transition, red failed, grey off.</summary>
    public static string StateColor(ModuleState s) => s switch
    {
        ModuleState.Running => "green", ModuleState.Starting or ModuleState.Stopping or ModuleState.Crashed => "amber", ModuleState.Failed or ModuleState.Stopped => "red", _ => "grey",
    };

    public static IReadOnlyList<Item> Build(IReadOnlyDictionary<string, ModuleState> states, string version, IReadOnlyDictionary<string, ModuleFailure>? failures = null)
    {
        int problems = states.Count(k => k.Value is ModuleState.Failed or ModuleState.Stopped or ModuleState.Crashed);
        var items = new List<Item> { new(Kind.Header, "Hatsu Desktop " + version, problems == 0 ? "Todo en marcha" : problems == 1 ? "1 módulo necesita atención" : $"{problems} módulos necesitan atención") };
        foreach (var (id, st) in states)
        {
            (string? act, string? label) = st switch
            {
                ModuleState.Running => ("restart:" + id, "Reiniciar"),
                ModuleState.Stopped or ModuleState.Failed => ("open:" + id, "Abrir"),
                ModuleState.Disabled => ("enable:" + id, "Activar"),
                _ => (null, null),
            };
            string text = st is ModuleState.Failed && failures is not null && failures.TryGetValue(id, out var fl) ? "No se pudo iniciar · " + fl.Short : StateText(st);
            items.Add(new(Kind.Module, Label(id), text, act, label, st));
        }

        items.Add(new(Kind.Separator, ""));
        items.Add(new(Kind.Command, "Ajustes de Hatsu Desktop…", Action: "settings"));
        items.Add(new(Kind.Command, "Reiniciar los módulos", Action: "restart-all"));
        items.Add(new(Kind.Command, "Salir de Hatsu Desktop", "Cierra todo y devuelve el escritorio a Windows", Action: "quit", Danger: true));
        return items;
    }

    public static double HeightOf(Item i) => i.Kind switch { Kind.Header => 54, Kind.Module => 46, Kind.Separator => 9, _ => i.Sub is null ? 32 : 44 };

    public static IReadOnlyList<(Item Item, double Y, double H)> Layout(IReadOnlyList<Item> items)
    {
        var r = new List<(Item, double, double)>(); double y = Pad;
        foreach (var i in items) { double h = HeightOf(i); r.Add((i, y, h)); y += h; }
        return r;
    }

    public static double Height(IReadOnlyList<Item> items) => Layout(items).Sum(l => l.H) + 2 * Pad;

    /// <summary>The module row's action button on the right (x from the panel's left edge).</summary>
    public static (double X, double W) ButtonX(Item i) => i.ActionLabel is { } l ? (Width - Pad - 12 - (l.Length * 7.2 + 22), l.Length * 7.2 + 22) : (0, 0);

    /// <summary>What a click at (x, y) does: a module row only acts on its button; commands act on the whole row.</summary>
    public static string? Hit(IReadOnlyList<Item> items, double x, double y)
    {
        foreach (var (i, iy, h) in Layout(items))
        {
            if (y < iy || y >= iy + h) continue;
            if (i.Kind == Kind.Command) return i.Action;
            if (i.Kind == Kind.Module && i.Action is not null) { var (bx, bw) = ButtonX(i); return x >= bx && x < bx + bw ? i.Action : null; }
            return null;
        }

        return null;
    }

    /// <summary>Keyboard: the actionable items in order (Up/Down move, Enter runs, Esc closes).</summary>
    public static IReadOnlyList<string> Focusable(IReadOnlyList<Item> items) => items.Where(i => i.Action is not null).Select(i => i.Action!).ToList();
}
