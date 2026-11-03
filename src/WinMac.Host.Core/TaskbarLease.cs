using System.Text.Json;

namespace WinMac.Host;

/// <summary>
/// WM-034: «Ocultar barra de tareas de Windows». Registro de recuperación (<c>%LOCALAPPDATA%\WinMac\taskbar-restore.json</c>) que se escribe ANTES de ocultar nada.
/// Mientras existe, la barra de tareas está (o puede estar) oculta por WinMac y dice cómo devolverla a como estaba: el estado de «Ocultar automáticamente» que tenía el usuario.
/// Lo leen el host al arrancar (si quedó de un cierre forzado), el proceso guardián (<c>WinMac.exe --taskbar-guard</c>) y <c>WinMac.exe --restore-taskbar</c>.
/// Borrarlo es la señal de que todo se restauró; si el guardián lo borra, el host deja de ocultar durante esa sesión (nunca pelean).
/// </summary>
public sealed record TaskbarLease(int HostPid, bool OriginalAutoHide, DateTime SinceUtc)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "taskbar-restore.json");

    public static TaskbarLease? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<TaskbarLease>(File.ReadAllText(path)) : null; }
        catch (Exception) { return new TaskbarLease(0, false, DateTime.MinValue); }   // ilegible: se trata como «hay algo que restaurar» (mostrar la barra); el estado original no se sabe y no se toca
    }

    /// <summary>Escritura atómica (archivo temporal y reemplazo): un corte a medias no deja un registro roto.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this)); File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string path) { try { File.Delete(path); } catch (Exception) { } }

    /// <summary>Un registro ilegible no conoce el estado original de «Ocultar automáticamente»: al restaurar solo se vuelven a mostrar las barras.</summary>
    public bool KnowsOriginal => SinceUtc != DateTime.MinValue;
}

/// <summary>
/// Freno contra peleas con Explorer: si Windows vuelve a mostrar la barra muchas veces seguidas (por ejemplo una versión que la redibuja sin parar), WinMac deja de ocultarla
/// en esta sesión en vez de parpadear. Puro: se prueba sin Windows.
/// </summary>
public sealed class ReshowLimiter(int max = 6, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(60);
    private readonly Queue<DateTime> _hits = new();

    /// <summary>Anota que hubo que volver a ocultar. Devuelve false si se superó el límite (hay que rendirse y restaurar).</summary>
    public bool Allow(DateTime now)
    {
        while (_hits.Count > 0 && now - _hits.Peek() > _window) _hits.Dequeue();
        _hits.Enqueue(now);
        return _hits.Count <= max;
    }

    public void Reset() => _hits.Clear();
}

/// <summary>Qué debe hacer el host en cada paso. Puro: el adaptador de Windows solo ejecuta la decisión.</summary>
public enum TaskbarStep { Nothing, Hide, Restore, GiveUp }

public static class TaskbarPolicy
{
    /// <param name="enabled">El ajuste está activado.</param>
    /// <param name="leaseExists">El registro de recuperación existe (si el guardián lo borró, WinMac ya no es dueño de la ocultación).</param>
    /// <param name="hiddenByUs">El host ocultó la barra en esta sesión.</param>
    /// <param name="anyVisible">Hay alguna barra de Explorer visible ahora mismo.</param>
    /// <param name="suspended">Se rindió en esta sesión (límite de reapariciones o el guardián tomó el control).</param>
    public static TaskbarStep Decide(bool enabled, bool leaseExists, bool hiddenByUs, bool anyVisible, bool suspended)
    {
        if (!enabled) return hiddenByUs || leaseExists ? TaskbarStep.Restore : TaskbarStep.Nothing;
        if (suspended) return TaskbarStep.Nothing;
        if (hiddenByUs && !leaseExists) return TaskbarStep.GiveUp;   // el guardián restauró (el host estuvo colgado): no se vuelve a ocultar en esta sesión
        return anyVisible || !hiddenByUs ? TaskbarStep.Hide : TaskbarStep.Nothing;
    }

    /// <summary>El guardián restaura si el host terminó sin borrar el registro, o si el registro es de otro proceso que ya no existe.</summary>
    public static bool GuardShouldRestore(TaskbarLease? lease, bool hostAlive) => lease is not null && !hostAlive;

    /// <summary>Al arrancar: un registro que dejó un host que ya no está (cierre forzado, apagado, cuelgue) se restaura antes de nada.</summary>
    public static bool StaleOnStart(TaskbarLease? lease, Func<int, bool> processAlive, int selfPid) => lease is not null && (lease.HostPid == 0 || lease.HostPid != selfPid && !processAlive(lease.HostPid));
}
