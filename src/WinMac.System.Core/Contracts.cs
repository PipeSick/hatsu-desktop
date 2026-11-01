namespace WinMac.SystemControl;

// Contratos de los controles. Los adaptadores de Windows (WinMac.System.Win) los implementan; la lógica de interfaz y las pruebas usan dobles.
// Todos son SÍNCRONOS y pueden tardar (DDC/CI, escaneo Wi-Fi): quien los llama los ejecuta fuera del hilo de interfaz.

public interface IAudioService
{
    AudioState Read();
    ServiceResult SetVolume(int percent);
    ServiceResult SetMuted(bool muted);
    /// <summary>Cambia la salida predeterminada. Puede devolver <see cref="ServiceStatus.Unavailable"/> si la API no oficial no es compatible: la interfaz ofrece abrir Configuración.</summary>
    ServiceResult SetDefaultOutput(string deviceId);
    /// <summary>¿Se puede cambiar la salida desde WinMac en este equipo? (detección de compatibilidad, sin cambiar nada)</summary>
    ServiceResult CanSwitchOutput();

    // Mezclador (el mismo servicio que el Control Center: una sola implementación para cada dispositivo).
    /// <summary>Micrófono predeterminado y los demás dispositivos de entrada activos.</summary>
    AudioInputState ReadInput();
    ServiceResult SetInputVolume(int percent);
    ServiceResult SetInputMuted(bool muted);
    ServiceResult SetDefaultInput(string deviceId);
    /// <summary>Aplicaciones con sesión de audio en la salida predeterminada (activas e inactivas no expiradas, como el mezclador de Windows).</summary>
    AudioSessionsState ReadSessions();
    /// <summary>Volumen de una aplicación (todas sus sesiones). <paramref name="sessionId"/> es el <see cref="AudioSession.Id"/>.</summary>
    ServiceResult SetSessionVolume(string sessionId, int percent);
    ServiceResult SetSessionMuted(string sessionId, bool muted);
}

/// <summary>Un servicio de audio que recuerda la salida anterior al último cambio hecho desde WinMac, para poder restaurarla.</summary>
public interface IOutputRestore
{
    string? PreviousDefaultId { get; }
}

public interface IWifiService
{
    WifiState Read(bool scan);
    ServiceResult Connect(string profileName, string ssid);
    ServiceResult Disconnect();
    ServiceResult SetRadio(bool on);
}

public interface IBluetoothService
{
    /// <param name="withDevices">false = solo el estado de la radio (barato: lo usa el icono de la barra); true = también los dispositivos emparejados (más lento: panel abierto).</param>
    BluetoothState Read(bool withDevices = true);
    ServiceResult SetRadio(bool on);
}

public interface IBrightnessService
{
    BrightnessState Read();
    ServiceResult Set(string monitorId, int percent);
}

public interface IPowerService
{
    PowerModeState ReadMode();
    ServiceResult SetMode(PowerMode mode);
}

public interface IAppearanceService
{
    AppearanceState Read();
    ServiceResult SetDark(bool dark);
}

public interface IDisplayService
{
    ServiceResult SetTopology(DisplayTopology topology);
}

public interface ISessionService
{
    ServiceResult Run(SystemActionKind action);
    /// <summary>Cierra a la fuerza el proceso de la ventana en primer plano (con las salvaguardas de <see cref="ForceQuitGuard"/>).</summary>
    ServiceResult ForceQuitForeground();
    ServiceResult OpenNotificationCenter();
}

/// <summary>Todo lo que el Control Center necesita, en un solo punto (se sustituye entero en las pruebas).</summary>
public sealed record SystemServices(
    IAudioService Audio, IWifiService Wifi, IBluetoothService Bluetooth, IBrightnessService Brightness,
    IPowerService Power, IAppearanceService Appearance, IDisplayService Display, ISessionService Session);
