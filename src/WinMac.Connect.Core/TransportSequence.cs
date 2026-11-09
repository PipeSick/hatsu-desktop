using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Connect;

public enum StageOutcome { Ok, Timeout, Threw, Cancelled, Skipped }

/// <param name="HResult">HRESULT de la excepción de Windows, conservado tal cual (0 si no hubo).</param>
public sealed record StageResult(string Stage, StageOutcome Outcome, long Ms, int HResult, string Detail)
{
    public bool Ok => Outcome == StageOutcome.Ok;
    public override string ToString() => $"{Stage}: {Outcome} ({Ms} ms){(HResult != 0 ? $" 0x{unchecked((uint)HResult):X8}" : "")}{(string.IsNullOrEmpty(Detail) ? "" : " — " + Detail)}";
}

/// <summary>
/// Las operaciones SÍNCRONAS del transporte telefónico de Windows (<c>PhoneLineTransportDevice</c>). Cada una puede BLOQUEAR sin límite (RegisterApp es una llamada síncrona de WinRT; ConnectAsync puede no terminar jamás):
/// por eso nunca se llaman desde el hilo del llamador ni del módulo, sino desde <see cref="StageWorker"/>.
/// </summary>
public interface ITransportOps
{
    AccessResult RequestAccess();
    bool IsRegistered();
    void RegisterApp();
    /// <summary>Variante por usuario (<c>RegisterAppForUser</c>): la documentación dice que el registro es por usuario; se prueba solo si <see cref="RegisterApp"/> volvió sin efecto.</summary>
    void RegisterAppForUser();
    void UnregisterAppForUser();
    /// <summary>Espera a que ConnectAsync termine. El valor devuelto por Windows no es fiable (ambos proyectos de referencia lo documentan) y no se evalúa.</summary>
    bool Connect();
    void UnregisterApp();
}

/// <summary>
/// UN hilo dedicado que ejecuta, de una en una, las llamadas al transporte. Garantías: nunca dos operaciones del transporte a la vez (sin carreras entre registro, observador y desconexión);
/// si una operación no vuelve a tiempo el trabajador queda <see cref="Poisoned"/> y ya NO acepta más trabajo (nada sigue tocando el transporte tras un tiempo de espera). Una llamada síncrona de WinRT no se
/// puede cancelar con un <see cref="CancellationToken"/>: lo único legítimo es aislarla en su propio hilo, dejar de depender de ella y, si hace falta, reiniciar el proceso del módulo.
/// </summary>
public sealed class StageWorker : IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _q = new();
    private readonly Thread _t;
    public bool Poisoned { get; private set; }
    public StageWorker(string name = "hatsu-hfp-transport") { _t = new Thread(Loop) { IsBackground = true, Name = name }; _t.Start(); }

    private void Loop() { foreach (var a in _q.GetConsumingEnumerable()) { try { a(); } catch (Exception) { } } }

    /// <summary>Encola <paramref name="f"/>. Si el trabajador está envenenado, la tarea se rechaza sin ejecutar nada.</summary>
    public Task<T> Run<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Poisoned || _q.IsAddingCompleted) { tcs.SetException(new InvalidOperationException("transporte aislado tras un bloqueo")); return tcs.Task; }
        _q.Add(() => { if (Poisoned) { tcs.TrySetException(new InvalidOperationException("transporte aislado tras un bloqueo")); return; } try { tcs.TrySetResult(f()); } catch (Exception e) { tcs.TrySetException(e); } });
        return tcs.Task;
    }

    public void Poison() => Poisoned = true;
    public void Dispose() { Poisoned = true; try { _q.CompleteAdding(); } catch (Exception) { } }
}

public sealed record SequenceBudget(TimeSpan Total, TimeSpan Access, TimeSpan IsRegistered, TimeSpan Register, TimeSpan Connect, TimeSpan Cleanup)
{
    /// <summary>40 s en total (por debajo del límite de 60 s del host): cada etapa tiene su tope y nunca excede lo que queda del presupuesto.</summary>
    public static readonly SequenceBudget Default = new(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(4));
}

