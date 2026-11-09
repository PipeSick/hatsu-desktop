using WinMac.Bar;
using WinMac.Dock;

namespace WinMac.Host;

/// <summary>Estado del asistente de primera ejecución (Fase 3). Vive en <c>winmac.json › onboarding</c>; las versiones anteriores ignoran la sección.</summary>
public enum OnboardingStatus
{
    /// <summary>Instalación nueva: el asistente se ofrece (los módulos esperan a que el usuario decida).</summary>
    NotStarted,
    /// <summary>Se estaba aplicando o quedó a medias (cierre inesperado, reinicio, aviso sin aceptar): el asistente se retoma en la revisión.</summary>
    InProgress,
    Completed,
    /// <summary>«Omitir configuración»: equivale a completar con los valores seguros de <see cref="OnboardingChoices.Safe"/>. No reaparece.</summary>
    Skipped,
    /// <summary>Configuración anterior al asistente (WinMac o una versión previa): se conserva tal cual y no se fuerza ninguna reconfiguración.</summary>
    Existing,
}

public sealed record OnboardingRecord
{
    public OnboardingStatus Status { get; init; } = OnboardingStatus.NotStarted;
    public int Version { get; init; } = 1;
    /// <summary>«fresh», «winmac-upgrade» o «preexisting» (informativo).</summary>
    public string? Origin { get; init; }
    public DateTime? AtUtc { get; init; }
    /// <summary>Elecciones guardadas ANTES de aplicarlas: permiten retomar tras un corte. Se vacía al completar.</summary>
    public OnboardingChoices? Pending { get; init; }
    /// <summary>Lo que no se pudo aplicar y el usuario aceptó dejar así (informativo; se puede reintentar desde Ajustes).</summary>
    public List<string>? Warnings { get; init; }
    /// <summary>Fase 3.1: cuándo se le mostró (o se descartó) la bienvenida de migración a un usuario con configuración anterior. Null = aún no se le ha ofrecido; con valor no se repite.</summary>
    public DateTime? WelcomeShownUtc { get; init; }
}

/// <summary>Todo lo que el asistente puede decidir. Cada campo está conectado con un ajuste real (módulo, Barra, Dock, inicio automático, barra de tareas); no hay controles decorativos.</summary>
public sealed record OnboardingChoices
{
    public bool Dock { get; init; } = true;
    public bool Bar { get; init; } = true;
    public bool Notch { get; init; } = true;
    public bool Connect { get; init; } = false;
    /// <summary>Periféricos y Limpieza son funciones de la Barra (no procesos): apagadas, la Barra no lee ni ejecuta nada de ellas.</summary>
    public bool Peripherals { get; init; } = false;
    public bool Cleanup { get; init; } = false;
    /// <summary>Funciones extra (módulos de productividad): todas apagadas de entrada. Cinco viven en la Barra; Shelf+ es una pestaña de la Isla.</summary>
    public bool Mission { get; init; } = false;
    public bool Snap { get; init; } = false;
    public bool LiveText { get; init; } = false;
    public bool SpotActions { get; init; } = false;
    public bool Gestures { get; init; } = false;
    public bool Shelf { get; init; } = false;
    /// <summary>Iconos de la barra, uno por uno (los valores de fábrica de la Barra). Periféricos y Limpieza van en sus campos de arriba.</summary>
    public bool IconVolume { get; init; } = true;
    public bool IconPerformance { get; init; } = true;
    public bool IconNetwork { get; init; } = true;
    public bool IconDisks { get; init; } = true;
    public bool IconWeather { get; init; } = true;
    public bool IconWifi { get; init; } = true;
    public bool IconBluetooth { get; init; } = true;
    public bool IconBattery { get; init; } = true;
    public bool IconVpn { get; init; } = false;
    public bool IconTaskManager { get; init; } = false;
    public bool IconTray { get; init; } = true;
    /// <summary>Tema del Dock (dock.json). La Barra y la Isla siguen siempre el tema de Windows; el asistente lo dice.</summary>
    public ThemeMode Theme { get; init; } = ThemeMode.System;
    /// <summary>Índice en <see cref="DockSettings.Presets"/>; -1 = no tocar el tamaño actual.</summary>
    public int DockSize { get; init; } = 1;
    public DockAlignment DockAlignment { get; init; } = DockAlignment.Center;
    public BarBackground BarBackground { get; init; } = BarBackground.Glass;
    public bool Autostart { get; init; } = false;
    public bool HideTaskbar { get; init; } = false;

    /// <summary>Valores seguros: lo de fábrica, sin inicio automático, sin ocultar la barra de tareas y sin nada opcional.</summary>
    public static OnboardingChoices Safe => new();
}

