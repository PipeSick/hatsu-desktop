using CallProbe.Logic;

namespace CallProbe;

/// <summary>Atajo para ejecutar cada llamada de Windows con tiempo máximo y registrar su inicio y su fin (si algo se cuelga, la última línea «→ …» dice qué fue).</summary>
internal static class Steps
{
    public static CancellationTokenSource Cancel { get; } = new();
    public static Task<StepResult<T>> Run<T>(string name, Func<Task<T>> work, int seconds = 15) => StepGuard.RunAsync(name, work, TimeSpan.FromSeconds(seconds), Report.Stamp);
    public static Task<StepResult<bool>> Run(string name, Action work, int seconds = 15) => StepGuard.RunAsync(name, work, TimeSpan.FromSeconds(seconds), Report.Stamp);
    public static async Task DelayAsync(int ms) { try { await Task.Delay(ms, Cancel.Token); } catch (OperationCanceledException) { } }
}
