using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Spikes.Common;

public readonly record struct DockPose(double? CursorX, double HideT);

public sealed record BenchReport(
    string Spike, string Variant, string OsVersion, double RefreshHz, uint Dpi, bool Quick,
    double StartupMs, double IdleCpuPct, double SweepCpuPct, double WorkingSetMb,
    FrameStats Sweep, FrameStats Hide, double ApplyP50Ms, double ApplyP95Ms,
    string[] FeaturesImplemented, string[] FeaturesMissing, string Notes)
{
    public string Save(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{Spike}-{Variant}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}

/// <summary>
/// Guion de medición idéntico para todos los prototipos:
/// calentamiento 2 s → REPOSO 10 s (sin bucle de render) → BARRIDO 15 s → OCULTAR/MOSTRAR 3 ciclos → informe.
/// Cada tecnología solo aporta el bucle de frames y cómo aplicar la <see cref="DockPose"/>.
/// </summary>
public sealed class BenchRun
{
    private const double SweepSeconds = 15, HideHold = 0.8, HideMove = 0.35;
    private const int HideCycles = 3;

    private readonly string _spike; private readonly CliOptions _opt;
    private readonly FrameRecorder _sweep = new(), _hide = new();
    private readonly List<double> _applyMs = new(8192);
    private readonly CpuSampler _cpu = new();
    private readonly double _scale;
    private long _loopStart; private double _idleCpu, _sweepCpu; private bool _done;
    public double StartupMs { get; private set; }

    public BenchRun(string spike, CliOptions opt) { _spike = spike; _opt = opt; _scale = opt.Quick ? 0.2 : 1.0; }

    public void MarkFirstRender() => StartupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    /// <summary>Espera calentamiento y reposo SIN bucle de render; luego invoca <paramref name="startFrames"/> en el hilo de UI.</summary>
    public async Task RunIdleAsync(Action<Action> postToUi, Action startFrames)
    {
        await Task.Delay((int)(2000 * _scale));
        _cpu.Start();
        await Task.Delay((int)(10000 * _scale));
        _idleCpu = _cpu.StopPercent();
        postToUi(() => { _loopStart = Stopwatch.GetTimestamp(); _cpu.Start(); startFrames(); });
    }

    /// <summary>Llamar una vez por frame. Devuelve null cuando terminó la medición.</summary>
    public DockPose? Frame()
    {
        if (_done) return null;
        double t = Stopwatch.GetElapsedTime(_loopStart).TotalSeconds;
        double sweepEnd = SweepSeconds * _scale;
        if (t < sweepEnd)
        {
            _sweep.Tick();
            return new DockPose(DockScene.SweepX(t, DockScene.RestWidth), 0);
        }
        if (_sweepCpu == 0) _sweepCpu = _cpu.StopPercent();
        double u = t - sweepEnd, cycle = 2 * (HideMove + HideHold);
        if (u >= HideCycles * cycle) { _done = true; return null; }
        _hide.Tick();
        double c = u % cycle, hideT;
        if (c < HideMove) hideT = Ease(c / HideMove);
        else if (c < HideMove + HideHold) hideT = 1;
        else if (c < 2 * HideMove + HideHold) hideT = 1 - Ease((c - HideMove - HideHold) / HideMove);
        else hideT = 0;
        return new DockPose(DockScene.RestWidth / 2, hideT);
    }

    private static double Ease(double x) => x * x * (3 - 2 * x); // smoothstep

    public long ApplyBegin() => Stopwatch.GetTimestamp();
    public void ApplyEnd(long t0) => _applyMs.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

    public string Finish(string variant, string[] implemented, string[] missing, string notes)
    {
        double hz = Native.PrimaryRefreshHz();
        var a = _applyMs.OrderBy(x => x).ToArray();
        double P(double p) => a.Length == 0 ? 0 : a[Math.Min(a.Length - 1, (int)(p * a.Length))];
        var rep = new BenchReport(_spike, variant, Environment.OSVersion.VersionString, hz, Native.GetDpiForSystem(), _opt.Quick,
            StartupMs, _idleCpu, _sweepCpu, Process.GetCurrentProcess().WorkingSet64 / 1048576.0,
            _sweep.Summarize(hz), _hide.Summarize(hz), P(.5), P(.95), implemented, missing, notes);
        return rep.Save(_opt.OutDir);
    }
}
