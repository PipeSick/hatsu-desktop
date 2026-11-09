using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Offscreen;

/// <summary>
/// It7 (CI): renderiza los diálogos de confirmación con el pintor real sobre un fondo sintético, en el tema indicado. Textos reales de cada acción. NO abre ventanas y NO valida
/// foco, teclado, posición ni el tema de Windows: solo la composición.
/// </summary>
public static class DialogOffscreen
{
    public static readonly string[] Cases = ["shutdown", "restart", "logoff", "forcequit", "clear-notifications", "speedtest", "notice"];

    public static string Render(string png, string kind, bool dark, double scale, int focus = 0)
    {
        var (title, text, action, danger, k) = kind switch
        {
            "shutdown" => ("¿Apagar el equipo?", "Las aplicaciones abiertas se cerrarán y podrías perder cambios sin guardar.", "Apagar", true, ConfirmKind.Power),
            "restart" => ("¿Reiniciar el equipo?", "Se reiniciará el equipo. Las apps abiertas se cerrarán.", "Reiniciar", true, ConfirmKind.Restart),
            "logoff" => ("¿Cerrar la sesión?", "Se cerrará tu sesión y las apps abiertas se cerrarán.", "Cerrar sesión", true, ConfirmKind.SignOut),
            "forcequit" => ("¿Forzar la salida de «Aplicación de muestra»?", "Se cerrará de inmediato y se perderá lo que no esté guardado.", "Forzar salida", true, ConfirmKind.ForceQuit),
            "clear-notifications" => ("¿Eliminar todas las notificaciones?", "Cuando Windows lo permita, también se eliminarán de su centro de notificaciones. No se puede deshacer.", "Eliminar notificaciones", true, ConfirmKind.Trash),
            "speedtest" => ("Prueba de velocidad", "La prueba consume datos: descarga hasta 25 MB y sube hasta 8 MB con tu conexión.", "Ejecutar la prueba", false, ConfirmKind.Info),
            _ => ("Aviso de muestra", "Esto es un aviso informativo con un solo botón.", "Aceptar", false, ConfirmKind.Info),
        };
        bool onlyOk = kind == "notice"; var theme = dark ? DockTheme.Dark : DockTheme.Light;
        using var dev = new D2DDevice(true); dev.SetDpi(scale);
        var (pw, ph) = DarkDialog.PreviewSize(title, text, onlyOk, scale, dev);
        using var under = SyntheticBackdrop.Create(dev, pw / scale, ph / scale, scale, theme, bands: false); using var target = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale);
        var c = dev.Context; c.Target = target; c.BeginDraw(); c.DrawBitmap(under, (Vortice.RawRectF?)new Vortice.RawRectF(0, 0, (float)(pw / scale), (float)(ph / scale)), 1f, Vortice.Direct2D1.BitmapInterpolationMode.Linear, (Vortice.RawRectF?)null); c.EndDraw();
        DarkDialog.Preview(dev, title, text, action, danger, k, theme, onlyOk, focus);
        var rgba = BarOffscreen.Readback(dev, target, pw, ph, scale); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!);
        File.WriteAllBytes(png, PngWriter.EncodeRgba(rgba, pw, ph)); c.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, diálogo {kind}, tema {(dark ? "oscuro" : "claro")}, escala {scale}";
    }
}
