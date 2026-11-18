namespace WinMac.Bar.Peripherals;

/// <summary>
/// Auriculares HyperX inalámbricos (Cloud II, II Core, III, III S, Alpha, Alpha 2, Flight, Stinger Core) y ratones Pulsefire Haste 2 (VID 03F0 de HP y 0951 de Kingston).
/// Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa (HeadsetControl, HyperHeadset, HyperXWM, HyperBatteryMonitor, ngenuity-linux-cli).
/// Aquí NO se copia código: solo estos datos. <b>SIN VERIFICAR con aparatos reales.</b>
/// Todas las consultas son de solo lectura. Las respuestas se leen sin el 0 inicial del número de informe.
/// </summary>
public static class HyperXProtocol
{
    public const ushort Hp = 0x03F0, Kingston = 0x0951;

    public enum Family { Cloud3, Cloud2, Cloud2Kingston, CloudAlpha, CloudAlpha2, Haste, CloudFlight, Cloud3S, StingerCore }

    /// <summary>Qué colección HID lleva el protocolo: por página/uso o por largos de informe (todos los límites que se indiquen deben cumplirse).</summary>
    public sealed record Sel(ushort Page = 0, ushort Usage = 0, bool PageAtLeast = false, int Out = -1, int In = -1, int OutMin = 0, int InMin = 0, int FeatMin = 0)
    {
        public bool Matches(ushort page, ushort usage, int outLen, int inLen, int featLen) =>
            (Page == 0 || (PageAtLeast ? page >= Page : page == Page && (Usage == 0 || usage == Usage))) && (Out < 0 || outLen == Out) && (In < 0 || inLen == In) && outLen >= OutMin && inLen >= InMin && featLen >= FeatMin;
    }

    public sealed record Model(ushort Vendor, IReadOnlyList<ushort> Pids, Sel Collection, Family Family);

    public static readonly IReadOnlyList<Model> Models =
    [
        new(Hp, [0x05B7, 0x0C9D], new(Out: 62, In: 62), Family.Cloud3),
        new(Hp, [0x069F, 0x0995], new(0xFF00, PageAtLeast: true, OutMin: 62, InMin: 62), Family.Cloud3),     // Cloud II Core Wireless: el mismo protocolo, informes de 64
        new(Hp, [0x06BE, 0x02CC], new(Out: 64, In: 64), Family.Cloud3S),                                        // el informe 0C vive en la colección de 64/64 (Windows la lista fuera de una página de fabricante)
        new(Hp, [0x0696, 0x018B, 0x0D93], new(0xFF90, 0x0303), Family.Cloud2),
        new(Kingston, [0x1718], new(Out: 62), Family.Cloud2Kingston),
        new(Hp, [0x098D], new(0xFF00, PageAtLeast: true, OutMin: 3), Family.CloudAlpha),
        new(Hp, [0x08BE], new(0xFF13, 0xFF00), Family.CloudAlpha2),
        new(Hp, [0x0AB5, 0x0D97, 0x0F98], new(0xFF00, PageAtLeast: true, OutMin: 2, InMin: 8), Family.Haste),
        new(Kingston, [0x16C4, 0x1723, 0x1749], new(0xFF00, PageAtLeast: true, OutMin: 3), Family.CloudFlight),
        new(Hp, [0x0E90], new(0xFF00, PageAtLeast: true, OutMin: 3), Family.CloudFlight),
        new(Hp, [0x0A93], new(0xFF00, 0x0001, FeatMin: 32), Family.StingerCore),
        new(Kingston, [0x170B, 0x1713, 0x173F], new(0xFF00, 0x0001, FeatMin: 32), Family.StingerCore),
    ];

    public sealed record Reading(string Name, DeviceKind Kind, bool Connected, int? Percent, bool Charging, int? Millivolts = null);

    /// <summary>Una pregunta y cómo reconocer su respuesta (el tamaño mínimo y los bytes que deben coincidir).</summary>
    public sealed record Ask(byte[] Request, Func<byte[], bool> Accept);

    private static bool At(byte[] r, int min, params (int Index, byte Value)[] eq) => r.Length >= min && eq.All(e => r[e.Index] == e.Value);

    // ── Cloud III / II Core: informe 66 <cmd>. 82 → [2] conectado; 8A → [2] cargando; 89 → [2..3] mV y [4] %.
    public static Ask Cloud3(byte cmd) => new([0x66, cmd], r => At(r, 5, (0, 0x66), (1, cmd)));
    public static string Cloud3Name(ushort pid) => pid is 0x069F or 0x0995 ? "HyperX Cloud II Core Wireless" : "HyperX Cloud III Wireless";