public sealed record SequenceResult(IReadOnlyList<StageResult> Stages, ProviderStatus Status, bool Wedged, string? WedgedStage, bool RegisteredByUsNow, bool RegisteredAfter, bool ConnectReturned)
{
    public bool Ready => Status.Ready;
}

/// <summary>
/// Secuencia ACCESO → ¿REGISTRADO? → REGISTRO → VERIFICACIÓN → CONEXIÓN → VERIFICACIÓN con tope por etapa y presupuesto total (docs/62). Cada etapa informa su resultado real (milisegundos, HRESULT, causa);
/// un bloqueo se atribuye a la etapa exacta; tras un bloqueo no se ejecuta nada más sobre el transporte. Pura respecto de Windows: se prueba con un doble.
/// </summary>
public sealed class TransportSequence : IDisposable
{
    private readonly ITransportOps _ops; private readonly SequenceBudget _b; private readonly StageWorker _w;
    private int _running;
    public StageWorker Worker => _w;

    public TransportSequence(ITransportOps ops, SequenceBudget? budget = null, StageWorker? worker = null) { _ops = ops; _b = budget ?? SequenceBudget.Default; _w = worker ?? new StageWorker(); }

    private async Task<(StageResult R, T? Value)> Stage<T>(string name, TimeSpan cap, Stopwatch total, Func<T> f, CancellationToken ct)
    {
        var left = _b.Total - total.Elapsed;
        if (_w.Poisoned || left <= TimeSpan.Zero) return (new(name, StageOutcome.Skipped, 0, 0, _w.Poisoned ? "transporte aislado tras un bloqueo" : "presupuesto de tiempo agotado"), default);
        if (ct.IsCancellationRequested) return (new(name, StageOutcome.Cancelled, 0, 0, "cancelado antes de empezar"), default);
        var limit = cap < left ? cap : left; var sw = Stopwatch.StartNew(); var task = _w.Run(f);
        try
        {
            var done = await Task.WhenAny(task, Task.Delay(limit, ct));
            if (done == task)
            {
                try { var v = await task; return (new(name, StageOutcome.Ok, sw.ElapsedMilliseconds, 0, ""), v); }
                catch (Exception e) { return (new(name, StageOutcome.Threw, sw.ElapsedMilliseconds, e.HResult, e.GetType().Name + ": " + e.Message), default); }
            }
            _w.Poison();   // no vuelve: nada más sobre el transporte desde este proceso
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);   // observar la excepción tardía sin actuar
            return ct.IsCancellationRequested
                ? (new(name, StageOutcome.Cancelled, sw.ElapsedMilliseconds, 0, "cancelado con la operación de Windows sin terminar"), default)
                : (new(name, StageOutcome.Timeout, sw.ElapsedMilliseconds, 0, $"Windows no respondió en {limit.TotalSeconds:0} s"), default);
        }
        catch (OperationCanceledException) { _w.Poison(); return (new(name, StageOutcome.Cancelled, sw.ElapsedMilliseconds, 0, "cancelado"), default); }
    }

    /// <summary>Una sola secuencia a la vez: una segunda llamada concurrente se rechaza (sin registros ni conexiones duplicados).</summary>
    public async Task<SequenceResult> RegisterAndConnectAsync(CancellationToken ct = default, Action<StageResult>? progress = null)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new([new("secuencia", StageOutcome.Skipped, 0, 0, "ya hay un registro en curso")], new(ProviderState.Unavailable, "Ya hay un registro en curso"), false, null, false, false, false);
        try
        {
            var total = Stopwatch.StartNew(); var stages = new List<StageResult>();
            void Add(StageResult r) { stages.Add(r); progress?.Invoke(r); }
            SequenceResult Stop(string reason, AccessResult acc, bool before, RegisterResult reg, bool after, bool byUs, bool conn, bool wedged = false)
            {
                var last = stages[^1];
                var status = wedged || last.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled
                    ? new ProviderStatus(ProviderState.Unavailable, $"Windows no respondió en la etapa «{last.Stage}» ({last.Ms / 1000.0:0.#} s); se aisló el transporte para no bloquear Hatsu")
                    : RegistrationPlanner.Evaluate(acc, before, reg, after);
                return new(stages, status, wedged || last.Outcome is StageOutcome.Timeout, last.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled ? last.Stage : null, byUs, after, conn);
            }

            var (a, accessV) = await Stage("acceso (RequestAccessAsync)", _b.Access, total, _ops.RequestAccess, ct); Add(a);
            if (!a.Ok) return Stop("", AccessResult.Other, false, RegisterResult.NotNeeded, false, false, false);
            var access = accessV;
            if (access != AccessResult.Allowed) { stages[^1] = a with { Detail = "resultado: " + access }; return new(stages, RegistrationPlanner.Evaluate(access, false, RegisterResult.NotNeeded, false), false, null, false, false, false); }

            var (r0, before) = await Stage("¿registrado antes? (IsRegistered)", _b.IsRegistered, total, _ops.IsRegistered, ct); Add(r0);
            if (!r0.Ok) return Stop("", access, false, RegisterResult.NotNeeded, false, false, false);

            var reg = RegisterResult.NotNeeded; bool byUs = false;
            if (!before)
            {
                var (rr, _) = await Stage("registro (RegisterApp)", _b.Register, total, () => { _ops.RegisterApp(); return true; }, ct);
                if (rr.Outcome == StageOutcome.Threw && (rr.HResult == unchecked((int)0x80070005))) { rr = rr with { Detail = "acceso denegado por Windows: " + rr.Detail }; reg = RegisterResult.Unauthorized; }
                else if (rr.Ok) { reg = RegisterResult.Returned; byUs = true; }
                else if (rr.Outcome == StageOutcome.Threw) reg = RegisterResult.Threw;
                Add(rr);
                if (rr.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled) return Stop("", access, false, reg, false, true, false, wedged: true);
            }

            var (v, after) = await Stage("verificación del registro (IsRegistered)", _b.IsRegistered, total, _ops.IsRegistered, ct); Add(v);
            if (!v.Ok) return Stop("", access, before, reg, false, byUs, false);
            // RegisterApp volvió sin error pero IsRegistered sigue en falso (síntoma documentado en Windows 11 por MyPhone #26 y un Q&A de Microsoft): único camino público alternativo, RegisterAppForUser.
            if (!after && reg == RegisterResult.Returned)
            {
                var (ru, _) = await Stage("registro alternativo (RegisterAppForUser)", _b.Register, total, () => { _ops.RegisterAppForUser(); return true; }, ct); Add(ru);
                if (ru.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled) return Stop("", access, before, reg, false, true, false, wedged: true);
                if (ru.Ok)
                {
                    var (v3, after3) = await Stage("verificación tras RegisterAppForUser (IsRegistered)", _b.IsRegistered, total, _ops.IsRegistered, ct); Add(v3);
                    if (v3.Ok && after3) { after = true; }
                }
            }
            var plan = RegistrationPlanner.Evaluate(access, before, reg, after);
            if (!plan.Ready) return new(stages, plan, false, null, byUs, after, false);

            var (c, _) = await Stage("conexión HFP (ConnectAsync)", _b.Connect, total, _ops.Connect, ct); Add(c);
            if (c.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled) return Stop("", access, before, reg, after, byUs, false, wedged: true);
            // Un fallo de ConnectAsync NO anula un registro verificado: las líneas pueden aparecer cuando el teléfono conecte por su cuenta.
            var (v2, after2) = await Stage("verificación final (IsRegistered)", _b.IsRegistered, total, _ops.IsRegistered, ct); Add(v2);
            bool still = v2.Ok && after2;
            return new(stages, still ? plan : RegistrationPlanner.Evaluate(access, before, reg, false), false, null, byUs, still, c.Ok);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>Deshace el registro (solo si sigue registrado). Con su propio tope; si el trabajador está aislado, no se intenta (se avisa).</summary>
    public async Task<StageResult> CleanupAsync()
    {
        if (_w.Poisoned) return new("deshacer registro (UnregisterApp)", StageOutcome.Skipped, 0, 0, "transporte aislado: el registro se deshace al reiniciar el módulo");
        var sw = Stopwatch.StartNew(); var t = _w.Run(() => { if (!_ops.IsRegistered()) return "no estaba registrado"; _ops.UnregisterApp(); if (_ops.IsRegistered()) _ops.UnregisterAppForUser(); return "deshecho"; });
        var done = await Task.WhenAny(t, Task.Delay(_b.Cleanup));
        if (done != t) { _w.Poison(); return new("deshacer registro (UnregisterApp)", StageOutcome.Timeout, sw.ElapsedMilliseconds, 0, "Windows no respondió"); }
        try { return new("deshacer registro (UnregisterApp)", StageOutcome.Ok, sw.ElapsedMilliseconds, 0, await t); }
        catch (Exception e) { return new("deshacer registro (UnregisterApp)", StageOutcome.Threw, sw.ElapsedMilliseconds, e.HResult, e.GetType().Name); }
    }

    /// <summary>Lectura periódica del registro, con tope (no puede colgar el bucle del proveedor).</summary>
    public async Task<bool?> ReadRegisteredAsync(TimeSpan cap)
    {
        if (_w.Poisoned) return null;
        var t = _w.Run(_ops.IsRegistered); var done = await Task.WhenAny(t, Task.Delay(cap));
        if (done != t) { _w.Poison(); return null; }
        try { return await t; } catch (Exception) { return null; }
    }

    /// <summary>Reintento de la conexión HFP con tope; true si volvió a tiempo.</summary>
    public async Task<StageResult> ReconnectAsync()
    {
        var total = Stopwatch.StartNew(); var (c, _) = await Stage("reconexión HFP (ConnectAsync)", _b.Connect, total, _ops.Connect, default); return c;
    }

    public void Dispose() => _w.Dispose();
}

