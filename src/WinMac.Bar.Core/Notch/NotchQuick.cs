using System.Text.Json;

namespace WinMac.Bar.Notch;

/// <summary>
/// Accesos rápidos de la pestaña Inicio de la Isla. El canal Isla→Barra (tópico <see cref="Topic"/>) es una LISTA CERRADA: solo acepta estos identificadores y cada uno se traduce a una
/// acción que YA existe en el registro central de acciones de Hatsu (las mismas que Spotlight); nunca ejecuta un comando, una ruta ni un texto que venga por el canal. Una acción de un módulo
/// apagado no está registrada: no se anuncia como disponible y, si llegara, el registro la rechaza.
/// </summary>
public static class NotchQuick
{
    public const string Topic = "notch.quick", AvailableKind = "quick", Gestures = "gestures";

    /// <summary>Identificador de la Isla → identificador de la acción en el registro de Hatsu.</summary>
    public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["capture"] = "winmac:capture:region",
        ["snap"] = "snap:edit",
        ["mission"] = "mission:open",
        ["livetext"] = "livetext:run",
    };

    /// <summary>Todo lo que el canal acepta, en el orden en que la Isla los muestra (Portapapeles es de la propia Isla y no pasa por aquí).</summary>
    public static readonly IReadOnlyList<string> Closed = ["capture", "snap", "mission", "livetext", Gestures];

    /// <summary>El identificador de un mensaje <c>{ "id": "capture" }</c>; null si no es un objeto, no trae texto o no está en la lista cerrada.</summary>
    public static string? ParseId(JsonElement? e)
    {
        if (e is not { ValueKind: JsonValueKind.Object } o || !o.TryGetProperty("id", out var v) || v.ValueKind != JsonValueKind.String) return null;
        var id = v.GetString(); return id is not null && Closed.Contains(id) ? id : null;
    }

    /// <summary>
    /// Qué accesos están disponibles ahora: los que tienen acción registrada y disponible, y «gestures» (abre la página de Ajustes; no depende de una acción) mientras el host pueda abrir Ajustes.
    /// </summary>
    public static IReadOnlyList<string> Available(Func<string, bool> actionIsAvailable, bool canOpenSettings) =>
        Closed.Where(id => id == Gestures ? canOpenSettings : Actions.TryGetValue(id, out var a) && actionIsAvailable(a)).ToList();

    /// <summary>Aviso para la Isla: <c>{ "kind": "quick", "available": [...] }</c> (por el tópico de avisos de la Isla).</summary>
    public static object Payload(IEnumerable<string> available) => new { kind = AvailableKind, available = available.ToList() };

    public static string Key(IEnumerable<string> available) => string.Join(",", available);
}
