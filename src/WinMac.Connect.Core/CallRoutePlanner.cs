namespace WinMac.Connect;

/// <summary>Resultado del sondeo de telefonía (<c>HatsuDesktop.Connect.exe --probe-calls</c>). Solo hechos observados; nada deducido del tipo de teléfono.</summary>
public sealed record CallProbeReport(
    bool PhoneCallManagerAvailable,
    bool PhoneLineApiReachable,          // PhoneCallStore/PhoneLine respondió sin error
    int BluetoothPhoneLines,             // líneas con Transport = Bluetooth que Windows expone a esta app
    bool TransportAccessGranted,         // PhoneLineTransportDevice.RequestAccessAsync == Allowed
    string? TransportAccessStatus,       // texto del estado (p. ej. DeniedBySystem)
    int HandsFreeAudioEndpoints,         // endpoints de audio Bluetooth «Hands-Free» visibles (existir NO prueba que haya audio de llamada)
    bool BidirectionalAudioVerified);    // solo true tras una llamada real con ambos sentidos confirmados (prueba manual, nunca automática)

/// <summary>Qué mecanismo de control de llamada hay.</summary>
public enum CallControlRoute { None, AncsAction, PhoneLineTransport, CompanionApp }

/// <summary>
/// Criterio de aceptación del producto (docs/57): contestar/rechazar/colgar solo se ofrecen si hay control Y audio bidireccional verificado del PC.
/// Contestar sin poder hablar desde el PC deja al usuario con el teléfono en la mano: no es una función terminada.
/// </summary>
public static class CallRoutePlanner
{
    public static CallControlCapabilities Evaluate(CallProbeReport p, CallControlRoute route, bool controlVerifiedOnDevice)
    {
        bool audio = p.BidirectionalAudioVerified;
        bool control = controlVerifiedOnDevice && route != CallControlRoute.None
            && (route != CallControlRoute.PhoneLineTransport || p.TransportAccessGranted);   // PhoneLine sin acceso concedido no sirve
        var ok = control && audio;
        return new(ok ? ControlSupport.Verified : ControlSupport.Unavailable, ok ? ControlSupport.Verified : ControlSupport.Unavailable,
            ok ? ControlSupport.Verified : ControlSupport.Unavailable, ControlSupport.Unavailable /* marcar: fuera de alcance */,
            audio ? ControlSupport.Verified : ControlSupport.Unavailable, ControlSupport.ExperimentalReadOnly);
    }

    /// <summary>Texto para el diagnóstico: por qué no hay botones.</summary>
    public static string Why(CallProbeReport p, CallControlRoute route, bool controlVerified)
    {
        if (!p.BidirectionalAudioVerified) return "sin audio bidireccional del PC verificado en una llamada real";
        if (route == CallControlRoute.None || !controlVerified) return "sin mecanismo de control verificado";
        if (route == CallControlRoute.PhoneLineTransport && !p.TransportAccessGranted) return "PhoneLineTransportDevice: " + (p.TransportAccessStatus ?? "acceso no concedido");
        return "disponible";
    }
}
