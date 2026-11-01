using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>Lo que el propio disco informa (temperatura, desgaste, horas, datos leídos/escritos). Todo es opcional: lo que el disco no entrega queda en null.</summary>
internal sealed record StorageHealth(double? TempC, long? Hours, double? WearPercent, double? ReadBytes, double? WrittenBytes, string? Why);

/// <summary>
/// Consulta al controlador del disco con IOCTL_STORAGE_QUERY_PROPERTY (API documentada): la temperatura (StorageDeviceTemperatureProperty) y, en NVMe, el registro de salud estándar (log 0x02:
/// temperatura compuesta, % usado, horas, unidades leídas/escritas). Se abre el disco solo para consultar propiedades (acceso 0 y, si no, de lectura); si Windows lo niega, el motivo REAL queda en <c>Why</c>.
/// Nunca escribe nada en el disco. Sin administrador puede no estar permitido en algunos equipos: se informa, no se inventa.
/// </summary>
internal static class StorageQuery
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400, GENERIC_READ = 0x80000000, SHARE_RW = 3, OPEN_EXISTING = 3;
    private const uint StorageDeviceTemperatureProperty = 12, StorageDeviceProtocolSpecificProperty = 50;

    public static StorageHealth Read(int diskNumber, bool nvme)
    {
        try
        {
            using var h = Open(diskNumber, out int err);
            if (h is null) return new StorageHealth(null, null, null, null, null, err == 5 ? "El disco solo entrega estos datos a un proceso con permisos de administrador" : "Windows no permitió consultar el disco (error " + err + ")");
            double? temp = ReadTemperature(h); StorageHealth? log = nvme ? ReadNvmeLog(h) : null;
            return new StorageHealth(log?.TempC ?? temp, log?.Hours, log?.WearPercent, log?.ReadBytes, log?.WrittenBytes,
                temp is null && log is null ? (nvme ? "El disco no respondió a la consulta de salud" : "El disco no informa su temperatura a las apps (SATA/USB sin esa función)") : null);
        }
        catch (Exception e) { return new StorageHealth(null, null, null, null, null, "No se pudo consultar el disco (" + e.GetType().Name + ")"); }
    }

    private static SafeFileHandle? Open(int n, out int err)
    {
        err = 0;
        foreach (uint access in new[] { 0u, GENERIC_READ })   // primero solo consulta de propiedades; si hace falta, lectura
        {
            var h = CreateFileW($@"\\.\PhysicalDrive{n}", access, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!h.IsInvalid) return h; err = Marshal.GetLastWin32Error(); h.Dispose(); if (err != 5) break;
        }

        return null;
    }

    private static IntPtr Buf(int size) { var p = Marshal.AllocHGlobal(size); for (int i = 0; i < size; i += 8) Marshal.WriteInt64(p, i, 0); return p; }

    private static double? ReadTemperature(SafeFileHandle h)
    {
        var inp = Buf(16); var outp = Buf(512);
        try
        {
            Marshal.WriteInt32(inp, 0, (int)StorageDeviceTemperatureProperty); Marshal.WriteInt32(inp, 4, 0);   // PropertyStandardQuery
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, inp, 12, outp, 512, out uint ret, IntPtr.Zero) || ret < 24 + 16) return null;
            int count = Marshal.ReadInt16(outp, 12); if (count < 1) return null;   // STORAGE_TEMPERATURE_DATA_DESCRIPTOR: InfoCount; el primer STORAGE_TEMPERATURE_INFO empieza en 24 (Temperature a +2)
            short t = Marshal.ReadInt16(outp, 24 + 2); return t is > 0 and < 150 ? t : null;
        }
        finally { Marshal.FreeHGlobal(inp); Marshal.FreeHGlobal(outp); }
    }

    private static StorageHealth? ReadNvmeLog(SafeFileHandle h)
    {
        const int header = 8, proto = 40, dataLen = 512, total = header + proto + 4096;   // STORAGE_PROPERTY_QUERY(8) + STORAGE_PROTOCOL_SPECIFIC_DATA(40) + datos
        var buf = Buf(total);
        try
        {
            Marshal.WriteInt32(buf, 0, (int)StorageDeviceProtocolSpecificProperty); Marshal.WriteInt32(buf, 4, 0);
            Marshal.WriteInt32(buf, header + 0, 3);        // ProtocolTypeNvme
            Marshal.WriteInt32(buf, header + 4, 2);        // NVMeDataTypeLogPage
            Marshal.WriteInt32(buf, header + 8, 2);        // NVME_LOG_PAGE_HEALTH_INFO
            Marshal.WriteInt32(buf, header + 12, 0);       // sub-valor
            Marshal.WriteInt32(buf, header + 16, proto);   // ProtocolDataOffset
            Marshal.WriteInt32(buf, header + 20, dataLen); // ProtocolDataLength
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, (uint)(header + proto + dataLen), buf, (uint)total, out uint ret, IntPtr.Zero) || ret < header + proto + 144) return null;
            int d = header + proto;   // el registro empieza tras el descriptor (cabecera de 8 + datos de protocolo)
            byte B(int i) => Marshal.ReadByte(buf, d + i); ulong U64(int i) => (ulong)Marshal.ReadInt64(buf, d + i);
            int kelvin = B(1) | (B(2) << 8); double? temp = kelvin is > 273 and < 423 ? kelvin - 273.15 : null;
            double? wear = B(5) <= 255 ? Math.Min(B(5), (byte)100) : null;
            ulong hours = U64(128), read = U64(32), written = U64(48);
            return new StorageHealth(temp, hours is > 0 and < 2_000_000 ? (long)hours : null, wear, read > 0 ? read * 512_000.0 : null, written > 0 ? written * 512_000.0 : null, null);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
