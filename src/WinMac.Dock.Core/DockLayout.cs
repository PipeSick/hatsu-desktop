namespace WinMac.Dock;

public readonly record struct ItemFrame(int Index, double X, double Size, double Scale);

/// <summary>Resultado de layout. X es relativa al borde izquierdo de la barra.</summary>
public sealed record DockFrame(ItemFrame[] Items, double BarWidth);

public static class DockLayout
{
    /// <summary>Centros de cada ítem en reposo (X relativa al borde izquierdo de la barra).</summary>
    public static double[] RestCenters(DockMetrics m, IReadOnlyList<DockItemModel> items)
    {
        var c = new double[items.Count]; double x = m.PadX;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == DockItemKind.Separator) { double line = x - m.Gap + m.SeparatorLeft; c[i] = line + m.SeparatorLine / 2; x = line + m.SeparatorLine + m.SeparatorRight; }
            else { c[i] = x + m.BaseSize / 2; x += m.BaseSize + m.Gap; }
        }
        return c;
    }

    public static double RestWidth(DockMetrics m, IReadOnlyList<DockItemModel> items) => Compute(m, items, null).BarWidth;

    /// <summary>Escala objetivo (1..MaxScale) de cada ítem para un cursor en coordenadas de reposo; null = sin cursor.</summary>
    public static double[] TargetScales(DockMetrics m, IReadOnlyList<DockItemModel> items, double? pointerRestX)
    {
        var s = new double[items.Count]; Array.Fill(s, 1.0);
        if (pointerRestX is not double p) return s;
        var centers = RestCenters(m, items);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == DockItemKind.Separator) continue;
            double d = Math.Abs(centers[i] - p);
            s[i] = d >= m.Radius ? 1 : 1 + (m.MaxScale - 1) * 0.5 * (1 + Math.Cos(Math.PI * d / m.Radius));
        }
        return s;
    }

    /// <summary>Layout con las escalas actuales (típicamente las animadas). Los íconos nunca se solapan: la barra crece.</summary>
    public static DockFrame Compute(DockMetrics m, IReadOnlyList<DockItemModel> items, IReadOnlyList<double>? scales)
    {
        var f = new ItemFrame[items.Count]; double x = m.PadX; double last = x;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == DockItemKind.Separator) { double line = x - m.Gap + m.SeparatorLeft; f[i] = new ItemFrame(i, line, 0, 1); x = line + m.SeparatorLine + m.SeparatorRight; last = x; continue; } // X = borde izquierdo de la línea
            double sc = scales is null ? 1 : Math.Max(0, scales[i]);
            double size = m.BaseSize * sc;
            f[i] = new ItemFrame(i, x, size, sc);
            x += size + m.Gap; last = x - m.Gap;
        }
        return new DockFrame(f, items.Count == 0 ? 2 * m.PadX : last + m.PadX);
    }

    /// <summary>Ancho máximo de barra en el peor caso (cursor sobre cada posición posible) para dimensionar la ventana.</summary>
    public static double MaxBarWidth(DockMetrics m, IReadOnlyList<DockItemModel> items)
    {
        double rest = RestWidth(m, items), worst = rest;
        for (double p = 0; p <= rest; p += 4)
            worst = Math.Max(worst, Compute(m, items, TargetScales(m, items, p)).BarWidth);
        return worst;
    }

    public static double WindowWidth(DockMetrics m, IReadOnlyList<DockItemModel> items) => MaxBarWidth(m, items) + 2 * m.ShadowMargin + 8;

    /// <summary>Índice del ítem bajo X (coordenada relativa a la barra) o -1. Ignora separadores.</summary>
    public static int HitTestX(DockFrame frame, IReadOnlyList<DockItemModel> items, double x)
    {
        for (int i = 0; i < frame.Items.Length; i++)
        {
            if (items[i].Kind == DockItemKind.Separator) continue;
            var it = frame.Items[i];
            if (x >= it.X - 3 && x <= it.X + it.Size + 3) return i;
        }
        return -1;
    }
}
