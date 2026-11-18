namespace WinMac.Bar.Peripherals;

/// <summary>
/// Aparatos que se leen con UNA consulta y UNA respuesta: Cooler Master, CHERRY, NZXT, Endgame Gear y LAMZU (lote 2). Cada marca es solo datos (qué colección, qué bytes mandar, cómo leer la respuesta);
/// un único lector de Windows las recorre todas. Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa
/// (syntactic-spice/coolermaster-usb-protocol, MM731-Controller, BatteryWatch, HaloBattery, vole, Endgame_Gear_Web_Configurator y el configurador web de LAMZU). Aquí NO se copia código. <b>SIN VERIFICAR con aparatos reales.</b>
/// </summary>
public static class QueryDevices
{
    /// <summary>Listen = no se escribe nada: el mando manda su batería dentro de sus informes normales y solo se escucha. Report = se escribe un informe de salida y se lee uno de entrada de la misma colección. Feature = se manda y se lee un informe de característica. FeatureThenInput = se manda un informe de característica por una colección y la respuesta llega como informe de entrada por otra hermana de la misma interfaz.</summary>
    public enum Transport { Report, Feature, FeatureThenInput, Listen }

    /// <summary>Qué colección HID lleva el protocolo: página/uso, número de interfaz USB y/o largos de informe. Lo que no se indique no se exige.</summary>
    public sealed record Selector(ushort Page = 0, ushort Usage = 0, int Interface = -1, int Out = -1, int In = -1, int FeatureMin = -1, int FeatureExact = -1, ushort PageMin = 0, int OutMin = -1, int InMin = -1)
    {
        public bool Matches(ushort page, ushort usage, int iface, int outLen, int inLen, int featLen) =>
            (Page == 0 || page == Page) && (Usage == 0 || usage == Usage) && (Interface < 0 || iface == Interface) && (Out < 0 || outLen == Out) && (In < 0 || inLen == In)
            && (FeatureMin < 0 || featLen >= FeatureMin) && (FeatureExact < 0 || featLen == FeatureExact) && page >= PageMin && outLen >= OutMin && inLen >= InMin;
    }

    public sealed record Reading(string Key, string Name, DeviceKind Kind, bool Cable, bool Connected, int? Percent, bool Charging);

    /// <param name="Vendors">ids de fabricante a mirar.</param>
    /// <param name="Pids">productos concretos (null = cualquiera del fabricante que tenga la colección).</param>
    /// <param name="Request">los bytes a mandar (con el número de informe delante); se rellenan con ceros hasta el largo del informe.</param>
    /// <param name="DelayMs">solo en informes de característica: espera entre mandar y leer.</param>
    /// <param name="Parse">respuesta → lectura; null = no es la respuesta (se sigue leyendo o se reintenta).</param>
    /// <param name="PrimeFeatureReport">informe de característica que se lee una vez antes de escuchar cuando la colección tiene informes largos (Bluetooth): hace que el mando mande los informes completos.</param>
    /// <param name="Exclude">productos que no son de este protocolo aunque sean del fabricante (p. ej. los auriculares ATK, que hablan por un puerto serie).</param>
    /// <param name="HighInterfaceFirst">probar primero las interfaces USB de número más alto (la 2 de Pulsar rechaza el informe de característica).</param>
    public sealed record Spec(string Brand, ushort[] Vendors, ushort[]? Pids, Selector Collection, Transport Transport, Func<ushort, byte[]> Request, int TimeoutMs, int DelayMs, Func<ushort, ushort, byte[], Reading?> Parse, ushort[]? Exclude = null, bool HighInterfaceFirst = false, Selector? ReplyCollection = null, byte? PrimeFeatureReport = null);

    public static IReadOnlyList<Spec> Specs => [CoolerMaster, Cherry, Nzxt, Endgame, Arbiter, Lamzu, KysonaDedicated, KysonaVendor, Pulsar, AtkNk, WlMouse, Vaxee, Areson, Dareu, PlayStation, SwitchPro, GameSirNova, GameSirCyclone2, Beitong];   // propiedad: los Spec se inicializan más abajo

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

