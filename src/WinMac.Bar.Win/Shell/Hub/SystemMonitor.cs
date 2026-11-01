using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Sensores del equipo SIN administrador y sin controladores propios. Cada sensor es independiente y, si no se puede leer, dice por qué; no se inventa ningún valor.
/// CPU % = GetSystemTimes; RAM/commit = GlobalMemoryStatusEx; GPU % y temperatura = NVML si hay GPU NVIDIA (si no, uso por los contadores «GPU Engine» de Windows y sin temperatura);
/// temperatura de CPU = zona térmica ACPI (aproximada, y se rotula como tal) o «no disponible»: leer el sensor real de la CPU exige un controlador con privilegios que WinMac no instala.
/// </summary>
internal sealed class SystemMonitor : ISystemMonitor, IDisposable
{
    private ulong _lastIdle, _lastTotal; private bool _haveCpu; private double _lastCpu;
    private PdhQuery? _gpuQuery, _thermalQuery; private IntPtr? _gpuCounter, _thermalCounter; private int _gpuReads, _gpuSamples;
    private readonly Nvml _nvml = new();
    private DateTime _slowAt = DateTime.MinValue; private (ulong Used, ulong Total)? _pageFile; private int? _usbCount; private ulong? _compressed;

    public SystemSample Read()
    {
        var cpu = ReadCpu(); var (gpu, gpuTemp) = ReadGpu(); var (cpuTemp, note) = ReadCpuTemp();
        MemoryInfo? mem = null; string? memReason = null;
        try
        {
            var ms = new HubNative.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<HubNative.MEMORYSTATUSEX>() };
            if (HubNative.GlobalMemoryStatusEx(ref ms))
            {
                RefreshSlow();
                mem = new MemoryInfo(ms.ullTotalPhys, ms.ullAvailPhys, _compressed, ms.ullTotalPageFile - ms.ullAvailPageFile, ms.ullTotalPageFile, _pageFile?.Used, _pageFile?.Total);
            }
            else memReason = "Windows no entregó el uso de memoria";
        }
        catch (Exception e) { memReason = "No se pudo leer la memoria: " + e.GetType().Name; }
        var devices = _usbCount is { } n ? Reading<int>.Of(n) : Reading<int>.Unavailable("No se pudo contar los dispositivos USB");
        return new SystemSample(cpu, gpu, cpuTemp, gpuTemp, mem, memReason, TimeSpan.FromMilliseconds(Environment.TickCount64), devices, note);
    }

    private Reading<double> ReadCpu()
    {
        if (!HubNative.GetSystemTimes(out var idle, out var kernel, out var user)) return Reading<double>.Unavailable("Windows no entregó los tiempos de CPU");
        ulong i = idle.Value, total = kernel.Value + user.Value;   // el tiempo de kernel incluye el de inactividad
        if (!_haveCpu) { _lastIdle = i; _lastTotal = total; _haveCpu = true; return Reading<double>.Unavailable("Midiendo…"); }
        ulong dt = total - _lastTotal, di = i - _lastIdle; _lastIdle = i; _lastTotal = total;
        if (dt == 0) return Reading<double>.Of(_lastCpu);
        _lastCpu = Math.Clamp(100.0 * (1.0 - (double)di / dt), 0, 100); return Reading<double>.Of(_lastCpu);
    }

    private (Reading<double> Usage, Reading<double> Temp) ReadGpu()
    {
        var nv = _nvml.Read();
        if (nv is { } g) return (Reading<double>.Of(g.Util), Reading<double>.Of(g.TempC));
        var usage = ReadGpuEngines();
        return (usage, Reading<double>.Unavailable(_nvml.Available ? "El controlador no entregó la temperatura" : "Sin GPU NVIDIA (NVML): no hay una API de temperatura de GPU sin controladores propios"));
    }

    /// <summary>Uso de GPU por los contadores «GPU Engine» (los del Administrador de tareas): por adaptador se suman los motores de cada tipo y se toma el mayor tipo.</summary>
    private Reading<double> ReadGpuEngines()
    {
        try
        {
            if (_gpuQuery is null || ++_gpuReads % 20 == 0) { _gpuQuery?.Dispose(); _gpuQuery = new PdhQuery(); _gpuCounter = _gpuQuery.Add(@"\GPU Engine(*)\Utilization Percentage"); _gpuSamples = 0; }   // se reabre cada 20 lecturas: aparecen y desaparecen instancias (procesos)
            if (_gpuCounter is null) return Reading<double>.Unavailable("Este equipo no expone contadores de GPU");
            _gpuQuery.Collect(); if (++_gpuSamples < 2) return Reading<double>.Unavailable("Midiendo…");
            var byKey = new Dictionary<(string Luid, string Type), double>();
            foreach (var (name, v) in PdhQuery.Doubles(_gpuCounter.Value))
            {
                int l = name.IndexOf("luid_", StringComparison.Ordinal), t = name.IndexOf("engtype_", StringComparison.Ordinal); if (l < 0 || t < 0) continue;
                string luid = name[l..].Split('_', 4) is { Length: >= 3 } p ? p[1] + p[2] : name[l..], type = name[(t + 8)..];
                byKey[(luid, type)] = byKey.GetValueOrDefault((luid, type)) + v;
            }

            return byKey.Count == 0 ? Reading<double>.Unavailable("No hay actividad de GPU que medir") : Reading<double>.Of(Math.Clamp(byKey.Values.Max(), 0, 100));
        }
        catch (Exception e) { return Reading<double>.Unavailable("No se pudo leer el uso de GPU: " + e.GetType().Name); }
    }

    private (Reading<double> Temp, string? Note) ReadCpuTemp()
    {
        try
        {
            if (_thermalQuery is null) { _thermalQuery = new PdhQuery(); _thermalCounter = _thermalQuery.Add(@"\Thermal Zone Information(*)\Temperature"); }
            if (_thermalCounter is null) return (Reading<double>.Unavailable("Este equipo no expone zonas térmicas; leer la CPU exige un controlador con privilegios que WinMac no instala"), null);
            _thermalQuery.Collect();
            var temps = PdhQuery.Doubles(_thermalCounter.Value).Select(x => x.Value - 273.15).Where(c => c is > 0 and < 150).ToList();   // kelvin → °C; lo absurdo se descarta
            return temps.Count == 0 ? (Reading<double>.Unavailable("Las zonas térmicas de este equipo no entregan lecturas válidas"), null) : (Reading<double>.Of(temps.Max()), "zona térmica ACPI (aproximada)");
        }
        catch (Exception e) { return (Reading<double>.Unavailable("No se pudo leer la temperatura: " + e.GetType().Name), null); }
    }

    /// <summary>Lo caro (WMI, procesos) se renueva cada 15 s, no en cada muestra.</summary>
    private void RefreshSlow()
    {
        if (DateTime.UtcNow - _slowAt < TimeSpan.FromSeconds(15)) return; _slowAt = DateTime.UtcNow;
        try
        {
            dynamic? cim = Wmi.Connect(@"root\cimv2");
            if (cim is not null)
            {
                ulong used = 0, total = 0; foreach (dynamic o in cim.ExecQuery("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage")) { total += (ulong)(uint)o.AllocatedBaseSize * 1024 * 1024; used += (ulong)(uint)o.CurrentUsage * 1024 * 1024; }
                _pageFile = total > 0 ? (used, total) : null;
                int usb = 0; foreach (dynamic o in cim.ExecQuery("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB\\\\VID%' AND Present = TRUE")) { string id = (string)o.PNPDeviceID; if (!id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) usb++; }
                _usbCount = usb;
            }
        }
        catch (Exception) { _pageFile = null; _usbCount = null; }
        try { using var p = Process.GetProcessesByName("Memory Compression").FirstOrDefault(); _compressed = p is null ? null : (ulong)p.WorkingSet64; } catch (Exception) { _compressed = null; }   // proceso protegido: si no se puede leer, se dice «no disponible»
    }

    public void Dispose() { _gpuQuery?.Dispose(); _thermalQuery?.Dispose(); _nvml.Dispose(); }

    /// <summary>NVML (biblioteca de NVIDIA que instala el controlador): temperatura y uso de la primera GPU. Se carga dinámicamente: sin GPU NVIDIA simplemente no existe.</summary>
    private sealed class Nvml : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Util { public uint Gpu, Memory; }
        private delegate int InitFn(); private delegate int ShutdownFn(); private delegate int HandleFn(uint index, out IntPtr h); private delegate int TempFn(IntPtr h, int sensor, out uint t); private delegate int UtilFn(IntPtr h, out Util u);
        private IntPtr _lib, _dev; private TempFn? _temp; private UtilFn? _util; private ShutdownFn? _shutdown; private bool _tried;
        public bool Available => _dev != IntPtr.Zero;

        private void Load()
        {
            if (_tried) return; _tried = true;
            try
            {
                if (!NativeLibrary.TryLoad("nvml.dll", out _lib)) return;
                T F<T>(string n) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, n));
                if (F<InitFn>("nvmlInit_v2")() != 0) return;
                _shutdown = F<ShutdownFn>("nvmlShutdown"); _temp = F<TempFn>("nvmlDeviceGetTemperature"); _util = F<UtilFn>("nvmlDeviceGetUtilizationRates");
                if (F<HandleFn>("nvmlDeviceGetHandleByIndex_v2")(0, out _dev) != 0) _dev = IntPtr.Zero;
            }
            catch (Exception) { _dev = IntPtr.Zero; }
        }

        public (double Util, double TempC)? Read()
        {
            Load(); if (_dev == IntPtr.Zero || _temp is null || _util is null) return null;
            try { return _temp(_dev, 0, out uint t) == 0 && _util(_dev, out var u) == 0 ? (u.Gpu, t) : null; } catch (Exception) { return null; }
        }

        public void Dispose() { try { _shutdown?.Invoke(); if (_lib != IntPtr.Zero) NativeLibrary.Free(_lib); } catch (Exception) { /* cierre */ } }
    }
}
