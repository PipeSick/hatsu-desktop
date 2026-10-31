namespace WinMac.Bar;

public enum NetKind { Unknown, Offline, Wifi, Ethernet }

/// <summary><see cref="Percent"/> 0–100. Sin batería (equipo de sobremesa) → <see cref="Present"/> = false y el indicador no se muestra.</summary>
public sealed record BatteryInfo(bool Present, int Percent, bool Charging, bool Plugged)
{
    public static readonly BatteryInfo None = new(false, 0, false, false);
    public double Fill => Math.Clamp(Percent, 0, 100) / 100.0;
    public bool Low => Present && !Charging && Percent <= 20;
}

public sealed record VolumeInfo(bool Available, int Percent, bool Muted)
{
    public static readonly VolumeInfo None = new(false, 0, false);
    /// <summary>Ondas del altavoz a dibujar: 0 (silencio) a 3.</summary>
    public int Waves => !Available || Muted || Percent <= 0 ? 0 : Percent < 34 ? 1 : Percent < 67 ? 2 : 3;
}

public sealed record NetworkInfo(NetKind Kind)
{
    public static readonly NetworkInfo Unknown = new(NetKind.Unknown);
}

/// <summary>Todo lo que la barra muestra. Lo rellenan los proveedores del adaptador de Windows; el núcleo no lee el sistema.</summary>
public sealed record BarState(DateTime Now, string? ActiveApp, BatteryInfo Battery, VolumeInfo Volume, NetworkInfo Network)
{
    public static BarState Empty(DateTime now) => new(now, null, BatteryInfo.None, VolumeInfo.None, NetworkInfo.Unknown);
}