    // ───────── Kysona, Pulsar (3554), ATK/VXE, Redragon (Compx), Hitscan, G-Wolves y HECATE: trama de 17 bytes, informe 08: [08][cmd][0×14][suma], suma = 0x55 − (bytes 0…15). cmd 04 = batería → [6] %, [7] cargando, [8..9] mV.
    public static byte[] Frame17(byte cmd) { var f = new byte[17]; f[0] = 0x08; f[1] = cmd; int sum = 0; for (int i = 0; i < 16; i++) sum += f[i]; f[16] = (byte)(0x55 - sum); return f; }
    public static bool IsFrame17Reply(byte cmd, byte[] r) { if (r.Length < 17 || r[0] != 0x08 || r[1] != cmd) return false; int sum = 0; for (int i = 0; i < 16; i++) sum += r[i]; return r[16] == (byte)(0x55 - sum); }

    private static readonly HashSet<ushort> Frame17Cables = [0xF55E, 0xF58C, 0xF58F, 0xF506];
    private static readonly ushort[] AtkHeadsets = [0x11CA, 0x11CB, 0x1203, 0x1204];   // hablan por un puerto serie: nunca se les manda una trama de ratón

    /// <summary>Nombre del ratón por fabricante/producto: los receptores se llaman a sí mismos «Dongle», así que se usan nombres genéricos por familia.</summary>
    public static string Frame17Name(ushort vid, ushort pid) => vid switch
    {
        0x3554 when pid is 0xF55D or 0xF5D5 or 0xF55E => "Redragon wireless mouse",
        0x3554 when pid is 0xF58A or 0xF58C or 0xF58E or 0xF58F or 0xF505 or 0xF506 => "VXE Dragonfly R1",
        0x3770 => "Hitscan Hyperlight", 0x35BB => "HECATE mouse", 0x373B => "ATK wireless mouse", 0x33E4 => GloriousProtocol.GWolvesName(pid), 0x3710 => "Pulsar wireless mouse", _ => "Wireless mouse",
    };

    private static bool Frame17IsCable(ushort vid, ushort pid) => (vid == 0x3554 && Frame17Cables.Contains(pid)) || (vid == 0x33E4 && GloriousProtocol.Identify(vid, pid) is { Cable: true }) || (vid == 0x35BB && pid is 0xD3FA or 0xD3FC or 0xD3FD);

    private static Reading? Frame17Parse(ushort vid, ushort pid, byte[] r) =>
        IsFrame17Reply(0x04, r) ? new($"kysona:{vid:X4}:{pid:X4}", Frame17Name(vid, pid), DeviceKind.Mouse, Frame17IsCable(vid, pid), true, r[6] <= 100 && r[6] > 0 ? r[6] : null, r[7] != 0) : null;

    private static readonly ushort[] Frame17Vendors = [0x3554, 0x373B, 0x3770, 0x33E4, 0x3710, 0x35BB];

    /// <summary>Colección propia de 17 bytes (Kysona).</summary>
    public static readonly Spec KysonaDedicated = new("Kysona, Pulsar, ATK, Redragon", Frame17Vendors, null, new(Out: 17, In: 17), Transport.Report, _ => Frame17(0x04), 600, 0, Frame17Parse, AtkHeadsets);
    /// <summary>Receptores ATK/VXE: el informe 08 va en una colección de fabricante que también lleva informes mayores (la FF02:0002 si existe). La respuesta con suma de comprobación descarta lo que no hable este protocolo.</summary>
    public static readonly Spec KysonaVendor = new("Kysona, Pulsar, ATK, Redragon", Frame17Vendors, null, new(PageMin: 0xFF00, OutMin: 17, InMin: 17), Transport.Report, _ => Frame17(0x04), 600, 0, Frame17Parse, AtkHeadsets);

    // ───────── Pulsar X2 V3 / X2 V3 eS (3710): informe de característica de 64 bytes. Batería = 08 81 01 con suma de 16 bits (bytes 0…61, LE en 62…63). Responde [estado][08][81][01]…[6] %; estado 01 = contestó, 02/05 = pendiente.
    private static readonly IReadOnlyDictionary<ushort, (bool Cable, string? Name)> PulsarProducts = new Dictionary<ushort, (bool, string?)> { [0x3402] = (true, "Pulsar X2 V3"), [0x3406] = (true, "Pulsar X2 V3 eS"), [0x5402] = (false, null), [0x5403] = (false, null) };

