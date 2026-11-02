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
        catch (Exception e) { return (NotificationAccess.Unavailable, $"Windows no ofrece el lector de notificaciones a WinMac ({e.GetType().Name}, 0x{e.HResult:X8})."); }
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
            bool icon = app is not null && IconFor(rec.GroupKey, app) is not null;
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

    private byte[]? IconFor(string key, AppInfo app)
    {
        if (_icons.TryGetValue(key, out var hit)) return hit;
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
        catch (Exception e) { _log("[notificaciones] sin icono para una app (" + e.GetType().Name + ")"); }
        if (_icons.Count > 128) _icons.Clear();
        _icons[key] = px; return px;
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
