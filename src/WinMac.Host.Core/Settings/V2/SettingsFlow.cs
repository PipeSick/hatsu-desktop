namespace WinMac.Host.SettingsV2;

public enum TextWeight { Regular, Semibold }
public readonly record struct TextStyle(string Key, double Size, TextWeight Weight = TextWeight.Regular);
public readonly record struct Size(double W, double H);
public readonly record struct Thickness(double L, double T, double R, double B)
{
    public Thickness(double all) : this(all, all, all, all) { }
    public double Horizontal => L + R; public double Vertical => T + B;
}

public readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W; public double Bottom => Y + H;
    public bool Intersects(Rect o) => X < o.Right - 0.01 && o.X < Right - 0.01 && Y < o.Bottom - 0.01 && o.Y < Bottom - 0.01;
    public bool Contains(Rect o) => o.X >= X - 0.01 && o.Y >= Y - 0.01 && o.Right <= Right + 0.01 && o.Bottom <= Bottom + 0.01;
}

/// <summary>Mide texto con ajuste de línea. En Windows lo implementa DirectWrite (el mismo motor que dibuja); las pruebas usan un estimador. Así la altura de una fila nunca depende de una suposición.</summary>
public interface ITextMeasurer
{
    /// <summary>Tamaño que ocupa <paramref name="text"/> repartido en líneas de como mucho <paramref name="maxWidth"/> (una palabra más larga puede exceder).</summary>
    Size Measure(string text, TextStyle style, double maxWidth);
    double LineHeight(TextStyle style);
}

/// <summary>Una caja ya colocada: <see cref="Node"/> dice qué es (el pintor decide cómo dibujarla) y <see cref="Rect"/> dónde, en coordenadas de la página.</summary>
public sealed record Placed(FlowNode Node, Rect Rect);

public sealed record FlowResult(IReadOnlyList<Placed> Boxes, double Height);

public sealed class FlowContext(ITextMeasurer measurer, List<Placed> sink)
{
    public ITextMeasurer Text { get; } = measurer;
    public void Add(FlowNode n, Rect r) => sink.Add(new Placed(n, r));
}

/// <summary>
/// Layout fluido de Ajustes V2: sin coordenadas absolutas. Cada nodo se MIDE con el ancho disponible y después se COLOCA; los hermanos se apilan o se reparten en columnas según el ancho,
/// de modo que un control nunca se dibuja sobre un texto (si no cabe a su lado, pasa debajo).
/// </summary>
public abstract record FlowNode
{
    /// <summary>Identificador que el pintor usa para saber qué dibujar (id de fila, «title», «card»…).</summary>
    public string? Tag { get; init; }
    public abstract Size Measure(FlowContext c, double width);
    public abstract void Place(FlowContext c, Rect r);

    public static FlowResult Layout(FlowNode root, double width, ITextMeasurer measurer)
    {
        var boxes = new List<Placed>(); var ctx = new FlowContext(measurer, boxes);
        var size = root.Measure(ctx, width);
        root.Place(ctx, new Rect(0, 0, width, size.H));
        return new FlowResult(boxes, size.H);
    }
}

public sealed record TextNode(string Text, TextStyle Style, int MaxLines = 0) : FlowNode
{
    public override Size Measure(FlowContext c, double width)
    {
        var s = c.Text.Measure(Text, Style, Math.Max(1, width)); double lh = c.Text.LineHeight(Style);
        double h = MaxLines > 0 ? Math.Min(s.H, lh * MaxLines) : s.H;
        return new Size(Math.Min(width, s.W), Math.Max(lh, h));
    }

    public override void Place(FlowContext c, Rect r) => c.Add(this, r);
}

/// <summary>Caja de tamaño fijo (un interruptor, un icono, una miniatura).</summary>
public sealed record FixedNode(double W, double H) : FlowNode
{
    public override Size Measure(FlowContext c, double width) => new(W, H);
    public override void Place(FlowContext c, Rect r) => c.Add(this, new Rect(r.X, r.Y, W, H));
}

public sealed record StackNode(IReadOnlyList<FlowNode> Children, double Spacing = 0) : FlowNode
{
    public override Size Measure(FlowContext c, double width)
    {
        double h = 0; foreach (var ch in Children) h += ch.Measure(c, width).H;
        return new Size(width, h + Spacing * Math.Max(0, Children.Count - 1));
    }

