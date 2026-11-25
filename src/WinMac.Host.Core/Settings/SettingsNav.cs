namespace WinMac.Host;

/// <summary>
/// Organización de Ajustes (propuesta A): las secciones se agrupan en cinco bloques con nombre por lo que haces. Se parte de las páginas que construye <see cref="SettingsBuilder.Build"/> y solo se
/// reordenan, se renombran y se juntan: ninguna fila cambia de identificador ni se pierde.
/// </summary>
public static partial class SettingsBuilder
{
    private static readonly Dictionary<string, string> Subtitles = new()
    {
        ["general"] = "Configura el comportamiento general de Hatsu y sus componentes.",
        ["bar"] = "Elementos, indicadores y comportamiento de la barra superior.",
        ["dock"] = "Acceso rápido a tus aplicaciones y ventanas.",
        ["notch"] = "Centro contextual con tus actividades, controles y avisos.",
        ["modules"] = "Activa, desactiva y configura las funciones adicionales de Hatsu.",
        ["gestures"] = "Esquinas activas, botones del ratón y atajos de teclado.",
        ["spot"] = "Búsqueda rápida de aplicaciones, archivos y acciones.",
        ["nc"] = "Centro de notificaciones y historial del portapapeles.",
        ["capture"] = "Capturas de pantalla y grabación: atajos, formato y destino.",
        ["weather"] = "Ubicación, unidades y dónde se muestra el tiempo.",
        ["connect"] = "Batería y notificaciones del iPhone por Bluetooth, sin apps adicionales.",
        ["periph"] = "Teclados, ratones y auriculares del PC: batería y avisos.",
        ["ext"] = "Extensiones, diagnóstico y herramientas de recuperación.",
    };

    internal static IReadOnlyList<SettingsPage> Organize(List<SettingsPage> pages)
    {
        SettingsPage? Get(string id) => pages.FirstOrDefault(p => p.Id == id);
        var result = new List<SettingsPage>();
        void Add(SettingsPage? p, string? section, string? title = null, string? glyph = null) { if (p is null) return; result.Add(p with { Section = section, Title = title ?? p.Title, Glyph = glyph ?? p.Glyph }); }

        // Atajos y gestos: la tarjeta de Gestures sale de Módulos a su propia página (es lo que más se busca).
        var modules = Get("modules"); SettingsPage? gestures = null;
        if (modules is not null)
        {
            var g = modules.Groups.FirstOrDefault(x => x.Rows.Any(r => r.Id == "bar:gesturesEnabled"));
            if (g is not null) { gestures = new SettingsPage("gestures", "Atajos y gestos", "gear", [g]); modules = modules with { Groups = modules.Groups.Where(x => x != g).ToList() }; }
        }

        // iPhone y periféricos: el grupo de Periféricos sale de la Barra superior y se junta con Hatsu Connect.
        var bar = Get("bar"); var connect = Get("connect"); SettingsGroup? periph = null;
        if (bar is not null)
        {
            periph = bar.Groups.FirstOrDefault(x => x.Header == "Periféricos");
            if (periph is not null) bar = bar with { Groups = bar.Groups.Where(x => x != periph).ToList() };
        }

        // Settings V2: iPhone y Periféricos son páginas independientes (las filas conservan sus ids; solo cambian de página).
        SettingsPage? periphPage = periph is null ? null : new SettingsPage("periph", "Periféricos", "periph", [periph with { Header = null }]);

        // Notificaciones y portapapeles: dos páginas pequeñas pasan a una.
        var nc = Get("nc"); var clip = Get("clip"); SettingsPage? both = null;
        if (nc is not null && clip is not null)
        {
            var clipGroups = clip.Groups.Select((g, i) => i == 0 && string.IsNullOrEmpty(g.Header) ? g with { Header = "Portapapeles" } : g).ToList();
            both = new SettingsPage("nc", "Notificaciones y portapapeles", "bell", [.. nc.Groups, .. clipGroups]);
        }

        Add(Get("general"), null);
        Add(bar, "Escritorio"); Add(Get("dock"), "Escritorio", "Dock"); Add(Get("notch"), "Escritorio", "Isla (Hatsu Notch)");
        Add(modules, "Funciones"); Add(gestures, "Funciones"); Add(Get("spot"), "Funciones", "Búsqueda (Spotlight)");
        Add(both ?? nc, "Contenido"); Add(Get("capture"), "Contenido"); Add(Get("weather"), "Contenido", "Clima");
        Add(connect, "Dispositivos", "iPhone (Hatsu Connect)", "phone"); Add(periphPage, "Dispositivos");
        Add(Get("ext"), "Avanzado", "Extensiones y diagnóstico");
        foreach (var p in pages.Where(p => result.All(r => r.Id != p.Id) && p.Id != "clip")) result.Add(p with { Section = "Avanzado" });   // cualquier página nueva no se pierde
        return result.Select(p => p with { Subtitle = p.Subtitle ?? Subtitles.GetValueOrDefault(p.Id) }).ToList();
    }
}
