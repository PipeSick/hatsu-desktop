using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>Una interfaz HID presente (con sus capacidades), consultada sin abrirla para leer ni escribir.</summary>
internal sealed record HidIface(string Path, ushort Vendor, ushort Product, ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength, string ProductName, ushort FeatureLength = 0);

/// <summary>Acceso común a HID para los lectores de periféricos: enumerar interfaces y hacer una consulta con plazo (escribir una trama y leer la respuesta). Un solo lugar para las declaraciones nativas.</summary>
internal static class HidInterop
{
    public static List<HidIface> Scan(Func<ushort, bool>? vendorFilter = null)
    {
        var list = new List<HidIface>(); Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 0x12);
        try
        {
            for (int i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                var det = new SP_DEVICE_INTERFACE_DETAIL_DATA { cbSize = IntPtr.Size == 8 ? 8 : 6 };
                if (!SetupDiGetDeviceInterfaceDetail(set, ref d, ref det, Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA>(), out _, IntPtr.Zero)) continue;
                IntPtr q = CreateFile(det.DevicePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); if (q == IntPtr.Zero || q == new IntPtr(-1)) continue;   // acceso 0: solo consultar
                try
                {
                    var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() }; if (!HidD_GetAttributes(q, ref a) || (vendorFilter is not null && !vendorFilter(a.VendorID))) continue;
                    if (!HidD_GetPreparsedData(q, out var pp)) continue;
                    try
                    {
                        if (HidP_GetCaps(pp, out var c) < 0) continue;
                        var sbp = new StringBuilder(256); HidD_GetProductString(q, sbp, 512);
                        list.Add(new HidIface(det.DevicePath, a.VendorID, a.ProductID, c.UsagePage, c.Usage, c.InputReportByteLength, c.OutputReportByteLength, sbp.ToString().Trim(), c.FeatureReportByteLength));
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                finally { CloseHandle(q); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    public static IntPtr OpenReadWrite(string path) { var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); return h == new IntPtr(-1) ? IntPtr.Zero : h; }
    /// <summary>Abre sin pedir lectura ni escritura (acceso 0): basta para los informes de característica y funciona aunque el sistema tenga el ratón o el teclado abiertos.</summary>
    public static IntPtr OpenFeature(string path) { var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); return h == new IntPtr(-1) ? IntPtr.Zero : h; }

    /// <summary>Envía un informe de característica. <paramref name="data"/>[0] es el número de informe (0 si el aparato no los numera).</summary>
    public static bool SetFeature(IntPtr h, byte[] data) => HidD_SetFeature(h, data, data.Length);

    /// <summary>Lee un informe de característica de <paramref name="length"/> bytes (con el número de informe delante). Null si falla.</summary>
    public static byte[]? GetFeature(IntPtr h, int length, byte reportId = 0)
    {
        var buf = new byte[length]; buf[0] = reportId; return HidD_GetFeature(h, buf, length) ? buf : null;
    }

    public static void Close(IntPtr h) { if (h != IntPtr.Zero) CloseHandle(h); }

    /// <summary>Manda <paramref name="feature"/> por <paramref name="featureHandle"/> y lee tramas de <paramref name="inputHandle"/> hasta que <paramref name="accept"/> devuelva algo (o se acabe el plazo o lleguen 32 tramas ajenas).</summary>
    public static T? SendFeatureThenRead<T>(IntPtr featureHandle, IntPtr inputHandle, byte[] feature, int inputLength, int timeoutMs, Func<byte[], T?> accept, CancellationToken ct) where T : class
    {
        T? result = null; var done = new ManualResetEventSlim(); var armed = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            var buf = new byte[inputLength]; armed.Set();
            for (int i = 0; i < 32 && result is null; i++) { if (!ReadFile(inputHandle, buf, (uint)buf.Length, out uint n, IntPtr.Zero)) break; result = accept(buf.AsSpan(0, (int)n).ToArray()); }
            done.Set();
        }) { IsBackground = true };
        t.Start(); armed.Wait(200); Thread.Sleep(20);
        if (!HidD_SetFeature(featureHandle, feature, feature.Length)) { CancelIoEx(inputHandle, IntPtr.Zero); t.Join(500); return null; }
        try { if (!done.Wait(timeoutMs, ct)) { CancelIoEx(inputHandle, IntPtr.Zero); t.Join(500); } } catch (OperationCanceledException) { CancelIoEx(inputHandle, IntPtr.Zero); t.Join(500); throw; }
        return result;
    }

    /// <summary>Escribe <paramref name="request"/> y lee tramas hasta que <paramref name="accept"/> devuelva algo (o se acabe el plazo o lleguen 16 tramas ajenas). Null = sin respuesta.</summary>
    public static T? Transact<T>(IntPtr h, byte[] request, int inputLength, int timeoutMs, Func<byte[], T?> accept, CancellationToken ct) where T : class
    {
        if (!WriteFile(h, request, (uint)request.Length, out _, IntPtr.Zero)) return null;
        T? result = null; var done = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            var buf = new byte[inputLength];
            for (int i = 0; i < 16 && result is null; i++)
            {
                if (!ReadFile(h, buf, (uint)buf.Length, out uint n, IntPtr.Zero)) break;
                result = accept(buf.AsSpan(0, (int)n).ToArray());
            }

            done.Set();
        }) { IsBackground = true };
        t.Start();
        try { if (!done.Wait(timeoutMs, ct)) { CancelIoEx(h, IntPtr.Zero); t.Join(500); } } catch (OperationCanceledException) { CancelIoEx(h, IntPtr.Zero); t.Join(500); throw; }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct SP_DEVICE_INTERFACE_DETAIL_DATA { public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string DevicePath; }
    [StructLayout(LayoutKind.Sequential)] private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
    [StructLayout(LayoutKind.Sequential)] private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] private static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] private static extern bool HidD_GetProductString(IntPtr h, StringBuilder s, int len);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr p);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, string? e, IntPtr h, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, ref SP_DEVICE_INTERFACE_DETAIL_DATA det, int size, out int req, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tpl);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr h, byte[] buf, uint len, out uint written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr h, byte[] buf, uint len, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(IntPtr h, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