/// <summary>Dependencias y validación (puro). Un componente que necesita a otro lo activa o se desactiva con él, y se le dice al usuario.</summary>
public static class OnboardingRules
{
    public const string ComponentDock = "dock", ComponentBar = "bar", ComponentNotch = "notch", ComponentConnect = "connect", ComponentPeripherals = "peripherals", ComponentCleanup = "cleanup";
    /// <summary>Las tarjetas de «Componentes». Periféricos y Limpieza pasaron a «Iconos de la barra» (son iconos de la Barra, no procesos).</summary>
    public static readonly string[] Components = [ComponentDock, ComponentBar, ComponentNotch, ComponentConnect];

    // ── Funciones extra
    public static readonly string[] ExtraIds = ["mission", "snap", "livetext", "spotactions", "gestures", "shelf"];
    public static bool GetExtra(OnboardingChoices c, string id) => id switch { "mission" => c.Mission, "snap" => c.Snap, "livetext" => c.LiveText, "spotactions" => c.SpotActions, "gestures" => c.Gestures, "shelf" => c.Shelf, _ => false };
    public static OnboardingChoices WithExtra(OnboardingChoices c, string id, bool on) => id switch
    {
        "mission" => c with { Mission = on }, "snap" => c with { Snap = on }, "livetext" => c with { LiveText = on }, "spotactions" => c with { SpotActions = on }, "gestures" => c with { Gestures = on }, "shelf" => c with { Shelf = on }, _ => c,
    };
    public static string ExtraName(string id) => id switch
    {
        "mission" => "Mission Control", "snap" => "Snap", "livetext" => "Live Text", "spotactions" => "Spotlight Actions", "gestures" => "Gestures", "shelf" => "Shelf+", _ => id,
    };
    public static string ExtraDescription(string id) => id switch
    {
        "mission" => "Vista general de todas tus ventanas y escritorios.",
        "snap" => "Acomoda ventanas en zonas y distribuciones. Lee el teclado y el mouse mientras arrastras.",
        "livetext" => "Copia el texto de cualquier imagen o captura. El reconocimiento es local.",
        "spotactions" => "Acciones rápidas desde la búsqueda de Spotlight.",
        "gestures" => "Esquinas, botones laterales del mouse y atajos. Lee el mouse y el teclado.",
        "shelf" => "Estantería de la Isla para archivos, carpetas, texto e imágenes.",
        _ => "",
    };
    /// <summary>Lo que necesita cada extra: cinco viven en la Barra superior y Shelf+ en Hatsu Notch.</summary>
    public static bool ExtraAvailable(OnboardingChoices c, string id) => id == "shelf" ? c.Notch : c.Bar;
    public static string ExtraNeedsText(string id) => id == "shelf" ? "Requiere Hatsu Notch" : "Requiere Barra superior";
    public static string ExtraNeedsLong(string id) => id == "shelf" ? "Hatsu Notch está apagado: actívalo en «Componentes» para usar Shelf+." : "La Barra superior está apagada: actívala en «Componentes» para usar este módulo.";

    // ── Iconos de la barra (uno por uno)
    public static readonly string[] IconIds = ["volume", "performance", "network", "disks", "weather", "wifi", "bluetooth", "battery", "vpn", "cleanup", "peripherals", "taskmanager", "tray"];
    public static string IconName(string id) => id switch
    {
        "volume" => "Sonido", "performance" => "Rendimiento", "network" => "Red", "disks" => "Discos", "weather" => "Tiempo", "wifi" => "Wi-Fi", "bluetooth" => "Bluetooth", "battery" => "Batería",
        "vpn" => "VPN", "cleanup" => "Limpieza", "peripherals" => "Periféricos", "taskmanager" => "Administrador de tareas", "tray" => "Apps en segundo plano", _ => id,
    };
    public static string IconDescription(string id) => id switch
    {
        "volume" => "Volumen y mezclador", "performance" => "CPU, memoria y temperaturas", "network" => "Velocidad y conexiones", "disks" => "Espacio libre y estado", "weather" => "El clima de tus ciudades",
        "wifi" => "Estado de la conexión", "bluetooth" => "Estado y dispositivos", "battery" => "Porcentaje y carga", "vpn" => "Conectar y desconectar tus VPN", "cleanup" => "Analiza primero; nunca limpia sola",
        "peripherals" => "Batería de teclado y mouse ASUS ROG; Logitech «probable»", "taskmanager" => "Abre el de Windows", "tray" => "Iconos de la bandeja, dentro de una flecha", _ => "",
    };
    public static bool GetIcon(OnboardingChoices c, string id) => id switch
    {
        "volume" => c.IconVolume, "performance" => c.IconPerformance, "network" => c.IconNetwork, "disks" => c.IconDisks, "weather" => c.IconWeather, "wifi" => c.IconWifi, "bluetooth" => c.IconBluetooth,
        "battery" => c.IconBattery, "vpn" => c.IconVpn, "cleanup" => c.Cleanup, "peripherals" => c.Peripherals, "taskmanager" => c.IconTaskManager, "tray" => c.IconTray, _ => false,
    };
    public static OnboardingChoices WithIcon(OnboardingChoices c, string id, bool on) => id switch
    {
        "volume" => c with { IconVolume = on }, "performance" => c with { IconPerformance = on }, "network" => c with { IconNetwork = on }, "disks" => c with { IconDisks = on }, "weather" => c with { IconWeather = on },
        "wifi" => c with { IconWifi = on }, "bluetooth" => c with { IconBluetooth = on }, "battery" => c with { IconBattery = on }, "vpn" => c with { IconVpn = on }, "cleanup" => c with { Cleanup = on },
        "peripherals" => c with { Peripherals = on }, "taskmanager" => c with { IconTaskManager = on }, "tray" => c with { IconTray = on }, _ => c,
    };

