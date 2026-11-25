namespace WinMac.Host.SettingsV2;

/// <summary>Reparto de la ventana: barra lateral y contenido. Puro; el ancho de la barra se adapta y, en ventanas estrechas, se reduce a iconos.</summary>
public static class SettingsShell
{
    public const double MinWindowW = 900, MinWindowH = 600, SidebarMin = 220, SidebarMax = 300, SidebarCompact = 76, TitleBarH = 40, ContentPad = 28, ContentMax = 1280;

    /// <summary>Ancho de la barra lateral para una ventana de ancho <paramref name="windowW"/> (DIP).</summary>
    public static double SidebarWidth(double windowW) => windowW < 760 ? SidebarCompact : Math.Clamp(windowW * 0.22, SidebarMin, SidebarMax);

    public sealed record Frame(Rect Sidebar, Rect Content, bool CompactSidebar, double ContentInnerW, double ContentInnerX);

    public static Frame Compute(double windowW, double windowH)
    {
        double w = Math.Max(1, windowW), h = Math.Max(1, windowH);
        bool compact = w < 760;
        double side = compact ? SidebarCompact : Math.Clamp(w * 0.22, SidebarMin, SidebarMax);
        double cw = w - side, innerW = Math.Max(1, Math.Min(ContentMax, cw - 2 * ContentPad));
        double innerX = side + Math.Max(ContentPad, (cw - innerW) / 2);
        return new Frame(new Rect(0, TitleBarH, side, Math.Max(0, h - TitleBarH)), new Rect(side, TitleBarH, cw, Math.Max(0, h - TitleBarH)), compact, innerW, innerX);
    }
}
