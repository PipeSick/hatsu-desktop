using System.Globalization;

namespace WinMac.Bar.Hub;

/// <summary>
/// Una lectura de un sensor o contador: o tiene valor, o NO está disponible con su motivo. Nunca se inventa un valor: lo que el equipo no entrega se muestra como «no se puede leer».
/// </summary>
public readonly record struct Reading<T>(T? Value, string? Reason = null) where T : struct
{
    public bool Available => Value.HasValue;
    public static Reading<T> Of(T value) => new(value);
    public static Reading<T> Unavailable(string reason) => new(null, reason);
}

/// <summary>Historial de tamaño fijo (las gráficas): al llenarse descarta lo más antiguo. Del más antiguo al más reciente.</summary>
public sealed class History(int capacity)
{
    private readonly double[] _buf = new double[Math.Max(2, capacity)]; private int _start, _count;
    public int Capacity => _buf.Length;
    public int Count => _count;

    public void Add(double v)
    {
        if (!double.IsFinite(v)) v = 0;
        if (_count < _buf.Length) _buf[(_start + _count++) % _buf.Length] = v;
        else { _buf[_start] = v; _start = (_start + 1) % _buf.Length; }
    }

    public void Clear() { _start = 0; _count = 0; }
    public double Max => _count == 0 ? 0 : Values().Max();

    public IReadOnlyList<double> Values()
    {
        var r = new double[_count]; for (int i = 0; i < _count; i++) r[i] = _buf[(_start + i) % _buf.Length]; return r;
    }

    /// <summary>Valores del ancho exacto pedido (relleno a la izquierda con ceros): la gráfica siempre ocupa todo su ancho aunque el historial aún sea corto.</summary>
    public IReadOnlyList<double> Padded(int width)
    {
        var v = Values(); if (v.Count >= width) return v.Skip(v.Count - width).ToList();
        return Enumerable.Repeat(0.0, width - v.Count).Concat(v).ToList();
    }
}

/// <summary>
/// Convierte un contador acumulado (bytes recibidos, bytes leídos…) en velocidad (bytes/s) y total de la sesión. Tolera lo habitual en la práctica: la primera muestra no tiene velocidad,
/// un contador que retrocede (reinicio del adaptador o del contador) no produce picos ni totales negativos, y dos muestras con el mismo instante no dividen por cero.
/// </summary>
public sealed class RateMeter
{
    private ulong? _last; private DateTime _lastAt; private double _rate; private double _total;
    /// <summary>Bytes por segundo entre las dos últimas muestras.</summary>
    public double Rate => _rate;
    /// <summary>Bytes acumulados desde la primera muestra (sesión).</summary>
    public double Total => _total;

    public double Sample(ulong counter, DateTime now)
    {
        if (_last is null) { _last = counter; _lastAt = now; _rate = 0; return 0; }
        double dt = (now - _lastAt).TotalSeconds;
        if (dt <= 0) return _rate;   // misma marca de tiempo: se conserva la velocidad anterior
        if (counter < _last.Value) { _last = counter; _lastAt = now; _rate = 0; return 0; }   // el contador se reinició: se vuelve a empezar sin contar el salto
        double delta = counter - _last.Value; _total += delta; _rate = delta / dt; _last = counter; _lastAt = now; return _rate;
    }

    public void Reset() { _last = null; _rate = 0; _total = 0; }
}

public enum RateUnit { Auto, KBps, MBps, Mbps }

/// <summary>Formato de tamaños y velocidades con coma decimal (es-CL), igual que las capturas de referencia.</summary>
public static class Units
{
    private static readonly CultureInfo Es = new("es-CL");

    /// <summary>Velocidad de red: base 1000 (así se miden las redes). Auto elige KB/s o MB/s según la magnitud.</summary>
    public static string FormatRate(double bytesPerSec, RateUnit unit = RateUnit.Auto)
    {
        if (!double.IsFinite(bytesPerSec) || bytesPerSec < 0) bytesPerSec = 0;
        switch (unit)
        {
            case RateUnit.Mbps: return Num(bytesPerSec * 8 / 1_000_000) + " Mb/s";
            case RateUnit.KBps: return Num(bytesPerSec / 1000) + " KB/s";
            case RateUnit.MBps: return Num(bytesPerSec / 1_000_000) + " MB/s";
            default:
                if (bytesPerSec < 1000) return bytesPerSec.ToString("0", Es) + " B/s";
                return bytesPerSec < 1_000_000 ? Num(bytesPerSec / 1000) + " KB/s" : Num(bytesPerSec / 1_000_000) + " MB/s";
        }
    }