    public static bool Get(OnboardingChoices c, string id) => id switch
    {
        ComponentDock => c.Dock, ComponentBar => c.Bar, ComponentNotch => c.Notch, ComponentConnect => c.Connect, ComponentPeripherals => c.Peripherals, ComponentCleanup => c.Cleanup, _ => false,
    };

    public static string Name(string id) => id switch
    {
        ComponentDock => "Dock", ComponentBar => "Barra superior", ComponentNotch => "Hatsu Notch", ComponentConnect => "Hatsu Connect", ComponentPeripherals => "Hatsu Peripherals", ComponentCleanup => "Hatsu Cleanup", _ => id,
    };

    public static string Description(string id) => id switch
    {
        ComponentDock => "Barra de aplicaciones inspirada en macOS, con vista previa de ventanas.",
        ComponentBar => "Controles del sistema, menús, reloj y bandeja.",
        ComponentNotch => "Música, controles multimedia, letras sincronizadas y otras funciones de Hatsu Notch.",
        ComponentConnect => "Batería y notificaciones del iPhone (herramientas de conectividad actuales).",
        ComponentPeripherals => "Batería de ratón, teclado y auriculares compatibles, con avisos opcionales.",
        ComponentCleanup => "Limpieza conservadora: analiza primero y pide confirmación; nunca limpia por sí sola.",
        _ => "",
    };

    /// <summary>Peripherals y Cleanup viven en la Barra superior: la tarjeta lo dice con «Requiere Barra superior».</summary>
    public static bool RequiresBar(string id) => id is ComponentPeripherals or ComponentCleanup;
    public const string RequiresBarText = "Requiere Barra superior";

    /// <summary>Qué necesita cada componente (el texto que se muestra), o null.</summary>
    public static string? Requirement(string id) => id switch
    {
        ComponentPeripherals => "Necesita la Barra superior (ahí vive su icono).",
        ComponentCleanup => "Necesita la Barra superior (ahí vive su icono). El progreso se muestra en Hatsu Notch si está activado.",
        ComponentConnect => "El indicador del iPhone aparece en la Barra superior.",
        _ => null,
    };

    /// <summary>Cambia un componente respetando las dependencias. Devuelve las elecciones nuevas y un aviso para el usuario (o null).</summary>
    public static (OnboardingChoices Choices, string? Note) Toggle(OnboardingChoices c, string id, bool on)
    {
        string? note = null;
        switch (id)
        {
            case ComponentDock:
                c = c with { Dock = on };
                if (!on && c.HideTaskbar) { c = c with { HideTaskbar = false }; note = "Sin el Dock no se oculta la barra de tareas de Windows: se dejó visible."; }
                break;
            case ComponentBar:
                c = c with { Bar = on };
                if (!on && (c.Peripherals || c.Cleanup)) { c = c with { Peripherals = false, Cleanup = false }; note = "Periféricos y Limpieza viven en la Barra superior: se desactivaron también."; }
                if (!on && (c.Mission || c.Snap || c.LiveText || c.SpotActions || c.Gestures)) { c = c with { Mission = false, Snap = false, LiveText = false, SpotActions = false, Gestures = false }; note = "Los módulos de «Funciones extra» viven en la Barra superior: se desactivaron también."; }
                break;
            case ComponentNotch:
                c = c with { Notch = on };
                if (!on && c.Shelf) { c = c with { Shelf = false }; note = "Shelf+ vive en Hatsu Notch: se desactivó también."; }
                break;
            case ComponentConnect: c = c with { Connect = on }; if (on && !c.Bar) note = "Hatsu Connect funcionará, pero su indicador se muestra en la Barra superior (desactivada)."; break;
            case ComponentPeripherals:
                c = c with { Peripherals = on }; if (on && !c.Bar) { c = c with { Bar = true }; note = "Periféricos necesita la Barra superior: se activó también."; } break;
            case ComponentCleanup:
                c = c with { Cleanup = on }; if (on && !c.Bar) { c = c with { Bar = true }; note = "Limpieza necesita la Barra superior: se activó también."; } break;
        }
        return (c, note);
    }

