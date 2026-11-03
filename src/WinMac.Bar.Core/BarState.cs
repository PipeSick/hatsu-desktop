namespace WinMac.Bar;

public enum NetKind { Unknown, Offline, Wifi, Ethernet }

/// <summary><see cref="Percent"/> 0–100. Sin batería (equipo de sobremesa) → <see cref="Present"/> = false y el indicador no se muestra.</summary>
/// <param name="SecondsLeft">Windows' estimate of the time left on battery (GetSystemPowerStatus); null when Windows does not know it (charging, just unplugged).</param>
public sealed record BatteryInfo(bool Present, int Percent, bool Charging, bool Plugged, int? SecondsLeft = null)
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
    IReadOnlyList<Tray.TrayIcon>? TrayIcons = null, bool TrayOpen = false)
{
    /// <summary>Iconos de la bandeja de Windows (apps en segundo plano) que la barra muestra.</summary>
    public IReadOnlyList<Tray.TrayIcon> Tray => TrayIcons ?? [];
    public WeatherBarInfo Weather => WeatherInfo ?? WeatherBarInfo.None;
    public BluetoothInfo Bt => Bluetooth ?? BluetoothInfo.None;
    public static BarState Empty(DateTime now) => new(now, null, BatteryInfo.None, VolumeInfo.None, NetworkInfo.Unknown);
}

/// <summary>
/// WM-010: what the battery itself reports, read when the Energy panel opens: capacities (Windows.Devices.Power), charge rate and the cycle count
/// (IOCTL_BATTERY_QUERY_INFORMATION). Each value is null when the battery or its driver does not report it — never estimated or invented.
/// </summary>
public sealed record BatteryDetails(int? DesignMWh, int? FullMWh, int? RemainingMWh, int? RateMW, int? Cycles, string? Chemistry = null)
{
    public static readonly BatteryDetails Unknown = new(null, null, null, null, null);

    /// <summary>Health = full-charge capacity over design capacity, only when both are real (some drivers report 0 or the same placeholder).</summary>
    public int? HealthPercent => DesignMWh is > 0 and var d && FullMWh is > 0 and var f ? (int)Math.Round(100.0 * f / d) : null;
}

public static class BatteryText
{
    /// <summary>«Quedan 3 h 20 min» / «Quedan 45 min»; null without an estimate.</summary>
    public static string? TimeLeft(int? seconds)
    {
        if (seconds is not > 0) return null;
        var t = TimeSpan.FromSeconds(seconds.Value);
        return t.TotalHours >= 1 ? $"Quedan {(int)t.TotalHours} h {t.Minutes} min" : $"Quedan {Math.Max(1, t.Minutes)} min";
    }

    public static string State(BatteryInfo b) => b.Charging ? (b.Percent >= 100 ? "Cargada" : "Cargando") : b.Plugged ? "Conectada, sin cargar" : b.Low ? "Batería baja" : "Con batería";

    public static string MWh(int mwh) => (mwh / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + " Wh";
}
