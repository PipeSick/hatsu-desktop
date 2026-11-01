using System.Runtime.InteropServices;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>P/Invoke de los monitores del panel multifunción. Solo APIs documentadas y SIN administrador (kernel32, pdh, iphlpapi, shell32, gdi32).</summary>
internal static class HubNative
{
    [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    // ── PDH (contadores de rendimiento de Windows)
    public const uint PDH_FMT_DOUBLE = 0x200, PDH_MORE_DATA = 0x800007D2;
    [DllImport("pdh.dll")] public static extern uint PdhOpenQuery(IntPtr source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] public static extern uint PdhGetRawCounterArrayW(IntPtr counter, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    // ── iphlpapi: conexiones TCP con su proceso dueño
    [DllImport("iphlpapi.dll", SetLastError = true)] public static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);

    // ── Iconos de aplicaciones
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfoW(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [DllImport("gdi32.dll")] public static extern int GetObjectW(IntPtr h, int size, out BITMAP bmp);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
}

/// <summary>Una consulta PDH: se abre, se le añaden contadores, se muestrea y se cierra (siempre se libera el manejador).</summary>
internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    public PdhQuery() { if (HubNative.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; throw new InvalidOperationException("PDH no disponible"); } }

    /// <summary>Devuelve null si el contador no existe en este equipo (p. ej. sin GPU compatible): quien llama lo trata como «no disponible».</summary>
    public IntPtr? Add(string path) => HubNative.PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var c) == 0 ? c : null;
    public bool Collect() => HubNative.PdhCollectQueryData(_query) == 0;

    /// <summary>Valores formateados (double) de un contador con comodín: (instancia, valor).</summary>
    public static List<(string Name, double Value)> Doubles(IntPtr counter)
    {
        var result = new List<(string, double)>(); uint size = 0, count = 0;
        if (HubNative.PdhGetFormattedCounterArrayW(counter, HubNative.PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero) != HubNative.PDH_MORE_DATA || size == 0) return result;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (HubNative.PdhGetFormattedCounterArrayW(counter, HubNative.PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return result;
            const int ItemSize = 24;   // PDH_FMT_COUNTERVALUE_ITEM_W: nombre (8) + CStatus (4) + relleno (4) + double (8)
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buf + i * ItemSize; string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)); uint status = (uint)Marshal.ReadInt32(item, 8);
                if (name is not null && (status == 0 || status == 1 /* PDH_CSTATUS_NEW_DATA */)) result.Add((name, BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16))));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    /// <summary>Valores CRUDOS acumulados (p. ej. bytes leídos desde el arranque) de un contador con comodín: (instancia, valor).</summary>
    public static List<(string Name, long Value)> Raw(IntPtr counter)
    {
        var result = new List<(string, long)>(); uint size = 0, count = 0;
        if (HubNative.PdhGetRawCounterArrayW(counter, ref size, out count, IntPtr.Zero) != HubNative.PDH_MORE_DATA || size == 0) return result;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (HubNative.PdhGetRawCounterArrayW(counter, ref size, out count, buf) != 0) return result;
            const int ItemSize = 48;   // PDH_RAW_COUNTER_ITEM_W: nombre (8) + PDH_RAW_COUNTER (40: CStatus, FILETIME, relleno, FirstValue, SecondValue, MultiCount)
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buf + i * ItemSize; string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)); uint status = (uint)Marshal.ReadInt32(item, 8);
                if (name is not null && (status == 0 || status == 1)) result.Add((name, Marshal.ReadInt64(item, 8 + 16)));   // FirstValue está en el desplazamiento 16 del PDH_RAW_COUNTER
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    public void Dispose() { if (_query != IntPtr.Zero) { HubNative.PdhCloseQuery(_query); _query = IntPtr.Zero; } }
}

/// <summary>WMI con enlace tardío (sin paquetes NuGet), como el brillo. Devuelve null si WMI o el espacio de nombres no están disponibles.</summary>
internal static class Wmi
{
    public static dynamic? Connect(string ns)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WbemScripting.SWbemLocator"); if (t is null) return null;
            dynamic locator = Activator.CreateInstance(t)!; return locator.ConnectServer(".", ns);
        }
        catch (Exception) { return null; }
    }
}
