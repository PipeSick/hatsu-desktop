namespace WinMac.Bar;

/// <summary>Medidas en DIP. Provisionales (sin referencia de macOS todavía): ver docs/12 §D5.</summary>
public sealed record BarMetrics
{
    public double Height { get; init; } = 28;
    public double PadX { get; init; } = 12;           // margen lateral de la barra
    public double ItemPadX { get; init; } = 8;        // margen interno de cada elemento (zona de resalte)
    public double ItemGap { get; init; } = 2;
    public double GlyphTextGap { get; init; } = 5;
    public double HighlightInsetY { get; init; } = 3;
    public double HighlightRadius { get; init; } = 6;
    public double FontSize { get; init; } = 13;
}

public sealed record BarItemFrame(BarItem Item, double X, double W, double ContentMaxW);
public sealed record BarFrame(IReadOnlyList<BarItemFrame> Items, double Width, double Height);

public static class BarLayout
{
    /// <param name="textWidth">Ancho medido de un texto (lo da el render; <paramref name="bold"/> = negrita).</param>
    public static BarFrame Compute(BarMetrics m, BarConfig c, double barWidth, IReadOnlyList<BarItem> left, IReadOnlyList<BarItem> right, Func<string, bool, double> textWidth)
    {
        double Content(BarItem i, double? max = null)
        {
            double t = string.IsNullOrEmpty(i.Text) ? 0 : textWidth(i.Text, i.Bold); if (max is double mx) t = Math.Min(t, Math.Max(0, mx));
            return i.GlyphWidth + (i.GlyphWidth > 0 && t > 0 ? m.GlyphTextGap : 0) + t;
        }
        double Outer(BarItem i, double? max = null) => Content(i, max) + 2 * m.ItemPadX;

        double centerGap = Math.Clamp(c.CenterGap, 0, 0.8), sideMax = Math.Max(0, barWidth * (1 - centerGap) / 2 - m.PadX);

        // Derecha: si no cabe, se descartan elementos de menor prioridad (volumen, red, batería); el reloj nunca.
        var r = right.ToList();
        double Total(List<BarItem> l) => l.Sum(i => Outer(i)) + Math.Max(0, l.Count - 1) * m.ItemGap;
        foreach (var kind in new[] { BarItemKind.Volume, BarItemKind.Network, BarItemKind.Battery })
            if (Total(r) > sideMax) r.RemoveAll(i => i.Kind == kind);

        var frames = new List<BarItemFrame>(); double x = m.PadX;
        for (int k = 0; k < left.Count; k++)
        {
            var i = left[k]; double remaining = m.PadX + sideMax - x - 2 * m.ItemPadX - i.GlyphWidth; if (remaining < 0) remaining = 0;
            double maxText = i.Kind == BarItemKind.AppName ? remaining : double.PositiveInfinity;
            double w = Outer(i, maxText); frames.Add(new BarItemFrame(i, x, w, w - 2 * m.ItemPadX)); x += w + m.ItemGap;
        }
        double rx = barWidth - m.PadX - Math.Min(Total(r), barWidth);
        foreach (var i in r) { double w = Outer(i); frames.Add(new BarItemFrame(i, rx, w, w - 2 * m.ItemPadX)); rx += w + m.ItemGap; }
        return new BarFrame(frames, barWidth, m.Height);
    }

    /// <summary>Índice del elemento bajo X (coordenada de la barra) o -1.</summary>
    public static int HitTest(BarFrame f, double x)
    {
        for (int i = 0; i < f.Items.Count; i++) if (x >= f.Items[i].X && x <= f.Items[i].X + f.Items[i].W) return i;
        return -1;
    }
}
