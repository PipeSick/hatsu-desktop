using System.Text;
using System.Text.RegularExpressions;

namespace WinMac.Host;

/// <summary>Lo que el informe necesita saber. Lo reúne el host de Windows (solo lectura); aquí no hay acceso al sistema, así que el informe se prueba en cualquier sistema.</summary>
public sealed record DiagnosticsInput(
    string Version, string InstallDir, string WindowsVersion, string TimeLocal, HostConfig Config, bool ConfigFileExists, IReadOnlyList<string> LegacyFound,
    string AutostartState, bool TaskbarRecoveryFilePresent, IReadOnlyDictionary<string, string> Signatures, IReadOnlyList<string> RunningProcesses,
    IReadOnlyList<string> HostLogTail, string? SecurityReport, string UserProfile, string UserName,
    IReadOnlyList<MonitorArea>? Monitors = null, IReadOnlyList<string>? BarLogTail = null);

/// <summary>Un monitor tal como Windows lo informa: rectángulo completo y área de trabajo (lo que queda tras las barras reservadas), en píxeles.</summary>
public sealed record MonitorArea(string Name, bool Primary, int Left, int Top, int Right, int Bottom, int WorkLeft, int WorkTop, int WorkRight, int WorkBottom, double Scale)
{
    /// <summary>Píxeles que Windows reservó arriba (lo que ocupan las barras registradas con ABM_SETPOS en el borde superior).</summary>
    public int ReservedTop => WorkTop - Top;
    public int ReservedBottom => Bottom - WorkBottom;
}

/// <summary>
/// Informe de diagnóstico LOCAL (Fase 3.1): versión, ruta, componentes elegidos y en ejecución, módulos que no arrancaron (con su causa), estado del asistente, configuración heredada, inicio automático,
/// barra de tareas y firma de los ejecutables. Sin telemetría: se escribe en un archivo del equipo, el usuario lo revisa y decide si lo comparte. No incluye ciudad, calendario, contenidos ni nombres de
/// aplicaciones fijadas; las rutas del perfil de usuario se sustituyen por <c>%USERPROFILE%</c>.
/// </summary>
public static class InstallDiagnostics
{
    public static readonly string[] ModuleExes = ["HatsuDesktop.exe", "HatsuDesktop.Bar.exe", "HatsuDesktop.Dock.exe", "HatsuDesktop.Connect.exe", @"NotchWin\NotchWin.App.exe"];

