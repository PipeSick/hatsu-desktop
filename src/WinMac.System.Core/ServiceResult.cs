namespace WinMac.SystemControl;

public enum ServiceStatus
{
    Ok,
    /// <summary>El equipo o Windows no ofrece esto (sin adaptador, monitor sin DDC/CI, API ausente). Lleva el motivo para mostrarlo.</summary>
    Unavailable,
    /// <summary>Windows lo rechazó por permisos o por una política (p. ej. exigiría administrador).</summary>
    Denied,
    /// <summary>Se intentó y falló (error de la API, tiempo agotado). Lleva el mensaje.</summary>
    Failed,
}

/// <summary>
/// Resultado de una operación sobre el sistema. Nunca se oculta un fallo: la interfaz muestra el <see cref="Message"/> y deshabilita el control con ese motivo.
/// </summary>
public readonly record struct ServiceResult(ServiceStatus Status, string? Message = null)
{
    public bool IsOk => Status == ServiceStatus.Ok;
    public static ServiceResult Ok => new(ServiceStatus.Ok);
    public static ServiceResult Unavailable(string reason) => new(ServiceStatus.Unavailable, reason);
    public static ServiceResult Denied(string reason) => new(ServiceStatus.Denied, reason);
    public static ServiceResult Failed(string message) => new(ServiceStatus.Failed, message);

    /// <summary>Ejecuta <paramref name="op"/> convirtiendo cualquier excepción en <see cref="ServiceStatus.Failed"/> (los adaptadores de Windows nunca deben lanzar hacia la interfaz).</summary>
    public static ServiceResult Guard(Func<ServiceResult> op, string what)
    {
        try { return op(); }
        catch (UnauthorizedAccessException e) { return Denied($"{what}: {e.Message}"); }
        catch (Exception e) { return Failed($"{what}: {e.GetType().Name}: {e.Message}"); }
    }
}
