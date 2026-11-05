namespace WinMac.Connect;

/// <summary>Lo que pasó al pedir acceso y registrar la app en el transporte telefónico (la secuencia que usan los proyectos que ya lo hacen funcionar, docs/61).</summary>
public enum AccessResult { Allowed, DeniedBySystem, DeniedByUser, Other }
public enum RegisterResult { NotNeeded, Returned, Unauthorized, Threw }

/// <summary>
/// Decide el estado del proveedor a partir de HECHOS leídos del sistema, no de que una llamada «no lanzó excepción». Aprendido de Klangbruecke y MyPhone:
/// 1) <c>RequestAccessAsync</c> SIEMPRE antes de <c>RegisterApp</c>; 2) <c>RegisterApp</c> puede volver sin hacer nada → se relee <c>IsRegistered()</c>;
/// 3) el booleano de <c>ConnectAsync</c> no es fiable y no se evalúa; 4) la línea telefónica solo aparece con la app registrada. Puro.
/// </summary>
public static class RegistrationPlanner
{
    public const string CapabilityHint = "hace falta la capacidad restringida phoneLineTransportManagement aprobada por Microsoft";

    public static ProviderStatus Evaluate(AccessResult access, bool registeredBefore, RegisterResult register, bool registeredAfter)
    {
        if (access == AccessResult.DeniedByUser) return new(ProviderState.Unavailable, "Windows tiene bloqueado el acceso de Hatsu a este teléfono (Configuración › Privacidad)");
        if (access != AccessResult.Allowed)
            return new(ProviderState.Unavailable, "Windows denegó el acceso al teléfono: otro programa (p. ej. Enlace Móvil) tiene el rol de manos libres de este teléfono —ciérralo o quita y vuelve a emparejar el teléfono—, o " + CapabilityHint);
        if (registeredBefore && register == RegisterResult.NotNeeded) return new(ProviderState.Ready, "Hatsu ya estaba registrado como cliente de telefonía");
        return register switch
        {
            RegisterResult.Unauthorized => new(ProviderState.Unavailable, "Windows negó el registro de Hatsu como cliente de telefonía: " + CapabilityHint),
            RegisterResult.Threw => new(ProviderState.Unavailable, "El registro como cliente de telefonía falló inesperadamente"),
            _ when !registeredAfter => new(ProviderState.Unavailable, "RegisterApp no tuvo efecto (IsRegistered sigue en falso): sin la capacidad aprobada Windows lo ignora en silencio, o hay otra app con el rol"),
            _ => new(ProviderState.Ready, "Windows gestiona la llamada del teléfono; el audio puede pasar al PC"),
        };
    }

    /// <summary>¿Hay que volver a registrar/conectar? El registro se pierde si se vuelve a emparejar el teléfono, otra app toma el rol o el usuario lo quita: se comprueba periódicamente.</summary>
    public static bool NeedsReregister(bool providerReady, bool? stillRegistered) => providerReady && stillRegistered == false;

    /// <summary>Reintentar la conexión HFP si no hay línea Bluetooth visible desde hace un rato (el teléfono salió de rango, suspensión/reanudación, el teléfono la cortó).</summary>
    public static bool ShouldReconnect(bool providerReady, TimeSpan sinceLastLine, TimeSpan sinceLastConnect) =>
        providerReady && sinceLastLine >= TimeSpan.FromSeconds(45) && sinceLastConnect >= TimeSpan.FromSeconds(45);

    /// <summary>La API de control de llamadas (PhoneCall, GetAllActivePhoneCalls, ChangeAudioDevice) es del contrato de llamadas 6: existe en Windows 11 y no en Windows 10.</summary>
    public const ushort RequiredCallsContract = 6;
}
