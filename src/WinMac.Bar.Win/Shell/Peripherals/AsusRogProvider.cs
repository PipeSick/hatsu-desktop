using System.Runtime.InteropServices;
using System.Text;
using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de periféricos ASUS ROG por el receptor ROG OMNI (docs/64). A diferencia de los otros lectores, este ENVÍA una consulta de 64 bytes al receptor (la misma que manda Armoury Crate,
/// observada en una captura del tráfico USB) y lee la respuesta. Por eso solo se crea si el usuario lo activó. Solo toca los canales del fabricante del receptor Omni (VID 0B05 / PID 1ACE),
/// nunca el teclado ni el ratón como tales. Sin respuesta en el plazo, el dispositivo se considera no disponible; nunca se inventa un porcentaje.
/// </summary>
internal sealed class AsusRogProvider : IPeripheralProvider
{
    private readonly string? _kbName, _mouseName;
    public AsusRogProvider(string? keyboardName, string? mouseName) { _kbName = string.IsNullOrWhiteSpace(keyboardName) ? null : keyboardName; _mouseName = string.IsNullOrWhiteSpace(mouseName) ? null : mouseName; }
    public string Id => "asus-rog";
    public string DisplayName => "ASUS ROG (receptor 2,4 GHz)";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var paths = Enumerate(out var others);
        foreach (var q in AsusRogProtocol.OmniQueries)
        {
            ct.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(q.UsagePage, out var path)) continue;             // el receptor no está conectado
            int? pct = Ask(path, q, ct);
            string name = (q.Role == AsusRogProtocol.Role.Keyboard ? _kbName : _mouseName) ?? AsusRogProtocol.DisplayName(q.Role);
            res.Add(new RawReading("asus-rog:" + q.Role, name, AsusRogProtocol.Kind(q.Role), ConnectionKind.Dongle, pct is not null, pct,
                ChargeState.Unknown, pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, "Lectura por el canal del receptor ROG OMNI; modelo no identificado."));
        }

        // Otros receptores ASUS (por ejemplo, los de auriculares 2,4 GHz): se listan por su nombre para que se vea que Hatsu los reconoció, pero su batería aún no se puede leer.
        foreach (var (pid, prod) in others)
            res.Add(new RawReading("asus-rx:" + pid.ToString("X4"), prod, prod.Contains("HEADSET", StringComparison.OrdinalIgnoreCase) || prod.Contains("DELTA", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Headset : DeviceKind.Other, ConnectionKind.Dongle, true, null,
                ChargeState.Unknown, BatteryCapability.Unsupported, "Receptor ASUS detectado; la batería de este modelo aún no es compatible."));
        return res;
    }, ct);

    private static int? Ask(string path, AsusRogProtocol.Query q, CancellationToken ct)
    {
        IntPtr h = CreateFile(path, 0xC0000000 /* LECTURA | ESCRITURA */, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return null;
        try
        {
            // Se manda la consulta y se lee con plazo; los informes que no sean la respuesta esperada se ignoran.
            var req = q.Request(); if (!WriteFile(h, req, (uint)req.Length, out _, IntPtr.Zero)) return null;
            var done = new ManualResetEventSlim(); int? result = null;
            var t = new Thread(() =>
            {
                var buf = new byte[AsusRogProtocol.FrameLength];
                for (int i = 0; i < 12 && result is null; i++)                         // unos pocos informes ajenos pueden llegar antes
                {
                    if (!ReadFile(h, buf, (uint)buf.Length, out uint n, IntPtr.Zero)) break;
                    result = AsusRogProtocol.ParsePercent(q, buf.AsSpan(0, (int)n).ToArray());
                }

                done.Set();
            }) { IsBackground = true };
            t.Start();
            if (!done.Wait(800, ct)) { CancelIoEx(h, IntPtr.Zero); t.Join(500); }      // sin respuesta: el dispositivo está apagado o dormido
            return result;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Rutas HID del receptor Omni por página de uso (solo canales del fabricante de 64 bytes).</summary>
    private static Dictionary<ushort, string> Enumerate(out List<(ushort Pid, string Name)> others)
    {
        var map = new Dictionary<ushort, string>(); others = []; var seen = new HashSet<ushort>(); Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 0x12);
        try
        {
            for (int i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                var det = new SP_DEVICE_INTERFACE_DETAIL_DATA { cbSize = IntPtr.Size == 8 ? 8 : 6 };
                if (!SetupDiGetDeviceInterfaceDetail(set, ref d, ref det, Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA>(), out _, IntPtr.Zero)) continue;
                IntPtr q = CreateFile(det.DevicePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); if (q == IntPtr.Zero || q == new IntPtr(-1)) continue;
                try
                {
                    var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() }; if (!HidD_GetAttributes(q, ref a) || a.VendorID != AsusRogProtocol.Vendor) continue;
                    if (a.ProductID != AsusRogProtocol.OmniReceiverPid)
                    {   // otro dispositivo ASUS: solo interesa si es un receptor o unos auriculares inalámbricos (no la placa, el AIO ni el controlador de luces)
                        var sbp = new StringBuilder(256); HidD_GetProductString(q, sbp, 512); string prod = sbp.ToString().Trim();
                        if (prod.Length > 0 && seen.Add(a.ProductID) && (prod.Contains("2.4", StringComparison.Ordinal) || prod.Contains("RECEIVER", StringComparison.OrdinalIgnoreCase) || prod.Contains("WIRELESS", StringComparison.OrdinalIgnoreCase))) others.Add((a.ProductID, prod));
                        continue;
                    }

                    if (!HidD_GetPreparsedData(q, out var pp)) continue;
                    try { if (HidP_GetCaps(pp, out var c) >= 0 && c.UsagePage >= 0xFF00 && c.InputReportByteLength == AsusRogProtocol.FrameLength && c.OutputReportByteLength == AsusRogProtocol.FrameLength) map.TryAdd(c.UsagePage, det.DevicePath); }
                    finally { HidD_FreePreparsedData(pp); }
                }
                finally { CloseHandle(q); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return map;
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
