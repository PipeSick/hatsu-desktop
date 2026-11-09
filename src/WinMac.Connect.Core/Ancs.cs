using System.Text;

namespace WinMac.Connect;

/// <summary>
/// Apple Notification Center Service (especificación pública de Apple, docs/39 §2). El iPhone es el proveedor; WinMac, el consumidor (cliente GATT).
/// Todos los números van en little endian y los textos en UTF-8. Los identificadores (UID) solo valen dentro de una sesión: al reconectar cambian.
/// </summary>
public static class Ancs
{
    public static readonly Guid Service = Guid.Parse("7905f431-b5ce-4e99-a40f-4b1e122d00d0");
    public static readonly Guid NotificationSource = Guid.Parse("9fbf120d-6301-42d9-8c58-25e699a21dbd");
    public static readonly Guid ControlPoint = Guid.Parse("69d1d8f3-45e1-49a8-9821-9bbdfdaad9d9");
    public static readonly Guid DataSource = Guid.Parse("22eac6e9-24d6-4bb5-be44-b36ace7c7bfb");

    public const byte CmdGetNotificationAttributes = 0, CmdGetAppAttributes = 1, CmdPerformNotificationAction = 2;
    public const byte AttrAppIdentifier = 0, AttrTitle = 1, AttrSubtitle = 2, AttrMessage = 3, AttrMessageSize = 4, AttrDate = 5, AttrPositiveLabel = 6, AttrNegativeLabel = 7;
    public const byte AppAttrDisplayName = 0;
    /// <summary>Errores del Control Point. 0xA2 (parámetro inválido) llega cuando la notificación ya no existe en el iPhone: es normal y se tolera.</summary>
    public const byte ErrUnknownCommand = 0xA0, ErrInvalidCommand = 0xA1, ErrInvalidParameter = 0xA2, ErrActionFailed = 0xA3;

    /// <summary>Longitudes máximas pedidas al iPhone (el texto de un mensaje se corta ahí).</summary>
    public const ushort MaxTitle = 128, MaxSubtitle = 128, MaxMessage = 512;

    /// <summary>
    /// Atributos que se piden según la privacidad elegida: con <see cref="ContentLevel.AppOnly"/> ni el título ni el texto salen del iPhone.
    /// La fecha siempre (no es contenido) y la app siempre (identifica el origen).
    /// </summary>
    public static IReadOnlyList<(byte Id, ushort Max)> AttributesFor(ContentLevel level) => level switch
    {
        ContentLevel.AppOnly => [(AttrAppIdentifier, 0), (AttrDate, 0)],
        ContentLevel.TitleOnly => [(AttrAppIdentifier, 0), (AttrTitle, MaxTitle), (AttrDate, 0)],
        _ => [(AttrAppIdentifier, 0), (AttrTitle, MaxTitle), (AttrSubtitle, MaxSubtitle), (AttrMessage, MaxMessage), (AttrDate, 0)],
    };

    /// <summary>Get Notification Attributes: CommandID · UID · [AttributeID (· longitud máx. para título, subtítulo y mensaje)]…</summary>
    public static byte[] GetNotificationAttributes(uint uid, IReadOnlyList<(byte Id, ushort Max)> attrs)
    {
        var b = new List<byte>(16) { CmdGetNotificationAttributes };
        b.AddRange(BitConverter.GetBytes(uid).AsLittleEndian());
        foreach (var (id, max) in attrs)
        {
            b.Add(id);
            if (id is AttrTitle or AttrSubtitle or AttrMessage) b.AddRange(BitConverter.GetBytes(max).AsLittleEndian());
        }
        return b.ToArray();
    }

    /// <summary>Get App Attributes: CommandID · AppIdentifier terminado en NUL · DisplayName.</summary>
    public static byte[] GetAppAttributes(string bundle)
    {
        var b = new List<byte> { CmdGetAppAttributes }; b.AddRange(Encoding.UTF8.GetBytes(bundle)); b.Add(0); b.Add(AppAttrDisplayName); return b.ToArray();
    }

    /// <summary>Perform Notification Action (investigación: contestar/rechazar una llamada). WinMac NO lo ofrece todavía: falta validarlo con el iPhone real.</summary>
    public static byte[] PerformAction(uint uid, bool positive)
    {
        var b = new List<byte> { CmdPerformNotificationAction }; b.AddRange(BitConverter.GetBytes(uid).AsLittleEndian()); b.Add(positive ? (byte)0 : (byte)1); return b.ToArray();
    }

    /// <summary>Fecha de ANCS («yyyyMMdd'T'HHmmSS», hora local del iPhone) → hora local de Windows. Null si no se entiende.</summary>
    public static DateTimeOffset? ParseDate(string? s)
    {
        if (s is null || s.Length < 15) return null;
        if (!DateTime.TryParseExact(s[..15], "yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var d)) return null;
        return new DateTimeOffset(d);
    }

    private static byte[] AsLittleEndian(this byte[] b) { if (!BitConverter.IsLittleEndian) Array.Reverse(b); return b; }
}

public enum AncsEventId : byte { Added = 0, Modified = 1, Removed = 2 }

public enum AncsCategory : byte
{
    Other = 0, IncomingCall = 1, MissedCall = 2, Voicemail = 3, Social = 4, Schedule = 5, Email = 6, News = 7, HealthAndFitness = 8, BusinessAndFinance = 9, Location = 10, Entertainment = 11,
}

[Flags]
public enum AncsFlags : byte { None = 0, Silent = 1, Important = 2, PreExisting = 4, PositiveAction = 8, NegativeAction = 16 }

/// <summary>Un aviso de Notification Source (8 bytes): qué pasó, categoría, cuántas hay en esa categoría y el UID de la sesión.</summary>
public readonly record struct AncsEvent(AncsEventId Event, AncsFlags Flags, AncsCategory Category, byte CategoryCount, uint Uid)
{
    public bool PreExisting => (Flags & AncsFlags.PreExisting) != 0;
    public bool Silent => (Flags & AncsFlags.Silent) != 0;

    /// <summary>Null si el paquete es corto o tiene un evento desconocido (se ignora; una versión futura de iOS puede añadir más).</summary>
    public static AncsEvent? Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < 8 || b[0] > 2) return null;
        return new AncsEvent((AncsEventId)b[0], (AncsFlags)b[1], (AncsCategory)b[2], b[3], BitConverter.ToUInt32(b.Slice(4, 4)));
    }
}

