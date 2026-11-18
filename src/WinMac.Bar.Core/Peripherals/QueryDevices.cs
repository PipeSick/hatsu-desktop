namespace WinMac.Bar.Peripherals;

/// <summary>
/// Aparatos que se leen con UNA consulta y UNA respuesta: Cooler Master, CHERRY, NZXT, Endgame Gear y LAMZU (lote 2). Cada marca es solo datos (qué colección, qué bytes mandar, cómo leer la respuesta);
/// un único lector de Windows las recorre todas. Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa
/// (syntactic-spice/coolermaster-usb-protocol, MM731-Controller, BatteryWatch, HaloBattery, vole, Endgame_Gear_Web_Configurator y el configurador web de LAMZU). Aquí NO se copia código. <b>SIN VERIFICAR con aparatos reales.</b>
/// </summary>
public static class QueryDevices
{
    public enum Transport { Report, Feature }

    /// <summary>Qué colección HID lleva el protocolo: página/uso, número de interfaz USB y/o largos de informe. Lo que no se indique no se exige.</summary>
    public sealed record Selector(ushort Page = 0, ushort Usage = 0, int Interface = -1, int Out = -1, int In = -1, int FeatureMin = -1, int FeatureExact = -1)
    {
        public bool Matches(ushort page, ushort usage, int iface, int outLen, int inLen, int featLen) =>
            (Page == 0 || page == Page) && (Usage == 0 || usage == Usage) && (Interface < 0 || iface == Interface) && (Out < 0 || outLen == Out) && (In < 0 || inLen == In)
            && (FeatureMin < 0 || featLen >= FeatureMin) && (FeatureExact < 0 || featLen == FeatureExact);
    }

    public sealed record Reading(string Key, string Name, DeviceKind Kind, bool Cable, bool Connected, int? Percent, bool Charging);

    /// <param name="Vendors">ids de fabricante a mirar.</param>
    /// <param name="Pids">productos concretos (null = cualquiera del fabricante que tenga la colección).</param>
    /// <param name="Request">los bytes a mandar (con el número de informe delante); se rellenan con ceros hasta el largo del informe.</param>
    /// <param name="DelayMs">solo en informes de característica: espera entre mandar y leer.</param>
    /// <param name="Parse">respuesta → lectura; null = no es la respuesta (se sigue leyendo o se reintenta).</param>
    public sealed record Spec(string Brand, ushort[] Vendors, ushort[]? Pids, Selector Collection, Transport Transport, Func<ushort, byte[]> Request, int TimeoutMs, int DelayMs, Func<ushort, ushort, byte[], Reading?> Parse);

    public static IReadOnlyList<Spec> Specs => [CoolerMaster, Cherry, Nzxt, Endgame, Lamzu];   // propiedad: los Spec se inicializan más abajo

    private static int? Pct(int v) => v is >= 1 and <= 100 ? v : null;

    // ───────── Cooler Master MM712 / MM731 (2516): interfaz 3, página FF0A. Pedir 46 81 00 → 47 81 <modelo> … [10] %, [11] cargando, [12] 0 cable / 1 receptor.
    public static readonly Spec CoolerMaster = new("Cooler Master", [0x2516], [0x0169, 0x0167, 0x01B7], new(Page: 0xFF0A, Interface: 3), Transport.Report, _ => [0x00, 0x46, 0x81, 0x00], 500, 0,
        (vid, pid, r) =>
        {
            if (r.Length < 13 || r[0] != 0x47 || r[1] != 0x81) return null;
            string name = pid == 0x0169 || (pid == 0x01B7 && r[2] == 0x50) ? "Cooler Master MM712" : pid == 0x0167 || (pid == 0x01B7 && r[2] == 0x70) ? "Cooler Master MM731" : "Cooler Master wireless mouse";
            return new("coolermaster:" + name, name, DeviceKind.Mouse, r[12] == 0, true, Pct(r[10]), r[11] != 0);
        });

    // ───────── CHERRY MX 3.0S Wireless (046A): página FF1C uso 0092, informes de 64. Trama: [0]=4, [1..2]=suma de los bytes 3… (LE), [3]=1A, [4..]=03 00 00 55. Respuesta: [3]=1A, [8] %, [9] alimentado por USB.
    public static byte[] CherryQuery()
    {
        var f = new byte[64]; f[0] = 0x04; f[3] = 0x1A; f[4] = 0x03; f[7] = 0x55; int sum = 0; for (int i = 3; i < f.Length; i++) sum += f[i]; f[1] = (byte)(sum & 0xFF); f[2] = (byte)((sum >> 8) & 0xFF); return f;
    }

    public static readonly Spec Cherry = new("CHERRY", [0x046A], [0x00E3, 0x00EA], new(Page: 0xFF1C, Usage: 0x0092, Out: 64, In: 64), Transport.Report, _ => CherryQuery(), 800, 0,
        (vid, pid, r) => r.Length >= 10 && r[0] == 0x04 && r[3] == 0x1A && r[8] <= 100 ? new("cherry:mx30s", "CHERRY MX 3.0S Wireless", DeviceKind.Keyboard, pid == 0x00E3, true, r[8], r[9] != 0) : null);

