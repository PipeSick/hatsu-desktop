using System.Text.Json;
using WinMac.Bar.Notifications;

namespace WinMac.Bar.Notch;

/// <summary>
/// Qué quiere la Isla (NotchWin) que le cuente la barra: sale de los ajustes centrales de la Isla (tópico retenido <c>config.notch</c>, campos <c>pillVolume</c>,
/// <c>pillBrightness</c>, <c>pillNotifications</c>). Lo que la Isla no quiere ni se lee ni se envía (las notificaciones no salen de la barra si está apagado).
/// </summary>
public sealed record NotchFeedOptions(bool Volume = true, bool Brightness = true, bool Notifications = true)
{
    public static NotchFeedOptions Parse(JsonElement? e)
    {
        var o = new NotchFeedOptions();
        if (e is not { ValueKind: JsonValueKind.Object } d) return o;
        static bool? B(JsonElement d, string n) => d.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        return new NotchFeedOptions(B(d, "pillVolume") ?? o.Volume, B(d, "pillBrightness") ?? o.Brightness, B(d, "pillNotifications") ?? o.Notifications);
    }
}

/// <summary>
/// Mensajes del evento <see cref="Topic"/> (barra → Isla, a través del host). El formato lo valida la Isla (<c>NotchWin.Core.Pill.PillFeed</c>).
/// Privacidad: el texto de una notificación solo viaja por el canal local con el host (tubería con token de sesión), nunca a disco ni a registros.
/// </summary>
public static class NotchFeed
{
    public const string Topic = "notch.feed";

    public static object Volume(int level, bool muted, string? device) => new { kind = "volume", level = Math.Clamp(level, 0, 100), muted, device };

    public static object Brightness(int level, string display) => new { kind = "brightness", level = Math.Clamp(level, 0, 100), display };

    /// <param name="icon">Píxeles BGRA premultiplicados <paramref name="iconSide"/>×<paramref name="iconSide"/> (los que la barra ya tiene en memoria), o null.</param>
    public static object Notification(AppNotification n, byte[]? icon, int iconSide = 32)
    {
        bool ok = icon is not null && iconSide > 0 && icon.Length == iconSide * iconSide * 4;
        return new
        {
            kind = "notification", app = n.DisplayApp, title = n.Title, body = n.Body,
            icon = ok ? Convert.ToBase64String(icon!) : null, iconW = ok ? iconSide : 0, iconH = ok ? iconSide : 0,
        };
    }

    /// <summary>Modo «No molestar» / presentación / pantalla completa: <c>SHQueryUserNotificationState</c> distinto de <c>QUNS_ACCEPTS_NOTIFICATIONS</c> (5).
    /// Desconocido (la llamada falla) cuenta como «acepta», igual que hace Windows con sus propias notificaciones.</summary>
    public static bool Quiet(int? userNotificationState) => userNotificationState is { } s && s != 5;
}

/// <summary>
/// Detecta las notificaciones NUEVAS entre dos lecturas del centro de notificaciones de Windows. La primera lectura solo fija la base (lo que ya estaba al arrancar no se
/// anuncia), lo antiguo no se anuncia (una notificación de hace minutos que aparece tarde, o el permiso recién concedido) y, si llegan muchas de golpe, solo las más recientes.
/// </summary>
public sealed class NewNotificationTracker(TimeSpan? maxAge = null, int maxBurst = 3)
{
    private readonly TimeSpan _maxAge = maxAge ?? TimeSpan.FromMinutes(2);
    private HashSet<uint>? _seen;

    public bool HasBaseline => _seen is not null;

    /// <summary>Devuelve las nuevas en orden de llegada (la más antigua primero).</summary>
    public IReadOnlyList<AppNotification> Observe(IReadOnlyList<AppNotification> current, DateTimeOffset now)
    {
        var ids = current.Select(n => n.Id).ToHashSet();
        if (_seen is null) { _seen = ids; return []; }
        var fresh = current.Where(n => !_seen.Contains(n.Id) && now - n.Time <= _maxAge && n.Time - now <= TimeSpan.FromMinutes(1))
            .OrderByDescending(n => n.Time).Take(Math.Max(1, maxBurst)).OrderBy(n => n.Time).ToList();
        _seen = ids;   // lo que Windows ya no tiene se olvida
        return fresh;
    }

    /// <summary>Se apagó la fuente o cambió el permiso: la próxima lectura vuelve a ser la base.</summary>
    public void Reset() => _seen = null;
}
