namespace WinMac.Bar.Peripherals;

/// <summary>
/// Batería de periféricos Logitech con receptor (Unifying, Lightspeed, Bolt) por HID++ 2.0, la parte pura: armar las consultas y leer las respuestas.
/// Origen: la documentación pública del protocolo (Solaar, el controlador hid-logitech-hidpp de Linux). <b>PROBABLE, NO VERIFICADO</b>: se escribió sin ningún dispositivo Logitech a mano; solo hay pruebas con tramas armadas desde la especificación.
/// Todas las consultas son de LECTURA (preguntar qué funciones tiene, cómo se llama y cuánta batería tiene).
/// </summary>
public static class LogitechHidpp
{
    public const ushort Vendor = 0x046D;
    public const byte LongReport = 0x11, SoftwareId = 0x07;   // SoftwareId distingue nuestras respuestas de las del software de Logitech (1–15)
    public const int LongLength = 20;
    public const ushort FeatureDeviceName = 0x0005, FeatureBatteryStatus = 0x1000, FeatureUnifiedBattery = 0x1004;

    /// <summary>Trama larga de petición: [0x11, dispositivo, índice de función, (función &lt;&lt; 4) | id de software, parámetros…].</summary>
    public static byte[] Request(byte device, byte featureIndex, byte function, params byte[] args)
    {
        var b = new byte[LongLength]; b[0] = LongReport; b[1] = device; b[2] = featureIndex; b[3] = (byte)((function << 4) | SoftwareId);
        for (int i = 0; i < args.Length && 4 + i < b.Length; i++) b[4 + i] = args[i];
        return b;
    }

    /// <summary>IRoot.GetFeature(featureId): pregunta en qué índice está una función del dispositivo.</summary>
    public static byte[] GetFeature(byte device, ushort featureId) => Request(device, 0x00, 0, (byte)(featureId >> 8), (byte)featureId);

    /// <summary>¿La trama es la respuesta a una petición (mismo dispositivo, índice y función+id de software)?</summary>
    public static bool IsReplyTo(byte[] req, byte[]? resp) =>
        resp is { Length: >= 7 } && resp[0] == LongReport && resp[1] == req[1] && resp[2] == req[2] && resp[3] == req[3];

    /// <summary>¿Es un error HID++ 2.0 para esa petición? (índice 0xFF, luego el índice y la función pedidos).</summary>
    public static bool IsErrorFor(byte[] req, byte[]? resp) => resp is { Length: >= 7 } && resp[0] == LongReport && resp[1] == req[1] && resp[2] == 0xFF && resp[3] == req[2] && resp[4] == req[3];

    /// <summary>Índice de una función según la respuesta de IRoot.GetFeature, o null si el dispositivo no la tiene (índice 0).</summary>
    public static byte? ParseFeatureIndex(byte[] req, byte[]? resp) => IsReplyTo(req, resp) && resp![4] != 0 ? resp[4] : null;

    public sealed record Battery(int Percent, ChargeState Charge);

    /// <summary>Batería: 0x1000 (BATTERY_STATUS, función 0): [porcentaje, siguiente nivel, estado de carga]. Porcentaje fuera de 1–100 = no creíble.</summary>
    public static Battery? ParseBatteryStatus(byte[] req, byte[]? resp)
    {
        if (!IsReplyTo(req, resp)) return null; int p = resp![4]; if (p is < 1 or > 100) return null;
        return new Battery(p, ChargeFrom(resp[6]));
    }

    /// <summary>Batería: 0x1004 (UNIFIED_BATTERY, función 1): [porcentaje, niveles, estado de carga, energía externa].</summary>
    public static Battery? ParseUnifiedBattery(byte[] req, byte[]? resp)
    {
        if (!IsReplyTo(req, resp)) return null; int p = resp![4]; if (p is < 1 or > 100) return null;
        return new Battery(p, ChargeFrom(resp[6]));
    }

    /// <summary>Estados de carga de HID++: 0 descargando; 1 cargando, 2 casi llena, 3 carga completa, 4 carga lenta = conectado a la corriente; 5–7 errores = desconocido.</summary>
    public static ChargeState ChargeFrom(byte status) => status switch { 0 => ChargeState.NotCharging, 1 or 2 or 4 => ChargeState.Charging, 3 => ChargeState.Charging, _ => ChargeState.Unknown };

    /// <summary>Tipo de dispositivo (0x0005 función 2): 0 teclado, 3 ratón, 4 touchpad, 5 trackball, 8 auriculares, 11 y 12 mandos.</summary>
    public static DeviceKind KindFrom(byte type) => type switch { 0 or 2 => DeviceKind.Keyboard, 3 or 4 or 5 => DeviceKind.Mouse, 8 => DeviceKind.Headset, 11 or 12 => DeviceKind.Gamepad, _ => DeviceKind.Other };

    /// <summary>Texto del nombre a partir de los trozos de 0x0005 (función 1): cada respuesta trae hasta 16 caracteres desde el índice pedido.</summary>
    public static string NameChunk(byte[] req, byte[]? resp)
    {
        if (!IsReplyTo(req, resp)) return "";
        int end = 4; while (end < resp!.Length && resp[end] != 0) end++;
        var s = new string(System.Text.Encoding.ASCII.GetString(resp, 4, end - 4).Where(c => !char.IsControl(c)).ToArray());
        return s;
    }
}