    public static byte[] PulsarRequest()
    {
        var req = new byte[65]; req[2] = 0x08; req[3] = 0x81; req[4] = 0x01; int sum = 0; for (int i = 1; i < 63; i++) sum += req[i]; req[63] = (byte)(sum & 0xFF); req[64] = (byte)(sum >> 8); return req;
    }

    public static readonly Spec Pulsar = new("Pulsar", [0x3710], PulsarProducts.Keys.ToArray(), new(FeatureMin: 65), Transport.Feature, _ => PulsarRequest(), 0, 50,
        (vid, pid, r) =>
        {
            if (r.Length < 65 || r[2] != 0x08 || r[3] != 0x81 || r[4] != 0x01 || r[1] != 0x01 || r[7] > 100 || !PulsarProducts.TryGetValue(pid, out var p)) return null;   // 02/05 = pendiente; FF = sin lectura
            int pct = r[7];
            return new("pulsar:" + pid.ToString("X4"), p.Name ?? "Pulsar wireless mouse", DeviceKind.Mouse, p.Cable, pct > 0 || p.Cable, pct > 0 || p.Cable ? pct : null, p.Cable);   // sin registro de carga: por cable se da por cargando
        }, HighInterfaceFirst: true);

    // ───────── ATK / VXE NearLink en el receptor 373B:1216: colección FF05:0001, informe 08 de 64: [08][suma][72][02][00][01][cmd][01]…, suma = bytes 2…63. cmd 07 = batería. Respuesta [08][72][estado][seq][00][cmd][01][valor]; estado FF = error.
    public static byte[] AtkNkQuery(byte cmd)
    {
        var f = new byte[64]; f[0] = 0x08; f[2] = 0x72; f[3] = 0x02; f[5] = 0x01; f[6] = cmd; f[7] = 0x01; int sum = 0; for (int i = 2; i < 64; i++) sum += f[i]; f[1] = (byte)(sum & 0xFF); return f;
    }

    public static readonly Spec AtkNk = new("ATK / VXE NK", [0x373B], [0x1216], new(Page: 0xFF05, Usage: 0x0001, OutMin: 64, InMin: 8), Transport.Report, _ => AtkNkQuery(0x07), 900, 0,
        (vid, pid, r) => r.Length >= 8 && r[0] == 0x08 && r[1] == 0x72 && r[5] == 0x07 && r[2] != 0xFF && r[7] is >= 1 and <= 100 ? new("atknk:1216", "ATK / VXE NK mouse", DeviceKind.Mouse, false, true, r[7], false) : null);   // la carga (cmd 0F) no se consulta en esta versión

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

    // ───────── Arbiter Studio Akitsu (373E): el mismo informe de 65 bytes que LAMZU, en la colección FFFF. Respuesta: [4]==02 y [6]==83 con el id delante (estado en [1]) o sin él (estado en [0]); A1/A2 = contestó, A0 = ratón dormido.
    // Va antes que LAMZU: comparten fabricante y LAMZU lo llamaría «LAMZU wireless mouse».
    private static readonly (ushort Cable, ushort Receiver, string Name)[] ArbiterModels = [(0x0009, 0x000A, "Arbiter Studio Akitsu"), (0x0038, 0x0039, "Arbiter Studio Akitsu 3950"), (0x0066, 0x0067, "Arbiter Studio Akitsu Medium")];

    public static readonly Spec Arbiter = new("Arbiter Studio", [0x373E], ArbiterModels.SelectMany(m => new[] { m.Cable, m.Receiver }).ToArray(), new(Page: 0xFFFF, FeatureExact: 65), Transport.Feature, _ => [0, 0, 0, 0x02, 0x02, 0, 0x83], 0, 100,
        (vid, pid, r) =>
        {
            byte status, chg, level;
            if (r.Length >= 9 && r[4] == 0x02 && r[6] == 0x83) { status = r[1]; chg = r[7]; level = r[8]; }
            else if (r.Length >= 8 && r[3] == 0x02 && r[5] == 0x83) { status = r[0]; chg = r[6]; level = r[7]; }
            else return null;
            var m = ArbiterModels.FirstOrDefault(x => x.Cable == pid || x.Receiver == pid); if (m.Name is null) return null;
            string key = $"arbiter:373E:{m.Receiver:X4}"; bool cable = m.Cable == pid;   // cable y receptor comparten clave: enchufar el cable actualiza la misma entrada
            if (status == 0xA0) return new(key, m.Name, DeviceKind.Mouse, cable, false, null, false);
            if ((status != 0xA1 && status != 0xA2) || level > 100) return null;
            return new(key, m.Name, DeviceKind.Mouse, cable, level > 0 || cable, level > 0 ? level : null, chg == 0x01);   // 0 desde un receptor = ratón apagado o fuera de alcance
        });