    // ───────── NZXT Lift Elite (1E71): colección FFCA:0001, informes de 64. Pedir 4E 02 81 00 B0 → 4E 02 97 …; [17..18] % (LE), [22] bit 0 = en el cable de carga. La respuesta 4E E5 es un acuse y se salta.
    public static readonly Spec Nzxt = new("NZXT", [0x1E71], [0x2129, 0x2101], new(Page: 0xFFCA, Usage: 0x0001), Transport.Report, _ => [0x00, 0x4E, 0x02, 0x81, 0x00, 0xB0], 1500, 0,
        (vid, pid, r) => r.Length >= 27 && r[0] == 0x4E && r[1] == 0x02 && r[2] == 0x97 && (r[17] | (r[18] << 8)) <= 100 ? new("nzxt:2101", "NZXT Lift Elite", DeviceKind.Mouse, pid == 0x2129, true, r[17] | (r[18] << 8), (r[22] & 0x01) != 0) : null);

    // ───────── Endgame Gear 4K (3367:1970): FF01:0002, informe de característica A1 de 64. Pedir A1 B4 00 → responde A1 <estado> …; [1] 01 bien, 08/11 dormido o fuera de alcance; [16] %.
    public static readonly Spec Endgame = new("Endgame Gear", [0x3367], [0x1970], new(Page: 0xFF01, Usage: 0x0002, FeatureMin: 64), Transport.Feature, _ => [0xA1, 0xB4, 0x00], 0, 200,
        (vid, pid, r) =>
        {
            if (r.Length < 20 || r[0] != 0xA1 || r[1] == 0x03) return null;   // 03 = ocupado: se lee otra vez
            if (r[1] == 0x01 && r[16] <= 100) return new("endgame:1970", "Endgame Gear 4k wireless mouse", DeviceKind.Mouse, false, true, r[16], false);
            return r[1] is 0x08 or 0x11 ? new("endgame:1970", "Endgame Gear 4k wireless mouse", DeviceKind.Mouse, false, false, null, false) : null;
        });

    // ───────── LAMZU (373E y 37B0): informe de característica de 65. Pedir 00 00 00 02 02 00 83 → responde con el informe id delante (A1 en [1]) o sin él (A1 en [0]): carga y % en los dos bytes que siguen a 83.
    public static readonly Spec Lamzu = new("LAMZU", [0x373E, 0x37B0], null, new(FeatureExact: 65), Transport.Feature, _ => [0, 0, 0, 0x02, 0x02, 0, 0x83], 0, 100,
        (vid, pid, r) =>
        {
            int chg, pct;
            if (r.Length >= 10 && r[1] == 0xA1 && r[4] == 0x02 && r[6] == 0x83) { chg = r[7]; pct = r[8]; }
            else if (r.Length >= 10 && r[0] == 0xA1 && r[3] == 0x02 && r[5] == 0x83) { chg = r[6]; pct = r[7]; }
            else return null;
            if (pct > 100) return null;
            bool cable = LamzuIsCable(vid, pid);
            return new($"lamzu:{vid:X4}:{pid:X4}", LamzuName(vid, pid) ?? "LAMZU wireless mouse", DeviceKind.Mouse, cable, pct > 0 || cable, pct > 0 ? pct : null, chg == 1);   // 0 desde un receptor = ratón apagado o fuera de alcance
        });

    private static readonly (ushort Vid, ushort[] Pids, string Name)[] LamzuModels =
    [
        (0x373E, [0x001C, 0x001D, 0x001E], "LAMZU Maya X"), (0x373E, [0x0016], "LAMZU Maya X V2"), (0x37B0, [0x001F], "LAMZU Maya X V2"), (0x37B0, [0x0005, 0x000B, 0x000C], "LAMZU Tachi"), (0x37B0, [0x001C, 0x001E], "LAMZU Tachi Lite"),
        (0x37B0, [0x0009, 0x000F, 0x0010], "LAMZU Inca"), (0x37B0, [0x0011, 0x0013, 0x0015], "LAMZU Maya"), (0x37B0, [0x0007, 0x000D, 0x000E], "LAMZU Paro"), (0x37B0, [0x0017, 0x0019, 0x001B], "LAMZU Thorn"),
        (0x37B0, [0x0021, 0x0003, 0x0023], "LAMZU Thorn V2"), (0x37B0, [0x0030, 0x0032, 0x0040], "LAMZU Thorn V2"), (0x37B0, [0x0025, 0x0027], "LAMZU Atlantis OG Champion"), (0x37B0, [0x0028, 0x002B, 0x002A], "LAMZU Atlantis Mini"),
        (0x37B0, [0x003E, 0x003F], "LAMZU Atlantis Mini"), (0x37B0, [0x002C, 0x0052], "LAMZU Maya M"), (0x37B0, [0x003A, 0x003B, 0x0044], "LAMZU Maya X"), (0x37B0, [0x003C, 0x0050], "LAMZU Maya"), (0x37B0, [0x0042, 0x0056], "LAMZU Orcus"),
        (0x37B0, [0x0046, 0x0054], "LAMZU Mini"),
    ];

    public static string? LamzuName(ushort vid, ushort pid) => LamzuModels.FirstOrDefault(m => m.Vid == vid && m.Pids.Contains(pid)).Name;

    private static readonly HashSet<ushort> LamzuCables = [0x0005, 0x0007, 0x0009, 0x0011, 0x0017, 0x001C, 0x001F, 0x0021, 0x0025, 0x0028, 0x002C, 0x0030, 0x0034, 0x0038, 0x003A, 0x003C, 0x003E, 0x0040, 0x0042, 0x0044, 0x0046, 0x0050, 0x0052, 0x0056];
    public static bool LamzuIsCable(ushort vid, ushort pid) => vid == 0x373E ? pid == 0x001C : LamzuCables.Contains(pid);
}
