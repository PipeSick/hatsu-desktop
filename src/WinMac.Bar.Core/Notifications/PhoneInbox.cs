using WinMac.Connect;

namespace WinMac.Bar.Notifications;

/// <summary>
/// WinMac Connect en la barra: el último estado del iPhone y sus notificaciones (las publica el módulo Connect por el host). Puro; solo en memoria.
/// <para>Quitar una notificación del iPhone en WinMac solo la OCULTA en WinMac durante la sesión: no se borra del iPhone (eso necesitaría una acción ANCS que aún no está validada).
/// Lo que se borra en el iPhone desaparece aquí solo (llega una lista nueva sin ella).</para>
/// </summary>
public sealed class PhoneInbox
{
    /// <summary>Prefijo de grupo de las apps del iPhone: separa sus grupos de los de Windows (también en las acciones del panel).</summary>
    public const string GroupPrefix = "iphone:";
    /// <summary>Ids de la Isla: los del iPhone empiezan aquí para no chocar nunca con los de Windows (que son de 32 bits).</summary>
    public const long IslandIdBase = 1L << 40;

    private IReadOnlyList<ConnectWire.Item> _items = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    public PhoneState? State { get; private set; }
    public int Version { get; private set; }
    public event Action? Changed;

    /// <summary>El módulo está activo y publicó algo (sin módulo no se muestra nada del iPhone).</summary>
    public bool Active => State is not null;

    public void SetState(PhoneState? s) { State = s; Version++; Changed?.Invoke(); }

    public void SetList(IReadOnlyList<ConnectWire.Item> items)
    {
        _items = items; var keys = items.Select(i => i.Key).ToHashSet(StringComparer.Ordinal); _hidden.IntersectWith(keys);   // lo que el iPhone ya no tiene se olvida
        Version++; Changed?.Invoke();
    }

    /// <summary>El módulo se apagó: se borra todo de memoria.</summary>
    public void Clear() { State = null; _items = []; _hidden.Clear(); Version++; Changed?.Invoke(); }

    public IReadOnlyList<ConnectWire.Item> Visible => _items.Where(i => !_hidden.Contains(i.Key)).ToList();

    public void Hide(uint uid) { foreach (var i in _items.Where(i => i.Uid == uid)) _hidden.Add(i.Key); Version++; Changed?.Invoke(); }
    public void HideGroup(string groupKey) { foreach (var i in _items.Where(i => GroupKey(i) == groupKey)) _hidden.Add(i.Key); Version++; Changed?.Invoke(); }
    public void HideAll() { foreach (var i in _items) _hidden.Add(i.Key); Version++; Changed?.Invoke(); }

    public static string GroupKey(ConnectWire.Item i) => GroupPrefix + (i.Bundle ?? i.App);

    /// <summary>Texto de una notificación del iPhone. Las llamadas dicen qué son («Llamada entrante · Mamá»); lo que no se pidió por privacidad no aparece.</summary>
    public static (string Title, string? Body) Text(ConnectWire.Item i)
    {
        string? who = i.Title;
        if (i.Category == AncsCategory.IncomingCall) return ("Llamada entrante", who);
        if (i.Category == AncsCategory.MissedCall) return ("Llamada perdida", who);
        string? body = string.Join(" · ", new[] { i.Subtitle, i.Body }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return (who ?? i.App, string.IsNullOrEmpty(body) ? null : body);
    }

    /// <summary>Como notificaciones de la barra (mismas tarjetas y agrupación que las de Windows); el nombre de la app dice que vienen del iPhone.</summary>
    public IReadOnlyList<AppNotification> AsNotifications() => Visible.Select(i =>
    {
        var (t, b) = Text(i);
        return new AppNotification(i.Uid, GroupKey(i), "iPhone · " + i.App, t, b, DateTimeOffset.FromUnixTimeMilliseconds(i.At).ToLocalTime());
    }).ToList();

    public IReadOnlyList<NotificationGroup> Groups => NotificationGrouping.Group(AsNotifications());
}
