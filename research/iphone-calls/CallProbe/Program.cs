namespace CallProbe;

/// <summary>
/// CallProbe — prueba de concepto AISLADA de investigación (no es WinMac ni NotchWin). Etapas:
///   stage1                       Descubrimiento (solo lectura)
///   stage2                       Permisos: RequestAccessAsync (consulta) — correr SIN y CON paquete
///   stage3 register|unregister   Registro como proveedor de llamadas (tiene efecto; exige escribir REGISTRAR)
///   stage3 monitor [--seconds N] Observar llamadas (no toca ninguna)
///   stage4 audio                 Dispositivos de audio predeterminados (correr antes y DURANTE una llamada)
///   stage5 answer|reject|hangup  Acción sobre la llamada real actual (exige escribir CONTESTAR/RECHAZAR/COLGAR) [--route-local]
///   stage5 guided                Llamada de prueba completa con preguntas
/// Opciones: --device NOMBRE · --out ARCHIVO · --show-numbers (por defecto nombres y números salen enmascarados)
/// Nunca marca ni inicia llamadas.
/// </summary>
internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        Report.ShowNumbers = args.Contains("--show-numbers");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Report.Open(Opt("--out") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CallProbe", $"probe-{stamp}.txt"));
        Report.Line($"CallProbe · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · args: {string.Join(' ', args.Where(a => a != "--show-numbers"))}");
        if (args.Length == 0) { Report.Line("Uso: CallProbe stage1 | stage2 | stage3 register|unregister|monitor | stage4 audio | stage5 answer|reject|hangup|guided  [--device NOMBRE] [--out ARCHIVO]"); return 2; }
        string? device = Opt("--device");
        try
        {
            switch (args[0].ToLowerInvariant(), args.Length > 1 ? args[1].ToLowerInvariant() : "")
            {
                case ("stage1", _): await Discovery.RunAsync(device); break;
                case ("stage2", _): await Permissions.RunAsync(device); break;
                case ("stage3", "register"): await Calls.RegisterAsync(device); break;
                case ("stage3", "unregister"): await Calls.UnregisterAsync(device); break;
                case ("stage3", "monitor"): await Calls.MonitorAsync(int.TryParse(Opt("--seconds"), out int s) ? s : 120); break;
                case ("stage4", "audio"): await Discovery.RunAsync(device); break;   // la parte de audio de la etapa 1 sirve para comparar antes/durante la llamada
                case ("stage5", "guided"): await Calls.GuidedCallTestAsync(); break;
                case ("stage5", "answer" or "reject" or "hangup"): await Calls.ActAsync(args[1].ToLowerInvariant(), args.Contains("--route-local")); break;
                default: Report.Line("Comando desconocido."); return 2;
            }
        }
        catch (Exception e) { Report.Line($"ERROR NO CONTROLADO: {e.GetType().Name}: {e.Message}"); return 1; }
        return 0;
    }
}