    // ── Cloud II (HP): 06 FF BB <cmd>. 02 → [7] % y [5..6] mV; 03 → [4] cargando.
    public static Ask Cloud2(byte cmd) => new([0x06, 0xFF, 0xBB, cmd], r => At(r, 8, (0, 0x06), (1, 0xFF), (2, 0xBB), (3, cmd)));
    // ── Cloud II (Kingston 1718): trama fija; la respuesta empieza por 0B y trae BB <cmd> en [2..3].
    public static Ask Cloud2Kingston(byte cmd) => new([0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB, cmd, 0x00], r => At(r, 8, (0, 0x0B), (2, 0xBB), (3, cmd)));
    // ── Cloud Alpha: 21 BB <cmd>. 03 → [3]==1 sin conexión; 0C → [3]==1 cargando; 0B → [3] %.
    public static Ask CloudAlpha(byte cmd) => new([0x21, 0xBB, cmd], r => At(r, 4, (0, 0x21), (2, cmd)));
    // ── Cloud Alpha 2 y Pulsefire Haste 2: <cmd><sub>; la respuesta empieza por cmd+1. 52 01 → [2] conectado; 50 02 → [2] %, [3] cargando, [6..7] mV.
    public static Ask Pair(byte cmd, byte sub) => new([cmd, sub], r => At(r, 8, (0, (byte)(cmd + 1)), (1, sub)));
    // ── Cloud Flight: 21 FF 05 → [3..4] mV.
    public static readonly Ask Flight = new([0x21, 0xFF, 0x05], r => r.Length >= 5);
    // ── Cloud III S: informe 0C 02 03 01 00 <cmd>; respuesta con [5]==cmd y [6] != FF. 02 → [6]==2 conectado; 06 → [6] %; 48 → [6] 1 cargando, 2 lleno.
    public static Ask Cloud3S(byte cmd) => new([0x0C, 0x02, 0x03, 0x01, 0x00, cmd], r => r.Length >= 7 && r[0] == 0x0C && r[5] == cmd && r[6] != 0xFF);

    public static Reading? Cloud3Reading(ushort pid, byte[] connected, byte[]? charging, byte[]? battery)
    {
        bool on = connected[2] != 0; var name = Cloud3Name(pid);
        if (!on) return new(name, DeviceKind.Headset, false, null, false);
        return new(name, DeviceKind.Headset, true, battery is { Length: >= 5 } && battery[4] <= 100 ? battery[4] : null, charging is { Length: >= 3 } && charging[2] == 1, battery is { Length: >= 5 } ? (battery[2] << 8) | battery[3] : null);
    }

    public static Reading? Cloud2Reading(string name, byte[] battery, byte[]? charging, bool millivolts) =>
        battery.Length >= 8 && battery[7] <= 100 ? new(name, DeviceKind.Headset, true, battery[7], charging is { Length: >= 5 } && charging[4] == 1, millivolts ? (battery[5] << 8) | battery[6] : null) : null;

    public static Reading CloudAlphaReading(byte[] link, byte[]? charging, byte[]? battery)
    {
        const string n = "HyperX Cloud Alpha Wireless"; bool on = link[3] != 0x01; if (!on) return new(n, DeviceKind.Headset, false, null, false);
        return new(n, DeviceKind.Headset, true, battery is { Length: >= 4 } && battery[3] <= 100 ? battery[3] : null, charging is { Length: >= 4 } && charging[3] == 0x01);
    }

    public static Reading CloudAlpha2Reading(byte[] link, byte[]? battery)
    {
        const string n = "HyperX Cloud Alpha 2 Wireless"; if (link[2] == 0) return new(n, DeviceKind.Headset, false, null, false);
        return battery is { Length: >= 8 } && battery[2] <= 100 ? new(n, DeviceKind.Headset, true, battery[2], battery[3] == 0x01, battery[6] | (battery[7] << 8)) : new(n, DeviceKind.Headset, true, null, false);
    }

    /// <summary>Pulsefire Haste 2: los dos primeros segundos tras encender, el ratón dice 40 % en vez de su nivel real; el proveedor descarta la lectura mientras dura ese periodo.</summary>
    public const int HasteSettleMs = 6000;
    public static string HasteName(ushort pid) => pid == 0x0AB5 ? "HyperX Pulsefire Haste 2 Core Wireless" : "HyperX Pulsefire Haste 2 Wireless";
    public static Reading HasteReading(ushort pid, bool linked, byte[]? battery, bool settled) =>
        new(HasteName(pid), DeviceKind.Mouse, linked, linked && settled && battery is { Length: >= 8 } && battery[2] <= 100 ? battery[2] : null, battery is { Length: >= 4 } && (battery[3] == 0x01 || battery[3] == 0x02), battery is { Length: >= 8 } ? battery[6] | (battery[7] << 8) : null);

