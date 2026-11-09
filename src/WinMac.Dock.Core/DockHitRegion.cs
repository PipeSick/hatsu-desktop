namespace WinMac.Dock;

/// <summary>
/// Qué parte de la ventana del Dock existe para el ratón (región de ventana). Puro: se prueba en Linux.
/// <para>
/// La ventana del Dock es mucho más alta y ancha que la barra (deja sitio a la ampliación, la etiqueta, el salto y la vista previa). Antes, lo transparente respondía
/// HTTRANSPARENT, pero Windows solo reenvía eso a ventanas DEL MISMO HILO: en las demás apps el clic se perdía (visto por el usuario: la barra de descargas de Steam
/// no respondía, y PowerShell en la franja del Dock no tomaba el foco). Con una región de ventana, fuera de ella Windows entrega el clic a la ventana de debajo,
/// sea del proceso que sea. Como el puntero se lee por sondeo, la región no afecta a la ampliación, al borde de autoocultado ni a la vista previa.
/// </para>
/// </summary>
public static class DockHitRegion
{
    /// <param name="hovering">El puntero está sobre el Dock (se reserva el sitio de la ampliación y de la etiqueta).</param>
    /// <param name="dragging">Se arrastra un ícono: ventana completa (el ícono puede ir a cualquier sitio de ella).</param>
    /// <returns>Rectángulos en DIP relativos a la ventana. Vacío = nada (Dock oculto del todo).</returns>
    public static IReadOnlyList<PRect> Compute(DockView v, DockMetrics m, bool hovering, bool dragging)
    {
        var full = new PRect(0, 0, v.WindowWidth, v.WindowHeight);
        if (dragging) return [full];
        if (v.HideT >= 0.999) return [];

        double drop = v.HideT * m.HideDistance;   // la capa baja al ocultarse: la región la sigue
        double barBottom = v.WindowHeight - m.ShadowMargin, barTop = barBottom - m.BarHeight;
        double headroom = v.Items.Where(i => i.Model.Kind != DockItemKind.Separator).Select(i => Math.Max(0, i.Size - m.BaseSize) + i.Lift).DefaultIfEmpty(0).Max();
        double reach = hovering ? m.BaseSize * (m.MaxScale - 1) + m.BounceHeight + m.LabelHeight + m.LabelGap : headroom + m.BounceHeight;
        double top = Math.Max(0, barTop - Math.Max(headroom, reach)) + drop;
        double left = Math.Max(0, v.BarLeft - m.ShadowMargin), right = Math.Min(v.WindowWidth, v.BarLeft + v.BarWidth + m.ShadowMargin);
        var list = new List<PRect>();
        if (top < v.WindowHeight && right > left) list.Add(new PRect(left, top, right - left, v.WindowHeight - top));

        if (v.Preview is { Alpha: > 0 } p)   // panel de vista previa, su sombra y la cola hasta la barra
        {
            var panel = p.Frame.Panel.Inflate(12);
            double pl = Math.Max(0, panel.X), pr = Math.Min(v.WindowWidth, panel.Right), pt = Math.Max(0, panel.Y), pb = Math.Min(v.WindowHeight, Math.Max(panel.Bottom, barTop));
            if (pr > pl && pb > pt) list.Add(new PRect(pl, pt, pr - pl, pb - pt));
        }

        return list;
    }
}
