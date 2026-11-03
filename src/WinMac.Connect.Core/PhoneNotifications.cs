namespace WinMac.Connect;

/// <summary>Privacidad del contenido (Ajustes › WinMac Connect). Lo que no se elige ni siquiera se pide al iPhone.</summary>
public enum ContentLevel { Full, TitleOnly, AppOnly }

/// <summary>Tipo de aviso de llamada (solo aviso: contestar y rechazar NO se ofrecen hasta validarlos con un iPhone real).</summary>
public enum PhoneCallKind { None, Incoming, Missed }

/// <summary>Una notificación del iPhone. <see cref="Uid"/> solo vale en la sesión <see cref="Session"/>. Contenido solo en memoria.</summary>
public sealed record PhoneNotification(long Session, uint Uid, AncsCategory Category, string? Bundle, string? AppName, string? Title, string? Subtitle, string? Message,
    DateTimeOffset Time, bool PreExisting, bool Complete)
{
    /// <summary>Clave estable dentro de la sesión.</summary>
    public string Key => $"{Session}:{Uid}";
    public PhoneCallKind Call => Category switch { AncsCategory.IncomingCall => PhoneCallKind.Incoming, AncsCategory.MissedCall => PhoneCallKind.Missed, _ => PhoneCallKind.None };
    public string DisplayApp => !string.IsNullOrWhiteSpace(AppName) ? AppName!.Trim() : PhoneApps.Friendly(Bundle);
}

/// <summary>Nombres de reserva cuando el iPhone aún no dio el nombre visible de la app.</summary>
public static class PhoneApps
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["com.apple.mobilephone"] = "Teléfono", ["com.apple.MobileSMS"] = "Mensajes", ["com.apple.mobilemail"] = "Mail", ["com.apple.mobilecal"] = "Calendario",
        ["com.apple.reminders"] = "Recordatorios", ["com.apple.Passbook"] = "Cartera", ["com.apple.facetime"] = "FaceTime", ["net.whatsapp.WhatsApp"] = "WhatsApp",
        ["ph.telegra.Telegraph"] = "Telegram",
    };

    public static string Friendly(string? bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle)) return "iPhone";
        if (Known.TryGetValue(bundle, out var n)) return n;
        var last = bundle.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? bundle : char.ToUpperInvariant(last[0]) + last[1..];
    }
}

/// <summary>
/// Las notificaciones del iPhone en la sesión ANCS actual. Puro: el adaptador de Windows le pasa eventos y atributos; esto decide qué pedir, qué mostrar y qué avisar.
/// <list type="bullet">
/// <item>Importación silenciosa: las que ya estaban (bandera PreExisting) entran en la lista sin aviso, aunque sean cientos.</item>
/// <item>Nuevas en tiempo real: se avisan UNA vez, cuando llegan sus datos (o, si el iPhone no los da, tras un tiempo con lo que haya).</item>
/// <item>Eliminadas en el iPhone: desaparecen de la lista (y de WinMac y la Isla).</item>
/// <item>Silenciosas (bandera Silent): se listan sin aviso.</item>
/// </list>
/// Límite de memoria: <see cref="MaxItems"/> (las más antiguas se descartan).
/// </summary>
public sealed class PhoneNotificationBook
{
    public const int MaxItems = 400;
    private readonly Dictionary<uint, PhoneNotification> _items = [];
    private readonly HashSet<uint> _alertPending = [];
    private readonly Dictionary<string, string> _appNames = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;
    public long Session { get; private set; }
    public int Version { get; private set; }

    public PhoneNotificationBook(Func<DateTimeOffset>? now = null) { _now = now ?? (() => DateTimeOffset.Now); }

    /// <summary>Nueva sesión ANCS (suscripción nueva tras conectar o reconectar): los UID anteriores ya no valen. Los nombres de apps se conservan.</summary>
    public void NewSession() { Session++; _items.Clear(); _alertPending.Clear(); Version++; }

    /// <summary>Se apagó la función o se desvinculó: se borra todo, también los nombres de apps.</summary>
    public void Clear() { _items.Clear(); _alertPending.Clear(); _appNames.Clear(); Version++; }

    public int Count => _items.Count;

