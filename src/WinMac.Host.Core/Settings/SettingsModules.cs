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
    ];

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