    public static Reading FlightReading(byte[] r)
    {
        int mv = (r[3] << 8) | r[4]; const string n = "HyperX Cloud Flight";
        if (mv > 0x100B) return new(n, DeviceKind.Headset, true, null, true, mv);
        int pct = mv <= 3648 ? (int)(0.00125 * mv) : mv > 3975 ? 100 : (int)Math.Round(0.00000002547505 * Math.Pow(mv, 4) - 0.0003900299 * Math.Pow(mv, 3) + 2.238321 * mv * mv - 5706.256 * mv + 5452299.0);
        return new(n, DeviceKind.Headset, true, Math.Clamp(pct, 0, 100), false, mv);
    }

    public static Reading? Cloud3SReading(byte[] link, byte[]? battery, byte[]? state)
    {
        const string n = "HyperX Cloud III S Wireless"; if (link[6] != 2) return new(n, DeviceKind.Headset, false, null, false);
        return new(n, DeviceKind.Headset, true, battery is { Length: >= 7 } && battery[6] <= 100 ? battery[6] : null, state is { Length: >= 7 } && (state[6] == 1 || state[6] == 2));
    }

    // ── Stinger Core / Cloud Core DTS: informes de característica FF en la colección FF00:0001
    public static readonly byte[] StingerConnection = [0xFF, 0x07, 0x00, 0xFD, 0x04, 0x00, 0x00, 0x02, 0x82, 0x03, 0x05, 0x29, 0x0F, 0x00, 0x00, 0x03, 0x68, 0x10, 0x00, 0x00];
    public static readonly byte[] StingerBattery = [0xFF, 0x07, 0x00, 0xFD, 0x04, 0x0C, 0xF1, 0x02, 0x01, 0x04, 0xF0, 0x0C];
    private static readonly int[] StingerPct = [100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 0];
    private static readonly int[] StingerChargingMv = [4130, 4090, 4050, 4000, 3967, 3946, 3930, 3916, 3871, 3831, 3400];
    private static readonly int[] StingerDischargingMv = [4121, 4042, 3954, 3905, 3838, 3785, 3763, 3738, 3700, 3651, 3517];
    /// <summary>Los receptores de PlayStation (0A93, 1713, 173F) pueden devolver primero el informe anterior: se vuelve a leer hasta que parezca el bueno.</summary>
    public static bool NeedsRereads(ushort pid) => pid is 0x0A93 or 0x1713 or 0x173F;
    public static bool IsFreshConnection(byte[] r) => !(r.Length > 2 && r[1] == 0x01 && r[2] == 0x00);
    public static bool IsStingerBattery(byte[] r) => r.Length >= 13 && r[0] == 0xFF && r[1] == 0x12 && r[2] == 0x05;
    public static string StingerName(ushort pid) => pid == 0x173F ? "HyperX Cloud Core Wireless" : "HyperX Cloud Stinger Core Wireless";

    /// <summary>Conexión: [11]==1 es que el auricular está encendido. Batería: [9] = 03 descargando, 05 cargando, 06 lleno y [11..12] = mV (little endian).</summary>
    public static Reading StingerReading(ushort pid, byte[] connection, byte[]? battery)
    {
        string name = StingerName(pid); if (connection.Length < 13 || connection[11] != 0x01) return new(name, DeviceKind.Headset, false, null, false);
        if (battery is null || battery.Length < 13 || battery[9] is not (0x03 or 0x05 or 0x06)) return new(name, DeviceKind.Headset, true, null, false);
        int mv = battery[11] | (battery[12] << 8); bool charging = battery[9] == 0x05;
        return new(name, DeviceKind.Headset, true, battery[9] == 0x06 ? 100 : Interpolate(mv, charging ? StingerChargingMv : StingerDischargingMv, StingerPct), charging, mv);
    }

    /// <summary>Interpolación lineal por tramos; las tablas van de mayor a menor voltaje. Fuera de la tabla, el extremo.</summary>
    public static int Interpolate(int mv, int[] mvTable, int[] pctTable)
    {
        if (mv >= mvTable[0]) return pctTable[0]; if (mv <= mvTable[^1]) return pctTable[^1];
        for (int i = 0; i + 1 < mvTable.Length; i++)
            if (mv <= mvTable[i] && mv >= mvTable[i + 1]) return (int)Math.Round(pctTable[i + 1] + (double)(pctTable[i] - pctTable[i + 1]) * (mv - mvTable[i + 1]) / (mvTable[i] - mvTable[i + 1]));
        return pctTable[^1];
    }
}