    /// <summary>Normaliza elecciones incoherentes (p. ej. de un archivo editado a mano) y dice qué cambió.</summary>
    public static (OnboardingChoices Choices, IReadOnlyList<string> Notes) Validate(OnboardingChoices c)
    {
        var notes = new List<string>();
        if (!c.Bar && c.Peripherals) { c = c with { Peripherals = false }; notes.Add("Periféricos necesita la Barra superior: se dejó desactivado."); }
        if (!c.Bar && c.Cleanup) { c = c with { Cleanup = false }; notes.Add("Limpieza necesita la Barra superior: se dejó desactivado."); }
        if (!c.Bar && (c.Mission || c.Snap || c.LiveText || c.SpotActions || c.Gestures)) { c = c with { Mission = false, Snap = false, LiveText = false, SpotActions = false, Gestures = false }; notes.Add("Los módulos extra viven en la Barra superior: se dejaron desactivados."); }
        if (!c.Notch && c.Shelf) { c = c with { Shelf = false }; notes.Add("Shelf+ vive en Hatsu Notch: se dejó desactivado."); }
        if (!c.Dock && c.HideTaskbar) { c = c with { HideTaskbar = false }; notes.Add("Sin el Dock no se oculta la barra de tareas de Windows: se dejó visible."); }
        if (c.DockSize < -1 || c.DockSize >= DockSettings.Presets.Length) { c = c with { DockSize = 1 }; notes.Add("Tamaño del Dock fuera de rango: se usó «Mediano»."); }
        if (!Enum.IsDefined(c.Theme)) c = c with { Theme = ThemeMode.System };
        if (!Enum.IsDefined(c.DockAlignment)) c = c with { DockAlignment = DockAlignment.Center };
        if (!Enum.IsDefined(c.BarBackground)) c = c with { BarBackground = BarBackground.Glass };
        return (c, notes);
    }

    /// <summary>Elecciones iniciales de un asistente que se reabre desde Ajustes: lo que hay hoy.</summary>
    public static OnboardingChoices FromCurrent(HostConfig cfg, DockConfig? dock, bool autostart)
    {
        bool On(string m) => cfg.Modules.TryGetValue(m, out var mc) ? mc.Enabled : true;
        var bar = cfg.Bar ?? new BarConfig();
        int size = dock is null ? -1 : Array.FindIndex(DockSettings.Presets, p => Math.Abs(p.Size - dock.IconSize) < 0.5);
        bool shelf = (cfg.Notch ?? NotchSettings.Factory).Sections.Any(sec => sec.Id == "shelf" && sec.Enabled);
        return new OnboardingChoices
        {
            Dock = On("dock"), Bar = On("bar"), Notch = On("notch"), Connect = On("connect"), Peripherals = bar.ShowPeripherals, Cleanup = bar.ShowCleanup,
            Mission = bar.McEnabled, Snap = bar.SnapEnabled, LiveText = bar.LiveTextEnabled, SpotActions = bar.SpotActionsEnabled, Gestures = bar.GesturesEnabled, Shelf = shelf,
            IconVolume = bar.ShowVolume, IconPerformance = bar.ShowHub && bar.ShowHubSystem, IconNetwork = bar.ShowHub && bar.ShowHubNetwork, IconDisks = bar.ShowHub && bar.ShowHubDisks, IconWeather = bar.ShowWeather,
            IconWifi = bar.ShowNetwork, IconBluetooth = bar.ShowBluetooth, IconBattery = bar.ShowBattery, IconVpn = bar.ShowVpn, IconTaskManager = bar.ShowTaskManager, IconTray = bar.ShowTrayIcons,
            Theme = dock?.Theme ?? ThemeMode.System, DockSize = size, DockAlignment = dock?.Alignment ?? DockAlignment.Center, BarBackground = bar.Background, Autostart = autostart, HideTaskbar = cfg.HideTaskbar,
        };
    }

    public static readonly string[] ThemeNames = ["Seguir Windows", "Claro", "Oscuro"], AlignmentNames = ["Izquierda", "Centro", "Derecha"], BackgroundNames = ["Vidrio", "Transparente"];

