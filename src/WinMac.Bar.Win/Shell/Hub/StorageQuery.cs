using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Lo que el propio disco informa (temperatura, desgaste, horas, datos leídos/escritos). Todo es opcional: lo que el disco no entrega queda en null.
/// Las propiedades <c>*Source</c> dicen de dónde salió cada métrica (para el diagnóstico; el panel no las muestra).
/// </summary>
internal sealed record StorageHealth(double? TempC, long? Hours, double? WearPercent, double? ReadBytes, double? WrittenBytes, string? Why, string? TempSource = null, string? HoursSource = null, string? WearSource = null);

/// <summary>Cada consulta de solo lectura que se le hace a un disco o volumen.</summary>
internal enum StorageOp { DeviceNumber, DeviceDescriptor, AdapterDescriptor, Temperature, NvmeIdentify, NvmeHealthViaDevice, NvmeHealthViaAdapter }

/// <summary>El resultado de una consulta: la traza (ruta, acceso, operación, error, bytes) y los bytes devueltos. La interpretación va aparte, en <see cref="StorageResponse"/>.</summary>
internal sealed record StorageStep(IoctlTrace Trace, byte[] Data);

/// <summary>
/// Consulta al controlador del disco con IOCTL_STORAGE_QUERY_PROPERTY (API documentada): la temperatura (StorageDeviceTemperatureProperty) y, en NVMe, el registro de salud estándar (log 0x02:
/// temperatura compuesta, % usado, horas, unidades leídas/escritas). Se abre el disco solo para consultar propiedades (acceso 0 y, si no, de lectura); si Windows lo niega, el motivo REAL queda en <c>Why</c>.
/// Nunca escribe nada en el disco. Sin administrador puede no estar permitido en algunos equipos: se informa, no se inventa.
/// El diagnóstico detallado (<c>--selftest-system</c>) usa las mismas funciones con la traza completa; el panel solo usa <see cref="Read"/>.
/// </summary>
internal static class StorageQuery
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")] private static extern bool DeviceIoControlBytes(SafeFileHandle h, uint code, [In] byte[] inBuf, uint inSize, [Out] byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400, IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const uint GENERIC_READ = 0x80000000, SHARE_RW = 3, OPEN_EXISTING = 3;

    /// <summary>Acceso 0: solo consultar propiedades del dispositivo (sin derecho de lectura de datos). Es el primero que se prueba.</summary>
    internal const uint QueryAccess = 0;

    /// <summary>Acceso de lectura (GENERIC_READ): solo se prueba si el de consulta recibe «acceso denegado». Sigue sin escribir nada.</summary>
    internal const uint ReadAccess = GENERIC_READ;

    internal static string AccessName(uint access) => access == QueryAccess ? "0 (solo consulta)" : "GENERIC_READ";

    public static StorageHealth Read(int diskNumber, bool nvme)
    {
        string target = $@"\\.\PhysicalDrive{diskNumber}";
        try
        {
            SafeFileHandle? h = null; uint used = QueryAccess; int err = 0;
            foreach (uint access in new[] { QueryAccess, ReadAccess })   // primero solo consulta de propiedades; si hace falta, lectura
            {
                used = access; h = Open(target, access, out err);
                if (h is not null || err != 5) break;
            }

            if (h is null) return new StorageHealth(null, null, null, null, null, err == 5 ? "El disco solo entrega estos datos a un proceso con permisos de administrador" : "Windows no permitió consultar el disco (error " + err + ")");
            using var handle = h;
            var tempStep = Run(handle, target, used, StorageOp.Temperature);
            TemperatureParse? temp = tempStep.Trace.Ok ? StorageResponse.ParseTemperature(tempStep.Data) : null;
            NvmeHealth? log = null;
            if (nvme)
            {
                var logStep = Run(handle, target, used, StorageOp.NvmeHealthViaDevice);
                if (logStep.Trace.Ok) log = StorageResponse.ParseNvmeHealth(logStep.Data);
            }

            bool any = temp?.Celsius is not null || (log?.HasAny ?? false);
            const string fromLog = "registro NVMe 0x02";
            double? t = log?.TempC ?? temp?.Celsius;
            return new StorageHealth(t, log?.Hours, log?.WearPercent, log?.ReadBytes, log?.WrittenBytes,
                any ? null : (nvme ? "El disco no respondió a la consulta de salud" : "El disco no informa su temperatura a las apps (SATA/USB sin esa función)"),
                log?.TempC is not null ? fromLog : temp?.Celsius is not null ? "propiedad de temperatura" : null, log?.Hours is not null ? fromLog : null, log?.WearPercent is not null ? fromLog : null);
        }
        catch (Exception e) { return new StorageHealth(null, null, null, null, null, "No se pudo consultar el disco (" + e.GetType().Name + ")"); }
    }

    /// <summary>Abre la ruta con el acceso dado. Devuelve null y el error Win32 real si Windows lo niega.</summary>
    internal static SafeFileHandle? Open(string path, uint access, out int error)
    {
        var h = CreateFileW(path, access, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (!h.IsInvalid) { error = 0; return h; }
        error = Marshal.GetLastWin32Error(); h.Dispose();
        return null;
    }

    /// <summary>Ejecuta UNA consulta y devuelve su traza. Si falla, el error Win32 queda en la traza y <c>Data</c> vacío.</summary>
    internal static StorageStep Run(SafeFileHandle h, string target, uint access, StorageOp op)
    {
        var (name, input, outSize, code) = Describe(op);
        var output = new byte[outSize];
        bool ok = DeviceIoControlBytes(h, code, input, (uint)input.Length, output, (uint)output.Length, out uint returned, IntPtr.Zero);
        int err = ok ? 0 : Marshal.GetLastWin32Error();
        int n = (int)Math.Min(returned, (uint)output.Length);
        byte[] data = ok ? output.AsSpan(0, n).ToArray() : Array.Empty<byte>();
        return new StorageStep(new IoctlTrace(target, AccessName(access), name, ok, err, n, Array.Empty<string>()), data);
    }

    private static (string Name, byte[] Input, int OutSize, uint Code) Describe(StorageOp op) => op switch
    {
        StorageOp.DeviceNumber => ("IOCTL_STORAGE_GET_DEVICE_NUMBER", Array.Empty<byte>(), 12, IOCTL_STORAGE_GET_DEVICE_NUMBER),
        StorageOp.DeviceDescriptor => ("StorageDeviceProperty (0): fabricante, producto, serie, bus", StorageRequests.PropertyQuery(StorageRequests.DeviceProperty), 1024, IOCTL_STORAGE_QUERY_PROPERTY),
        StorageOp.AdapterDescriptor => ("StorageAdapterProperty (1): bus y transferencia del adaptador", StorageRequests.PropertyQuery(StorageRequests.AdapterProperty), 256, IOCTL_STORAGE_QUERY_PROPERTY),
        StorageOp.Temperature => ("StorageDeviceTemperatureProperty (52): temperatura", StorageRequests.PropertyQuery(StorageRequests.DeviceTemperatureProperty), 512, IOCTL_STORAGE_QUERY_PROPERTY),
        StorageOp.NvmeIdentify => ("NVMe Identify Controller vía propiedad de dispositivo (50)",
            StorageRequests.ProtocolQuery(StorageRequests.DeviceProtocolSpecificProperty, StorageRequests.ProtocolTypeNvme, StorageRequests.NvmeDataTypeIdentify, StorageRequests.NvmeIdentifyController, 0, 4096),
            StorageRequests.ProtocolPrefix + 4096, IOCTL_STORAGE_QUERY_PROPERTY),
        StorageOp.NvmeHealthViaDevice => ("NVMe registro de salud 0x02 vía propiedad de dispositivo (50)",
            StorageRequests.ProtocolQuery(StorageRequests.DeviceProtocolSpecificProperty, StorageRequests.ProtocolTypeNvme, StorageRequests.NvmeDataTypeLogPage, StorageRequests.NvmeLogHealthInfo, 0, 512),
            StorageRequests.ProtocolPrefix + 4096, IOCTL_STORAGE_QUERY_PROPERTY),
        StorageOp.NvmeHealthViaAdapter => ("NVMe registro de salud 0x02 vía propiedad de adaptador (49)",
            StorageRequests.ProtocolQuery(StorageRequests.AdapterProtocolSpecificProperty, StorageRequests.ProtocolTypeNvme, StorageRequests.NvmeDataTypeLogPage, StorageRequests.NvmeLogHealthInfo, 0, 512),
            StorageRequests.ProtocolPrefix + 4096, IOCTL_STORAGE_QUERY_PROPERTY),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>Discos físicos que contienen el volumen, preguntándoselo al propio volumen (IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS), sin pasar por WMI. <paramref name="letter"/> es «C:».</summary>
    internal static (IoctlTrace Trace, IReadOnlyList<int> Disks) VolumeDisks(string letter)
    {
        string target = $@"\\.\{letter}";
        var h = Open(target, QueryAccess, out int err);
        if (h is null) return (new IoctlTrace(target, AccessName(QueryAccess), "abrir el volumen", false, err, 0, Array.Empty<string>()), Array.Empty<int>());
        using (h)
        {
            var output = new byte[256];
            bool ok = DeviceIoControlBytes(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, Array.Empty<byte>(), 0, output, (uint)output.Length, out uint returned, IntPtr.Zero);
            int ioErr = ok ? 0 : Marshal.GetLastWin32Error();
            int n = (int)Math.Min(returned, (uint)output.Length);
            const string name = "IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS";
            if (!ok) return (new IoctlTrace(target, AccessName(QueryAccess), name, false, ioErr, n, Array.Empty<string>()), Array.Empty<int>());
            var parsed = StorageResponse.ParseVolumeExtents(output.AsSpan(0, n));
            return (new IoctlTrace(target, AccessName(QueryAccess), name, true, 0, n, parsed.Problems), parsed.Disks);
        }
    }
}
