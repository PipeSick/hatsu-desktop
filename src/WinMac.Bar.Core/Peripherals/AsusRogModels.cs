namespace WinMac.Bar.Peripherals;

/// <summary>
/// Tabla de teclados y ratones inalámbricos ASUS ROG (VID 0B05) con su receptor Omni o su receptor/cable propio, y cómo leer su batería.
/// Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los proyectos abiertos en que se basa (peripheral-battery-tray de SteveWang92, MIT; G-Helper; BatteryTool de 1gcat).
/// Aquí NO se copia código: solo estos datos. Verificado con el Strix Scope II 96 y el Keris II Origin por el receptor Omni (docs/64); <b>el resto SIN VERIFICAR</b>.
/// Consulta: [informe][12][sub], sub 01 = teclado y 07 = ratón. Respuesta: [informe][12][sub]…; FF AA en [1..2] = el aparato no contestó (dormido o apagado).
/// Teclado: [offset] % (Falchion: [11]) y [9]==1 cargando. Ratón: [5] nivel (×25 en los medidores antiguos de 0 a 4) y [10] cargando.
/// </summary>
public static class AsusRogModels
{
    public sealed record Model(ushort Pid, string Name, AsusRogProtocol.Role Role, byte ReportId, int Interface, string? Col, int Offset, int Scale);

    private const AsusRogProtocol.Role KB = AsusRogProtocol.Role.Keyboard, MS = AsusRogProtocol.Role.Mouse;

    /// <summary>ReportId distinto de 0 = el modelo se lee por un canal numerado del receptor Omni (Col = parte de la ruta que identifica la colección); 0 = por su propio receptor o por el cable (informe sin número en la interfaz Interface; −1 = cualquiera del fabricante).</summary>
    public static readonly IReadOnlyList<Model> Models =
    [
        // Teclados (Omni = col02, informe 2; receptor o cable = interfaz 1 sin número)
        new(0x1A85, "ROG Azoth", KB, 2, 2, "col02", 6, 1), new(0x1A85, "ROG Azoth", KB, 0, 1, null, 6, 1), new(0x1A83, "ROG Azoth", KB, 0, 1, null, 6, 1),
        new(0x1B42, "ROG Azoth Extreme", KB, 2, 2, "col02", 6, 1), new(0x1B3F, "ROG Azoth Extreme", KB, 0, 1, null, 6, 1),
        new(0x1CF1, "ROG Azoth Extreme SE", KB, 2, 2, "col02", 6, 1), new(0x1CEF, "ROG Azoth Extreme SE", KB, 0, 1, null, 6, 1),
        new(0x1C25, "ROG Azoth X", KB, 0, 1, null, 6, 1), new(0x1C24, "ROG Azoth X", KB, 0, 1, null, 6, 1),
        new(0x193E, "ROG Falchion", KB, 0, 1, null, 11, 1), new(0x193C, "ROG Falchion", KB, 0, 1, null, 11, 1),
        new(0x1B04, "ROG Falchion RX", KB, 0, -1, null, 11, 1), new(0x1B06, "ROG Falchion RX Low Profile", KB, 2, 2, "col02", 11, 1),
        new(0x1AB0, "ROG Strix Scope II 96", KB, 2, 2, "col02", 6, 1), new(0x1AAE, "ROG Strix Scope II 96", KB, 0, 1, null, 6, 1),
        new(0x1B7A, "ROG Strix Scope II 96 RX", KB, 2, 2, "col02", 6, 1), new(0x1B78, "ROG Strix Scope II 96 RX", KB, 0, 1, null, 6, 1),
        new(0x1A07, "ROG Strix Scope RX TKL", KB, 0, -1, null, 6, 1),
        // Ratones (Omni = col03, informe 3; receptor o cable = interfaz 0 sin número)
        new(0x18E5, "ROG Chakram", MS, 0, 0, null, 5, 25), new(0x18E3, "ROG Chakram", MS, 0, 0, null, 5, 25),
        new(0x1A1A, "ROG Chakram X", MS, 0, 0, null, 5, 1), new(0x1A18, "ROG Chakram X", MS, 0, 0, null, 5, 1),
        new(0x18A0, "ROG Gladius II Wireless", MS, 0, 2, null, 5, 25), new(0x197F, "ROG Gladius III Wireless", MS, 0, 0, null, 5, 1), new(0x197D, "ROG Gladius III Wireless", MS, 0, 0, null, 5, 1),
        new(0x1A72, "ROG Gladius III Aimpoint", MS, 3, 2, "col03", 5, 1), new(0x1A72, "ROG Gladius III Aimpoint", MS, 0, 0, null, 5, 1), new(0x1A70, "ROG Gladius III Aimpoint", MS, 0, 0, null, 5, 1),
        new(0x1B0C, "ROG Gladius III Aimpoint EVA-02", MS, 0, 0, null, 5, 1), new(0x1B0A, "ROG Gladius III Aimpoint EVA-02", MS, 0, 0, null, 5, 1),
        new(0x1A94, "ROG Harpe Ace Aim Lab Edition", MS, 3, 2, "col03", 5, 1), new(0x1A94, "ROG Harpe Ace Aim Lab Edition", MS, 0, 0, null, 5, 1), new(0x1A92, "ROG Harpe Ace Aim Lab Edition", MS, 0, 0, null, 5, 1),
        new(0x1B68, "ROG Harpe Ace Extreme", MS, 3, 2, "col03", 5, 1), new(0x1B69, "ROG Harpe Ace Extreme", MS, 3, 2, "col03", 5, 1), new(0x1B67, "ROG Harpe Ace Extreme", MS, 0, 0, null, 5, 1),
        new(0x1B65, "ROG Harpe Ace Mini", MS, 3, 2, "col03", 5, 1), new(0x1B63, "ROG Harpe Ace Mini", MS, 0, 0, null, 5, 1),
        new(0x1AD0, "ROG Harpe II Ace", MS, 3, 2, "col03", 5, 1), new(0x1C69, "ROG Harpe II Ace", MS, 0, 0, null, 5, 1),
        new(0x1960, "ROG Keris Wireless", MS, 0, 0, null, 5, 25), new(0x195E, "ROG Keris Wireless", MS, 0, 0, null, 5, 25),
        new(0x1A59, "ROG Keris Wireless EVA Edition", MS, 0, 0, null, 5, 25), new(0x1A57, "ROG Keris Wireless EVA Edition", MS, 0, 0, null, 5, 25),
        new(0x1A68, "ROG Keris Wireless Aimpoint", MS, 3, 2, "col03", 5, 1), new(0x1A6A, "ROG Keris Wireless Aimpoint", MS, 3, 2, "col03", 5, 1),
        new(0x1A68, "ROG Keris Wireless Aimpoint", MS, 0, 0, null, 5, 1), new(0x1A66, "ROG Keris Wireless Aimpoint", MS, 0, 0, null, 5, 1),
        new(0x1B1A, "ROG Keris II Ace", MS, 3, 2, "col03", 5, 1), new(0x1B18, "ROG Keris II Ace", MS, 3, 2, "col03", 5, 1), new(0x1B16, "ROG Keris II Ace", MS, 0, 0, null, 5, 1),
        new(0x1C0E, "ROG Keris II Origin", MS, 3, 2, "col03", 5, 1), new(0x1C0C, "ROG Keris II Origin", MS, 0, 0, null, 5, 1),
        new(0x1D4E, "ROG Keris II Origin KJP", MS, 3, 2, "col03", 5, 1), new(0x1D4C, "ROG Keris II Origin KJP", MS, 0, 0, null, 5, 1),
        new(0x1908, "ROG Pugio II", MS, 0, 0, null, 5, 25), new(0x1906, "ROG Pugio II", MS, 0, 0, null, 5, 25),
        new(0x1979, "ROG Spatha X", MS, 0, 0, null, 5, 1), new(0x1977, "ROG Spatha X", MS, 0, 0, null, 5, 1),
        new(0x18B4, "ROG Strix Carry", MS, 0, 1, null, 7, 25),
        new(0x1949, "ROG Strix Impact II Wireless", MS, 0, 0, null, 5, 25), new(0x1947, "ROG Strix Impact II Wireless", MS, 0, 0, null, 5, 25),
        new(0x1AD7, "ROG Strix Impact III Wireless", MS, 3, 2, "col03", 5, 1),
    ];

