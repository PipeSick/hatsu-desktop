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
    public static (int Left, int Top) Place(PxRect monitor, PxRect work, int winW, int winH, double scale, DockMetrics m, DockConfig c)
    {
        int inset = (int)Math.Round(m.BarInset * scale), edge = (int)Math.Round(c.EdgeMargin * scale);
        int left = c.Alignment switch
        {
            DockAlignment.Left => monitor.Left + edge - inset,
            DockAlignment.Right => monitor.Right - edge + inset - winW,
            _ => monitor.Left + (monitor.Width - winW) / 2,
        };
        int top = work.Bottom - winH + (int)(4 * scale) - (int)Math.Round(ClampBottom(c.BottomOffset) * scale);   // justo sobre la barra de tareas; el margen de sombra queda parcialmente debajo
        return (left, Math.Max(monitor.Top, top));
    }
}
