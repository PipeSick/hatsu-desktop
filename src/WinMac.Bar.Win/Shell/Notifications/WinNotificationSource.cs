using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinMac.Bar.Notifications;
using WinMac.SystemControl;

namespace WinMac.Bar.Win.Shell.Notifications;

/// <summary>
/// Notificaciones de otras apps con la API pública de Windows <c>UserNotificationListener</c> (Windows.UI.Notifications.Management). Sin trucos:
/// no se lee la base de datos interna de Windows (wpndatabase.db), no se automatiza la interfaz y no se interceptan notificaciones.
/// <list type="bullet">
/// <item>Permiso: <c>GetAccessStatus</c> (sin preguntar) y <c>RequestAccessAsync</c> (Windows decide si muestra su cuadro). Denegado = solo se cambia en Configuración.</item>
/// <item>Contenido: solo las notificaciones tipo toast que siguen en el centro de Windows (lo que el usuario ya borró allí no existe para nadie), con los textos de su plantilla ToastGeneric.</item>
/// <item>Cambios: el evento <c>NotificationChanged</c>. Para apps sin paquete Windows puede no ofrecerlo (lanza al suscribirse): entonces el almacén relee solo con el panel abierto.</item>
/// <item>Acciones: <c>RemoveNotification</c> y <c>ClearNotifications</c> eliminan de verdad del centro de Windows. Activar la notificación concreta no es posible desde fuera: se abre la app.</item>
/// </list>
/// Los iconos se piden una vez por app (<c>AppDisplayInfo.GetLogo</c>), se reducen a 32×32 y se guardan SOLO en memoria.
/// </summary>
public sealed class WinNotificationSource : INotificationSource, IDisposable
{
    private UserNotificationListener? _listener; private TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs>? _handler;
    private readonly ConcurrentDictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _log;

    public WinNotificationSource(Action<string> log) { _log = log; }

    private UserNotificationListener Listener => _listener ??= UserNotificationListener.Current;

    public (NotificationAccess Access, string? Reason) GetAccess()
    {
        try
        {
            return Listener.GetAccessStatus() switch
            {
                UserNotificationListenerAccessStatus.Allowed => (NotificationAccess.Allowed, null),
                UserNotificationListenerAccessStatus.Denied => (NotificationAccess.Denied, null),
                _ => (NotificationAccess.Unspecified, null),
            };
        }
        catch (Exception e) { return (NotificationAccess.Unavailable, $"Windows no ofrece el lector de notificaciones a Hatsu Desktop ({e.GetType().Name}, 0x{e.HResult:X8})."); }
    }

