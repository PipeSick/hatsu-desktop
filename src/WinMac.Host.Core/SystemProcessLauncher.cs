using System.Diagnostics;

namespace WinMac.Host;

/// <summary>
/// Lanza módulos como procesos hijos (<c>--hosted --pipe &lt;nombre&gt;</c>; el token va en la variable de entorno <c>WINMAC_TOKEN</c>, no en la línea de comandos).
/// El host de Windows lo extiende para meter cada hijo en un Job Object y para pedir el cierre a las ventanas del proceso.
/// </summary>
public class SystemProcessLauncher : IProcessLauncher
{
    /// <summary>Se invoca justo tras lanzar (el host de Windows lo usa para asignar el Job Object).</summary>
    public Action<Process>? OnStarted { get; set; }
    /// <summary>Petición cortés de cierre; por defecto <see cref="Process.CloseMainWindow"/>.</summary>
    public Action<Process>? CloseHook { get; set; }

    public IModuleProcess Start(ModuleSpec spec, string pipeName, string token)
    {
        if (Path.IsPathRooted(spec.ExePath) && !File.Exists(spec.ExePath)) throw new FileNotFoundException("no existe el ejecutable", spec.ExePath);
        var psi = new ProcessStartInfo(spec.ExePath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in spec.Args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--hosted"); psi.ArgumentList.Add("--pipe"); psi.ArgumentList.Add(pipeName);
        psi.Environment[Ipc.ModuleHostLink.TokenEnvVar] = token;
        var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start devolvió null");
        try { OnStarted?.Invoke(p); } catch (Exception) { /* sin Job Object seguimos: el cierre ordenado sigue funcionando */ }
        return new Child(p, CloseHook);
    }

    private sealed class Child : IModuleProcess
    {
        private readonly Process _p; private readonly Action<Process>? _close; private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Child(Process p, Action<Process>? close)
        {
            _p = p; _close = close; Id = p.Id; p.EnableRaisingEvents = true;
            p.Exited += (_, _) => _exit.TrySetResult(SafeExitCode());
            if (p.HasExited) _exit.TrySetResult(SafeExitCode());
        }
        private int SafeExitCode() { try { return _p.ExitCode; } catch (Exception) { return -1; } }
        public int Id { get; }
        public Task<int> Exited => _exit.Task;
        public void RequestClose() { try { if (_close is not null) _close(_p); else _p.CloseMainWindow(); } catch (Exception) { } }
        public void Kill() { try { _p.Kill(entireProcessTree: true); } catch (Exception) { } }
    }
}
