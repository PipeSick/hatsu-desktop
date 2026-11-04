namespace WinMac.Dock;

/// <summary>Resultado de <see cref="ChoiceMenuLayout.Compute"/> (todo en píxeles de pantalla).</summary>
/// <param name="Rect">Ventana del menú.</param>
/// <param name="OpensUp">Se abre por encima del ancla (no cabía debajo).</param>
/// <param name="VisibleRows">Filas visibles; menos que el total = el menú se desplaza.</param>
/// <param name="RowHeightPx">Alto de cada fila.</param>
/// <param name="PadPx">Margen interior arriba y abajo.</param>
public readonly record struct ChoiceMenuPlacement(PxRect Rect, bool OpensUp, int VisibleRows, int RowHeightPx, int PadPx);

/// <summary>
/// WM-035 (iteración 6): colocación PURA del desplegable de selección (dispositivos de audio, salida por aplicación). Un menú nativo de Windows es blanco, se abre donde esté el
/// puntero y recorta los nombres largos; este se coloca contra el control que lo abrió, es tan ancho como hace falta para ver los nombres COMPLETOS (hasta el límite de la
/// pantalla), nunca sale del área de trabajo de su monitor y, si no cabe debajo, se abre hacia arriba; si no cabe en ninguno, se desplaza.
/// </summary>
public static class ChoiceMenuLayout
{
    public const double RowDip = 32, PadDip = 6, EdgeDip = 8, CheckColumnDip = 30, RightPadDip = 16, GapDip = 4, MinWidthDip = 220;

    /// <param name="itemTextDip">Ancho de cada nombre (DIP), medido por quien dibuja.</param>
    /// <param name="anchor">El control que abrió el menú (px de pantalla).</param>
    /// <param name="work">Área de trabajo del monitor del ancla (px).</param>
    public static ChoiceMenuPlacement Compute(IReadOnlyList<double> itemTextDip, PxRect anchor, PxRect work, double scale)
    {
        int n = Math.Max(1, itemTextDip.Count); int row = (int)Math.Ceiling(RowDip * scale), pad = (int)Math.Ceiling(PadDip * scale), edge = (int)Math.Ceiling(EdgeDip * scale), gap = (int)Math.Ceiling(GapDip * scale);

        // Ancho: el nombre más largo completo + marca de elegido + margen derecho; como mínimo, el del control que lo abrió; como máximo, la pantalla menos márgenes.
        double textMax = itemTextDip.Count == 0 ? 0 : itemTextDip.Max();
        int want = (int)Math.Ceiling((textMax + CheckColumnDip + RightPadDip) * scale), min = (int)Math.Ceiling(MinWidthDip * scale);
        int maxW = Math.Max(1, work.Width - 2 * edge);
        int w = Math.Min(maxW, Math.Max(Math.Max(want, anchor.Width), Math.Min(min, maxW)));

        // Horizontal: alineado al borde izquierdo del ancla; si se sale por la derecha, alineado al derecho; siempre dentro del área de trabajo.
        int x = anchor.Left; if (x + w > work.Right - edge) x = anchor.Right - w;
        x = Math.Clamp(x, work.Left + edge, Math.Max(work.Left + edge, work.Right - edge - w));

        // Vertical: debajo del ancla si cabe todo; si no, encima; si en ninguno, donde haya más sitio con las filas que quepan (y desplazamiento).
        int full = n * row + 2 * pad; int below = work.Bottom - edge - (anchor.Bottom + gap), above = anchor.Top - gap - (work.Top + edge);
        bool up; int h;
        if (full <= below) { up = false; h = full; }
        else if (full <= above) { up = true; h = full; }
        else
        {
            up = above > below; int room = Math.Max(row + 2 * pad, up ? above : below);
            int rows = Math.Max(1, (room - 2 * pad) / row); rows = Math.Min(rows, n); h = rows * row + 2 * pad;
        }

        int y = up ? anchor.Top - gap - h : anchor.Bottom + gap;
        y = Math.Clamp(y, work.Top + edge, Math.Max(work.Top + edge, work.Bottom - edge - h));
        return new ChoiceMenuPlacement(new PxRect(x, y, x + w, y + h), up, (h - 2 * pad) / row, row, pad);
    }
}

/// <summary>
/// Colores del desplegable (RGB 0–1) y su contraste (WCAG). Puro, para comprobar que el texto se lee en el tema oscuro y en el claro, y sobre la fila resaltada.
/// </summary>
public static class ChoiceMenuPalette
{
    public static readonly (double R, double G, double B) DarkBackground = (0.14, 0.14, 0.16), LightBackground = (0.96, 0.96, 0.97), Accent = (0.04, 0.52, 1.0);
    public static readonly (double R, double G, double B) DarkText = (1, 1, 1), LightText = (0.08, 0.08, 0.10), HotText = (1, 1, 1);
    /// <summary>Opacidad con que se pinta el texto normal en el tema oscuro (se compone sobre el fondo).</summary>
    public const double DarkTextAlpha = 0.94;

    public static double Luminance((double R, double G, double B) c)
    {
        static double Lin(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        double la = Luminance(a), lb = Luminance(b); if (la < lb) (la, lb) = (lb, la); return (la + 0.05) / (lb + 0.05);
    }

    public static (double R, double G, double B) Over((double R, double G, double B) fg, double alpha, (double R, double G, double B) bg) =>
        (fg.R * alpha + bg.R * (1 - alpha), fg.G * alpha + bg.G * (1 - alpha), fg.B * alpha + bg.B * (1 - alpha));
}