    /// <summary>Aplica un aviso de Notification Source. Devuelve el UID cuyos atributos hay que pedir (alta o modificación), o null.</summary>
    public uint? Apply(AncsEvent e)
    {
        switch (e.Event)
        {
            case AncsEventId.Removed:
                if (_items.Remove(e.Uid)) Version++;
                _alertPending.Remove(e.Uid); return null;
            case AncsEventId.Added:
                if (_items.Count >= MaxItems) { var oldest = _items.Values.OrderBy(i => i.Time).First(); _items.Remove(oldest.Uid); _alertPending.Remove(oldest.Uid); }
                _items[e.Uid] = new PhoneNotification(Session, e.Uid, e.Category, null, null, null, null, null, _now(), e.PreExisting, false);
                if (!e.PreExisting && !e.Silent) _alertPending.Add(e.Uid);
                Version++; return e.Uid;
            case AncsEventId.Modified:
                if (!_items.ContainsKey(e.Uid)) { _items[e.Uid] = new PhoneNotification(Session, e.Uid, e.Category, null, null, null, null, null, _now(), true, false); Version++; }
                return e.Uid;
        }
        return null;
    }

    /// <summary>Llegaron los atributos de una notificación. Devuelve el bundle si todavía no se conoce el nombre visible de esa app (para pedirlo una vez).</summary>
    public string? SetAttributes(AncsNotificationAttributes a)
    {
        if (!_items.TryGetValue(a.Uid, out var n)) return null;
        string? V(byte id) => a.Values.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        var bundle = V(Ancs.AttrAppIdentifier) ?? n.Bundle;
        _items[a.Uid] = n with
        {
            Bundle = bundle, AppName = bundle is not null && _appNames.TryGetValue(bundle, out var name) ? name : n.AppName,
            Title = V(Ancs.AttrTitle), Subtitle = V(Ancs.AttrSubtitle), Message = V(Ancs.AttrMessage), Time = Ancs.ParseDate(V(Ancs.AttrDate)) ?? n.Time, Complete = true,
        };
        Version++;
        return bundle is not null && !_appNames.ContainsKey(bundle) ? bundle : null;
    }

    /// <summary>Nombre visible de una app (Get App Attributes).</summary>
    public void SetAppName(AncsAppAttributes a)
    {
        if (string.IsNullOrWhiteSpace(a.DisplayName)) { _appNames.TryAdd(a.Bundle, PhoneApps.Friendly(a.Bundle)); return; }
        _appNames[a.Bundle] = a.DisplayName.Trim();
        foreach (var (uid, n) in _items.ToList()) if (n.Bundle == a.Bundle) _items[uid] = n with { AppName = _appNames[a.Bundle] };
        Version++;
    }

    public bool KnowsApp(string bundle) => _appNames.ContainsKey(bundle);

    /// <summary>El iPhone rechazó la petición de atributos (normalmente porque ya no existe): se marca completa con lo que haya, para no bloquear el aviso.</summary>
    public void AttributesFailed(uint uid) { if (_items.TryGetValue(uid, out var n) && !n.Complete) { _items[uid] = n with { Complete = true }; Version++; } }

    /// <summary>
    /// Avisos a mostrar ahora (en orden de llegada): nuevas ya completas, o incompletas tras <paramref name="waitForData"/>. Cada una se devuelve una sola vez.
    /// Una llamada entrante que ya se eliminó no se avisa (colgó antes de que llegaran sus datos).
    /// </summary>
    public IReadOnlyList<PhoneNotification> TakeAlerts(TimeSpan? waitForData = null)
    {
        var wait = waitForData ?? TimeSpan.FromSeconds(3); var now = _now(); var list = new List<PhoneNotification>();
        foreach (var uid in _alertPending.ToList())
        {
            if (!_items.TryGetValue(uid, out var n)) { _alertPending.Remove(uid); continue; }
            if (!n.Complete && now - n.Time < wait) continue;
            _alertPending.Remove(uid); list.Add(n);
        }
        return list.OrderBy(n => n.Time).ToList();
    }

    /// <summary>Lista actual, de la más reciente a la más antigua.</summary>
    public IReadOnlyList<PhoneNotification> Snapshot(int max = MaxItems) => _items.Values.OrderByDescending(n => n.Time).ThenByDescending(n => n.Uid).Take(max).ToList();

    /// <summary>Llamada entrante en curso (la última notificación de categoría IncomingCall que sigue en el iPhone).</summary>
    public PhoneNotification? IncomingCall => _items.Values.Where(n => n.Call == PhoneCallKind.Incoming).OrderByDescending(n => n.Time).FirstOrDefault();
}
