using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Connect;

/// <summary>Ajustes de WinMac Connect (sección <c>connect</c> de winmac.json). El módulo en sí se activa en Ajustes › General (APAGADO por defecto).</summary>
public sealed record ConnectSettings
{
    /// <summary>Parte del nombre del iPhone emparejado en Windows. Vacío = el primer iPhone emparejado.</summary>
    public string? DeviceName { get; init; }
    public bool Battery { get; init; } = true;
    public bool Notifications { get; init; } = true;
    public ContentLevel Content { get; init; } = ContentLevel.Full;
    /// <summary>Las notificaciones del iPhone también en la pestaña Notificaciones de la Isla.</summary>
    public bool Island { get; init; } = true;
    /// <summary>Aviso emergente (píldora de la Isla) para las nuevas.</summary>
    public bool Alerts { get; init; } = true;
    /// <summary>Avisos de llamadas entrantes y perdidas.</summary>
    public bool Calls { get; init; } = true;

    public ConnectSettings Sanitize() => this with
    {
        DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName.Trim()[..Math.Min(DeviceName.Trim().Length, 64)],
        Content = Enum.IsDefined(Content) ? Content : ContentLevel.Full,
    };

    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static ConnectSettings Parse(JsonElement? e)
    {
        try { return e is { ValueKind: JsonValueKind.Object } d ? (d.Deserialize<ConnectSettings>(Wire) ?? new()).Sanitize() : new(); }
        catch (Exception) { return new(); }
    }
}

public enum PhoneLinkStatus
{
    /// <summary>No hay ningún iPhone emparejado en Windows (o ninguno coincide con el nombre elegido).</summary>
    NoDevice,
    /// <summary>Buscando o abriendo la conexión.</summary>
    Connecting,
    Connected,
    /// <summary>Emparejado pero fuera de alcance, apagado o con el Bluetooth desactivado.</summary>
    Disconnected,
    /// <summary>El Bluetooth del PC está apagado o no existe.</summary>
    NoBluetooth,
    Error,
}

public enum PhoneFeatureStatus { Off, Starting, Active, NotAuthorized, Unavailable }

/// <summary>Estado que publica el módulo (tópico retenido <see cref="ConnectTopics.State"/>). Solo datos reales: lo que no se sabe va a null.</summary>
public sealed record PhoneState
{
    public PhoneLinkStatus Status { get; init; } = PhoneLinkStatus.Connecting;
    public string? DeviceName { get; init; }
    /// <summary>Último porcentaje leído del iPhone (Battery Service) y cuándo. Null = nunca leído en esta ejecución.</summary>
    public int? Battery { get; init; }
    public DateTime? BatteryAtUtc { get; init; }
    /// <summary>Última vez que la conexión estaba activa.</summary>
    public DateTime? LastSeenUtc { get; init; }
    public PhoneFeatureStatus BatteryStatus { get; init; } = PhoneFeatureStatus.Off;
    public PhoneFeatureStatus NotificationsStatus { get; init; } = PhoneFeatureStatus.Off;
    /// <summary>Explicación para el usuario cuando algo no va (sin datos técnicos ni contenido).</summary>
    public string? Message { get; init; }
    /// <summary>iPhones emparejados que el módulo ve (nombres), para elegir en Ajustes.</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];
}

/// <summary>Reglas de frescura y textos del estado del iPhone. Puras (la barra y los ajustes usan las mismas).</summary>
public static class PhoneStatusText
{
    /// <summary>Una lectura de batería vale como «actual» con la conexión activa y si tiene menos de esto.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    /// <summary>Porcentaje a mostrar en la barra: solo si es real y reciente. Desconectado o viejo → null (nunca un porcentaje viejo como si fuera actual).</summary>
    public static int? CurrentBattery(PhoneState s, DateTime nowUtc) =>
        s.Status == PhoneLinkStatus.Connected && s.Battery is { } b && s.BatteryAtUtc is { } at && nowUtc - at <= Fresh && at - nowUtc <= TimeSpan.FromMinutes(1) ? Math.Clamp(b, 0, 100) : null;