    /// <summary>Tamaño en base 1024 (como Windows y el Explorador): «193 GB», «4,72 GB», «18,20 TB».</summary>
    public static string FormatSize(double bytes, int tbDecimals = 2)
    {
        if (!double.IsFinite(bytes) || bytes < 0) bytes = 0;
        string[] u = ["B", "KB", "MB", "GB", "TB", "PB"]; int i = 0; double v = bytes;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return (i >= 4 ? v.ToString("0." + new string('0', tbDecimals), Es) : i == 3 ? NumSmart(v) : i == 0 ? v.ToString("0", Es) : NumSmart(v)) + " " + u[i];
    }

    /// <summary>Tiempo de actividad: «1d 19h», «5h 03m», «12m».</summary>
    public static string FormatUptime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes:00}m";
        return $"{Math.Max(0, t.Minutes)}m";
    }

    /// <summary>«Techo agradable» (1, 2, 5, 10, 20, 50… ×10ⁿ) para el eje de una gráfica: «200 MB/s» en la referencia.</summary>
    public static double NiceCeiling(double max)
    {
        if (!double.IsFinite(max) || max <= 0) return 1;
        double p = Math.Pow(10, Math.Floor(Math.Log10(max))), m = max / p;
        double n = m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10;
        return n * p;
    }

    private static string Num(double v) => v >= 100 ? v.ToString("0", Es) : v >= 10 ? v.ToString("0.#", Es) : v.ToString("0.0", Es);
    private static string NumSmart(double v) => v >= 100 ? v.ToString("0", Es) : v.ToString("0.##", Es);
}

public enum MemoryPressureLevel { Normal, Warning, Critical }

public sealed record MemoryInfo(ulong TotalBytes, ulong AvailableBytes, ulong? CompressedBytes, ulong CommitUsedBytes, ulong CommitLimitBytes, ulong? PageFileUsedBytes, ulong? PageFileTotalBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public double UsedFraction => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;

    /// <summary>
    /// Windows NO publica una «presión de memoria» como macOS; esta es una estimación fundamentada y documentada: Crítica si queda menos del 8 % de RAM disponible o la memoria comprometida
    /// supera el 92 % del límite (el sistema empezará a rechazar asignaciones); Advertencia con menos del 18 % disponible o más del 80 % comprometido; Normal en el resto.
    /// </summary>
    public MemoryPressureLevel Pressure
    {
        get
        {
            if (TotalBytes == 0) return MemoryPressureLevel.Normal;
            double avail = (double)AvailableBytes / TotalBytes, commit = CommitLimitBytes == 0 ? 0 : (double)CommitUsedBytes / CommitLimitBytes;
            if (avail < 0.08 || commit > 0.92) return MemoryPressureLevel.Critical;
            if (avail < 0.18 || commit > 0.80) return MemoryPressureLevel.Warning;
            return MemoryPressureLevel.Normal;
        }
    }
}

/// <summary>Una muestra del hardware. Cada sensor es independiente: que uno no se pueda leer no afecta a los demás.</summary>
public sealed record SystemSample(Reading<double> CpuPercent, Reading<double> GpuPercent, Reading<double> CpuTempC, Reading<double> GpuTempC, MemoryInfo? Memory, string? MemoryReason, TimeSpan Uptime, Reading<int> ConnectedDevices, string? CpuTempNote = null)
{
    public static SystemSample Empty { get; } = new(Reading<double>.Unavailable("Leyendo…"), Reading<double>.Unavailable("Leyendo…"), Reading<double>.Unavailable("Leyendo…"), Reading<double>.Unavailable("Leyendo…"), null, "Leyendo…", TimeSpan.Zero, Reading<int>.Unavailable("Leyendo…"));
}
