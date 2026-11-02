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

/// <summary>Red. <see cref="Ssid"/> y <see cref="Bars"/> (0–4) solo vienen rellenados con Wi-Fi conectado; -1 = no se sabe (no se inventa).</summary>
public sealed record NetworkInfo(NetKind Kind, string? Ssid = null, int Bars = -1)
{
    public static readonly NetworkInfo Unknown = new(NetKind.Unknown);
}

/// <summary><see cref="Present"/> = hay adaptador Bluetooth. <see cref="On"/> = radio encendida; <see cref="Connected"/> = dispositivos conectados.</summary>
public sealed record BluetoothInfo(bool Present, bool On, int Connected)
{
    public static readonly BluetoothInfo None = new(false, false, 0);
}

/// <summary>Lo que muestra el icono del Tiempo en la barra: clave de icono y texto («12°C»; vacío mientras carga o sin ciudad).</summary>
public sealed record WeatherBarInfo(string IconKey, string Text)
{
    public static readonly WeatherBarInfo None = new("cloudy", "");
}

/// <summary>Todo lo que la barra muestra. Lo rellenan los proveedores del adaptador de Windows; el núcleo no lee el sistema.</summary>
public sealed record BarState(DateTime Now, string? ActiveApp, BatteryInfo Battery, VolumeInfo Volume, NetworkInfo Network, BluetoothInfo? Bluetooth = null, IReadOnlyList<string>? AppMenus = null, WeatherBarInfo? WeatherInfo = null,
    IReadOnlyList<Tray.TrayIcon>? TrayIcons = null)
{
    /// <summary>Iconos de la bandeja de Windows (apps en segundo plano) que la barra muestra.</summary>
    public IReadOnlyList<Tray.TrayIcon> Tray => TrayIcons ?? [];
    public WeatherBarInfo Weather => WeatherInfo ?? WeatherBarInfo.None;
    public BluetoothInfo Bt => Bluetooth ?? BluetoothInfo.None;
    public static BarState Empty(DateTime now) => new(now, null, BatteryInfo.None, VolumeInfo.None, NetworkInfo.Unknown);
}
