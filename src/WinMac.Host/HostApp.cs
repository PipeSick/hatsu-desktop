using System.Diagnostics;
using WinMac.Ipc;

namespace WinMac.Host.App;

/// <summary>Opciones de línea de comandos del host (las de prueba están marcadas).</summary>
internal sealed record HostOptions(string ConfigPath, string LogPath, bool NoTray, bool Fast, string? PipeName)
{
    public static string Arg(string[] a, string n, string def) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
    public static HostOptions Parse(string[] a) => new(
        Arg(a, "--config", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "winmac.json")),
        Arg(a, "--log", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "host.log")),
        a.Contains("--no-tray"),          // PRUEBAS: sin icono de bandeja (runners sin escritorio)
        a.Contains("--fast"),             // PRUEBAS: tiempos de supervisión reducidos
        a.Contains("--pipe-name") ? Arg(a, "--pipe-name", "") : null);
}

internal sealed class FileLog(string path)
{
    private readonly object _l = new();
    public void Write(string m)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {m}";
        try { Console.WriteLine(line); } catch (Exception) { }
        try { lock (_l) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.AppendAllText(path, line + Environment.NewLine); } } catch (Exception) { }
    }
}

/// <summary>
/// Host de WinMac: instancia única → canal local → supervisor → bucle de mensajes (bandeja) → cierre ordenado.
/// Sin privilegios elevados, sin puertos de red, sin tocar Explorer. Orden de cierre: quitar icono → cerrar módulos en orden inverso → liberar Job Object y canal.
/// </summary>
internal static class HostApp
{
    public const string MutexName = @"Local\WinMac.Host", QuitEventName = @"Local\WinMac.Host.Quit";

    public static int Run(HostOptions o)
    {
        var log = new FileLog(o.LogPath).Write;
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first) { log("[host] ya hay una instancia de WinMac en ejecución: esta se cierra sin hacer nada."); return 0; }
        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        quit.Reset();

        var cfg = HostConfig.Load(o.ConfigPath);
        string baseDir = AppContext.BaseDirectory, pipe = o.PipeName ?? PipeNames.ForCurrentUser(), token = PipeNames.NewToken();
        log($"[host] iniciado (pid {Environment.ProcessId}); configuración: {o.ConfigPath}; canal: {pipe}");

        JobObject? job = null;
        try { job = new JobObject(); } catch (Exception e) { log("[host] AVISO: sin Job Object (" + e.Message + "): si el host muriera de golpe, los módulos podrían quedar abiertos."); }

        Supervisor? sup = null;
        var server = new IpcServer(new ServerOptions(pipe, token) { PeerValidator = pid => sup?.IsKnownPid(pid) ?? false });
        server.Start();
        var launcher = new SystemProcessLauncher
        {
            OnStarted = p => { try { job?.Assign(p); } catch (Exception e) { log($"[host] AVISO: no se pudo asignar {p.ProcessName} al Job Object: {e.Message}"); } },
            CloseHook = p => PostCloseToWindows(p.Id),
        };
        var policy = o.Fast
            ? new SupervisorPolicy { ReadyTimeout = TimeSpan.FromSeconds(8), HeartbeatInterval = TimeSpan.FromMilliseconds(500), BackoffStart = TimeSpan.FromMilliseconds(100), BackoffMax = TimeSpan.FromMilliseconds(500), StopGrace = TimeSpan.FromSeconds(5), StartStagger = TimeSpan.FromMilliseconds(50) }
            : new SupervisorPolicy();
        sup = new Supervisor(launcher, new IpcHostBus(server), new SystemClock(), policy, pipe, token, log);
        foreach (var (spec, enabled) in cfg.Resolve(baseDir)) sup.Add(spec, enabled);

        bool quitting = false; void RequestQuit() => quitting = true;
        Tray? tray = null;
        try { tray = new Tray(() => sup.States, RequestQuit, log, showIcon: !o.NoTray); }
        catch (Exception e) { log("[host] sin ventana/bandeja: " + e.Message); }

        sup.StartAsync().GetAwaiter().GetResult();
        log("[host] módulos lanzados: " + string.Join(", ", sup.States.Select(kv => $"{kv.Key}={kv.Value}")));

        // Bucle de mensajes (la bandeja y el cierre de sesión lo necesitan) + vigilancia de la señal de cierre (--quit).
        while (!quitting)
        {
            while (Native.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg); }
            if (quit.WaitOne(0)) { log("[host] señal de cierre recibida (--quit)"); break; }
            Native.MsgWaitForMultipleObjectsEx(0, null, 100, Native.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }

        log("[host] cerrando…");
        tray?.RemoveIcon();
        sup.StopAllAsync().GetAwaiter().GetResult();
        log("[host] módulos cerrados: " + string.Join(", ", sup.States.Select(kv => $"{kv.Key}={kv.Value}")));
        tray?.Dispose(); job?.Dispose(); server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        log("[host] fin."); return 0;
    }

    /// <summary>Pide el cierre a las ventanas de nivel superior de un proceso (WM_CLOSE): cooperativo, no fuerza nada.</summary>
    private static void PostCloseToWindows(int pid)
    {
        Native.EnumWindows((h, _) => { Native.GetWindowThreadProcessId(h, out uint p); if (p == (uint)pid) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
    }

    /// <summary>Pide al host en ejecución que se cierre normalmente.</summary>
    public static int SignalQuit()
    {
        try { using var ev = EventWaitHandle.OpenExisting(QuitEventName); ev.Set(); Console.WriteLine("Señal de cierre enviada a WinMac."); return 0; }
        catch (WaitHandleCannotBeOpenedException) { Console.WriteLine("WinMac no está en ejecución."); return 0; }
    }
}
