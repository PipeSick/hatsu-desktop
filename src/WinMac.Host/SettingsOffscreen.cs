using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Host.App;

/// <summary>
/// Renderiza la ventana de Ajustes FUERA de pantalla (Direct2D sobre WARP si hace falta) con el pintor real y una configuración de muestra, para revisarla en CI o en cualquier equipo.
/// No abre ventanas ni lee el sistema: valida el dibujo y la composición, NO el ratón ni el foco.
/// </summary>
internal static class SettingsOffscreen
{
    public static int Run(string[] args)
    {
        string page = HostOptions.Arg(args, "--render-settings", "general"), png = HostOptions.Arg(args, "--out", "ajustes.png");
        bool dark = !string.Equals(HostOptions.Arg(args, "--theme", "dark"), "light", StringComparison.OrdinalIgnoreCase);
        double scale = double.TryParse(HostOptions.Arg(args, "--scale", "2"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 2;
        double scroll = double.TryParse(HostOptions.Arg(args, "--scroll", "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sy) ? sy : 0;
        try { File.WriteAllText(png + ".txt", Render(png, page, dark, scale, scroll, args.Contains("--suggest"), args.Contains("--no-notch"), args.Contains("--edit"))); return 0; }
        catch (Exception e) { try { File.WriteAllText(png + ".txt", "ERROR: " + e); } catch (IOException) { } return 1; }
    }

    public static string Render(string pngPath, string pageId, bool dark, double scale, double scroll, bool suggest, bool noNotch, bool edit = false)
    {
        var cfg = new HostConfig { Bar = new WinMac.Bar.BarConfig { WeatherCity = "Molina|-35.1167|-71.2833", WeatherFavorites = "Santiago|-33.45|-70.67;Valparaíso|-33.05|-71.62", ShowSeconds = false }, Notch = new NotchSettings { GlassLevel = 60, LyricsEnabled = true } };
        var states = noNotch ? new Dictionary<string, ModuleState> { ["notch"] = ModuleState.NotInstalled } : new Dictionary<string, ModuleState>();
        var ctl = new SettingsController(); ctl.SetPages(SettingsBuilder.Build(new SettingsInput(cfg, states, true, suggest ? "" : "Ciudad: Molina, Maule, Chile. Otras: Región de Murcia, España. Para elegir, escribe «Molina, país»"))); ctl.SelectPage(pageId);
        if (scroll > 0) ctl.Scroll(scroll);
        if (suggest) ctl.SetSuggestions("hub.city", ["Molina, Maule, Chile", "Molina de Segura, Región de Murcia, España", "Molina de Aragón, Castilla-La Mancha, España", "Molina, Cataluña, España"]);
        int pw = (int)Math.Ceiling(SettingsLayout.WinW * scale), ph = (int)Math.Ceiling(SettingsLayout.WinH * scale);
        using var dev = new D2DDevice(true); dev.SetDpi(scale); using var painter = new SettingsPainter(dev, dark);
        using var target = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale); dev.Context.Target = target;
        painter.Paint(ctl, SettingsLayout.WinW, SettingsLayout.WinH, suggest ? new EditView("hub.city", "Molina", 6, 6, true) : edit ? new EditView("hub.city", "Molina, Chile", 0, 6, true) : null);   // --suggest y --edit: el campo de ciudad con el cursor o con una selección
        using var cpu = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale, readable: true); cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read); var bgra = new byte[map.Pitch * ph];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pngPath))!); File.WriteAllBytes(pngPath, PngWriter.EncodeRgba(PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, pw, ph), pw, ph)); dev.Context.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, página {pageId}, tema {(dark ? "oscuro" : "claro")}, desplazamiento {ctl.ScrollOffset:0}/{ctl.Frame.MaxScroll:0}";
    }
}
