namespace WinMac.Bar.Peripherals;

/// <summary>
/// Ratones inalámbricos Glorious, Hitscan, G-Wolves y WLMouse YING, que comparten un protocolo de un mismo proveedor: un informe de característica de 65 bytes.
/// Fuente de los datos de protocolo: lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa (ikajdan/glorious-indicator, glorious-ctl y los configuradores web de cada marca).
/// Aquí NO se copia código: solo estos datos. <b>SIN VERIFICAR con aparatos reales.</b>
/// </summary>
public static class GloriousProtocol
{
    public const int ReportLength = 65;

    /// <summary>Marca y cómo se espera la respuesta (ms entre enviar y leer; cada fabricante tarda distinto).</summary>
    public sealed record Vendor(ushort Id, string Key, int ReplyDelayMs);

    public static readonly IReadOnlyList<Vendor> Vendors =
    [
        new(0x258A, "glorious:", 50),     // Glorious (chip Sinowealth: el mismo id lo usan otras marcas)
        new(0x3770, "hitscan:", 200),
        new(0x33E4, "gwolves:", 100),
        new(0x36A7, "wlmouse:", 100),     // solo el receptor 8K YING (producto A874, colección de la página FFFF)
    ];

    /// <summary>La consulta: 65 bytes, número de informe 0, y los bytes 3, 4 y 6 con 02, 02 y 83.</summary>
    public static byte[] Request() { var b = new byte[ReportLength]; b[3] = 0x02; b[4] = 0x02; b[6] = 0x83; return b; }

    public enum State { NotThisProtocol, WakingUp, Asleep, Awake }

    /// <summary>Lo que dice la respuesta. Estado: A1 despierto, A4 dormido, A0 despertando; otro valor = no es este protocolo (o inalcanzable).</summary>
    public sealed record Reply(State State, int? Percent, bool Charging);

    public static Reply Parse(byte[]? r)
    {
        if (r is null || r.Length < 9 || r[6] != 0x83 || (r[1] & 0xF0) != 0xA0) return new(State.NotThisProtocol, null, false);
        var st = r[1] switch { 0xA1 => State.Awake, 0xA4 => State.Asleep, 0xA0 => State.WakingUp, _ => State.Asleep };
        int? pct = st == State.Awake && r[8] <= 100 ? Math.Max(1, (int)r[8]) : null;   // 0 se lee como «1»: el aparato no manda 0 estando despierto
        return new(st, pct, r[7] == 0x01);
    }

    /// <summary>Nombre y si el producto es el cable (carga por USB) o el receptor. Null = id de producto que no se conoce de esta marca.</summary>
    public static (string Name, bool Cable)? Identify(ushort vendor, ushort pid) => vendor switch
    {
        0x258A => pid switch
        {
            0x2022 => ("Glorious Model O Wireless", false), 0x2011 => ("Glorious Model O Wireless", true),
            0x2023 => ("Glorious Model D Wireless", false), 0x2012 => ("Glorious Model D Wireless", true),
            0x2024 => ("Glorious Model O- Wireless", false), 0x2013 => ("Glorious Model O- Wireless", true),
            0x2025 => ("Glorious Model D- Wireless", false), 0x2014 => ("Glorious Model D- Wireless", true),
            0x2033 => ("Glorious Model O 2 Wireless", false),
            0x2034 or 0x2036 => ("Glorious Model D 2 PRO", false), 0x201A => ("Glorious Model D 2 PRO", true),
            _ => null,
        },
        0x3770 => pid switch { 0x0136 => ("Hitscan Hyperlight X", true), 0x0194 => ("Hitscan Hyperlight N", true), _ => ("Hitscan Hyperlight", false) },
        0x36A7 => pid == 0xA874 ? ("WLMouse YING", false) : null,
        0x33E4 => (GWolvesName(pid), GWolvesCables.Contains(pid)),
        _ => null,
    };

    private static readonly HashSet<ushort> GWolvesCables = [0x1619, 0x2119, 0x2619, 0x2708, 0x2719, 0x3508, 0x3519, 0x3608, 0x3619, 0x3708, 0x3808, 0x3908, 0x4219, 0x4718, 0x4719, 0x5219, 0x5308, 0x5404, 0x5408, 0x5418, 0x5419,
        0x5608, 0x5618, 0x5619, 0x5704, 0x5708, 0x5804, 0x5808, 0x5904, 0x5908, 0x6808, 0x7204, 0x7704, 0x7708, 0x7904, 0x7908];

    /// <summary>Modelos G-Wolves por id de producto (cable y receptor). El receptor 3854 lo comparten muchos modelos y no tiene nombre propio.</summary>
    public static string GWolvesName(ushort pid) => pid switch
    {
        0x3808 or 0x3817 => "G-Wolves HTM Plus", 0x6808 or 0x6817 => "G-Wolves HSK Pro 2.0", 0x5608 or 0x5617 or 0x5618 or 0x5619 => "G-Wolves HTXU", 0x3608 or 0x3617 or 0x3619 => "G-Wolves Fenrir Pro",
        0x3908 or 0x3917 => "G-Wolves VUK", 0x7904 or 0x7913 => "G-Wolves HT-S2", 0x3708 or 0x3717 => "G-Wolves Fenrir Max", 0x5308 or 0x5317 => "G-Wolves HTS Ultra", 0x7704 or 0x7713 => "G-Wolves HTR",
        0x3508 or 0x3517 => "G-Wolves Fenrir", 0x7908 or 0x7917 => "G-Wolves HT-S2 Pro", 0x5808 or 0x5817 => "G-Wolves HSK Pro", 0x2708 or 0x2717 or 0x2719 => "G-Wolves HTX Mini",
        0x5408 or 0x5417 or 0x5418 or 0x5419 => "G-Wolves HTS Plus", 0x5708 or 0x5717 => "G-Wolves HTX", 0x5908 or 0x5917 => "G-Wolves HSK Plus", 0x7204 or 0x7203 => "G-Wolves HSK Lite",
        0x7708 or 0x7717 => "G-Wolves HTR Pro", 0x5804 or 0x5803 => "G-Wolves HSK Pro ACE", 0x5404 or 0x5403 => "G-Wolves HTS Plus ACE", 0x5704 or 0x5703 => "G-Wolves HTX ACE",
        0x5904 or 0x5903 => "G-Wolves HSK Plus ACE", 0x4718 or 0x4719 => "G-Wolves Lycan", 0x4219 => "G-Wolves WARG", 0x5219 => "G-Wolves HTS Plus Pro", 0x3519 => "G-Wolves Fenrir Asym",
        0x2619 => "G-Wolves HTX Mini Asym", 0x1619 => "G-Wolves Fenrir Max Asym", 0x2119 => "G-Wolves VUK Mini", _ => "G-Wolves wireless mouse",
    };
}