    public static byte[] Request(Model m) => [m.ReportId, (byte)0x12, (byte)(m.Role == KB ? 0x01 : 0x07)];

    public enum Answer { Ok, Asleep, None }
    public sealed record Result(Answer Answer, int? Percent, bool Charging);

    /// <summary>Respuesta de un modelo. <paramref name="r"/> lleva el número de informe en [0] (0 si el aparato no los numera). Un ratón con nivel 0 se toma por «sin dato», no por vacío.</summary>
    public static Result? Parse(Model m, byte[]? r)
    {
        byte sub = (byte)(m.Role == KB ? 0x01 : 0x07);
        if (r is null || r.Length < 12) return null;
        if (r[1] == 0xFF && r[2] == 0xAA) return new(Answer.Asleep, null, false);
        if (r[1] != (byte)0x12 || r[2] != sub || m.Offset >= r.Length) return null;
        if (m.Role == KB) return new(Answer.Ok, r[m.Offset] <= 100 ? r[m.Offset] : null, r[9] == 1);
        int v = r[m.Offset] * m.Scale;
        return v is > 0 and <= 100 ? new(Answer.Ok, v, r[10] != 0) : new(Answer.Asleep, null, r[10] != 0);
    }

    /// <summary>Qué aparatos tiene emparejados el receptor Omni: consulta 01 A0 00 por la colección col01 de la interfaz 2.</summary>
    public static readonly byte[] PairingRequest = [0x01, 0xA0, 0x00];

    public static bool IsPairingReply(byte[]? r) => r is { Length: >= 12 } && r[1] == 0xA0 && r[2] == 0x00;

    /// <summary>Ids de producto de los aparatos emparejados: ranuras de 4 bytes desde el byte 5, con el id en los dos primeros (LE); un 0 termina la lista.</summary>
    public static IReadOnlyList<ushort> ParsePairing(byte[] r)
    {
        var list = new List<ushort>();
        for (int at = 5; at + 1 < r.Length; at += 4) { ushort pid = (ushort)(r[at] | (r[at + 1] << 8)); if (pid == 0) break; list.Add(pid); }
        return list;
    }
}