    public async Task<NotificationAccess> RequestAccessAsync() => await Listener.RequestAccessAsync() switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
        _ => NotificationAccess.Unspecified,
    };

    public IReadOnlyList<AppNotification> Read()
    {
        var list = Listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().GetAwaiter().GetResult();
        var result = new List<AppNotification>(list.Count);
        foreach (var n in list)
        {
            string appId = "", appName = ""; AppInfo? app = null;
            try { app = n.AppInfo; appId = app?.AppUserModelId ?? ""; appName = app?.DisplayInfo?.DisplayName ?? ""; }
            catch (Exception) { /* algunas apps Win32 no exponen su información: queda vacía y se agrupa por lo que haya */ }

            var (title, body) = Texts(n);
            var rec = new AppNotification(n.Id, appId, appName, title, body, n.CreationTime);
            bool icon = IconFor(rec.GroupKey, app, appId, appName) is not null;
            result.Add(rec with { HasIcon = icon });
        }

        return result;
    }

    /// <summary>Título = primer texto de la plantilla ToastGeneric; cuerpo = el resto unido. Sin plantilla o sin textos, ambos quedan vacíos (no se inventa nada).</summary>
    private static (string? Title, string? Body) Texts(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return (null, null);
            var texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            return texts.Count == 0 ? (null, null) : (texts[0], texts.Count > 1 ? string.Join("\n", texts.Skip(1)) : null);
        }
        catch (Exception) { return (null, null); }
    }

    /// <summary>Píxeles BGRA premultiplicados 32×32 del icono de una app (los que se pidieron al leer), o null. Nunca bloquea: lo que no está en memoria no se pide aquí.</summary>
    public byte[]? CachedIcon(string groupKey) => _icons.TryGetValue(groupKey, out var px) ? px : null;

    /// <summary>
    /// Icono de la app: primero el logo que da Windows (<c>AppDisplayInfo.GetLogo</c>, que suele funcionar con apps empaquetadas); si no hay, el del Shell para su AUMID
    /// (<c>shell:AppsFolder\AUMID</c>, el mismo proveedor que usa el Dock), que también cubre muchas apps Win32. Medido en el PC del usuario: solo con GetLogo, 4 de 20 notificaciones tenían icono.
    /// </summary>
    private byte[]? IconFor(string key, AppInfo? app, string aumid, string name)
    {
        if (_icons.TryGetValue(key, out var hit)) return hit;
        byte[]? px = app is null ? null : LogoOf(app);
        if (px is null && !string.IsNullOrWhiteSpace(aumid))
        {
            try
            {
                var b = new WinMac.Apps.Win.ShellIconProvider().Load(new WinMac.Apps.IconRequest(new WinMac.Apps.AppIdentity(aumid, WinMac.Apps.AppKind.Win32, name, Aumid: aumid), Size: 64));
                if (b is not null) px = Downscale(b.Bgra, b.Width, b.Height, 32);
            }
            catch (Exception e) { _log("[notificaciones] sin icono del Shell para una app (" + e.GetType().Name + ")"); }
        }

        if (_icons.Count > 128) _icons.Clear();
        _icons[key] = px; return px;
    }

    private byte[]? LogoOf(AppInfo app)
    {
        byte[]? px = null;
        try
        {
            var logo = app.DisplayInfo?.GetLogo(new Size(32, 32));
            if (logo is not null)
            {
                using var stream = logo.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                var transform = new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32, InterpolationMode = BitmapInterpolationMode.Fant };
                var data = decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
                var bytes = data.DetachPixelData(); if (bytes.Length == 32 * 32 * 4) px = bytes;
            }
        }
        catch (Exception) { /* apps Win32 sin logo: se prueba el icono del Shell */ }
        return px;
    }

    /// <summary>Reduce BGRA premultiplicado a size×size promediando bloques (sin el pixelado del vecino más cercano). Una imagen no cuadrada se centra.</summary>
    internal static byte[] Downscale(byte[] src, int w, int h, int size)
    {
        if (w == size && h == size) return src;
        var dst = new byte[size * size * 4]; int side = Math.Max(w, h), ox = (side - w) / 2, oy = (side - h) / 2;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int x0 = x * side / size - ox, x1 = (x + 1) * side / size - ox, y0 = y * side / size - oy, y1 = (y + 1) * side / size - oy;
                long b = 0, g = 0, r = 0, a = 0; int n = Math.Max(1, (x1 - x0) * (y1 - y0));
                for (int sy = Math.Max(0, y0); sy < Math.Min(h, y1); sy++)
                    for (int sx = Math.Max(0, x0); sx < Math.Min(w, x1); sx++) { int i = (sy * w + sx) * 4; b += src[i]; g += src[i + 1]; r += src[i + 2]; a += src[i + 3]; }
                int o = (y * size + x) * 4; dst[o] = (byte)(b / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(r / n); dst[o + 3] = (byte)(a / n);
            }

        return dst;
    }

    public ServiceResult Remove(uint id)
    {
        try { Listener.RemoveNotification(id); return ServiceResult.Ok; }
        catch (Exception e) { return ServiceResult.Failed($"{e.GetType().Name}, 0x{e.HResult:X8}"); }
    }

    public ServiceResult ClearAll()
    {
        try { Listener.ClearNotifications(); return ServiceResult.Ok; }
        catch (Exception e) { return ServiceResult.Failed($"{e.GetType().Name}, 0x{e.HResult:X8}"); }
    }

    /// <summary>Abre la app por su AUMID en la carpeta de aplicaciones del Shell. Antes se comprueba que el Shell la conoce: si no, se dice en vez de abrir el Explorador en un sitio raro.</summary>
    public ServiceResult OpenApp(string appId)
    {
        string target = @"shell:AppsFolder\" + appId;
        var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");   // IShellItem
        int hr = SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out var item);
        if (hr < 0 || item == IntPtr.Zero) return ServiceResult.Unavailable("Windows no sabe abrir la app que envió esta notificación.");
        Marshal.Release(item);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + target + "\"") { UseShellExecute = false })?.Dispose(); return ServiceResult.Ok; }
        catch (Exception e) { return ServiceResult.Failed("No se pudo abrir la app (" + e.GetType().Name + ")"); }
    }

    public bool StartWatching(Action changed, out string? why)
    {
        StopWatching();
        try
        {
            _handler = (_, _) => changed(); Listener.NotificationChanged += _handler; why = null; return true;
        }
        catch (Exception e) { _handler = null; why = $"Windows no envía el evento de cambios a esta app ({e.GetType().Name}, 0x{e.HResult:X8})"; return false; }
    }

    public void StopWatching()
    {
        var h = _handler; _handler = null; if (h is null) return;
        try { Listener.NotificationChanged -= h; } catch (Exception) { /* la suscripción ya no existe */ }
    }

    public void Dispose() { StopWatching(); _icons.Clear(); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IntPtr ppv);
}
