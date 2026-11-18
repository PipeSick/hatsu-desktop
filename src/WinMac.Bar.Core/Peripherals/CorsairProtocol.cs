namespace WinMac.Bar.Peripherals;

/// <summary>
/// Auriculares, ratones y teclados inalámbricos Corsair (VID 1B1C). Datos de protocolo tomados de lo que describen el proyecto Wireless-Device-Battery-Monitor (GPL-3.0), HeadsetControl, OpenRGB y HaloBattery.
/// Aquí NO se copia código: solo estos datos. <b>SIN VERIFICAR con aparatos reales.</b> Hay tres protocolos: Void/HS (consulta C9 64), Bragi (lectura de propiedades) y Compx (tramas de 17 bytes).
/// </summary>
public static class CorsairProtocol
{
    public const ushort Vendor = 0x1B1C;

    public static readonly HashSet<ushort> VoidPids = [0x1B1C, 0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A, 0x1B2A, 0x1B23, 0x1B29, 0x0A55, 0x0A51, 0x0A52, 0x0A38, 0x0A4F, 0x0A2B, 0x0A75, 0x0A56];
    public static readonly HashSet<ushort> BragiPids = [0x2A08, 0x2A02, 0x0A97, 0x0A64, 0x0A62, 0x0A3E, 0x0A3D, 0x2B00];
    public static readonly IReadOnlyDictionary<ushort, string> CompxMice = new Dictionary<ushort, string> { [0x2B2A] = "Corsair Sabre V2 Pro Ultralight" };
    /// <summary>Aparatos vistos detrás de un receptor, por el id de producto que ellos mismos dicen (propiedad 12).</summary>
    public static readonly IReadOnlyDictionary<ushort, (string Name, DeviceKind Kind)> Paired = new Dictionary<ushort, (string, DeviceKind)> { [0x1B7E] = ("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse) };

    // ── Void / Void Pro / Void Elite / HS70 / HS80
    public static readonly byte[] VoidRequest = [0xC9, 0x64];

    public sealed record VoidReading(bool Connected, bool Charging, int? Percent);

    /// <summary>Respuesta de la consulta C9 64: [2] % (el bit 7 es «micrófono arriba»), [4] estado (0 sin conexión, 1–2 bien, 4–5 cargando).</summary>
    public static VoidReading? ParseVoid(byte[]? r)
    {
        if (r is null || r.Length < 5) return null;
        byte st = r[4]; bool charging = st is 4 or 5;
        return new(st != 0, charging, st is 1 or 2 || charging ? Math.Clamp(r[2] & 0x7F, 0, 100) : null);
    }

    // ── Bragi: lectura de propiedades (GET), solo lectura
    public const byte PropBattery = 0x0F, PropChargeState = 0x10, PropProductId = 0x12;
    /// <summary>Destinos: 08 es el propio aparato o receptor; 09 en adelante, los emparejados con un receptor. Primero los emparejados.</summary>
    public static readonly byte[] BragiTargets = [0x09, 0x0A, 0x0B, 0x08];

    /// <summary>Dos formas de trama: la «con prefijo» (Bragi antiguo: 02 destino 02 propiedad) y la «lisa» (receptores nuevos como el 2B00: 00 destino 02 propiedad, informe 0).</summary>
    public static byte[] BragiRequest(bool plain, bool numbered, byte target, byte prop) =>
        plain ? [0x00, target, 0x02, prop] : numbered ? [0x02, target, 0x02, prop] : [0x00, 0x02, target, 0x02, prop];

    public enum BragiStatus { Value, Error }
    public sealed record BragiAnswer(BragiStatus Status, uint Value);

    /// <summary>¿Esta trama contesta a la consulta? Null = es otra cosa (una notificación o la respuesta de otra app): se sigue leyendo. Error = el aparato respondió con un estado distinto de 0.</summary>
    public static BragiAnswer? ParseBragi(bool plain, byte target, byte[]? payload)
    {
        int o = plain ? 0 : 1; byte source = (byte)(target == 0x08 ? 0x00 : 0x01);
        if (payload is null || payload.Length < o + 7 || payload[o + 1] != 0x02 || payload[o] != source) return null;
        if (payload[o + 2] != 0x00) return new(BragiStatus.Error, 0);
        return new(BragiStatus.Value, (uint)(payload[o + 3] | (payload[o + 4] << 8) | (payload[o + 5] << 16) | (payload[o + 6] << 24)));
    }

    /// <summary>Nivel de la propiedad 0F (0…1000) → porcentaje; null si no es creíble (0 o más de 1000).</summary>
    public static int? BragiPercent(uint level) => level is >= 1 and <= 1000 ? Math.Clamp((int)(level + 5) / 10, 0, 100) : null;
    public static bool BragiCharging(uint state) => state is 1 or 3;

    /// <summary>Tipo de aparato por su nombre y por el id de producto (los de Corsair con 0Axx son auriculares).</summary>
    public static DeviceKind GuessKind(string name, ushort pid, uint pairedPid = 0)
    {
        string n = name.ToLowerInvariant();
        if (Paired.TryGetValue((ushort)pairedPid, out var p)) return p.Kind;
        if ((pairedPid >> 8) == 0x0A || n.Contains("headset") || n.Contains("virtuoso") || n.Contains("void") || n.Contains("hs80") || n.Contains("hs70") || n.Contains("headphone")) return DeviceKind.Headset;
        if (n.Contains("mouse") || n.Contains("dark core") || n.Contains("scimitar") || n.Contains("katar") || n.Contains("sabre") || n.Contains("harpoon") || n.Contains("nightsabre") || n.Contains("ironclaw")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("k70") || n.Contains("k100") || n.Contains("k65") || n.Contains("k63")) return DeviceKind.Keyboard;
        return (pid >> 8) == 0x0A ? DeviceKind.Headset : DeviceKind.Other;
    }

    // ── Compx: ratones con tramas de 17 bytes (informe 08, comando 03 = conectado, 04 = batería, byte 16 = suma de comprobación)
    public const int CompxLength = 17;
    public static byte CompxChecksum(byte[] b) { int sum = 0; for (int i = 0; i < 16; i++) sum += b[i]; return (byte)(0x55 - sum); }
    public static byte[] CompxRequest(byte cmd) { var r = new byte[CompxLength]; r[0] = 0x08; r[1] = cmd; r[16] = CompxChecksum(r); return r; }
    public static bool IsCompxReply(byte cmd, byte[]? r) => r is { Length: >= CompxLength } && r[0] == 0x08 && r[1] == cmd && r[16] == CompxChecksum(r);
    public static bool CompxOnline(byte[] r) => r[6] != 0;
    public sealed record CompxBattery(int? Percent, bool Charging, int Millivolts);
    public static CompxBattery CompxParseBattery(byte[] r) => new(r[6] <= 100 ? r[6] : null, r[7] != 0, (r[8] << 8) | r[9]);
}