    /// <summary>Hasta tres nombres y «…» si hay más: la línea de la revisión es de una sola línea.</summary>
    private static string Brief(IReadOnlyList<string> names) => names.Count > 3 ? string.Join(", ", names.Take(3)) + "…" : string.Join(", ", names);

    /// <summary>Resumen para la pantalla de revisión: una línea por decisión, con lo que cambia en Windows destacado al final.</summary>
    public static IReadOnlyList<(string Label, string Value)> Review(OnboardingChoices c)
    {
        string Onoff(bool b) => b ? "Activado" : "Desactivado";
        var l = new List<(string, string)>
        {
            ("Dock", Onoff(c.Dock)), ("Barra superior", Onoff(c.Bar)), ("Hatsu Notch", Onoff(c.Notch)), ("Hatsu Connect", Onoff(c.Connect)),
        };
        if (c.Bar) { var on = IconIds.Where(i => GetIcon(c, i)).Select(IconName).ToList(); l.Add(("Iconos de la barra", on.Count == 0 ? "Ninguno" : $"{on.Count} de {IconIds.Length}: " + Brief(on))); }
        var extras = ExtraIds.Where(i => GetExtra(c, i)).Select(ExtraName).ToList(); l.Add(("Funciones extra", extras.Count == 0 ? "Ninguna" : $"{extras.Count}: " + Brief(extras)));
        l.Add(("Tema del Dock", ThemeNames[(int)c.Theme]));
        if (c.Dock) l.Add(("Tamaño y posición del Dock", $"{(c.DockSize >= 0 ? DockSettings.Presets[c.DockSize].Name : "tamaño actual")} · {AlignmentNames[(int)c.DockAlignment]}"));
        if (c.Bar) l.Add(("Fondo de la Barra", BackgroundNames[c.BarBackground == BarBackground.Glass ? 0 : 1]));
        l.Add(("Iniciar con Windows", c.Autostart ? "Sí: entrada de inicio solo para tu usuario" : "No"));
        l.Add(("Barra de tareas de Windows", c.HideTaskbar ? "Se oculta mientras funcione; se restaura al salir" : "Se queda como está"));
        return l;
    }
}

/// <summary>Qué hacer al arrancar el host respecto al asistente.</summary>
/// <summary>ShowMigrationWelcome: usuario con configuración anterior (WinMac o Hatsu previo) al que aún no se le ofreció la bienvenida; no se fuerza nada y sus módulos arrancan como siempre.</summary>
public enum OnboardingAction { None, ShowWizard, ResumeWizard, ShowMigrationWelcome }

public static class OnboardingDecision
{
    /// <param name="configFileExisted">winmac.json existía antes de este arranque.</param>
    /// <param name="legacyDataExists">Hay datos de WinMac/Hatsu (bar.json, dock.json…) aunque no haya winmac.json.</param>
    public static (OnboardingAction Action, OnboardingRecord Record) Decide(HostConfig cfg, bool configFileExisted, bool legacyDataExists)
    {
        if (cfg.Onboarding is { } r)
            return r.Status switch
            {
                OnboardingStatus.InProgress => (OnboardingAction.ResumeWizard, r),
                OnboardingStatus.NotStarted => (OnboardingAction.ShowWizard, r),
                OnboardingStatus.Existing when r.WelcomeShownUtc is null => (OnboardingAction.ShowMigrationWelcome, r),
                _ => (OnboardingAction.None, r),
            };
        // sin registro: configuración de WinMac o de Hatsu Desktop anterior al asistente → se conserva (no se fuerza el asistente) y se le ofrece UNA bienvenida de migración; instalación nueva → asistente
        if (configFileExisted || legacyDataExists) return (OnboardingAction.ShowMigrationWelcome, new OnboardingRecord { Status = OnboardingStatus.Existing, Origin = configFileExisted ? "winmac-upgrade" : "preexisting", AtUtc = DateTime.UtcNow });
        return (OnboardingAction.ShowWizard, new OnboardingRecord { Status = OnboardingStatus.NotStarted, Origin = "fresh" });
    }

    /// <summary>Instalación nueva: hasta que el usuario decida, ningún módulo arranca (ni se altera el escritorio).</summary>
    public static HostConfig GateFresh(HostConfig cfg, OnboardingRecord rec) =>
        cfg with { Onboarding = rec, Modules = cfg.Modules.ToDictionary(kv => kv.Key, kv => kv.Value with { Enabled = false }) };
}

