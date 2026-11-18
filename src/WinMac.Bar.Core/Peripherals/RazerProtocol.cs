namespace WinMac.Bar.Peripherals;

/// <summary>
/// Teclados y ratones inalámbricos Razer (VID 1532), por el protocolo de informes de característica de 90 bytes que documenta el proyecto OpenRazer (GPL-2.0).
/// Aquí NO se copia código: solo el formato de la trama y las tablas de modelos. <b>SIN VERIFICAR con aparatos reales.</b> Synapse comparte el mismo buzón de respuesta, por eso se reintenta.
/// </summary>
public static class RazerProtocol
{
    public const ushort Vendor = 0x1532;
    /// <summary>Informe de 90 bytes; con el número de informe (0) delante, 91.</summary>
    public const int ReportLength = 91;
    public const byte ClassPower = 0x07, CmdBattery = 0x80, CmdCharging = 0x84, ArgSize = 0x02;

    /// <summary>Identificadores de transacción que se prueban cuando el modelo no tiene uno conocido.</summary>
    public static readonly byte[] ProbeTransactions = [0x1F, 0x3F, 0xFF, 0x08, 0x9F];

    /// <summary>La trama: [0]=0 (informe), [2]=transacción, [6]=tamaño de datos, [7]=clase, [8]=comando, [89]=XOR de los bytes 3…88.</summary>
    public static byte[] Request(byte transaction, byte cls, byte command, byte size)
    {
        var rep = new byte[ReportLength]; rep[2] = transaction; rep[6] = size; rep[7] = cls; rep[8] = command; rep[89] = Crc(rep); return rep;
    }

    /// <summary>XOR de los bytes 3…88 de la trama de 91 (los 2…87 del informe de 90).</summary>
    public static byte Crc(byte[] frame91) { byte x = 0; for (int i = 3; i < 89; i++) x ^= frame91[i]; return x; }

    public enum Status { Ok, Busy, Offline, Unsupported, NotAnswer }
    public sealed record Answer(Status Status, byte[] Args);

    /// <summary>¿Esta respuesta es la de la consulta? Valida transacción, tamaño, clase, comando y el XOR. Estado: 01 ocupado, 02 bien, 04 sin respuesta del aparato, 03/05 no soportado.</summary>
    public static Answer Parse(byte[]? resp, byte transaction, byte cls, byte command, byte size)
    {
        if (resp is null || resp.Length < ReportLength) return new(Status.NotAnswer, []);
        var a = resp.AsSpan(1);
        if (a[1] != transaction || a[2] != 0 || a[3] != 0 || a[4] != 0 || a[5] != size || a[6] != cls || a[7] != command || a[88] != Crc(resp)) return new(Status.NotAnswer, []);
        return a[0] switch { 0x01 => new(Status.Busy, []), 0x02 => new(Status.Ok, a.Slice(8, 80).ToArray()), 0x04 => new(Status.Offline, []), 0x03 or 0x05 => new(Status.Unsupported, []), _ => new(Status.NotAnswer, []) };
    }

    /// <summary>Porcentaje: el argumento 1 va de 0 a 255.</summary>
    public static int? Percent(byte[] args) => args.Length > 1 ? Math.Clamp((args[1] * 100 + 127) / 255, 0, 100) : null;
    public static bool Charging(byte[] args) => args.Length > 1 && args[1] == 1;

    /// <summary>Transacción propia de cada teclado inalámbrico o con cable (0 = sin dato: se prueban las comunes).</summary>
    public static byte KeyboardTransaction(ushort pid) => pid switch
    {
        0x025C or 0x0271 or 0x0290 or 0x0296 or 0x02BA or 0x02C9 or 0x02D2 or 0x02D5 => 0x9F,
        0x0258 or 0x0292 or 0x0298 or 0x02B9 or 0x02CD or 0x02D4 or 0x02D7 => 0x1F,
        0x025A => 0x3F,
        _ => 0,
    };

