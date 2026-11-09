using WinMac.SystemControl;

namespace WinMac.Bar.Notifications;

/// <summary>Permiso de Windows para leer las notificaciones de otras apps (Configuración → Privacidad y seguridad → Notificaciones).</summary>
public enum NotificationAccess
{
    /// <summary>Todavía no se ha preguntado: Windows pide la decisión al usuario con <see cref="INotificationSource.RequestAccessAsync"/>.</summary>
    Unspecified,
    Allowed,
    /// <summary>El usuario (o una directiva) lo negó: solo se puede cambiar en Configuración, nunca desde WinMac.</summary>
    Denied,
    /// <summary>La API no existe o no responde para esta app en este equipo (versión de Windows, servicio parado, falta de identidad…).</summary>
    Unavailable,
}

/// <summary>
/// Una notificación tipo «toast» que Windows tiene en su centro de notificaciones, tal como la entrega <c>UserNotificationListener</c>.
/// <see cref="Title"/> y <see cref="Body"/> pueden faltar (una notificación solo con imagen, o una app que no rellena el texto).
/// </summary>
/// <param name="Id">Identificador que da Windows (sirve para eliminarla).</param>
/// <param name="AppId">Identificador de la app de origen (AUMID). Puede ir vacío.</param>
/// <param name="AppName">Nombre visible de la app. Puede ir vacío.</param>
/// <param name="Time">Hora de llegada según Windows.</param>
/// <param name="HasIcon">Windows entregó un icono para esta app (si no, se dibuja la inicial).</param>
public sealed record AppNotification(uint Id, string AppId, string AppName, string? Title, string? Body, DateTimeOffset Time, bool HasIcon = false)
{
    /// <summary>Clave de agrupación: la app; si Windows no dio identificador, su nombre; si tampoco, un grupo común.</summary>
    public string GroupKey => !string.IsNullOrWhiteSpace(AppId) ? AppId.Trim() : !string.IsNullOrWhiteSpace(AppName) ? "name:" + AppName.Trim() : "unknown";
    public string DisplayApp => !string.IsNullOrWhiteSpace(AppName) ? AppName.Trim() : !string.IsNullOrWhiteSpace(AppId) ? AppId.Trim() : "Aplicación desconocida";
}

/// <summary>
/// Acceso a las notificaciones de Windows. La implementación real usa <c>Windows.UI.Notifications.Management.UserNotificationListener</c>; las pruebas usan un doble.
/// Ningún método se llama desde el hilo de la interfaz salvo <see cref="RequestAccessAsync"/> (Windows puede mostrar su propio cuadro de consentimiento).
/// </summary>
public interface INotificationSource
{
    /// <summary>Estado del permiso sin preguntar nada al usuario. <c>Reason</c> explica un <see cref="NotificationAccess.Unavailable"/>.</summary>
    (NotificationAccess Access, string? Reason) GetAccess();

    /// <summary>Pide el permiso a Windows (solo si está sin decidir; si ya se decidió, Windows devuelve la decisión tomada).</summary>
    Task<NotificationAccess> RequestAccessAsync();

    /// <summary>Las notificaciones tipo toast que Windows tiene ahora. Lanza excepción si la API falla.</summary>
    IReadOnlyList<AppNotification> Read();

    /// <summary>Elimina UNA notificación del centro de notificaciones de Windows.</summary>
    ServiceResult Remove(uint id);

    /// <summary>Elimina TODAS las notificaciones del centro de notificaciones de Windows.</summary>
    ServiceResult ClearAll();

    /// <summary>Abre la app de origen (no la notificación concreta: Windows no permite activarla desde fuera).</summary>
    ServiceResult OpenApp(string appId);

    /// <summary>Empieza a escuchar el evento de cambios de Windows. Devuelve false (con el motivo) si Windows no lo ofrece a esta app: entonces se relee solo con el panel abierto.</summary>
    bool StartWatching(Action changed, out string? why);
    void StopWatching();
}

/// <summary>Las notificaciones de una app, de la más reciente a la más antigua.</summary>
public sealed record NotificationGroup(string Key, string AppName, string AppId, IReadOnlyList<AppNotification> Items)
{
    public DateTimeOffset Latest => Items.Count == 0 ? DateTimeOffset.MinValue : Items[0].Time;
    public bool HasIcon => Items.Any(i => i.HasIcon);
}

public enum NcStatus
{
    /// <summary>El usuario no activó la lectura de notificaciones de Windows en WinMac (por defecto).</summary>
    Off,
    /// <summary>Activado en WinMac, pero Windows aún no tiene decisión del usuario.</summary>
    NeedsPermission,
    Requesting,
    Denied,
    Unavailable,
    /// <summary>Primera lectura en curso.</summary>
    Loading,
    Ready,
    /// <summary>La lectura falló; se conserva lo último leído y se dice el motivo.</summary>
    Error,
}

/// <summary>Lo que el panel dibuja: estado, grupos (ya sin lo ocultado), motivo y si llegan avisos de cambios por evento.</summary>
public sealed record NcState(NcStatus Status, IReadOnlyList<NotificationGroup> Groups, string? Message, bool Live, string? WatchReason, DateTime? LastReadUtc)
{
    public static NcState Off { get; } = new(NcStatus.Off, [], null, false, null, null);
    public int Count => Groups.Sum(g => g.Items.Count);
}

/// <summary>Widgets de la parte inferior del Centro de Notificaciones. Se eligen y ordenan en la configuración («calendar,weather,battery»).</summary>
public enum NcWidget { Calendar, Weather, Battery }

public static class NcWidgets
{
    public const string Default = "calendar,weather,battery";

    /// <summary>Lista de la configuración → widgets en ese orden, sin repetidos; lo desconocido se ignora. null = todos; cadena vacía = ninguno.</summary>
    public static IReadOnlyList<NcWidget> Parse(string? s)
    {
        if (s is null) return [NcWidget.Calendar, NcWidget.Weather, NcWidget.Battery];
        var list = new List<NcWidget>();
        foreach (var part in s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<NcWidget>(part, ignoreCase: true, out var w) && Enum.IsDefined(w) && !list.Contains(w)) list.Add(w);
        return list;
    }

    public static string Serialize(IEnumerable<NcWidget> list) => string.Join(",", list.Distinct().Select(w => w.ToString().ToLowerInvariant()));
}
