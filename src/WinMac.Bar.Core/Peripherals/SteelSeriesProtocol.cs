namespace WinMac.Bar.Peripherals;

/// <summary>
/// Auriculares y ratones inalámbricos SteelSeries (VID 1038). Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa
/// (HeadsetControl para los auriculares, rivalcfg para los ratones). Aquí NO se copia código: solo estos datos. <b>SIN VERIFICAR con aparatos reales.</b>
/// Los informes de salida se rellenan con ceros hasta el largo del informe del aparato; en la respuesta se quita un 0 inicial (número de informe) y los desplazamientos de abajo cuentan desde ahí.
/// </summary>
public static class SteelSeriesProtocol
{
    public const ushort Vendor = 0x1038;

    public enum Family { Arctis1, Arctis7, Arctis9, ArctisProWireless, NovaProWireless, Nova, Aerox, Rival, RivalPro }

    /// <summary>Qué colección HID del aparato hay que abrir: por página y uso, o por número de interfaz USB.</summary>
    public sealed record Where(ushort UsagePage = 0, ushort Usage = 0, int Interface = -1);

    public sealed record Model(Family Family, IReadOnlyList<ushort> Pids, Where Collection);

    public static readonly IReadOnlyList<Model> Models =
    [
        new(Family.Arctis1, [0x12B3, 0x12B6, 0x12D7], new(0xFF43, 0x0202)),
        new(Family.Arctis7, [0x1260, 0x12AD, 0x1252], new(0xFF43, 0x0202)),
        new(Family.Arctis9, [0x12C2], new(Interface: 0)),
        new(Family.ArctisProWireless, [0x1290], new(Interface: 0)),
        new(Family.NovaProWireless, [0x12E0, 0x225D], new(Interface: 4)),
        new(Family.Nova, [0x220E, 0x2212, 0x2216, 0x2236, 0x2269, 0x226D, 0x2232, 0x2253, 0x230A, 0x2202, 0x22A1, 0x227E, 0x2206, 0x2258, 0x229E, 0x22AD, 0x223A, 0x22A9, 0x227A, 0x22A4, 0x22A5, 0x220A, 0x22A7], new(0xFFC0, 0x0001)),
        new(Family.Aerox, [0x1838, 0x183A, 0x1852, 0x1854, 0x1858, 0x185A, 0x1840, 0x1842], new(Interface: 3)),
        new(Family.Rival, [0x1830, 0x1872, 0x172B, 0x1726], new(Interface: 3)),   // el Rival 650 (172B cable, 1726 receptor) responde en la interfaz 0
        new(Family.RivalPro, [0x1880], new(Interface: 2)),
    ];

    public static Model? Find(ushort pid) => Models.FirstOrDefault(m => m.Pids.Contains(pid));

    /// <summary>Colección donde responde cada producto (el Rival 650 va por la interfaz 0, no por la 3 de los demás Rival).</summary>
    public static Where CollectionFor(ushort pid) => pid is 0x172B or 0x1726 ? new(Interface: 0) : Find(pid)?.Collection ?? new();

    /// <summary>Lo que se entiende de una respuesta. <paramref name="NeedsLevel"/>: el estado dice que está conectado pero el nivel hay que pedirlo aparte (Arctis Pro Wireless).</summary>
    public sealed record Reading(string Name, string Via, bool Connected, int? Percent, bool Charging, bool NeedsLevel = false);

    /// <summary>Peticiones de estado, en el orden en que se prueban (algunos firmware numeran sus informes y otros no).</summary>
    public static IReadOnlyList<byte[]> StatusRequests(Family f, ushort pid) => f switch
    {
        Family.Arctis1 => [[0x06, 0x12]],
        Family.Arctis7 => [[0x06, 0x18]],
        Family.Arctis9 => [[0x00, 0x20]],
        Family.ArctisProWireless => [[0x00, 0x41, 0xAA], [0x41, 0xAA]],
        Family.NovaProWireless => [[0x06, 0xB0]],
        Family.Nova => [[0x00, 0xB0]],
        Family.Aerox => [[0x00, (byte)(IsAeroxCable(pid) ? 0x92 : 0xD2)]],
        Family.Rival => [[0x00, 0xAA, 0x01]],
        Family.RivalPro => [[0x00, 0x92]],
        _ => [],
    };

    public static IReadOnlyList<byte[]> LevelRequests => [[0x00, 0x40, 0xAA], [0x40, 0xAA]];   // Arctis Pro Wireless: nivel 0…4 en el byte 0
    public static int TimeoutMs(Family f) => f is Family.Aerox or Family.Rival ? 300 : f == Family.RivalPro ? 500 : 1000;

    public static bool IsAeroxCable(ushort pid) => pid is 0x183A or 0x1854 or 0x185A or 0x1842;

    private static int Clamp(int v) => Math.Clamp(v, 0, 100);
    public static int Map(int v, int lo, int hi) => Math.Clamp((v - lo) * 100 / (hi - lo), 0, 100);

