using System.Buffers.Binary;
using System.Text;

namespace WinMac.Bar.Tray;

/// <summary>Orden de Shell_NotifyIcon (NIM_*).</summary>
public enum TrayCommand { Add = 0, Modify = 1, Delete = 2, SetFocus = 3, SetVersion = 4 }

/// <summary>
/// Lo que una app manda a la bandeja con Shell_NotifyIcon. Los identificadores de ventana e icono viajan como enteros de 32 bits (formato fijo de la bandeja,
/// el mismo para apps de 32 y 64 bits). Solo vale lo que <see cref="Flags"/> marca como presente.
/// </summary>
public sealed record TrayMessage(TrayCommand Command, uint Hwnd, uint Id, uint Flags, uint CallbackMessage, uint Icon, string Tip, uint State, uint StateMask, uint Version, Guid? Guid);

/// <summary>
/// Lectura del mensaje WM_COPYDATA que Windows envía a la ventana «Shell_TrayWnd» por cada Shell_NotifyIcon (dwData = 1). Puro: se prueba con búferes sintéticos.
/// Disposición (NOTIFYICONDATA de 32 bits, Unicode), en bytes: firma 0 · orden 4 · cbSize 8 · hWnd 12 · uID 16 · uFlags 20 · uCallbackMessage 24 · hIcon 28 · szTip[128] 32 ·
/// dwState 288 · dwStateMask 292 · szInfo[256] 296 · uVersion 808 · szInfoTitle[64] 812 · dwInfoFlags 940 · guidItem 944 · hBalloonIcon 960.
/// </summary>
public static class TrayProtocol
{
    public const uint Signature = 0x34753423, CopyDataNotify = 1;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_INFO = 0x10, NIF_GUID = 0x20;
    public const uint NIS_HIDDEN = 0x1;
    private const int MinLength = 8 + 32 + 256;   // hasta el final de szTip: lo mínimo que mandan las versiones antiguas

    /// <summary>Interpreta el bloque de datos. null si no es un mensaje de la bandeja o está truncado.</summary>
    public static TrayMessage? Parse(ReadOnlySpan<byte> d)
    {
        if (d.Length < MinLength || U32(d, 0) != Signature) return null;
        uint cmd = U32(d, 4); if (cmd > 4) return null;
        uint flags = U32(d, 20);
        string tip = (flags & NIF_TIP) != 0 ? Str(d, 32, 128) : "";
        uint state = d.Length >= 296 ? U32(d, 288) : 0, mask = d.Length >= 296 ? U32(d, 292) : 0, version = d.Length >= 812 ? U32(d, 808) : 0;
        Guid? guid = (flags & NIF_GUID) != 0 && d.Length >= 960 ? new Guid(d.Slice(944, 16)) : null;
        return new TrayMessage((TrayCommand)cmd, U32(d, 12), U32(d, 16), flags, U32(d, 24), U32(d, 28), tip, state, mask, version, guid);
    }

    private static uint U32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(o, 4));

    private static string Str(ReadOnlySpan<byte> d, int o, int chars)
    {
        var s = Encoding.Unicode.GetString(d.Slice(o, Math.Min(chars * 2, d.Length - o)));
        int z = s.IndexOf('\0'); return (z >= 0 ? s[..z] : s).Trim();
    }

    /// <summary>Construye un mensaje (para las pruebas y el diagnóstico).</summary>
    public static byte[] Build(TrayCommand cmd, uint hwnd, uint id, uint flags, uint callback = 0, uint icon = 0, string tip = "", uint state = 0, uint mask = 0, uint version = 0, Guid? guid = null)
    {
        var b = new byte[964];
        void W(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o, 4), v);
        W(0, Signature); W(4, (uint)cmd); W(8, 956); W(12, hwnd); W(16, id); W(20, flags); W(24, callback); W(28, icon);
        Encoding.Unicode.GetBytes(tip.Length > 127 ? tip[..127] : tip).CopyTo(b, 32);
        W(288, state); W(292, mask); W(808, version);
        guid?.TryWriteBytes(b.AsSpan(944, 16));
        return b;
    }
}

/// <summary>Un icono de la bandeja tal como lo conoce WinMac.</summary>
/// <param name="Key">Identidad estable: el GUID si la app lo dio; si no, ventana + id.</param>
/// <param name="Icon">Identificador del icono (HICON); 0 = sin icono todavía.</param>
public sealed record TrayIcon(string Key, uint Hwnd, uint Id, uint CallbackMessage, uint Icon, string Tip, uint Version, bool Hidden, long Order);