/// <summary>
/// Si Windows deja una llamada de transporte colgada, el módulo se reinicia (es el único modo legítimo de recuperar el hilo bloqueado) y el módulo nuevo NO vuelve a tocar el transporte hasta que pase un tiempo
/// creciente: así un bloqueo repetido no crea un bucle de reinicios. Estado en un archivo pequeño (sin datos personales).
/// </summary>
public static class WedgeGuard
{
    public sealed record State(DateTime LastUtc, int Count, string Stage);
    private static readonly TimeSpan[] Steps = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(6)];

    public static TimeSpan Cooldown(int count) => count <= 0 ? TimeSpan.Zero : Steps[Math.Min(count, Steps.Length) - 1];
    public static bool InCooldown(State? s, DateTime nowUtc) => s is not null && nowUtc - s.LastUtc < Cooldown(s.Count);
    public static DateTime Until(State s) => s.LastUtc + Cooldown(s.Count);
    public static State Record(State? prev, string stage, DateTime nowUtc) => new(nowUtc, Math.Min((prev?.Count ?? 0) + 1, 99), stage);
    /// <summary>Un arranque limpio tras el enfriamiento no borra la cuenta (si vuelve a bloquearse, sube); un día sin bloqueos la reinicia.</summary>
    public static State? Decay(State? s, DateTime nowUtc) => s is null || nowUtc - s.LastUtc > TimeSpan.FromDays(1) ? null : s;

    public static string Serialize(State s) => JsonSerializer.Serialize(new { lastUtc = s.LastUtc.ToString("O"), count = s.Count, stage = s.Stage });
    public static State? Parse(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using var d = JsonDocument.Parse(json); var r = d.RootElement;
            return new(DateTime.Parse(r.GetProperty("lastUtc").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(), Math.Clamp(r.GetProperty("count").GetInt32(), 0, 99), r.GetProperty("stage").GetString() ?? "?");
        }
        catch (Exception) { return null; }
    }
}
