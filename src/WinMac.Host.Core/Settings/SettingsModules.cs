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

    internal static readonly List<ModuleCard> ModuleCards =
    [
        new("spotactions", "spotActionsEnabled", "Spotlight Actions", "Spotlight como centro de comandos: capturas, grabación, Launchpad, Ajustes, espacios de trabajo, acciones del sistema y las de los módulos que tengas activos (Mission Control, Snap, Live Text).",
            b => [new(RowKind.Info, "mod.spotactions.info", "Escribe, por ejemplo, «mission», «snap», «live text», «bloquear» o «capturar». Las acciones que cierran sesión, suspenden, reinician o apagan piden un segundo Intro. Si apagas un módulo, sus acciones desaparecen; si apagas este, Spotlight vuelve a su lista clásica.")]),
        new("mission", "mcEnabled", "Hatsu Mission Control", "Vista general de las ventanas abiertas, con miniaturas, búsqueda y varios monitores. Clic o Intro activan una ventana, Supr la cierra, Esc sale.",
            b => [HotkeyRow("mcHotkey", "Atajo", b.McHotkey),
                  new(RowKind.Info, "mod.mission.limit", "Muestra las ventanas del escritorio virtual actual (Windows no ofrece una API pública para listar las de otros escritorios). Las miniaturas son capturas, no vídeo en vivo; las ventanas protegidas muestran la inicial de la app.")], "Barra · Mission Control"),
        new("snap", "snapEnabled", "Hatsu Snap", "Mantén la tecla elegida mientras arrastras una ventana: aparecen las zonas del monitor y al soltar la ventana se coloca en la zona. Sin la tecla, Windows funciona como siempre.",
            b => [new(RowKind.Segmented, "snap.mod", "Tecla al arrastrar", Options: SnapMods, Selected: Math.Max(0, Array.IndexOf(WinMac.Bar.Features.FeatureConfig.SnapModifiers, b.SnapModifier))),
                  new(RowKind.Segmented, "snap.layout", "Distribución", Options: SnapLayoutNames(), Selected: Math.Max(0, SnapLayoutNames().ToList().FindIndex(n => string.Equals(n, b.SnapLayout, StringComparison.OrdinalIgnoreCase)))),
                  new(RowKind.Field, "snap.gap", "Separación entre ventanas (px)", Text: b.SnapGap.ToString(), Placeholder: "0"),
                  HotkeyRow("snapNextHotkey", "Atajo: ventana activa a la zona siguiente", b.SnapNextHotkey),
                  new(RowKind.Button, "snap.edit", "Crear una distribución nueva (editor de zonas)…"),
                  new(RowKind.Info, "mod.snap.limit", "No mueve ventanas sin marco redimensionable, a pantalla completa ni de programas con más permisos que Hatsu (Windows lo impide). Las distribuciones propias se guardan en %APPDATA%\\WinMac\\snap-layouts.json.")], "Barra · Snap"),
        new("livetext", "liveTextEnabled", "Hatsu Live Text", "Elige una región de la pantalla y Hatsu reconoce su texto en tu equipo (sin Internet, nada sale del PC) y lo copia.",
            b => [HotkeyRow("ltHotkey", "Atajo", b.LtHotkey),
                  new(RowKind.Toggle, "bar:ltAutoCopy", "Copiar el texto al reconocerlo", "También se puede copiar desde el panel", b.LtAutoCopy),
                  new(RowKind.Toggle, "bar:ltKeepLineBreaks", "Conservar los saltos de línea", "Apagado, las líneas se unen en un párrafo", b.LtKeepLineBreaks),
                  new(RowKind.Field, "mod.lang", "Idioma de reconocimiento", "Etiqueta como es-ES o en-US. Vacío = los idiomas de tu perfil de Windows", Text: b.LtLanguage, Placeholder: "automático"),
                  new(RowKind.Info, "mod.livetext.limit", "Usa los paquetes de reconocimiento de texto que Windows tenga instalados (el estado de arriba dice cuáles). Regiones muy grandes se rechazan con un aviso.")], "Barra · Live Text"),
        new("gestures", "gesturesEnabled", "Hatsu Gestures", "Esquinas activas, botones laterales del ratón y atajos de teclado que abren Mission Control, Spotlight, Launchpad, Live Text, cambian de escritorio y más.",
            b => [GestRow("gestCornerTL", "Esquina superior izquierda", b.GestCornerTL, b), GestRow("gestCornerTR", "Esquina superior derecha", b.GestCornerTR, b),
                  GestRow("gestCornerBL", "Esquina inferior izquierda", b.GestCornerBL, b), GestRow("gestCornerBR", "Esquina inferior derecha", b.GestCornerBR, b),
                  new(RowKind.Field, "gest.delay", "Espera en la esquina (ms)", "Entre 150 y 2000. Con un botón del ratón pulsado nunca se activa", Text: b.GestCornerDelayMs.ToString(), Placeholder: "450"),
                  GestRow("gestMouseX1", "Botón lateral «atrás» del ratón", b.GestMouseX1, b), GestRow("gestMouseX2", "Botón lateral «adelante» del ratón", b.GestMouseX2, b),
                  HotkeyRow("gestKey1", "Atajo 1", b.GestKey1), GestRow("gestKey1Action", "Acción del atajo 1", b.GestKey1Action, b),
                  HotkeyRow("gestKey2", "Atajo 2", b.GestKey2), GestRow("gestKey2Action", "Acción del atajo 2", b.GestKey2Action, b),
                  new(RowKind.Info, "mod.gest.limit", "No captura gestos del panel táctil (los de 3 y 4 dedos son de Windows y no hay una vía compatible). Un botón lateral solo se reasigna si le pones una acción; sin acción sigue siendo atrás/adelante. Sin esquinas si hay otra pantalla pegada. Cambiar de escritorio envía Win+Ctrl+←/→.")], "Barra · Gestures"),
    ];

    internal static readonly string[] GestFields = ["gestCornerTL", "gestCornerTR", "gestCornerBL", "gestCornerBR", "gestMouseX1", "gestMouseX2", "gestKey1Action", "gestKey2Action"];

    private static string ActionName(string id) => WinMac.Bar.Features.GestureActions.All.FirstOrDefault(a => a.Id == id)?.Name ?? "Nada";
    private static string GestValue(BarConfig b, string field) => field switch
    {
        "gestCornerTL" => b.GestCornerTL, "gestCornerTR" => b.GestCornerTR, "gestCornerBL" => b.GestCornerBL, "gestCornerBR" => b.GestCornerBR,
        "gestMouseX1" => b.GestMouseX1, "gestMouseX2" => b.GestMouseX2, "gestKey1Action" => b.GestKey1Action, _ => b.GestKey2Action,
    };

    /// <summary>Fila que rota entre «Nada» y las acciones disponibles (las de módulos apagados no se ofrecen).</summary>
    public static SettingsRow GestRow(string field, string title, string value, BarConfig b) =>
        new(RowKind.Button, "gest.pick:" + field, $"{title}: {ActionName(value)}{(value != "none" && !WinMac.Bar.Features.GestureRules.IsAvailable(value, b) ? " (módulo apagado: no actúa)" : "")}", "Clic para elegir la acción de una lista");

    /// <summary>Lista desplegable de una asignación: «Nada» + las acciones disponibles (las de módulos apagados no se ofrecen). Null si el campo no es de Gestures.</summary>
    public static IReadOnlyList<(string Id, string Name, bool Current)>? GestureChoices(BarConfig b, string field)
    {
        if (!GestFields.Contains(field)) return null;
        string cur = GestValue(b, field);
        return WinMac.Bar.Features.GestureActions.All.Where(a => a.Id == "none" || WinMac.Bar.Features.GestureRules.IsAvailable(a.Id, b)).Select(a => (a.Id, a.Name, a.Id == cur)).ToList();
    }

    /// <summary>Siguiente acción disponible tras la actual (se conserva para pruebas). Null si el campo no es de Gestures.</summary>
    public static string? NextGestureAction(BarConfig b, string field)
    {
        if (!GestFields.Contains(field)) return null;
        var options = WinMac.Bar.Features.GestureActions.All.Where(a => a.Id == "none" || WinMac.Bar.Features.GestureRules.IsAvailable(a.Id, b)).Select(a => a.Id).ToList();
        int i = options.IndexOf(GestValue(b, field)); return options[(i + 1) % options.Count];
    }

    public static readonly string[] SnapMods = ["Mayús", "Ctrl", "Alt"];
    public static string SnapLayoutsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "snap-layouts.json");

    /// <summary>Predeterminadas + las del usuario (leídas del archivo; si no se puede leer, solo las predeterminadas).</summary>
    public static string[] SnapLayoutNames()
    {
        IReadOnlyList<WinMac.Bar.Features.SnapLayout> custom = [];
        try { if (File.Exists(SnapLayoutsFile)) custom = WinMac.Bar.Features.SnapLayouts.Parse(File.ReadAllText(SnapLayoutsFile)); } catch (Exception) { }
        return WinMac.Bar.Features.SnapLayouts.All(custom).Select(l => l.Name).ToArray();
    }

    /// <summary>Campos de atajo editables desde Ajustes (el patrón de la Barra solo acepta estos).</summary>
    public static readonly string[] HotkeyFields = ["mcHotkey", "ltHotkey", "snapNextHotkey", "gestKey1", "gestKey2"];

    internal static SettingsRow HotkeyRow(string field, string title, string value, string? subtitle = null) =>
        new(RowKind.Field, "mod.key:" + field, title, subtitle ?? "Haz clic y pulsa la combinación (Ctrl o Alt + una tecla). Retroceso la quita", Text: value, Placeholder: "sin atajo");

    internal static SettingsPage? ModulesPage(HostConfig cfg, bool notchInstalled)
    {
        var bar = cfg.Bar ?? new BarConfig();
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

        groups.Add(ShelfGroup(cfg.Notch ?? NotchSettings.Factory, notchInstalled));
        return new SettingsPage("modules", "Módulos", "puzzle", groups);
    }

    /// <summary>Shelf+ es la estantería de la Isla (otro proceso): su interruptor es la pestaña «shelf» y sus límites viajan por la configuración de la Isla.</summary>
    private static SettingsGroup ShelfGroup(NotchSettings n, bool installed)
    {
        const string why = "Hatsu Notch no está instalado junto a Hatsu Desktop.";
        bool on = n.Sections.FirstOrDefault(s => s.Id == "shelf")?.Enabled ?? true;
        var rows = new List<SettingsRow>
        {
            new(RowKind.Toggle, "shelf.enabled", "Hatsu Shelf+", "Bandeja temporal en la Isla: suelta archivos, carpetas, imágenes o texto y arrástralos después a otra app.", on, installed, installed ? null : why),
            new(RowKind.Info, "mod.status:shelf", installed ? (on ? "Estado: activa en la Isla (pestaña «Estantería»)" : "Estado: apagada") : "Estado: " + why),
        };
        if (on && installed)
        {
            rows.Add(new(RowKind.Field, "shelf.max", "Elementos que guarda (1–100)", "El más antiguo sale primero al pasarse", Text: n.ShelfMaxItems.ToString(), Placeholder: "20"));
            rows.Add(new(RowKind.Info, "mod.shelf.info", "Archivos y carpetas se guardan por referencia: nunca se copian, mueven ni borran; si el original desaparece, el elemento se marca «no encontrado». Texto e imágenes sueltas se guardan como copia propia temporal. Al reiniciar, la estantería se vacía. Desde Live Text puedes enviar el texto reconocido aquí."));
        }

        return new(null, rows);
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
