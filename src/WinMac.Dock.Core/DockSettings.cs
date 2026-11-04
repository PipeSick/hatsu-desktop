namespace WinMac.Dock;

public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Operaciones PURAS sobre el tamaño y la posición del Dock (devuelven una configuración nueva; no tocan nada) y el cálculo de dónde va la ventana.
/// El Dock mantiene exactamente su aspecto aprobado: el tamaño escala todas las proporciones y la posición solo mueve la ventana.
/// </summary>
public static class DockSettings
{
    public const double DefaultIconSize = 48, MinIconSize = 28, MaxIconSize = 128, DefaultEdgeMargin = 16, MaxBottomOffset = 400, NudgeStep = 8;

    /// <summary>
    /// Tamaños del menú (nombre, DIP). WM-019: en la beta, «Pequeño» (48 DIP) medía ~119 px de alto a 150 %; ahora Pequeño 36 (~89 px a 150 %), Mediano 48,
    /// Grande 64, Muy grande 80. El escalado se aplica una sola vez (DIP × escala del monitor).
    /// </summary>
    public static readonly (string Name, double Size)[] Presets = [("Pequeño", 36), ("Mediano", 48), ("Grande", 64), ("Muy grande", 80)];

    /// <summary>
    /// WM-001: la franja que el Dock reserva en la parte inferior del área de trabajo (px) para que las ventanas maximizadas queden por ENCIMA de él: la altura de
    /// la barra visible más su separación del borde. <paramref name="bandBottom"/> es el borde inferior disponible sin contar el propio Dock (encima de la barra
    /// de tareas de Windows si está visible).
    /// </summary>
    public static PxRect ReservedBand(PxRect monitor, int bandBottom, double scale, DockMetrics m, DockConfig c)
    {
        int h = (int)Math.Ceiling((m.BarHeight + 6 + ClampBottom(c.BottomOffset)) * scale);
        return new PxRect(monitor.Left, Math.Max(monitor.Top, bandBottom - h), monitor.Right, bandBottom);
    }

    /// <summary>Tamaño de la versión anterior → tamaño equivalente de esta (solo los predefinidos exactos; un valor elegido a mano se respeta).</summary>
    public static double MigrateIconSize(double old) => old switch { 48 => 36, 60 => 48, 80 => 64, 96 => 80, _ => old };

    public static double ClampIconSize(double s) => double.IsNaN(s) ? DefaultIconSize : Math.Clamp(Math.Round(s), MinIconSize, MaxIconSize);
    public static double ClampBottom(double v) => double.IsNaN(v) ? 0 : Math.Clamp(Math.Round(v), 0, MaxBottomOffset);

    public static DockConfig WithIconSize(DockConfig c, double size) { double s = ClampIconSize(size); return s == c.IconSize ? c : c with { IconSize = s }; }
    public static DockConfig WithAlignment(DockConfig c, DockAlignment a) => a == c.Alignment ? c : c with { Alignment = a };
    public static DockConfig NudgeBottom(DockConfig c, double delta) { double v = ClampBottom(c.BottomOffset + delta); return v == c.BottomOffset ? c : c with { BottomOffset = v }; }
    public static DockConfig ResetBottom(DockConfig c) => c.BottomOffset == 0 ? c : c with { BottomOffset = 0 };

    /// <summary>Configuración tal como la sanea el Dock al cargarla (un archivo editado a mano con valores absurdos no rompe nada).</summary>
    public static DockConfig Sanitize(DockConfig c) => c with { IconSize = ClampIconSize(c.IconSize), BottomOffset = ClampBottom(c.BottomOffset), EdgeMargin = double.IsNaN(c.EdgeMargin) ? DefaultEdgeMargin : Math.Clamp(c.EdgeMargin, 0, 200) };

    /// <summary>¿Cambió algo que obliga a recolocar o reescalar la ventana?</summary>
    public static (double, DockAlignment, double, double) LayoutKey(DockConfig c) => (c.IconSize, c.Alignment, c.EdgeMargin, c.BottomOffset);

    /// <summary>
    /// Esquina superior izquierda de la ventana (px). Centro y altura normal = exactamente la posición de siempre (centrada sobre la barra de tareas).
    /// Izquierda/derecha alinean la BARRA (no la ventana, más ancha por la vista previa) a <see cref="DockConfig.EdgeMargin"/> del borde. Nunca se sale por arriba.
    /// </summary>
    /// <summary>
    /// WM-033: where the hidden Dock wakes up — a thin strip (3 px) at the edge the Dock hides behind: the bottom of its window, i.e. the screen's bottom edge
    /// when Windows' taskbar is hidden, or just above the taskbar when it is shown (the monitor's last pixel would be ON the taskbar), and only across the Dock's width.
    /// </summary>
    /// <summary>
    /// Franja que despierta al Dock oculto (WM-033, WM-043): el borde inferior de su ventana Y, siempre, el borde físico inferior del monitor.
    /// WM-043: con la barra de tareas de Windows oculta por WinMac, el Dock podía quedar colocado sobre el hueco que dejaba la barra (el área de trabajo
    /// cambió y el Dock no se recolocó): su franja quedaba ~48 px por encima del borde y el cursor en el borde de la pantalla no lo despertaba nunca.
    /// Ahora, si NO hay una barra de tareas visible en ese monitor (<paramref name="screenEdgeFree"/>), el último píxel de la pantalla también vale (dentro del ancho del Dock).
    /// Con la barra de tareas visible se mantiene WM-033: su borde no despierta al Dock (estorbaría al usarla).
    /// </summary>
    public static bool EdgeHot(int x, int y, int monitorBottom, int winLeft, int winWidth, int winBottom, bool screenEdgeFree = false)
    {
        if (x < winLeft || x >= winLeft + winWidth) return false;
        int edge = Math.Min(monitorBottom, winBottom);
        return y >= edge - 3 && y <= edge + 1 || screenEdgeFree && y >= monitorBottom - 2 && y < monitorBottom + 1;
    }

