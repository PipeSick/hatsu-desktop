namespace WinMac.Bar.Peripherals;

/// <summary>
/// Batería de los periféricos ASUS ROG conectados por el receptor ROG OMNI (USB VID 0x0B05, PID 0x1ACE), por su canal propio de 64 bytes.
/// Origen: captura pasiva del tráfico USB de Armoury Crate (docs/64). Armoury Crate manda una consulta de 64 bytes por cada dispositivo y el receptor contesta con otro de 64 bytes.
/// Aquí solo está la parte pura (armar la consulta y leer la respuesta); el envío lo hace el lector de Windows y solo si el usuario lo activa.
/// Verificado con dos dispositivos reales: Strix Scope II 96 (60 %) y Keris II Origin (97 %). Otros modelos NO están verificados.
/// </summary>
public static class AsusRogProtocol
{
    public const ushort Vendor = 0x0B05, OmniReceiverPid = 0x1ACE, FrameLength = 64;

    public enum Role { Keyboard, Mouse }

    /// <summary>Una consulta: el canal HID del fabricante por el que va (página de uso), el número de informe y los 64 bytes a enviar.</summary>
    public sealed record Query(Role Role, ushort UsagePage, byte ReportId, byte Command, byte Sub)
    {
        /// <summary>Los 64 bytes: [id, 0x12, sub, ceros…]. Es exactamente lo que Armoury Crate manda.</summary>
        public byte[] Request() { var b = new byte[FrameLength]; b[0] = ReportId; b[1] = Command; b[2] = Sub; return b; }
        public int PercentIndex => Role == Role.Keyboard ? 6 : 5;   // medido: teclado 0x3C = 60 % en el byte 6; ratón 0x61 = 97 % en el byte 5
    }

    /// <summary>Las consultas conocidas del receptor Omni: teclado por el canal FF00 (informe 2) y ratón por el FF01 (informe 3).</summary>
    public static readonly IReadOnlyList<Query> OmniQueries =
    [
        new(Role.Keyboard, 0xFF00, 0x02, 0x12, 0x01),
        new(Role.Mouse, 0xFF01, 0x03, 0x12, 0x07),
    ];

    /// <summary>Porcentaje de una respuesta, o null si no es la respuesta a esa consulta o el valor no es creíble (0–100). Nunca inventa un valor.</summary>
    public static int? ParsePercent(Query q, byte[]? response)
    {
        if (response is null || response.Length < 16) return null;
        if (response[0] != q.ReportId || response[1] != q.Command || response[2] != q.Sub) return null;
        int p = response[q.PercentIndex];
        return p is >= 1 and <= 100 ? p : null;
    }

    // ── Auriculares ROG Pelta y ROG Delta II (receptor 2,4 GHz propio, no el Omni).
    // Fuente: el protocolo que describe el proyecto Wireless-Device-Battery-Monitor (krisgfx9, GPL-3.0) y los proyectos en que se basa; aquí NO se copia código, solo estos datos de protocolo.
    // NO verificado con el aparato: pendiente de probar con el audífono encendido (docs/64).
    public const byte HeadsetReportId = 0xCC, HeadsetCommand = 0x12, HeadsetSubBattery = 0x07, HeadsetSubCharging = 0x08;
    public const ushort HeadsetUsagePage = 0xFF00, HeadsetUsage = 0x0001;

    /// <summary>Modelos de auriculares conocidos, por el PID de su receptor (VID 0B05).</summary>
    public static readonly IReadOnlyDictionary<ushort, string> Headsets = new Dictionary<ushort, string> { [0x1B84] = "ROG Pelta", [0x1AFA] = "ROG Delta II" };

    /// <summary>Consulta de 64 bytes: [CC, 12, sub, ceros…]. sub 07 = batería, sub 08 = carga.</summary>
    public static byte[] HeadsetRequest(byte sub) { var b = new byte[FrameLength]; b[0] = HeadsetReportId; b[1] = HeadsetCommand; b[2] = sub; return b; }

    /// <summary>Respuesta de batería del audífono: porcentaje (1–100), dormido/apagado (el aparato contesta FF AA) o nada creíble. Un 0xFE significa «aún no lo sabe» y no se muestra como número.</summary>
    public sealed record HeadsetBattery(int? Percent, bool Asleep);

    public static HeadsetBattery? ParseHeadsetBattery(byte[]? r)
    {
        if (r is null || r.Length < 8 || r[0] != HeadsetReportId) return null;
        if ((r[1] == 0xFF && r[2] == 0xAA) || (r[1] == HeadsetCommand && r[5] == 0xFF && r[6] == 0xAA)) return new HeadsetBattery(null, true);
        if (r[1] != HeadsetCommand || r[2] != HeadsetSubBattery) return null;
        return r[6] is >= 1 and <= 100 ? new HeadsetBattery(r[6], false) : new HeadsetBattery(null, false);
    }

    /// <summary>true = cargando, false = no, null = la respuesta no es la de carga.</summary>
    public static bool? ParseHeadsetCharging(byte[]? r) => r is { Length: >= 8 } && r[0] == HeadsetReportId && r[1] == HeadsetCommand && r[2] == HeadsetSubCharging ? r[5] == 1 : null;

    /// <summary>Nombre para mostrar: el receptor no dice el modelo, solo el tipo de dispositivo.</summary>
    public static string DisplayName(Role r) => r == Role.Keyboard ? "Teclado ROG (receptor 2,4 GHz)" : "Ratón ROG (receptor 2,4 GHz)";
    public static DeviceKind Kind(Role r) => r == Role.Keyboard ? DeviceKind.Keyboard : DeviceKind.Mouse;
}