    public static string Redact(string? text, string userProfile, string userName)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string t = text;
        if (!string.IsNullOrWhiteSpace(userProfile)) t = Regex.Replace(t, Regex.Escape(userProfile), "%USERPROFILE%", RegexOptions.IgnoreCase);
        if (!string.IsNullOrWhiteSpace(userName) && userName.Length >= 3) t = Regex.Replace(t, @"(?<=[\\/]Users[\\/])" + Regex.Escape(userName) + @"(?=[\\/]|$)", "<usuario>", RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>Estado de cada módulo según las líneas del registro del host (la última que aparece) y la última causa de fallo. Solo líneas del supervisor («[bar] Running», «[dock] no se pudo lanzar: …»).</summary>
    public static IReadOnlyDictionary<string, (string State, string? Failure)> ParseLog(IEnumerable<string> lines)
    {
        var r = new Dictionary<string, (string State, string? Failure)>(); var rx = new Regex(@"\[(bar|dock|notch|connect)\]\s+(.*)$");
        foreach (var line in lines)
        {
            var m = rx.Match(line); if (!m.Success) continue; string id = m.Groups[1].Value, msg = m.Groups[2].Value.Trim(); r.TryGetValue(id, out var cur);
            if (Enum.TryParse<ModuleState>(msg, out _)) r[id] = (msg, cur.Failure);
            else if (msg.StartsWith("no se pudo lanzar:") || msg.StartsWith("terminó de forma inesperada") || msg.StartsWith("sin latido") || msg.StartsWith("no llegó a estar listo") || msg.Contains("cortacircuito"))
                r[id] = (cur.State ?? "?", msg);
            else if (msg.StartsWith("desactivado por el usuario")) r[id] = ("Disabled", null);
        }
        return r;
    }

    public static string Build(DiagnosticsInput d)
    {
        var sb = new StringBuilder(); string R(string? s) => Redact(s, d.UserProfile, d.UserName);
        void H(string t) { sb.AppendLine(); sb.AppendLine("== " + t); }
        sb.AppendLine($"Hatsu Desktop — informe de diagnóstico · {d.TimeLocal}");
        sb.AppendLine("Solo lectura: no se cambió nada. Se guarda en tu equipo; NO se envía a ningún sitio. Revísalo antes de compartirlo.");
        sb.AppendLine("No incluye ciudad, calendario, aplicaciones ancladas ni contenidos; las rutas de tu perfil aparecen como %USERPROFILE%.");

        H("Producto");
        sb.AppendLine($"Versión: {d.Version}"); sb.AppendLine($"Carpeta de instalación: {R(d.InstallDir)}"); sb.AppendLine($"Windows: {d.WindowsVersion}");
        sb.AppendLine("Tipo: " + (d.InstallDir.Contains(@"\Programs\Hatsu Desktop", StringComparison.OrdinalIgnoreCase) ? "instalación MSI por usuario (ruta estándar)" : "otra carpeta (copia portable o ruta personalizada)"));

        var c = d.Config; var log = ParseLog(d.HostLogTail);
        H("Componentes elegidos y su estado");
        sb.AppendLine(d.ConfigFileExists ? "Configuración: winmac.json existe" : "Configuración: winmac.json NO existe (instalación nueva o aún sin guardar)");
        foreach (var (id, name) in new[] { ("dock", "Dock"), ("bar", "Barra superior"), ("notch", "Hatsu Notch"), ("connect", "Hatsu Connect") })
        {
            bool on = c.Modules.TryGetValue(id, out var m) ? m.Enabled : true; log.TryGetValue(id, out var l);
            string exe = id == "notch" ? "NotchWin.App" : id == "bar" ? "HatsuDesktop.Bar" : id == "dock" ? "HatsuDesktop.Dock" : "HatsuDesktop.Connect";
            bool running = d.RunningProcesses.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
            string state = !on ? "desactivado por ti (no es un fallo)" : running ? "en ejecución" : "NO está en ejecución" + (l.State is null ? "" : $" (último estado en el registro: {l.State})");
            sb.AppendLine($"  {name}: elegido={(on ? "sí" : "no")} · {state}");
            if (on && !running && l.Failure is { } f) sb.AppendLine($"      causa registrada: {R(f)}");
        }
        var bar = c.Bar; sb.AppendLine($"  Hatsu Peripherals (función de la Barra): {(bar?.ShowPeripherals == true ? "sí" : "no")} · Hatsu Cleanup (función de la Barra): {(bar?.ShowCleanup == true ? "sí" : "no")}");

        H("Reserva del borde superior (Barra superior) y área de trabajo de Windows");
        bool reserveCfg = (c.Bar ?? new WinMac.Bar.BarConfig()).ReserveSpace; bool barRunning = d.RunningProcesses.Any(p => string.Equals(p, "HatsuDesktop.Bar", StringComparison.OrdinalIgnoreCase));
        sb.AppendLine($"  Reservar el borde superior (ajuste): {(reserveCfg ? "activado" : "DESACTIVADO (la Barra se superpone y no reserva espacio)")} · Barra en ejecución: {(barRunning ? "sí" : "no")}");
        if (d.Monitors is { Count: > 0 })
            foreach (var m in d.Monitors)
                sb.AppendLine($"  Monitor {m.Name}{(m.Primary ? " (principal)" : "")}: pantalla {m.Right - m.Left}×{m.Bottom - m.Top} px en ({m.Left},{m.Top}) · escala {m.Scale:0.##} · Windows reservó arriba {m.ReservedTop} px y abajo {m.ReservedBottom} px (área de trabajo desde y={m.WorkTop})");
        else sb.AppendLine("  (no se pudo leer el área de trabajo de los monitores)");
        if (reserveCfg && barRunning && d.Monitors is { Count: > 0 })
            sb.AppendLine(d.Monitors.Any(m => m.ReservedTop > 0) ? "  Lectura: Windows SÍ tiene una banda reservada arriba en al menos un monitor (coherente con la reserva de la Barra; otra barra también podría causarla)."
                : "  Lectura: con la Barra en ejecución y la reserva activada, Windows NO tiene ninguna banda reservada arriba: la reserva no se aplicó (ver las líneas «[bar] …» de abajo).");
        var barLines = (d.BarLogTail ?? []).Where(l => l.Contains("AppBar") || l.Contains("reserva") || l.Contains("área de trabajo") || l.Contains("ABM_") || l.Contains("Explorer")).TakeLast(25).ToList();
        sb.AppendLine(barLines.Count == 0 ? "  bar.log: sin líneas sobre la reserva" : "  bar.log (reserva):"); foreach (var l in barLines) sb.AppendLine("    " + R(l));
        sb.AppendLine("  Nota: Hatsu Desktop reserva espacio con la API estándar de barras de aplicación (AppBar); NO mueve ni reordena los iconos del escritorio. Si Windows no recoloca los iconos que ya estaban en esa zona, se quedan donde estaban.");

        H("Procesos de Hatsu Desktop en ejecución");
        sb.AppendLine(d.RunningProcesses.Count == 0 ? "  ninguno" : "  " + string.Join(", ", d.RunningProcesses.OrderBy(x => x)));

        H("Asistente de configuración");
        var o = c.Onboarding;
        sb.AppendLine(o is null ? "  Estado: sin registro (configuración anterior al asistente)" : $"  Estado: {o.Status} · origen: {o.Origin ?? "?"} · bienvenida de migración mostrada: {(o.WelcomeShownUtc is null ? "no" : "sí")}");
        sb.AppendLine("  Configuración heredada: " + (d.LegacyFound.Count == 0 ? "no se encontró" : string.Join(", ", d.LegacyFound)));

        H("Inicio automático y barra de tareas");
        sb.AppendLine($"  Inicio automático: {R(d.AutostartState)}"); sb.AppendLine($"  Marca en la configuración: {(c.Autostart ? "activado" : "desactivado")}");
        sb.AppendLine($"  Ocultar la barra de tareas de Windows: {(c.HideTaskbar ? "activado" : "desactivado")} · archivo de recuperación: {(d.TaskbarRecoveryFilePresent ? "existe (puede estar oculta; se restaura con HatsuDesktop.exe --restore-taskbar)" : "no existe")}");

        H("Firma digital (Authenticode) de los ejecutables");
        foreach (var exe in ModuleExes) sb.AppendLine($"  {exe}: {(d.Signatures.TryGetValue(exe, out var sig) ? sig : "no está")}");
        sb.AppendLine("  (Una firma válida de un emisor reconocido es lo que Smart App Control y SmartScreen valoran; ver docs/49.)");

        if (!string.IsNullOrWhiteSpace(d.SecurityReport)) { H("Seguridad de Windows (solo lectura)"); sb.AppendLine(R(d.SecurityReport).TrimEnd()); }
        H("Fin del informe");
        return sb.ToString();
    }
}