    // ───────── WLMouse (36A7): el mismo informe de 65 bytes en la colección FFFF. Responde con [3]==02, [4]==02, [6]==83; [1] A1/A2 = contestó, A0 = dormido; [7]==1 cargando y [8] %. El cable que contesta se lista con la clave de su receptor.
    private static readonly (ushort Pid, bool Receiver, string Name, ushort ReceiverPid)[] WlMouseModels =
    [
        (0xA879, false, "WLMouse Sword X 8K", 0xA878), (0xA884, false, "WLMouse Beast X 8K", 0xA883), (0xA860, false, "WLMouse mouse", 0), (0xA866, false, "WLMouse mouse", 0), (0xA867, false, "WLMouse mouse", 0),
        (0xA856, true, "WLMouse Beast X V2", 0), (0xA868, true, "WLMouse Beast X Mini Pro", 0), (0xA870, true, "WLMouse Beast X Pro", 0), (0xA878, true, "WLMouse Sword X 8K", 0), (0xA880, true, "WLMouse Beast MAX", 0),
        (0xA883, true, "WLMouse Beast X 8K", 0), (0xA885, true, "WLMouse Beast X Mini", 0),
    ];

    public static readonly Spec WlMouse = new("WLMouse", [0x36A7], WlMouseModels.Select(m => m.Pid).ToArray(), new(Page: 0xFFFF, FeatureMin: 65), Transport.Feature, _ => [0, 0, 0, 0x02, 0x02, 0, 0x83], 0, 100,
        (vid, pid, r) =>
        {
            if (r.Length < 9 || r[3] != 0x02 || r[4] != 0x02 || r[6] != 0x83) return null;   // todavía no es la respuesta
            var m = WlMouseModels.FirstOrDefault(x => x.Pid == pid); if (m.Name is null) return null;
            string key = "wlmouse:" + (m.ReceiverPid != 0 ? m.ReceiverPid : pid).ToString("X4");
            if (r[1] == 0xA0) return new(key, m.Name, DeviceKind.Mouse, !m.Receiver, false, null, false);
            if ((r[1] != 0xA1 && r[1] != 0xA2) || r[8] > 100) return null;
            return new(key, m.Name, DeviceKind.Mouse, !m.Receiver, true, r[8], r[7] == 0x01);
        });

    // ───────── VAXEE 4K y 8K (3057: receptores 1002 y 2002, cable ZYGEN 1011): colección FF05:0001, informe de característica de 64. Lectura (no escritura): 0E A5 <cmd> 01 01. cmd 0B = batería en pasos de 5 % (0…20). La carga (cmd 10) no se consulta en esta versión.
    public static readonly Spec Vaxee = new("VAXEE", [0x3057], [0x1002, 0x2002, 0x1011], new(Page: 0xFF05, Usage: 0x0001, FeatureExact: 64), Transport.Feature, _ => [0x0E, 0xA5, 0x0B, 0x01, 0x01], 0, 100,
        (vid, pid, r) =>
        {
            if (r.Length < 6 || r[0] != 0x0E || r[1] != 0xA5 || r[2] != 0x0B || r[4] != 1 || r[5] > 20) return null;
            string name = pid == 0x1011 ? "ZYGEN NP-01S V2 Wireless" : pid == 0x2002 ? "VAXEE 8K Wireless" : "VAXEE wireless mouse";
            return new("vaxee:" + pid.ToString("X4"), name, DeviceKind.Mouse, pid == 0x1011, true, r[5] * 5, false);
        });

