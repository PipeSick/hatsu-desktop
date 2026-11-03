namespace WinMac.SystemControl;

// ───────────── Audio
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>Salida de audio. <see cref="Available"/> = false con <see cref="Reason"/> si no hay dispositivo de salida o Core Audio falla.</summary>
public sealed record AudioState(bool Available, int Volume, bool Muted, IReadOnlyList<AudioDevice> Outputs, string? Reason = null)
{
    public static AudioState None(string reason) => new(false, 0, false, [], reason);
    /// <summary>Ondas del altavoz a dibujar: 0 (silencio) a 3.</summary>
    public int Waves => !Available || Muted || Volume <= 0 ? 0 : Volume < 34 ? 1 : Volume < 67 ? 2 : 3;
}

/// <summary>Entrada de audio (micrófono) del dispositivo predeterminado. <see cref="VolumeControllable"/> = false si el dispositivo no permite ajustar su volumen desde Windows (el control se muestra deshabilitado, no simulado).</summary>
public sealed record AudioInputState(bool Available, int Volume, bool Muted, bool VolumeControllable, IReadOnlyList<AudioDevice> Devices, string? Reason = null)
{
    public static AudioInputState None(string reason) => new(false, 0, false, false, [], reason);
}

/// <summary>
/// Una aplicación con sesión de audio en la salida predeterminada. <see cref="Id"/> es estable mientras la app corre (agrupa todas las sesiones del mismo proceso: «pid:1234»; «system» = sonidos del sistema).
/// </summary>
public sealed record AudioSession(string Id, int Pid, string Name, string? ExePath, int Volume, bool Muted, bool IsSystemSounds, bool Active);

public sealed record AudioSessionsState(bool Available, IReadOnlyList<AudioSession> Sessions, string? Reason = null)
{
    public static AudioSessionsState None(string reason) => new(false, [], reason);
}

// ───────────── Wi-Fi
public sealed record WifiNetwork(string Ssid, int Signal, bool Secured, bool Connected, bool HasProfile, string? ProfileName, int? RssiDbm = null, int? FrequencyMhz = null)
{
    /// <summary>«5 GHz» / «2,4 GHz» / «6 GHz» según la frecuencia central (null si Windows no la dio).</summary>
    public string? BandText => FrequencyMhz switch { null => null, >= 5925 => "6 GHz", >= 4900 => "5 GHz", >= 2400 => "2,4 GHz", _ => null };
    /// <summary>Barras 1–4 según la calidad de señal (0–100) que da Windows.</summary>
    public int Bars => WifiState.BarsFor(Signal);
}

public sealed record WifiState(bool Available, bool Connected, string? Ssid, int Signal, IReadOnlyList<WifiNetwork> Networks, string? Reason = null, bool? RadioOn = null)
{
    public static WifiState None(string reason) => new(false, false, null, 0, [], reason);
    public int Bars => Connected ? BarsFor(Signal) : 0;
    public static int BarsFor(int signal) => signal switch { <= 0 => 0, < 25 => 1, < 50 => 2, < 75 => 3, _ => 4 };
}

// ───────────── Bluetooth
public sealed record BluetoothDeviceInfo(string Id, string Name, bool Connected);

public sealed record BluetoothState(bool Available, bool RadioOn, IReadOnlyList<BluetoothDeviceInfo> Devices, string? Reason = null)
{
    public static BluetoothState None(string reason) => new(false, false, [], reason);
    public int ConnectedCount => Devices.Count(d => d.Connected);
}

// ───────────── Brillo
public sealed record MonitorBrightness(string Id, string Name, int Percent, string Method);

public sealed record BrightnessState(bool Available, IReadOnlyList<MonitorBrightness> Monitors, string? Reason = null)
{
    public static BrightnessState None(string reason) => new(false, [], reason);
    /// <summary>Valor representativo para el control maestro: el de la primera pantalla controlable.</summary>
    public int Percent => Monitors.Count > 0 ? Monitors[0].Percent : 0;
}

// ───────────── Energía y apariencia
public enum PowerMode { BestEfficiency, Balanced, Performance, BestPerformance }

public sealed record PowerModeState(bool Available, PowerMode Mode, string? Reason = null)
{
    public static PowerModeState None(string reason) => new(false, PowerMode.Balanced, reason);

    public static string Label(PowerMode m) => m switch
    {
        PowerMode.BestEfficiency => "Mayor eficiencia",
        PowerMode.Balanced => "Equilibrado",
        PowerMode.Performance => "Mayor rendimiento",
        _ => "Máximo rendimiento",
    };
}

/// <param name="Dark">Tema de las APLICACIONES (AppsUseLightTheme = 0).</param>
/// <param name="SystemDark">Tema de Windows: barra de tareas, Inicio, centro de notificaciones (SystemUsesLightTheme = 0). Null si Windows no lo tiene. El tema de WinMac es aparte (Ajustes › Barra › Tema).</param>
public sealed record AppearanceState(bool Available, bool Dark, string? Reason = null, bool? SystemDark = null)
{
    public static AppearanceState None(string reason) => new(false, false, reason);
}

// ───────────── Pantallas
public enum DisplayTopology { InternalOnly, Duplicate, Extend, ExternalOnly }