    /// <summary>Respuesta de estado → lectura, o null si es demasiado corta o no es de este aparato. <paramref name="r"/> = la respuesta sin el 0 inicial.</summary>
    public static Reading? Parse(Family f, ushort pid, byte[]? r)
    {
        if (r is null) return null;
        const string Rx = "Receptor 2,4 GHz", Cable = "Cable USB";
        switch (f)
        {
            case Family.Arctis1:
            {
                if (r.Length < 4) return null; bool on = r[2] != 0x01; return new("SteelSeries Arctis 1 Wireless", Rx, on, on ? Clamp(r[3]) : null, false);
            }
            case Family.Arctis7:
            {
                if (r.Length < 3) return null; bool on = r[2] != 0; return new(pid == 0x1252 ? "SteelSeries Arctis Pro" : "SteelSeries Arctis 7", Rx, on, on ? Clamp(r[2]) : null, false);
            }
            case Family.Arctis9:
                return r.Length < 5 ? null : new("SteelSeries Arctis 9", Rx, true, Map(r[3], 0x64, 0x9A), r[4] == 0x01);
            case Family.ArctisProWireless:
            {
                if (r.Length < 1) return null; bool on = r[0] != 0x02; return new("SteelSeries Arctis Pro Wireless", Rx, on, null, false, NeedsLevel: on);
            }
            case Family.NovaProWireless:
            {
                if (r.Length < 16) return null; bool on = r[15] != 0x01; return new("SteelSeries Arctis Nova Pro Wireless", Rx, on, on ? Map(r[6], 0, 8) : null, r[15] == 0x02);
            }
            case Family.Nova: return ParseNova(pid, r, Rx);
            case Family.Aerox:
            {
                if (r.Length < 2) return null; bool wired = IsAeroxCable(pid); int raw = r[1] & 0x7F;
                string name = pid is 0x1838 or 0x183A ? "SteelSeries Aerox 3 Wireless" : pid is 0x1852 or 0x1854 ? "SteelSeries Aerox 5 Wireless" : pid is 0x1858 or 0x185A ? "SteelSeries Aerox 9 Wireless" : "SteelSeries Prime Wireless";
                if (raw == 0) return new(name, wired ? Cable : Rx, false, null, false);   // apagado o fuera de alcance
                return raw <= 21 ? new(name, wired ? Cable : Rx, true, (raw - 1) * 5, (r[1] & 0x80) != 0) : new(name, wired ? Cable : Rx, true, null, false);   // solo los niveles 1…21 valen 0…100 %
            }
            case Family.Rival:
            {
                if (r.Length < 3) return null; bool r650 = pid is 0x172B or 0x1726;
                string name = r650 ? "SteelSeries Rival 650 Wireless" : pid == 0x1872 ? "SteelSeries Rival 3 Wireless Gen 2" : "SteelSeries Rival 3 Wireless";
                return new(name, pid == 0x172B ? Cable : Rx, true, Clamp(r[0]), r[2] != 0);
            }
            case Family.RivalPro:
                // respuesta: 92 00 <% del ratón> …; otras tramas se ignoran
                return r.Length >= 3 && r[0] == 0x92 && r[1] == 0x00 && r[2] <= 100 ? new("SteelSeries Rival Pro", "Base UWB", true, r[2], false) : null;
        }

        return null;
    }

    private static Reading? ParseNova(ushort pid, byte[] r, string rx)
    {
        if (r.Length < 7) return null;
        if (pid is 0x220E or 0x2212 or 0x2216 or 0x2236)        // Arctis 7+
        {
            bool on = r[1] != 0x01; return new("SteelSeries Arctis 7+", rx, on, on ? Map(r[2], 0, 4) : null, r[3] == 0x01);
        }

        if (pid is 0x2269 or 0x226D or 0x2232 or 0x2253)        // Nova 3P Wireless y Nova 5
        {
            bool on = r[1] != 0x02; return new(pid is 0x2232 or 0x2253 ? "SteelSeries Arctis Nova 5" : "SteelSeries Arctis Nova 3P Wireless", rx, on, on ? Clamp(r[3]) : null, r[4] == 0x01);
        }

        if (pid == 0x230A)                                       // GameBuds: el menor de los auriculares que están fuera del estuche
        {
            int lvl = -1; if (r[3] == 0x03) lvl = r[5]; if (r[4] == 0x03) lvl = lvl < 0 ? r[6] : Math.Min(lvl, r[6]);
            return new("SteelSeries Arctis GameBuds", rx, lvl >= 0, lvl >= 0 ? Clamp(lvl) : null, false);
        }

        bool discrete = pid is 0x2202 or 0x2206 or 0x220A or 0x223A or 0x227A or 0x22A4;      // Arctis Nova 7 / 7X / 7P y variantes
        bool conn = r[3] != 0x00;
        return new("SteelSeries Arctis Nova 7", rx, conn, conn ? (discrete ? Map(r[2], 0, 4) : Clamp(r[2])) : null, r[3] is 0x01 or 0x02);
    }

    /// <summary>Respuesta del nivel (Arctis Pro Wireless): 0…4 en el byte 0 → porcentaje; null si no es creíble.</summary>
    public static int? ParseLevel(byte[]? r) => r is { Length: >= 1 } && r[0] <= 4 ? Map(r[0], 0, 4) : null;

    /// <summary>Quita el 0 inicial (informe sin número) de una trama leída; las que traen número de informe se dejan.</summary>
    public static byte[] Payload(byte[] frame) => frame.Length > 0 && frame[0] == 0 ? frame[1..] : frame;
}