    public override void Place(FlowContext c, Rect r)
    {
        double y = r.Y;
        foreach (var ch in Children) { double h = ch.Measure(c, r.W).H; ch.Place(c, new Rect(r.X, y, r.W, h)); y += h + Spacing; }
    }
}

/// <summary>Superficie con relleno: se dibuja su fondo y dentro va el hijo. Altura = contenido + relleno.</summary>
public sealed record BoxNode(FlowNode Child, Thickness Pad) : FlowNode
{
    public override Size Measure(FlowContext c, double width) => new(width, Child.Measure(c, Math.Max(1, width - Pad.Horizontal)).H + Pad.Vertical);
    public override void Place(FlowContext c, Rect r)
    {
        c.Add(this, r);
        double iw = Math.Max(1, r.W - Pad.Horizontal), ih = Child.Measure(c, iw).H;
        Child.Place(c, new Rect(r.X + Pad.L, r.Y + Pad.T, iw, ih));
    }
}

/// <summary>
/// Texto a la izquierda y un control a la derecha. Si el ancho no alcanza para <paramref name="StartMin"/> + el control, el control PASA DEBAJO (alineado a la derecha).
/// Es la regla estructural que impide que un campo se dibuje encima de su descripción.
/// </summary>
public sealed record SplitNode(FlowNode Start, FlowNode End, double EndWidth, double EndHeight, double Gap = 16, double StartMin = 180) : FlowNode
{
    private bool SideBySide(double width) => width >= StartMin + Gap + EndWidth;

    public override Size Measure(FlowContext c, double width)
    {
        if (SideBySide(width)) return new Size(width, Math.Max(Start.Measure(c, width - Gap - EndWidth).H, EndHeight));
        return new Size(width, Start.Measure(c, width).H + Gap / 2 + EndHeight);
    }

    public override void Place(FlowContext c, Rect r)
    {
        if (SideBySide(r.W))
        {
            double sw = r.W - Gap - EndWidth, sh = Start.Measure(c, sw).H, h = Math.Max(sh, EndHeight);
            Start.Place(c, new Rect(r.X, r.Y + (h - sh) / 2, sw, sh));
            End.Place(c, new Rect(r.Right - EndWidth, r.Y + (h - EndHeight) / 2, EndWidth, EndHeight));
        }
        else
        {
            double sh = Start.Measure(c, r.W).H;
            Start.Place(c, new Rect(r.X, r.Y, r.W, sh));
            double w = Math.Min(EndWidth, r.W);
            End.Place(c, new Rect(r.Right - w, r.Y + sh + Gap / 2, w, EndHeight));
        }
    }
}

/// <summary>Cuadrícula responsive: tantas columnas como quepan con <paramref name="MinItemW"/> (entre 1 y <paramref name="MaxCols"/>); cada fila ocupa el alto de su elemento más alto.</summary>
public sealed record WrapNode(IReadOnlyList<FlowNode> Items, double MinItemW, double Gap = 12, int MaxCols = int.MaxValue) : FlowNode
{
    public int Columns(double width) => Items.Count == 0 ? 1 : Math.Clamp((int)Math.Floor((width + Gap) / (MinItemW + Gap)), 1, Math.Min(MaxCols, Items.Count));

    public override Size Measure(FlowContext c, double width)
    {
        int cols = Columns(width); double iw = (width - Gap * (cols - 1)) / cols, h = 0;
        for (int i = 0; i < Items.Count; i += cols) { double rowH = 0; for (int k = i; k < Math.Min(i + cols, Items.Count); k++) rowH = Math.Max(rowH, Items[k].Measure(c, iw).H); h += rowH + (i > 0 ? Gap : 0); }
        return new Size(width, h);
    }

    public override void Place(FlowContext c, Rect r)
    {
        int cols = Columns(r.W); double iw = (r.W - Gap * (cols - 1)) / cols, y = r.Y;
        for (int i = 0; i < Items.Count; i += cols)
        {
            double rowH = 0; for (int k = i; k < Math.Min(i + cols, Items.Count); k++) rowH = Math.Max(rowH, Items[k].Measure(c, iw).H);
            for (int k = i; k < Math.Min(i + cols, Items.Count); k++) Items[k].Place(c, new Rect(r.X + (k - i) * (iw + Gap), y, iw, rowH));
            y += rowH + Gap;
        }
    }
}