    /// <summary>
    /// Alto (px) de la franja que una barra de tareas «autoocultada» deja asomar en el borde: por debajo de esto la barra NO cuenta como visible. Windows deja 1–2 px;
    /// 6 px de margen cubren el redondeo de DPI sin confundirla con una barra mostrada (la más baja mide ≥ 32 px).
    /// </summary>
    public const int TaskbarSliverPx = 6;

    /// <summary>
    /// WM-043 (corrección): borde inferior (px) sobre el que se apoya el Dock.
    /// <list type="bullet">
    /// <item>Con una barra de tareas de Explorer REALMENTE visible en ese borde (<paramref name="visibleTaskbarTop"/> = su borde superior), el Dock se apoya sobre ella: el menor de
    /// «fondo del área de trabajo» y «parte superior de la barra» (otra AppBar encima de la barra de tareas también se respeta).</item>
    /// <item>Sin barra visible (oculta por WinMac, autoocultada o en otro borde) se apoya en el <b>borde físico del monitor</b>, no en el área de trabajo: Explorer puede dejar
    /// reservada la franja de la barra aunque no se vea (la captura de la iteración 6: ~64 px de hueco bajo el Dock), y esa franja no debe separar al Dock del borde.</item>
    /// </list>
    /// Pura: no consulta Windows (el anfitrión pasa los datos), así que se prueba con cualquier DPI y monitor.
    /// </summary>
    public static int Baseline(PxRect monitor, PxRect work, int? visibleTaskbarTop)
    {
        if (visibleTaskbarTop is not { } top) return monitor.Bottom;
        return Math.Clamp(Math.Min(work.Bottom, top), monitor.Top, monitor.Bottom);
    }

    /// <summary>
    /// ¿Una ventana de barra de tareas cuenta como VISIBLE en el borde inferior de este monitor? Devuelve su borde superior, o null. Visible = ventana visible, pegada al borde
    /// inferior (± 2 px) y con más de <see cref="TaskbarSliverPx"/> px dentro del monitor (una barra autoocultada solo asoma 1–2 px).
    /// </summary>
    public static int? VisibleTaskbarTop(PxRect monitor, int left, int top, int right, int bottom, bool windowVisible)
    {
        if (!windowVisible || !(left < monitor.Right && right > monitor.Left)) return null;
        if (right - left < monitor.Width / 2 || bottom - top > monitor.Height / 2) return null;   // una barra vertical (borde izquierdo/derecho) no ocupa el borde inferior
        if (bottom < monitor.Bottom - 2 || top >= monitor.Bottom) return null;        // no está pegada al borde inferior de este monitor
        return monitor.Bottom - top > TaskbarSliverPx ? top : null;                   // solo asoma una franja (autoocultada)
    }

    /// <summary>Separación (DIP) entre la barra del Dock y el borde físico del monitor cuando NO hay barra de tareas visible debajo (como el Dock de macOS: unos pocos puntos).</summary>
    public const double FreeEdgeGapDip = 10;

    /// <summary>
    /// <paramref name="freeEdge"/>: no hay barra de tareas visible; <paramref name="work"/>.Bottom es el borde físico del monitor (<see cref="Baseline"/>) y la barra queda a
    /// <see cref="FreeEdgeGapDip"/> de él (la sombra de la ventana sobresale por debajo del monitor; no se ve). Con barra visible, la separación de siempre.
    /// </summary>
    public static (int Left, int Top) Place(PxRect monitor, PxRect work, int winW, int winH, double scale, DockMetrics m, DockConfig c, bool freeEdge = false)
    {
        int inset = (int)Math.Round(m.BarInset * scale), edge = (int)Math.Round(c.EdgeMargin * scale);
        int left = c.Alignment switch
        {
            DockAlignment.Left => monitor.Left + edge - inset,
            DockAlignment.Right => monitor.Right - edge + inset - winW,
            _ => monitor.Left + (monitor.Width - winW) / 2,
        };
        int below = freeEdge ? (int)Math.Round((m.ShadowMargin - FreeEdgeGapDip) * scale) : (int)(4 * scale);   // cuánto sobresale la ventana por debajo de la base
        int top = work.Bottom - winH + below - (int)Math.Round(ClampBottom(c.BottomOffset) * scale);   // justo sobre la barra de tareas; el margen de sombra queda parcialmente debajo
        return (left, Math.Max(monitor.Top, top));
    }
}
