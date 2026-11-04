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