    // ───────── Redragon con receptor Areson (25A7:FA07 y FA08) y DAREU EM901X (260D): la consulta va como informe de característica de 17 bytes [08][04]…[suma 0x55 − bytes 0…15] por la colección FF02:0002 de la interfaz 1 y la respuesta llega como informe de entrada de 17 por la FF01:0000 de la misma interfaz.
    // Areson: la trama entera suma 0x55 (mod 256), [6] = nivel de 0 a 10 (×10) y [7] cargando. DAREU: [6] = % directo y [7] cargando.
    private static readonly Selector FeatureFf02 = new(Page: 0xFF02, Usage: 0x0002, Interface: 1, FeatureMin: 17), InputFf01 = new(Page: 0xFF01, Usage: 0x0000, Interface: 1, InMin: 17);

    public static readonly Spec Areson = new("Redragon (Areson)", [0x25A7], [0xFA07, 0xFA08], FeatureFf02, Transport.FeatureThenInput, _ => Frame17(0x04), 1800, 0,
        (vid, pid, r) =>
        {
            if (r.Length < 17 || r[0] != 0x09 || r[1] != 0x04 || r[2] != 0x00 || r[6] > 10) return null; int total = 0; foreach (var b in r.Take(17)) total += b; if ((total & 0xFF) != 0x55) return null;
            return new($"areson:25A7:{pid:X4}", "Redragon wireless mouse", DeviceKind.Mouse, false, true, r[6] * 10, r[7] != 0);
        }, ReplyCollection: InputFf01);

    public static readonly Spec Dareu = new("DAREU", [0x260D], [0x1090, 0x1074], FeatureFf02, Transport.FeatureThenInput, _ => Frame17(0x04), 1000, 0,
        (vid, pid, r) => r.Length == 17 && r[0] == 0x09 && r[1] == 0x04 && r[2] == 0x00 && r[6] <= 100 ? new($"dareu:260D:{pid:X4}", "DAREU EM901X", DeviceKind.Mouse, pid == 0x1090, true, r[6], r[7] != 0) : null, ReplyCollection: InputFf01);

    // ───────── MANDOS ─────────

    // PlayStation DualShock 4 / DualSense / Edge (054C): la batería va en el informe de entrada normal; solo se escucha.
    // DS4: USB informe 01 byte 30, Bluetooth informe 11 byte 32: bits 0–3 nivel, bit 4 cable. DualSense: USB informe 01 byte 53, Bluetooth informe 31 byte 54: bits 0–3 nivel (0–10), bits 4–7 estado.
    // Por Bluetooth mandan un informe reducido (largo mayor que 64) hasta que se lee el informe de característica 05; el informe 01 de más de 64 bytes se descarta.
    private static bool IsDs4(ushort pid) => pid is 0x05C4 or 0x09CC or 0x0BA0;

    public static Reading? PlayStationParse(ushort vid, ushort pid, byte[] r)
    {
        bool ds4 = IsDs4(pid); int at = ds4 ? (r.Length > 0 && r[0] == 0x01 ? 30 : r.Length > 0 && r[0] == 0x11 ? 32 : 0) : (r.Length > 0 && r[0] == 0x01 ? 53 : r.Length > 0 && r[0] == 0x31 ? 54 : 0);
        if (at == 0 || r.Length <= at || (r[0] == 0x01 && r.Length > 64)) return null;
        string name = ds4 ? "DualShock 4" : pid == 0x0DF2 ? "DualSense Edge" : "DualSense"; bool usb = r[0] == 0x01; string key = $"ps:{pid:X4}";
        int level = r[at] & 0x0F;
        if (ds4)
        {
            if (pid == 0x0BA0 && r[at] == 0) return null;   // adaptador inalámbrico sin mando
            bool cable = (r[at] & 0x10) != 0;
            return new(key, name, DeviceKind.Gamepad, usb, true, cable ? (level >= 11 ? 100 : Math.Clamp(level * 10, 0, 100)) : Math.Clamp(level * 10 + 5, 0, 100), cable && level < 11);
        }

        int state = r[at] >> 4; int pct = Math.Clamp(level * 10 + 5, 0, 100); bool charging = state == 0x1;
        if (state == 0x2) { pct = 100; charging = true; }
        return new(key, name, DeviceKind.Gamepad, usb, true, pct, charging);
    }

    public static readonly Spec PlayStation = new("PlayStation", [0x054C], [0x05C4, 0x09CC, 0x0BA0, 0x0CE6, 0x0DF2], new(Page: 0x01, Usage: 0x05), Transport.Listen, _ => [], 500, 0, PlayStationParse, PrimeFeatureReport: 0x05);

