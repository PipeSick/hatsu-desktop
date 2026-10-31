namespace WinMac.Dock;

public readonly record struct PRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    public PRect Inflate(double d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
}

/// <summary>Una tarjeta de la vista previa: etiqueta con el título (píldora) y miniatura de la ventana.</summary>
public sealed record PreviewCardFrame(PRect Pill, PRect Thumb);

/// <summary>Geometría de la vista previa en DIP, relativa a la ventana del Dock. El pico («cola») apunta al ícono.</summary>
public sealed record PreviewFrame(PRect Panel, PRect Header, PreviewCardFrame[] Cards, double TailX, double TailBaseY, double TailTipY);

/// <summary>
/// Medidas de la vista previa de ventanas (panel de vidrio sobre el ícono: cabecera con icono y nombre, una tarjeta por ventana con su título y su miniatura).
/// Estructura tomada de la referencia aportada por el usuario (macOS Tahoe); las medidas son aproximadas y NO están calibradas.
/// </summary>
public static class PreviewSpec
{
    public const double Pad = 14, HeaderH = 28, HeaderGap = 8, PillH = 24, PillGap = 6;
    public const double ThumbMaxW = 240, ThumbMaxH = 150, ThumbMinW = 120, CardGap = 12;
    public const double Radius = 22, ThumbRadius = 10, TailW = 22, TailH = 10, AnchorGap = 8, Margin = 8, MinPanelW = 220;
    public const int MaxCards = 5;
    public const double DefaultAspect = 1.6;

    public static double PanelHeight => Pad + HeaderH + HeaderGap + PillH + PillGap + ThumbMaxH + Pad;
    /// <summary>Alto extra que necesita la ventana sobre el ícono más alto, descontando la holgura que ya reserva la etiqueta.</summary>
    public static double Headroom(DockMetrics m) => Math.Ceiling(PanelHeight + TailH + AnchorGap - m.LabelHeight - m.LabelGap);
    public static double MaxPanelWidth => 2 * Pad + MaxCards * ThumbMaxW + (MaxCards - 1) * CardGap;

    /// <param name="aspects">Proporción ancho/alto de cada ventana (≤ 0 → por defecto).</param>
    /// <param name="anchorX">Centro X del ícono; <paramref name="iconTopY"/>: borde superior del ícono (coordenadas de la ventana).</param>
    public static PreviewFrame Compute(IReadOnlyList<double> aspects, double anchorX, double iconTopY, double windowWidth, double maxPanelWidth)
    {
        int n = Math.Max(1, aspects.Count);
        var tw = new double[n]; var th = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = i < aspects.Count && aspects[i] > 0.2 && aspects[i] < 8 ? aspects[i] : DefaultAspect;
            double w = Math.Clamp(ThumbMaxH * a, ThumbMinW, ThumbMaxW), h = w / a;
            if (h > ThumbMaxH) { h = ThumbMaxH; w = h * a; }
            tw[i] = w; th[i] = h;
        }
        double content = tw.Sum() + (n - 1) * CardGap, avail = Math.Max(MinPanelW, maxPanelWidth) - 2 * Pad;
        if (content > avail) { double k = (avail - (n - 1) * CardGap) / tw.Sum(); for (int i = 0; i < n; i++) { tw[i] *= k; th[i] *= k; } content = avail; }
        double rowH = th.Max(), pw = Math.Max(MinPanelW, content + 2 * Pad), ph = Pad + HeaderH + HeaderGap + PillH + PillGap + rowH + Pad;
        double bottom = iconTopY - AnchorGap - TailH, px = Math.Clamp(anchorX - pw / 2, Margin, Math.Max(Margin, windowWidth - Margin - pw)), py = bottom - ph;
        var panel = new PRect(px, py, pw, ph);
        var header = new PRect(px + Pad, py + Pad, pw - 2 * Pad, HeaderH);
        double x = px + (pw - content) / 2, yPill = py + Pad + HeaderH + HeaderGap, yThumb = yPill + PillH + PillGap;
        var cards = new PreviewCardFrame[n];
        for (int i = 0; i < n; i++)
        {
            cards[i] = new PreviewCardFrame(new PRect(x, yPill, tw[i], PillH), new PRect(x, yThumb, tw[i], th[i]));
            x += tw[i] + CardGap;
        }
        double tail = Math.Clamp(anchorX, px + Radius + TailW / 2, px + pw - Radius - TailW / 2);
        return new PreviewFrame(panel, header, cards, tail, bottom, bottom + TailH);
    }
}