    public static readonly HashSet<ushort> Keyboards =
    [
        0x010D, 0x010E, 0x010F, 0x0111, 0x0113, 0x0118, 0x011A, 0x011B, 0x011C, 0x0201, 0x0202, 0x0203, 0x0204, 0x0205, 0x0207, 0x0208, 0x0209, 0x020F, 0x0210, 0x0211, 0x0214, 0x0216, 0x0217, 0x021A, 0x021E, 0x021F,
        0x0220, 0x0221, 0x0224, 0x0225, 0x0226, 0x0227, 0x0228, 0x022A, 0x022B, 0x022C, 0x022D, 0x022F, 0x0232, 0x0233, 0x0234, 0x0235, 0x0237, 0x0239, 0x023A, 0x023B, 0x023F, 0x0240, 0x0241, 0x0243, 0x0244, 0x0245,
        0x0246, 0x024A, 0x024B, 0x024C, 0x024D, 0x024E, 0x0252, 0x0253, 0x0255, 0x0256, 0x0257, 0x0258, 0x0259, 0x025A, 0x025C, 0x025D, 0x025E, 0x0266, 0x0268, 0x0269, 0x026A, 0x026B, 0x026C, 0x026D, 0x026E, 0x026F,
        0x0270, 0x0271, 0x0276, 0x0279, 0x027A, 0x0282, 0x0287, 0x028A, 0x028B, 0x028C, 0x028D, 0x028F, 0x0290, 0x0292, 0x0293, 0x0294, 0x0295, 0x0296, 0x0298, 0x029D, 0x029E, 0x029F, 0x02A0, 0x02A1, 0x02A2, 0x02A3,
        0x02A5, 0x02A6, 0x02A7, 0x02B0, 0x02B6, 0x02B8, 0x02B9, 0x02BA, 0x02C5, 0x02C6, 0x02C7, 0x02C9, 0x02CD, 0x02CF, 0x02D2, 0x02D4, 0x02D5, 0x02D7, 0x02E0, 0x02E1, 0x02EA, 0x0A24,
    ];

    public static readonly HashSet<ushort> Mice =
    [
        0x0013, 0x0015, 0x0016, 0x001F, 0x0020, 0x0024, 0x0025, 0x0029, 0x002E, 0x002F, 0x0032, 0x0034, 0x0036, 0x0037, 0x0038, 0x0039, 0x003E, 0x003F, 0x0040, 0x0041, 0x0042, 0x0043, 0x0044, 0x0045, 0x0046, 0x0048,
        0x004C, 0x004F, 0x0050, 0x0053, 0x0054, 0x0059, 0x005A, 0x005B, 0x005C, 0x005E, 0x005F, 0x0060, 0x0062, 0x0064, 0x0065, 0x0067, 0x006A, 0x006B, 0x006C, 0x006E, 0x006F, 0x0070, 0x0071, 0x0072, 0x0073, 0x0077,
        0x0078, 0x007A, 0x007B, 0x007C, 0x007D, 0x0080, 0x0083, 0x0084, 0x0085, 0x0086, 0x0088, 0x008A, 0x008C, 0x008D, 0x008F, 0x0090, 0x0091, 0x0094, 0x0095, 0x0096, 0x0098, 0x0099, 0x009A, 0x009C, 0x009E, 0x009F,
        0x00A1, 0x00A3, 0x00A5, 0x00A6, 0x00A7, 0x00A8, 0x00AA, 0x00AB, 0x00AF, 0x00B0, 0x00B2, 0x00B3, 0x00B4, 0x00B6, 0x00B7, 0x00B8, 0x00B9, 0x00BE, 0x00BF, 0x00C0, 0x00C1, 0x00C2, 0x00C3, 0x00C4, 0x00C5, 0x00C7,
        0x00C8, 0x00CB, 0x00CC, 0x00CD, 0x00D0, 0x00D1, 0x00D3, 0x00D4, 0x00D6, 0x00D7, 0x00DA, 0x00DB, 0x00DE, 0x00DF, 0x00E0, 0x00E5, 0x00E6,
    ];

    public static DeviceKind? Kind(ushort pid) => Keyboards.Contains(pid) ? DeviceKind.Keyboard : Mice.Contains(pid) ? DeviceKind.Mouse : null;

    /// <summary>Quita sufijos que no son parte del modelo («Dongle», «Receiver»…).</summary>
    public static string CleanName(string name)
    {
        foreach (var suffix in new[] { " HyperSpeed Dongle", " Wireless Receiver", " (Wireless)", " (Wired)", " Receiver", " Dongle" })
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];
        return name.Length == 0 ? "Razer device" : name;
    }
}