/// <summary>Cálculo puro de la configuración resultante.</summary>
public static class OnboardingPlan
{
    public static HostConfig Apply(HostConfig cfg, OnboardingChoices c, OnboardingRecord record)
    {
        var mods = new Dictionary<string, ModuleConfig>(cfg.Modules);
        void Set(string id, bool on) { mods[id] = (mods.TryGetValue(id, out var m) ? m : new ModuleConfig()) with { Enabled = on }; }
        Set("dock", c.Dock); Set("bar", c.Bar); Set("notch", c.Notch); Set("connect", c.Connect);
        var bar = (cfg.Bar ?? new BarConfig()) with
        {
            ShowPeripherals = c.Peripherals, ShowCleanup = c.Cleanup, Background = c.BarBackground,
            McEnabled = c.Mission, SnapEnabled = c.Snap, LiveTextEnabled = c.LiveText, SpotActionsEnabled = c.SpotActions, GesturesEnabled = c.Gestures,
            ShowVolume = c.IconVolume, ShowHubSystem = c.IconPerformance, ShowHubNetwork = c.IconNetwork, ShowHubDisks = c.IconDisks, ShowHub = c.IconPerformance || c.IconNetwork || c.IconDisks,
            ShowWeather = c.IconWeather, ShowNetwork = c.IconWifi, ShowBluetooth = c.IconBluetooth, ShowBattery = c.IconBattery, ShowVpn = c.IconVpn, ShowTaskManager = c.IconTaskManager, ShowTrayIcons = c.IconTray,
        };
        // Shelf+ es la pestaña «shelf» de la Isla: solo se toca si cambia (una instalación nueva sin ajustes de la Isla conserva los de fábrica cuando coinciden)
        var notch = cfg.Notch ?? NotchSettings.Factory;
        if (notch.Sections.Any(sec => sec.Id == "shelf" && sec.Enabled != c.Shelf)) cfg = cfg with { Notch = notch with { Sections = notch.Sections.Select(sec => sec.Id == "shelf" ? sec with { Enabled = c.Shelf } : sec).ToList() } };
        // Periféricos sin avisos propios: los avisos son una opción aparte de Ajustes (el asistente no activa nada que escriba notificaciones)
        return cfg with { Modules = mods, Bar = bar.Sanitize(), HideTaskbar = c.HideTaskbar, Autostart = c.Autostart, Onboarding = record };
    }

    public static DockConfig ApplyDock(DockConfig d, OnboardingChoices c)
    {
        d = d with { Theme = c.Theme };
        d = DockSettings.WithAlignment(d, c.DockAlignment);
        if (c.DockSize >= 0 && c.DockSize < DockSettings.Presets.Length) d = DockSettings.WithIconSize(d, DockSettings.Presets[c.DockSize].Size);
        return d;
    }
}

public enum ApplyOutcome { Ok, Warning, Failed }
/// <param name="Detail">Texto para el usuario («No se pudo iniciar»).</param>
/// <param name="Technical">Detalle técnico aparte (estado del supervisor…), disponible en «Detalles técnicos» y en el registro; null si no hay.</param>
/// <param name="Inactive">Desactivado a propósito: no es un acierto ni un fallo (indicador neutro).</param>
public sealed record ApplyItem(string Id, string Label, ApplyOutcome Outcome, string? Detail = null, string? Technical = null, bool Inactive = false, FailureKind? Cause = null);

public sealed record ApplyResult(IReadOnlyList<ApplyItem> Items, OnboardingStatus Status, bool RolledBack)
{
    public bool EssentialFailed => Items.Any(i => i.Outcome == ApplyOutcome.Failed);
    public bool HasWarnings => Items.Any(i => i.Outcome == ApplyOutcome.Warning);
    public bool AllOk => !EssentialFailed && !HasWarnings;
}

/// <summary>Lo que el aplicador necesita del host. Se sustituye por un doble en las pruebas.</summary>
public interface IOnboardingPorts
{
    HostConfig Config { get; }
    /// <summary>Aplica un cambio a winmac.json por el dueño único (ConfigService); devuelve el error de guardado en disco o null.</summary>
    string? Update(Func<HostConfig, HostConfig> change);
    DockConfig? LoadDock();
    bool SaveDock(DockConfig d);
    bool AutostartOn { get; }
    /// <summary>Lanza si no se pudo (registro bloqueado por una directiva, etc.).</summary>
    void SetAutostart(bool on);
    IReadOnlyDictionary<string, ModuleState> ModuleStates { get; }
    /// <summary>Causa conocida de cada módulo que no está en marcha (vacío si todo va bien).</summary>
    IReadOnlyDictionary<string, ModuleFailure> ModuleFailures { get; }
    /// <summary>Espera (hasta el plazo) a que el módulo salga de Starting; se sustituye por un reloj falso en las pruebas.</summary>
    Task DelayAsync(TimeSpan t);
}

