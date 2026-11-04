using CallProbe.Logic;

namespace CallProbe;

/// <summary>
/// CallProbe — prueba de concepto AISLADA de investigación (no es WinMac ni NotchWin). Etapas:
///   stage1 [--lines]               Descubrimiento (solo lectura). --lines intenta enumerar líneas (con tiempo máximo)
///   stage2                         Permisos: RequestAccessAsync (consulta) — correr SIN y CON paquete
///   stage3 status                  Estado del registro y del diario (solo lectura)
///   stage3 register [opciones]     Registro como proveedor de llamadas: precondiciones, confirmación REGISTRAR, verificación, reversión automática
///        --connect                 además ConnectAsync (segunda confirmación CONECTAR)
///        --keep                    NO revertir al terminar (revertir luego con stage3 unregister)
///        --hold N                  segundos con el registro puesto (0-60, por defecto 10)
///        --allow-phone-link / --allow-disconnected   saltan esos dos bloqueos concretos (nunca el de una llamada real)
///   stage3 unregister              Revertir el registro (UnregisterApp)
///   stage3 monitor [--seconds N]   Observar llamadas (no toca ninguna)
///   stage4 audio                   Dispositivos de audio predeterminados (correr antes y DURANTE una llamada)
///   stage5 answer|reject|hangup    Acción sobre la llamada real actual (exige escribir CONTESTAR/RECHAZAR/COLGAR)  [--route-local]
///   stage5 guided                  Llamada de prueba completa con preguntas
/// Opciones: --device NOMBRE · --out ARCHIVO · --show-numbers (por defecto nombres, números, direcciones y rutas salen enmascarados)
/// Nunca marca ni inicia llamadas. Ctrl+C cancela con orden (y revierte si corresponde).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Steps.Cancel.Cancel(); Report.Line("\n  Ctrl+C: cancelando con orden (se revierte lo que corresponda)…"); };
        int code = 1;
        try { code = Task.Run(() => RunAsync(args)).GetAwaiter().GetResult(); }   // fuera del hilo STA: las esperas no dependen de una bomba de mensajes
        catch (Exception e) { Report.Line($"ERROR NO CONTROLADO: {e.GetType().Name}: {e.Message}"); }
        Report.Line($"\nFin (código {code})."); Environment.Exit(code);   // salida explícita: un hilo de Windows colgado no debe dejar el proceso abierto
        return code;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        args = CommandFile.Resolve(args);
        string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        Report.ShowNumbers = args.Contains("--show-numbers");
        Report.Open(Opt("--out") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CallProbe", $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
        Report.Line($"CallProbe · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · args: {Report.ScrubPath(string.Join(' ', args.Where(a => a != "--show-numbers")))}");
        if (args.Length == 0) { Report.Line("Uso: ver el encabezado de Program.cs / README."); return 2; }
        string? device = Opt("--device");
        var opt = new Stage3Options(Connect: args.Contains("--connect"), Keep: args.Contains("--keep"), AllowPhoneLink: args.Contains("--allow-phone-link"), AllowDisconnected: args.Contains("--allow-disconnected"),
            HoldSeconds: int.TryParse(Opt("--hold"), out int h) ? h : 10);
        switch (args[0].ToLowerInvariant(), args.Length > 1 ? args[1].ToLowerInvariant() : "")
        {
            case ("stage1", _): await Discovery.RunAsync(device, args.Contains("--lines")); return 0;
            case ("stage2", _): await Permissions.RunAsync(device); return 0;
            case ("stage3", "status"): return await Calls.StatusAsync(device);
            case ("stage3", "register"): return await Calls.RegisterAsync(device, opt);
            case ("stage3", "unregister"): return await Calls.UnregisterAsync(device);
            case ("stage3", "observe"): return await Observer.ObserveAsync(device, int.TryParse(Opt("--seconds"), out int os) ? Math.Clamp(os, 1, 600) : 60, int.TryParse(Opt("--interval"), out int oi) ? Math.Max(250, oi) : 1000, args.Contains("--lines"));
            case ("stage3", "monitor"): await Calls.MonitorAsync(int.TryParse(Opt("--seconds"), out int s) ? s : 120); return 0;
            case ("stage4", "audio"): await Discovery.RunAsync(device, lines: false); return 0;   // la parte de audio de la etapa 1 sirve para comparar antes/durante la llamada
            case ("stage5", "guided"): await Calls.GuidedCallTestAsync(); return 0;
            case ("stage5", "answer" or "reject" or "hangup"): await Calls.ActAsync(args[1].ToLowerInvariant(), args.Contains("--route-local")); return 0;
            default: Report.Line("Comando desconocido."); return 2;
        }
    }
}