    // Nintendo Switch Pro y compatibles (057E:2009, incluido el GameSir Tarantula Pro en modo Switch): informe completo 30, byte 2: bits 7…5 = nivel (0…4), bit 4 = cargando. Quien lo use (Steam Input u otro) debe activar los informes completos: Hatsu solo escucha.
    public static readonly Spec SwitchPro = new("Nintendo Switch Pro", [0x057E], [0x2009], new(Page: 0x01, InMin: 13), Transport.Listen, _ => [], 350, 0,
        (vid, pid, r) => r.Length >= 13 && r[0] == 0x30 && ((r[2] & 0xE0) >> 5) <= 4 ? new("switchpro:2009", "Nintendo Switch Pro Controller", DeviceKind.Gamepad, false, true, 25 * ((r[2] & 0xE0) >> 5), (r[2] & 0x10) != 0 && ((r[2] & 0xE0) >> 5) < 4) : null);

    // GameSir Nova / Nova Lite / Nova 2 (3537:1098, 100F cable, 1040): colección FF7A de 64, sin número de informe. Pedir 01 01 → [0]==01 y [18] = batería 0…100 (FF = cargando, sin porcentaje). Las tramas F4 00 que llegan solas se saltan.
    public static readonly Spec GameSirNova = new("GameSir", [0x3537], [0x1098, 0x100F, 0x1040], new(Page: 0xFF7A, OutMin: 3), Transport.Report, _ => [0x00, 0x01, 0x01], 800, 0,
        (vid, pid, r) => r.Length >= 19 && r[0] == 0x01 && (r[18] <= 100 || r[18] == 0xFF) ? new("gamesir:" + pid.ToString("X4"), "GameSir Nova", DeviceKind.Gamepad, pid == 0x100F, true, r[18] <= 100 ? r[18] : null, r[18] == 0xFF) : null);

    // GameSir Cyclone 2 (3537:100B): colección FFF0:0040, informes 0F de salida y 12 de entrada. Pedir 0F 03 → informe 12: [35] cable o cargando, [36] % (los eventos 0x10 no traen batería).
    public static readonly Spec GameSirCyclone2 = new("GameSir", [0x3537], [0x100B], new(Page: 0xFFF0, Usage: 0x0040, OutMin: 3, InMin: 37), Transport.Report, _ => [0x0F, 0x03], 1500, 0,
        (vid, pid, r) => r.Length >= 37 && r[0] == 0x12 && r[36] <= 100 ? new("gamesir:100B", "GameSir Cyclone 2", DeviceKind.Gamepad, false, true, r[36], r[35] == 1) : null);

    // BEITONG (Betop) KP20 en su receptor 2,4 GHz (20BC:5128): colección 00FF:0003, informes de 64 con número 02. Todos los bytes después del número van revueltos con un cifrado fijo por nibbles.
    // Pedir 02 15 (cifrado) → 02 15 <batería 0…100> 00 <estado de carga>.
    private static readonly byte[] BeitongNibble = [0x0, 0xA, 0x6, 0xF, 0x1, 0xC, 0x2, 0x9, 0xE, 0x3, 0x7, 0xD, 0x4, 0xB, 0x5, 0x8];
    public static byte BeitongEncode(byte x) => (byte)((BeitongNibble[x >> 4] << 4) | ((x & 0x0F) ^ 0x08));
    public static byte BeitongDecode(byte x) => (byte)((Array.IndexOf(BeitongNibble, (byte)(x >> 4)) << 4) | ((x & 0x0F) ^ 0x08));

    public static readonly Spec Beitong = new("BEITONG", [0x20BC], [0x5128], new(Page: 0x00FF, Usage: 0x0003, OutMin: 3, InMin: 5), Transport.Report, _ => [0x02, BeitongEncode(0x15)], 800, 0,
        (vid, pid, r) => r.Length >= 5 && r[0] == 0x02 && BeitongDecode(r[1]) == 0x15 && BeitongDecode(r[2]) <= 100 ? new("beitong:5128", "BEITONG KP20", DeviceKind.Gamepad, false, true, BeitongDecode(r[2]), BeitongDecode(r[4]) == 1) : null);

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
