// WinMac Connect — módulo opcional (docs/39). Lo lanza y vigila el host (WinMac.exe) con --hosted; APAGADO por defecto (Ajustes › General).
// Batería y estado del iPhone (Battery Service) y sus notificaciones (ANCS) por Bluetooth LE, con la API pública de Windows. Sin app en el iPhone, sin Phone Link,
// sin servidores. No lee, envía ni borra mensajes; no contesta ni rechaza llamadas (solo avisa).
using WinMac.Connect;
using WinMac.Connect.App;
using WinMac.Ipc;

internal static class Program
{
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "connect.log");
    private static readonly object LogLock = new();

    private static void Log(string m)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024) File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {m}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--probe-hfp-experimental") || args.Contains("--probe-audio-route") || args.Contains("--restore-hfp"))   // experimentos acotados y reversibles (docs/59): solo los lanza Ajustes tras una confirmación
        {
            int oi2 = Array.IndexOf(args, "--out"); string outFile2 = oi2 >= 0 && oi2 + 1 < args.Length ? args[oi2 + 1] : Path.Combine(Path.GetDirectoryName(LogPath)!, "connect-probe-hfp.txt");
            string text2;
            try { text2 = args.Contains("--restore-hfp") ? HfpExperiments.Restore() : args.Contains("--probe-audio-route") ? await HfpExperiments.AudioRouteAsync() : await HfpExperiments.RegisterAndConnectAsync(); }
            catch (Exception e) { text2 = "== Experimento ==\nRESUMEN|Experimento: Error de ejecución — " + e.GetType().Name + " 0x" + e.HResult.ToString("X8") + " — " + DiagIds.HResultName(e.HResult) + "\n"; }
            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile2))!); File.WriteAllText(outFile2, text2); Log("[connect] experimento HFP terminado"); } catch (Exception) { }
            return 0;
        }
        if (args.Contains("--probe-calls"))   // diagnóstico de solo lectura (docs/57-58): lo lanza Ajustes o se ejecuta a mano; escribe el informe y termina. No necesita el host
        {
            int oi = Array.IndexOf(args, "--out"); string outFile = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : Path.Combine(Path.GetDirectoryName(LogPath)!, "connect-probe-calls.txt");
            try
            {
                var (_, text) = await CallProbe.RunAsync(LogPath);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!); File.WriteAllText(outFile, text);
                Log("[connect] diagnóstico de telefonía escrito");
                return 0;
            }
            catch (Exception e) { Log("[connect] diagnóstico de telefonía falló: " + e.GetType().Name); try { File.WriteAllText(outFile, "== Diagnóstico ==\nRESUMEN|Diagnóstico: Error de ejecución — " + e.GetType().Name + " 0x" + e.HResult.ToString("X8") + "\n"); } catch (Exception) { } return 1; }
        }
        using var single = new Mutex(true, "WinMac.Connect.SingleInstance", out bool first);
        if (!first) { Log("[connect] ya hay un módulo Connect en marcha"); return 0; }
        var link = ModuleHostLink.FromArgs(args, "connect", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0");
        if (link is null) { Log("[connect] este módulo solo funciona dentro de Hatsu Desktop (lo inicia HatsuDesktop.exe): actívalo en Ajustes › General"); return 2; }

        var quit = new TaskCompletionSource();
        link.ShutdownRequested += () => quit.TrySetResult();
        PhoneLink? phone = null;
        phone = new PhoneLink(
            s => link.Publish(ConnectTopics.State, s),
            list => link.Publish(ConnectTopics.Notifications, list),
            item => link.Event(ConnectTopics.Alert, item),
            Log);
        phone.PublishCall = c => link.Publish(ConnectTopics.Call, c);
        if (Environment.GetEnvironmentVariable("HATSU_CONNECT_SYSTEM_CALLS") == "1") phone.SetObservation(true);   // avanzado; el interruptor de Ajustes es lo normal
        link.Subscribe(ConnectTopics.CallCmd, d => { if (d is { ValueKind: System.Text.Json.JsonValueKind.Object } o && o.TryGetProperty("cmd", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) phone.Command(v.GetString() ?? ""); });   // docs/60: órdenes de la Isla
        link.Subscribe(ConnectTopics.Diag, d => { if (d is { ValueKind: System.Text.Json.JsonValueKind.Object } o && o.TryGetProperty("observe", out var v) && v.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) phone.SetObservation(v.GetBoolean()); });
        link.HealthCheck = () => true;   // el trabajo con Bluetooth va en su propio bucle: un iPhone que no responde no es un módulo colgado
        link.Subscribe(ConnectTopics.Config, d => phone.ApplySettings(ConnectSettings.Parse(d)));
        link.Start();
        Log("[connect] módulo iniciado");
        await quit.Task;
        Log("[connect] cerrando: se deja de mantener la conexión con el iPhone");
        await phone.DisposeAsync();
        await link.ByeAsync(TimeSpan.FromSeconds(1));
        await link.DisposeAsync();
        return 0;
    }
}