/// <summary>Iconos de la bandeja: altas, cambios y bajas en el orden en que llegan. Puro (se prueba en Linux); lo alimenta la ventana de bandeja de WinMac.</summary>
public sealed class TrayIconStore
{
    private readonly Dictionary<string, TrayIcon> _icons = new(StringComparer.Ordinal); private long _order;

    public static string KeyOf(TrayMessage m) => m.Guid is { } g && g != Guid.Empty ? "g:" + g.ToString("N") : $"w:{m.Hwnd:X}:{m.Id}";

    /// <summary>Iconos visibles (no ocultos por la app y con imagen), por orden de llegada.</summary>
    public IReadOnlyList<TrayIcon> Visible => _icons.Values.Where(i => !i.Hidden && i.Icon != 0).OrderBy(i => i.Order).ToList();
    public int Count => _icons.Count;
    public TrayIcon? Get(string key) => _icons.GetValueOrDefault(key);

    /// <summary>Aplica un mensaje. Devuelve true si cambió algo visible.</summary>
    public bool Apply(TrayMessage m)
    {
        string key = KeyOf(m);
        switch (m.Command)
        {
            case TrayCommand.Delete: return _icons.Remove(key);
            case TrayCommand.Add:
            case TrayCommand.Modify:
            {
                bool exists = _icons.TryGetValue(key, out var cur);
                if (!exists && m.Command == TrayCommand.Modify) return false;   // Explorer también lo rechaza: la app lo añadirá
                cur ??= new TrayIcon(key, m.Hwnd, m.Id, 0, 0, "", 0, false, _order++);
                var next = cur with
                {
                    Hwnd = m.Hwnd,
                    CallbackMessage = (m.Flags & TrayProtocol.NIF_MESSAGE) != 0 ? m.CallbackMessage : cur.CallbackMessage,
                    Icon = (m.Flags & TrayProtocol.NIF_ICON) != 0 ? m.Icon : cur.Icon,
                    Tip = (m.Flags & TrayProtocol.NIF_TIP) != 0 ? m.Tip : cur.Tip,
                    Hidden = (m.Flags & TrayProtocol.NIF_STATE) != 0 && (m.StateMask & TrayProtocol.NIS_HIDDEN) != 0 ? (m.State & TrayProtocol.NIS_HIDDEN) != 0 : cur.Hidden,
                };
                _icons[key] = next; return next != cur;
            }
            case TrayCommand.SetVersion:
            {
                if (!_icons.TryGetValue(key, out var cur)) return false;
                _icons[key] = cur with { Version = m.Version }; return false;
            }
            default: return false;
        }
    }

    /// <summary>Quita los iconos de ventanas que ya no existen (apps cerradas sin borrar su icono, como hace Explorer al pasar el ratón).</summary>
    public bool RemoveDead(Func<uint, bool> windowAlive)
    {
        var dead = _icons.Values.Where(i => !windowAlive(i.Hwnd)).Select(i => i.Key).ToList();
        foreach (var k in dead) _icons.Remove(k);
        return dead.Count > 0;
    }
}

public enum TrayButton { Left, Right, Double }

/// <summary>Qué mensajes recibe la app al pulsar su icono (lo mismo que hace Explorer). Puro.</summary>
public static class TrayClick
{
    public const uint WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;

    /// <summary>
    /// Pares (wParam, lParam) a enviar con el mensaje de aviso de la app. Versión 4 o más (NOTIFYICON_VERSION_4): wParam = punto del icono (x, y) y lParam = (evento, id),
    /// con NIN_SELECT tras el clic izquierdo y WM_CONTEXTMENU tras el derecho. Versiones anteriores: wParam = id y lParam = evento del ratón.
    /// </summary>
    public static IReadOnlyList<(nint WParam, nint LParam)> Messages(TrayIcon icon, TrayButton button, int x, int y)
    {
        uint[] events = button switch
        {
            TrayButton.Right => icon.Version >= 4 ? [WM_RBUTTONDOWN, WM_RBUTTONUP, WM_CONTEXTMENU] : [WM_RBUTTONDOWN, WM_RBUTTONUP],
            TrayButton.Double => [WM_LBUTTONDBLCLK, WM_LBUTTONUP],
            _ => icon.Version >= 4 ? [WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT] : [WM_LBUTTONDOWN, WM_LBUTTONUP],
        };
        if (icon.Version >= 4)
        {
            nint w = (nint)(uint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            return events.Select(e => (w, (nint)(uint)(((icon.Id & 0xFFFF) << 16) | (e & 0xFFFF)))).ToList();
        }

        return events.Select(e => ((nint)icon.Id, (nint)e)).ToList();
    }
}
