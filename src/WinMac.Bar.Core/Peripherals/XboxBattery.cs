namespace WinMac.Bar.Peripherals;

/// <summary>Batería de los mandos Xbox (y los GameSir en modo Xbox): de la API de Windows (Windows.Gaming.Input: mWh restantes y totales) o, si no la hay, de XInput (cuatro niveles gruesos). Puro: las dos conversiones.</summary>
public static class XboxBattery
{
    public const ushort Microsoft = 0x045E, GameSir = 0x3537;

    /// <summary>Porcentaje real: capacidad restante entre la total. Null si no hay capacidad total o falta el dato.</summary>
    public static int? WgiPercent(int? remainingMwh, int? fullMwh) => fullMwh is > 0 && remainingMwh is >= 0 ? Math.Clamp((int)((long)remainingMwh.Value * 100 / fullMwh.Value), 0, 100) : null;

    /// <summary>Nivel grueso de XInput: 0 vacía, 1 baja, 2 media, 3 llena → porcentaje aproximado y texto.</summary>
    public static (int Percent, string Text) XInputLevel(byte level) => level switch { 0 => (5, "Vacía"), 1 => (25, "Baja"), 2 => (60, "Media"), _ => (100, "Llena") };

    /// <summary>Los tipos de batería de XInput que no son de un mando con pilas: 0 sin conectar y 1 con cable.</summary>
    public static bool IsBatteryType(byte type) => type is not (0 or 1);
}