/// <summary>
/// Aplicación TRANSACCIONAL de las elecciones: valida → guarda las elecciones pendientes → aplica (Dock, inicio automático, configuración) → verifica los módulos → marca como completado
/// solo si lo esencial quedó aplicado. Ante un fallo esencial restaura el estado anterior (configuración, dock.json, inicio automático). Los módulos que no arrancan se informan; nunca se declara éxito.
/// Esencial = guardar la configuración y el inicio automático. Aviso = ajustes del Dock y arranque de módulos.
/// </summary>
public sealed class OnboardingApplier(IOnboardingPorts ports, Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });
    public TimeSpan ModuleTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public async Task<ApplyResult> ApplyAsync(OnboardingChoices requested, bool skipped = false)
    {
        var (c, notes) = OnboardingRules.Validate(requested);
        var items = new List<ApplyItem>(); foreach (var n in notes) items.Add(new("rules", "Ajuste de dependencias", ApplyOutcome.Warning, n));
        var oldCfg = ports.Config; var oldDock = ports.LoadDock(); bool oldAutostart = ports.AutostartOn;
        string origin = oldCfg.Onboarding?.Origin ?? "fresh";

        // 1) las elecciones pendientes se guardan primero: si algo se corta, el asistente se retoma con ellas
        var pending = new OnboardingRecord { Status = OnboardingStatus.InProgress, Origin = origin, Pending = c, AtUtc = DateTime.UtcNow };
        string? err = ports.Update(cfg => cfg with { Onboarding = pending });
        if (err is not null) { items.Add(new("config", "Guardar la configuración", ApplyOutcome.Failed, "No se pudo escribir winmac.json: " + err)); return new(items, OnboardingStatus.InProgress, false); }

        // 2) Dock (dock.json es del Dock; solo se toca si existe y se puede leer)
        if (c.Dock && oldDock is not null)
        {
            var nd = OnboardingPlan.ApplyDock(oldDock, c);
            if (nd != oldDock) items.Add(ports.SaveDock(nd) ? new("dock", "Apariencia del Dock", ApplyOutcome.Ok) : new("dock", "Apariencia del Dock", ApplyOutcome.Warning, "No se pudo escribir dock.json; el Dock conserva su aspecto actual."));
        }
        else if (c.Dock) items.Add(new("dock", "Apariencia del Dock", ApplyOutcome.Warning, "El Dock aún no creó dock.json: se podrá ajustar desde Ajustes › Dock."));

        // 3) inicio automático (esencial); solo si cambia
        if (c.Autostart != oldAutostart)
        {
            try { ports.SetAutostart(c.Autostart); items.Add(new("autostart", "Inicio automático", ApplyOutcome.Ok, c.Autostart ? "Activado" : "Desactivado")); }
            catch (Exception e) { items.Add(new("autostart", "Inicio automático", ApplyOutcome.Failed, e.Message)); return Rollback(items, oldCfg, oldDock, oldAutostart, "inicio automático"); }
        }

        // 4) configuración final (módulos, Barra, barra de tareas) — esto arranca/para los módulos a través del host
        var done = new OnboardingRecord { Status = skipped ? OnboardingStatus.Skipped : OnboardingStatus.Completed, Origin = origin, AtUtc = DateTime.UtcNow };
        err = ports.Update(cfg => OnboardingPlan.Apply(cfg, c, pending));   // aún «en curso»: se marca completo solo tras verificar
        if (err is not null) { items.Add(new("config", "Guardar la configuración", ApplyOutcome.Failed, "No se pudo escribir winmac.json: " + err)); return Rollback(items, oldCfg, oldDock, oldAutostart, "configuración"); }
        items.Add(new("config", "Guardar la configuración", ApplyOutcome.Ok));
        if (c.HideTaskbar) items.Add(new("taskbar", "Barra de tareas de Windows", ApplyOutcome.Ok, "Se oculta mientras Hatsu Desktop funcione y se restaura al salir."));

        // 5) verificación de módulos: no se declara éxito si uno no arrancó
        foreach (var (id, on) in new[] { ("bar", c.Bar), ("dock", c.Dock), ("notch", c.Notch), ("connect", c.Connect) })
        {
            string label = OnboardingRules.Name(id);
            if (!on) { items.Add(new("mod:" + id, label, ApplyOutcome.Ok, "Desactivado: no se inicia", Inactive: true)); continue; }
            var st = await WaitAsync(id).ConfigureAwait(false);
            var f = ports.ModuleFailures.TryGetValue(id, out var ff) ? ff : null;
            items.Add(st switch
            {
                ModuleState.Running => new("mod:" + id, label, ApplyOutcome.Ok, "En ejecución"),
                ModuleState.NotInstalled => new("mod:" + id, label, ApplyOutcome.Warning, "No está incluido en esta instalación", "estado del supervisor: NotInstalled", Cause: FailureKind.Missing),
                _ => new("mod:" + id, label, ApplyOutcome.Warning, st == ModuleState.Starting ? "Sigue iniciándose" : "No se pudo iniciar",
                    f is null ? "estado del supervisor: " + st : $"{f.Reason}. {f.Advice} [{f.Technical}; estado del supervisor: {st}]", Cause: f?.Kind),
            });
        }

        // Funciones extra: viven en la Barra; aquí solo se informa de lo que quedó activado (su salud se ve en Ajustes › Módulos)
        var onExtras = OnboardingRules.ExtraIds.Where(i => OnboardingRules.GetExtra(c, i)).Select(OnboardingRules.ExtraName).ToList();
        if (onExtras.Count > 0) items.Add(new("extras", "Funciones extra", ApplyOutcome.Ok, "Activadas: " + string.Join(", ", onExtras)));

        // 6) completado solo si no quedó ningún fallo ni aviso; con avisos queda «en curso» hasta que el usuario reintente o acepte
        if (items.All(i => i.Outcome == ApplyOutcome.Ok))
        {
            err = ports.Update(cfg => cfg with { Onboarding = done });
            if (err is not null) { items.Add(new("record", "Marcar el asistente como completado", ApplyOutcome.Failed, err)); return Rollback(items, oldCfg, oldDock, oldAutostart, "registro"); }
            return new(items, done.Status, false);
        }
        return new(items, OnboardingStatus.InProgress, false);
    }

    /// <summary>El usuario acepta los avisos (p. ej. un módulo que no arranca): el asistente se da por completado y los avisos quedan anotados.</summary>
    public string? AcceptWarnings(ApplyResult r, bool skipped = false)
    {
        var w = r.Items.Where(i => i.Outcome == ApplyOutcome.Warning).Select(i => $"{i.Label}: {i.Detail}" + (i.Technical is null ? "" : $" ({i.Technical})")).ToList();
        return ports.Update(cfg => cfg with { Onboarding = new OnboardingRecord { Status = skipped ? OnboardingStatus.Skipped : OnboardingStatus.Completed, Origin = cfg.Onboarding?.Origin, AtUtc = DateTime.UtcNow, Warnings = w } });
    }

    private async Task<ModuleState> WaitAsync(string id)
    {
        var until = ModuleTimeout; var step = TimeSpan.FromMilliseconds(250); var waited = TimeSpan.Zero; ModuleState st = ModuleState.Starting;
        while (true)
        {
            st = ports.ModuleStates.TryGetValue(id, out var s) ? s : ModuleState.Disabled;
            if (st is ModuleState.Running or ModuleState.NotInstalled or ModuleState.Failed or ModuleState.Crashed or ModuleState.Stopped) return st;
            if (waited >= until) return st;
            await ports.DelayAsync(step).ConfigureAwait(false); waited += step;
        }
    }

    private ApplyResult Rollback(List<ApplyItem> items, HostConfig oldCfg, DockConfig? oldDock, bool oldAutostart, string why)
    {
        _log($"[asistente] fallo esencial ({why}): se restaura el estado anterior");
        bool ok = true;
        try { if (ports.AutostartOn != oldAutostart) ports.SetAutostart(oldAutostart); } catch (Exception e) { ok = false; _log("[asistente] no se pudo restaurar el inicio automático: " + e.Message); }
        if (oldDock is not null) { try { ports.SaveDock(oldDock); } catch (Exception e) { ok = false; _log("[asistente] no se pudo restaurar dock.json: " + e.Message); } }
        // vuelve la configuración anterior, con el asistente aún pendiente (nunca «completado»)
        var restore = oldCfg with { Onboarding = (oldCfg.Onboarding ?? new OnboardingRecord()) with { Status = oldCfg.Onboarding?.Status == OnboardingStatus.NotStarted || oldCfg.Onboarding is null ? OnboardingStatus.NotStarted : oldCfg.Onboarding.Status, Pending = null } };
        string? e2 = ports.Update(_ => restore); if (e2 is not null) ok = false;
        items.Add(new("rollback", "Restaurar el estado anterior", ok ? ApplyOutcome.Ok : ApplyOutcome.Warning, ok ? "No se dejó nada a medias." : "No todo se pudo restaurar; revisa Ajustes."));
        return new(items, restore.Onboarding!.Status, true);
    }
}
