namespace CallProbe.Logic;

public enum StepStatus { Completed, TimedOut, Failed }

public sealed record StepResult<T>(string Name, StepStatus Status, T? Value, Exception? Error, TimeSpan Elapsed)
{
    public bool Ok => Status == StepStatus.Completed;
    public string Describe() => Status switch
    {
        StepStatus.Completed => $"{Name}: OK ({Elapsed.TotalMilliseconds:0} ms)",
        StepStatus.TimedOut => $"{Name}: SIN RESPUESTA tras {Elapsed.TotalSeconds:0.#} s (la llamada quedó colgada en Windows y se abandonó)",
        _ => $"{Name}: ERROR {Error?.GetType().Name}: {Error?.Message}",
    };
}

/// <summary>
/// Ejecuta una llamada que puede quedarse colgada (muchas APIs de WinRT son síncronas y no admiten cancelación) con un tiempo máximo.
/// Si se agota, se ABANDONA el hilo (es de segundo plano: no impide cerrar el proceso) y se sigue. Antes de cada paso se anota su inicio:
/// si el proceso se cuelga igualmente, la última línea del informe dice qué paso fue. Nace del cuelgue de la etapa 1 (PhoneLineWatcher).
/// </summary>
public static class StepGuard
{
    public static async Task<StepResult<T>> RunAsync<T>(string name, Func<Task<T>> work, TimeSpan timeout, Action<string>? log = null)
    {
        log?.Invoke("→ " + name); var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = Task.Run(work);   // en el grupo de hilos: una llamada síncrona bloqueada no bloquea al llamador
        var winner = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        StepResult<T> result;
        if (winner != task) { _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); result = new(name, StepStatus.TimedOut, default, null, sw.Elapsed); }
        else if (task.IsFaulted) result = new(name, StepStatus.Failed, default, task.Exception!.GetBaseException(), sw.Elapsed);
        else result = new(name, StepStatus.Completed, task.Result, null, sw.Elapsed);
        log?.Invoke("  " + result.Describe()); return result;
    }

    public static Task<StepResult<bool>> RunAsync(string name, Action work, TimeSpan timeout, Action<string>? log = null) =>
        RunAsync(name, () => { work(); return Task.FromResult(true); }, timeout, log);
}
