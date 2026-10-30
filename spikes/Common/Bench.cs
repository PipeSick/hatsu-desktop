using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WinMac.Spikes.Common;

/// <summary>Registra intervalos entre frames (ms). Mide cadencia de render, NO latencia de presentación real (usar PresentMon para eso).</summary>
public sealed class FrameRecorder
{
    private readonly List<double> _ms = new(8192);
    private long _last;

    public void Tick()
    {
        long now = Stopwatch.GetTimestamp();
        if (_last != 0) _ms.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        _last = now;
    }

    public void Reset() { _ms.Clear(); _last = 0; }

    public FrameStats Summarize(double refreshHz)
    {
        if (_ms.Count == 0) return new FrameStats(0, 0, 0, 0, 0, 0, 0);
        var sorted = _ms.OrderBy(x => x).ToArray();
        double Pct(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
        double budget = 1000.0 / refreshHz;
        double dropped = _ms.Count(x => x > budget * 1.5) * 100.0 / _ms.Count;
        return new FrameStats(_ms.Count, Pct(0.50), Pct(0.95), Pct(0.99), sorted[^1], dropped, 1000.0 / _ms.Average());
    }
}

public sealed record FrameStats(int Frames, double P50Ms, double P95Ms, double P99Ms, double MaxMs, double DroppedPct, double MeanFps);

/// <summary>CPU% del proceso (normalizado por núcleos lógicos) entre Start y Stop.</summary>
public sealed class CpuSampler
{
    private TimeSpan _cpu0; private long _t0;
    public void Start() { _cpu0 = Process.GetCurrentProcess().TotalProcessorTime; _t0 = Stopwatch.GetTimestamp(); }
    public double StopPercent()
    {
        var cpu = Process.GetCurrentProcess().TotalProcessorTime - _cpu0;
        var wall = Stopwatch.GetElapsedTime(_t0);
        return wall.TotalSeconds <= 0 ? 0 : cpu.TotalSeconds / wall.TotalSeconds / Environment.ProcessorCount * 100.0;
    }
}

public static class Native
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();

    public static double PrimaryRefreshHz()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try { int hz = GetDeviceCaps(dc, 116 /* VREFRESH */); return hz > 1 ? hz : 60; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}

