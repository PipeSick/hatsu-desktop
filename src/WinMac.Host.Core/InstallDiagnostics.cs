using System.Text;
using System.Text.RegularExpressions;

namespace WinMac.Host;

/// <summary>Lo que el informe necesita saber. Lo reúne el host de Windows (solo lectura); aquí no hay acceso al sistema, así que el informe se prueba en cualquier sistema.</summary>
public sealed record DiagnosticsInput(
    string Version, string InstallDir, string WindowsVersion, string TimeLocal, HostConfig Config, bool ConfigFileExists, IReadOnlyList<string> LegacyFound,
    string AutostartState, bool TaskbarRecoveryFilePresent, IReadOnlyDictionary<string, string> Signatures, IReadOnlyList<string> RunningProcesses,
    IReadOnlyList<string> HostLogTail, string? SecurityReport, string UserProfile, string UserName);

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
        foreach (var (id, name) in new[] { ("dock", "Dock"), ("bar", "Barra superior"), ("notch", "Dynamic Island"), ("connect", "Hatsu Connect") })
        {
            bool on = c.Modules.TryGetValue(id, out var m) ? m.Enabled : true; log.TryGetValue(id, out var l);
            string exe = id == "notch" ? "NotchWin.App" : id == "bar" ? "HatsuDesktop.Bar" : id == "dock" ? "HatsuDesktop.Dock" : "HatsuDesktop.Connect";
            bool running = d.RunningProcesses.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
            string state = !on ? "desactivado por ti (no es un fallo)" : running ? "en ejecución" : "NO está en ejecución" + (l.State is null ? "" : $" (último estado en el registro: {l.State})");
            sb.AppendLine($"  {name}: elegido={(on ? "sí" : "no")} · {state}");
            if (on && !running && l.Failure is { } f) sb.AppendLine($"      causa registrada: {R(f)}");
        }
        var bar = c.Bar; sb.AppendLine($"  Hatsu Peripherals (función de la Barra): {(bar?.ShowPeripherals == true ? "sí" : "no")} · Hatsu Cleanup (función de la Barra): {(bar?.ShowCleanup == true ? "sí" : "no")}");

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