    public static string Ago(DateTime? atUtc, DateTime nowUtc)
    {
        if (atUtc is not { } at) return "nunca";
        var d = nowUtc - at; if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalSeconds < 60) return "hace un momento";
        if (d.TotalMinutes < 60) return $"hace {(int)d.TotalMinutes} min";
        if (d.TotalHours < 24) return $"hace {(int)d.TotalHours} h";
        return at.ToLocalTime().ToString("dd/MM HH:mm");
    }

    public static string Status(PhoneState s) => s.Status switch
    {
        PhoneLinkStatus.Connected => "Conectado",
        PhoneLinkStatus.Connecting => "Conectando…",
        PhoneLinkStatus.Disconnected => "Sin conexión",
        PhoneLinkStatus.NoDevice => "Ningún iPhone emparejado",
        PhoneLinkStatus.NoBluetooth => "Bluetooth apagado",
        _ => "Error",
    };
}

/// <summary>Tópicos del canal con el host. Estado y lista son retenidos (llegan al conectar); los avisos son eventos.</summary>
public static class ConnectTopics
{
    public const string Config = "config.connect", State = "connect.state", Notifications = "connect.notifications", Alert = "connect.alert", Call = "connect.call", Diag = "connect.diag";   // Diag: orden puntual del host (Ajustes › Diagnóstico): {observe:bool}; nunca se guarda
}

/// <summary>Formato de la lista y de los avisos (módulo → barra). El contenido solo viaja por el canal local con el host; nunca a disco ni a registros.</summary>
public static class ConnectWire
{
    public sealed record Item(string Key, uint Uid, long Session, string App, string? Bundle, AncsCategory Category, string? Title, string? Subtitle, string? Body, long At, bool PreExisting);
    public sealed record ListMessage(IReadOnlyList<Item> Items);

    public static Item ToItem(PhoneNotification n) => new(n.Key, n.Uid, n.Session, n.DisplayApp, n.Bundle, n.Category, n.Title, n.Subtitle, n.Message, n.Time.ToUnixTimeMilliseconds(), n.PreExisting);

    public static ListMessage List(IReadOnlyList<PhoneNotification> list, bool calls) => new(list.Where(n => calls || n.Call == PhoneCallKind.None).Select(ToItem).ToList());

    public static IReadOnlyList<Item> ParseList(JsonElement? e)
    {
        try { return e is { ValueKind: JsonValueKind.Object } d ? d.Deserialize<ListMessage>(ConnectSettings.Wire)?.Items ?? [] : []; }
        catch (Exception) { return []; }
    }

    public static Item? ParseItem(JsonElement? e)
    {
        try { return e is { ValueKind: JsonValueKind.Object } d ? d.Deserialize<Item>(ConnectSettings.Wire) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Lo publica el HOST cuando el módulo se apaga o se detiene: la barra borra todo lo del iPhone (el último estado retenido ya no vale).</summary>
    public static readonly object Off = new { off = true };

    public static CallState? ParseCall(JsonElement? e)
    {
        try { return e is { ValueKind: JsonValueKind.Object } d ? d.Deserialize<CallState>(ConnectSettings.Wire) : null; }
        catch (Exception) { return null; }
    }

    public static PhoneState? ParseState(JsonElement? e)
    {
        try { return e is { ValueKind: JsonValueKind.Object } d && !d.TryGetProperty("off", out _) ? d.Deserialize<PhoneState>(ConnectSettings.Wire) : null; }
        catch (Exception) { return null; }
    }
}

/// <summary>Espera entre reintentos de conexión: 2 s, 4 s, 8 s… hasta 60 s; vuelve a empezar tras conectar. Sin sondeo agresivo.</summary>
public sealed class Backoff(double firstSeconds = 2, double maxSeconds = 60)
{
    private int _n;
    public TimeSpan Next() { var s = Math.Min(maxSeconds, firstSeconds * Math.Pow(2, _n)); _n = Math.Min(_n + 1, 16); return TimeSpan.FromSeconds(s); }
    public void Reset() => _n = 0;
}