/// <summary>Respuesta completa de Data Source.</summary>
public abstract record AncsResponse;
public sealed record AncsNotificationAttributes(uint Uid, IReadOnlyDictionary<byte, string> Values) : AncsResponse;
public sealed record AncsAppAttributes(string Bundle, string? DisplayName) : AncsResponse;

/// <summary>
/// Reensambla Data Source. Una respuesta puede llegar en varios trozos (MTU) y no dice cuántos atributos trae: se sabe por la petición, así que las peticiones
/// se registran en orden (<see cref="Expect"/>) y el iPhone responde en ese orden. Si algo no cuadra (petición rechazada, basura), se vacía y se sigue:
/// perder un atributo no puede bloquear las demás notificaciones.
/// </summary>
public sealed class AncsDataAssembler
{
    private readonly List<byte> _buf = [];
    private readonly Queue<(byte Cmd, int Attrs)> _pending = new();
    public int Pending => _pending.Count;

    /// <summary>Anota una petición enviada: comando y cuántos atributos se pidieron.</summary>
    public void Expect(byte cmd, int attributes) { if (_pending.Count > 64) _pending.Dequeue(); _pending.Enqueue((cmd, attributes)); }

    /// <summary>Una petición fue rechazada por el Control Point (no llegará respuesta): se olvida la más antigua de ese comando.</summary>
    public void Rejected(byte cmd)
    {
        if (_pending.Count == 0) return;
        var keep = new Queue<(byte, int)>(); bool dropped = false;
        while (_pending.Count > 0) { var p = _pending.Dequeue(); if (!dropped && p.Cmd == cmd) { dropped = true; continue; } keep.Enqueue(p); }
        while (keep.Count > 0) _pending.Enqueue(keep.Dequeue());
    }

    public void Reset() { _buf.Clear(); _pending.Clear(); }

    public IReadOnlyList<AncsResponse> Feed(ReadOnlySpan<byte> chunk)
    {
        _buf.AddRange(chunk.ToArray());
        var done = new List<AncsResponse>();
        while (_buf.Count > 0)
        {
            if (_pending.Count == 0 || _buf[0] != _pending.Peek().Cmd)
            {   // inesperado: o el iPhone respondió a algo rechazado, o se perdió un trozo. Se busca la siguiente respuesta esperada; si no hay, se vacía.
                if (_pending.Count > 0 && _pending.Any(p => p.Cmd == _buf[0])) { while (_pending.Count > 0 && _pending.Peek().Cmd != _buf[0]) _pending.Dequeue(); continue; }
                _buf.Clear(); break;
            }

            var (cmd, attrs) = _pending.Peek();
            int consumed = cmd == Ancs.CmdGetNotificationAttributes ? TryNotification(attrs, out var r) : TryApp(out r);
            if (consumed == 0) break;   // falta otro trozo
            if (consumed < 0) { _buf.Clear(); _pending.Dequeue(); break; }
            _buf.RemoveRange(0, consumed); _pending.Dequeue(); if (r is not null) done.Add(r);
        }
        return done;
    }

    private int TryNotification(int attrs, out AncsResponse? r)
    {
        r = null; if (_buf.Count < 5) return 0;
        uint uid = BitConverter.ToUInt32([_buf[1], _buf[2], _buf[3], _buf[4]], 0);
        int pos = 5; var values = new Dictionary<byte, string>();
        for (int i = 0; i < attrs; i++)
        {
            if (pos + 3 > _buf.Count) return 0;
            byte id = _buf[pos]; int len = _buf[pos + 1] | (_buf[pos + 2] << 8);
            if (len > 4096) return -1;
            if (pos + 3 + len > _buf.Count) return 0;
            values[id] = Encoding.UTF8.GetString(_buf.GetRange(pos + 3, len).ToArray()); pos += 3 + len;
        }
        r = new AncsNotificationAttributes(uid, values); return pos;
    }

    private int TryApp(out AncsResponse? r)
    {
        r = null; int nul = _buf.IndexOf(0, 1); if (nul < 0) return _buf.Count > 300 ? -1 : 0;
        string bundle = Encoding.UTF8.GetString(_buf.GetRange(1, nul - 1).ToArray());
        int pos = nul + 1; if (pos + 3 > _buf.Count) return 0;
        int len = _buf[pos + 1] | (_buf[pos + 2] << 8); if (len > 1024) return -1;
        if (pos + 3 + len > _buf.Count) return 0;
        string? name = len == 0 ? null : Encoding.UTF8.GetString(_buf.GetRange(pos + 3, len).ToArray());
        r = new AncsAppAttributes(bundle, name); return pos + 3 + len;
    }
}
