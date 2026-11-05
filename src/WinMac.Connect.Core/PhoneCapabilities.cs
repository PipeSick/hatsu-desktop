namespace WinMac.Connect;

/// <summary>Estado de una capacidad del telefono. Solo se marca <see cref="Advertised"/> con evidencia real (UUID de servicio visto); nunca se deduce de «es iPhone/Android».</summary>
public enum CapabilityState { Unknown, Advertised, Absent }

/// <summary>
/// Capacidades de un telefono a partir de los servicios Bluetooth que ANUNCIA (SDP clasico y/o GATT). Puro.
/// «Anunciado» no es «utilizable»: acceder a HFP/PhoneLine desde MSIX necesita ademas la capacidad restringida phoneLineTransportManagement
/// (ver docs/55), asi que la UI no debe prometer llamadas por el solo hecho de ver HFP.
/// </summary>
public sealed record PhoneCapabilities(
    CapabilityState HandsFreeAg, CapabilityState Pbap, CapabilityState Map, CapabilityState A2dpSource,
    CapabilityState BatteryGatt, CapabilityState Ancs)
{
    public const ushort UuidHfpAg = 0x111F, UuidPbapPse = 0x112F, UuidMapMas = 0x1132, UuidA2dpSource = 0x110A, UuidBattery = 0x180F;
    public static readonly Guid AncsService = new("7905F431-B5CE-4E99-A40F-4B1E122D00D0");

    /// <summary>Un servicio Bluetooth en forma 16 bits (base SIG) o Guid completo.</summary>
    public static PhoneCapabilities From(IEnumerable<Guid> services, bool servicesEnumerated)
    {
        var set = new HashSet<Guid>(services);
        CapabilityState S(Guid g) => set.Contains(g) ? CapabilityState.Advertised : servicesEnumerated ? CapabilityState.Absent : CapabilityState.Unknown;
        return new(S(Sig(UuidHfpAg)), S(Sig(UuidPbapPse)), S(Sig(UuidMapMas)), S(Sig(UuidA2dpSource)), S(Sig(UuidBattery)), S(AncsService));
    }

    /// <summary>UUID de 16 bits sobre la base Bluetooth SIG (0000xxxx-0000-1000-8000-00805F9B34FB).</summary>
    public static Guid Sig(ushort s) => new($"0000{s:X4}-0000-1000-8000-00805F9B34FB");

    /// <summary>Lo que el modulo puede hacer HOY con servicios publicos, sin la capacidad restringida: avisos/bateria; nunca contestar ni marcar.</summary>
    public bool CanShowIncomingCallNotice => Ancs == CapabilityState.Advertised;
    public bool CanReadBattery => BatteryGatt == CapabilityState.Advertised;
    public bool CallControlRouteAdvertised => HandsFreeAg == CapabilityState.Advertised;
}

/// <summary>Estado de verificación de un control de llamadas.</summary>
public enum ControlSupport { Unavailable, ExperimentalReadOnly, Verified }

/// <summary>
/// Controles de llamada que Hatsu ofrece HOY. Regla: <b>ninguno</b> hasta que exista un mecanismo verificado con un iPhone real (docs/56).
/// La Isla/Barra solo dibuja un botón si su valor es distinto de <see cref="ControlSupport.Unavailable"/> y el mecanismo existe de verdad.
/// </summary>
public sealed record CallControlCapabilities(ControlSupport Answer, ControlSupport Reject, ControlSupport Hangup, ControlSupport Dial, ControlSupport PcAudio, ControlSupport ActiveState)
{
    /// <summary>Contestar/rechazar/colgar/marcar/audio: sin mecanismo. Estado «activa»: observador de solo lectura del sistema, sin validar con iPhone.</summary>
    public static readonly CallControlCapabilities Today = new(ControlSupport.Unavailable, ControlSupport.Unavailable, ControlSupport.Unavailable,
        ControlSupport.Unavailable, ControlSupport.Unavailable, ControlSupport.ExperimentalReadOnly);
    public bool AnyButton => Answer == ControlSupport.Verified || Reject == ControlSupport.Verified || Hangup == ControlSupport.Verified;   // solo «Verified»: nunca por estar «experimental»
}
