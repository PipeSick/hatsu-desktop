using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Rendering;

/// <summary>Quien recibe los mensajes de una ventana Win32 de la barra.</summary>
internal interface IWndTarget { IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l); }

/// <summary>
/// Una sola clase de ventana y un solo procedimiento de ventana para TODAS las barras (una por monitor): reparte cada mensaje a su dueño por el HWND. Con un delegado por instancia,
/// la segunda ventana de la misma clase usaría el procedimiento de la primera. El delegado es estático para que el recolector no lo libere mientras Windows lo tiene.
/// </summary>
internal static class WndRouter
{
    private static readonly Dictionary<IntPtr, IWndTarget> Map = [];
    private static readonly HashSet<string> Classes = [];
    private static readonly object Gate = new();
    private static readonly BarNative.WndProc Proc = Dispatch;

    public static void EnsureClass(string name)
    {
        lock (Gate)
        {
            if (!Classes.Add(name)) return;
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = Proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = name, hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
        }
    }

    public static void Register(IntPtr hwnd, IWndTarget target) { lock (Gate) Map[hwnd] = target; }
    public static void Unregister(IntPtr hwnd) { lock (Gate) Map.Remove(hwnd); }

    private static IntPtr Dispatch(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        IWndTarget? t; lock (Gate) Map.TryGetValue(h, out t);
        if (t is null) return BarNative.DefWindowProc(h, msg, w, l);   // mensajes de creación/destrucción, antes o después de registrar el dueño
        return t.OnMessage(h, msg, w, l);
    }
}
