namespace WinMac.Bar.Capture;

/// <summary>Editor tools (docs/26): crop, pen, arrow, rectangle, text and highlighter.</summary>
public enum EditTool { Crop, Pen, Arrow, Rectangle, Text, Highlight }

/// <summary>A colour as BGRA bytes-friendly floats (0..1).</summary>
public readonly record struct EditColor(float R, float G, float B)
{
    public static readonly EditColor Red = new(1f, 0.23f, 0.19f), Yellow = new(1f, 0.84f, 0.04f), Blue = new(0.04f, 0.52f, 1f), Green = new(0.2f, 0.78f, 0.35f), White = new(1, 1, 1), Black = new(0, 0, 0);
    public static readonly EditColor[] Palette = [Red, Yellow, Blue, Green, White, Black];
}

/// <summary>One annotation in IMAGE pixel coordinates (independent of zoom and of the window's DPI).</summary>
public abstract record Annotation(EditColor Color, float Width);
public sealed record PenStroke(EditColor Color, float Width, IReadOnlyList<(float X, float Y)> Points) : Annotation(Color, Width);
public sealed record ArrowShape(EditColor Color, float Width, float X1, float Y1, float X2, float Y2) : Annotation(Color, Width);
public sealed record RectShape(EditColor Color, float Width, float X1, float Y1, float X2, float Y2) : Annotation(Color, Width);
/// <summary>Highlighter: a translucent filled rectangle (like a marker over text).</summary>
public sealed record HighlightShape(EditColor Color, float X1, float Y1, float X2, float Y2) : Annotation(Color, 0);
public sealed record TextShape(EditColor Color, float Size, float X, float Y, string Text) : Annotation(Color, Size);

/// <summary>
/// The editor document: the original image is never modified; annotations and the crop are a list of steps with undo/redo. Exporting renders them over a copy.
/// Pure and tested on Linux; the window (WinMac.Bar.Win) only draws it.
/// </summary>
public sealed class EditDocument
{
    private readonly List<object> _done = [], _undone = [];

    public EditDocument(int width, int height) { Width = width; Height = height; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The current crop (image px), or the whole image.</summary>
    public CaptureRect Crop => _done.OfType<CaptureRect>().LastOrDefault() is { IsEmpty: false } c ? c : new CaptureRect(0, 0, Width, Height);
    public IReadOnlyList<Annotation> Annotations => _done.OfType<Annotation>().ToList();
    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;
    /// <summary>True once anything was drawn or cropped (the window asks before closing with unsaved changes).</summary>
    public bool IsDirty => _done.Count > 0;

    public void Add(Annotation a)
    {
        if (a is TextShape { Text: var t } && string.IsNullOrWhiteSpace(t)) return;
        if (a is PenStroke { Points.Count: < 2 }) return;
        _done.Add(a); _undone.Clear();
    }

    /// <summary>Crops to <paramref name="r"/> (clipped to the image; tiny rectangles are ignored). Undoable like any other step.</summary>
    public bool SetCrop(CaptureRect r)
    {
        var c = r.Intersect(new CaptureRect(0, 0, Width, Height));
        if (c.W < CaptureGeometry.MinDrag || c.H < CaptureGeometry.MinDrag) return false;
        _done.Add(c); _undone.Clear(); return true;
    }

    public bool Undo() { if (_done.Count == 0) return false; _undone.Add(_done[^1]); _done.RemoveAt(_done.Count - 1); return true; }
    public bool Redo() { if (_undone.Count == 0) return false; _done.Add(_undone[^1]); _undone.RemoveAt(_undone.Count - 1); return true; }

    /// <summary>Arrow head: the two side points of a head of length <paramref name="len"/> at (x2,y2).</summary>
    public static ((float X, float Y) A, (float X, float Y) B) ArrowHead(float x1, float y1, float x2, float y2, float len)
    {
        double ang = Math.Atan2(y2 - y1, x2 - x1), spread = Math.PI / 7;
        return (((float)(x2 - len * Math.Cos(ang - spread)), (float)(y2 - len * Math.Sin(ang - spread))), ((float)(x2 - len * Math.Cos(ang + spread)), (float)(y2 - len * Math.Sin(ang + spread))));
    }

    /// <summary>Default stroke width for an image: readable both on a small region and on a 4K screenshot.</summary>
    public static float DefaultWidth(int w, int h) => Math.Clamp(Math.Max(w, h) / 320f, 3f, 12f);

    /// <summary>«Captura … (editada).png» next to the original; never overwrites (adds « 2», « 3»…).</summary>
    public static string EditedPath(string original, Func<string, bool> exists)
    {
        string dir = Path.GetDirectoryName(original) ?? "", stem = Path.GetFileNameWithoutExtension(original), ext = Path.GetExtension(original);
        if (ext.Length == 0) ext = ".png";
        for (int i = 1; ; i++)
        {
            var p = Path.Combine(dir, stem + (i == 1 ? " (editada)" : $" (editada {i})") + ext);
            if (!exists(p)) return p;
        }
    }
}

/// <summary>Fit an image into a view and map points between them (the editor shows the cropped image scaled to fit).</summary>
public readonly record struct EditView(float OffsetX, float OffsetY, float Scale)
{
    public static EditView Fit(CaptureRect crop, float viewX, float viewY, float viewW, float viewH)
    {
        float s = Math.Min(1f, Math.Min(viewW / Math.Max(1, crop.W), viewH / Math.Max(1, crop.H)));
        return new EditView(viewX + (viewW - crop.W * s) / 2 - crop.X * s, viewY + (viewH - crop.H * s) / 2 - crop.Y * s, s);
    }

    public (float X, float Y) ToImage(float vx, float vy) => ((vx - OffsetX) / Scale, (vy - OffsetY) / Scale);
    public (float X, float Y) ToView(float ix, float iy) => (ix * Scale + OffsetX, iy * Scale + OffsetY);
}
