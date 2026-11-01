using WinMac.Calendar;

namespace WinMac.Bar;

public enum PanelKind { ControlCenter, Wifi, Bluetooth, Sound, Battery, Calendar, Display }

public enum WidgetKind
{
    Header,      // título del panel (con «‹» si Id = "back")
    Nav,         // navegación del calendario: «‹  mes  ›»  (clics "prev" / "next")
    Tile,        // control con estado (azul = activo)
    Slider,      // deslizador 0–100
    Row,         // fila pulsable (red, salida de audio, modo…)
    Info,        // fila informativa, no pulsable
    Button,      // botón de texto
    Note,        // texto de apoyo (motivos, avisos)
    Separator,
    Month,       // cuadrícula del mes
}

/// <summary>
/// Un elemento del panel. NO hay controles decorativos: un control cuyo respaldo no existe en este equipo lleva <c>Enabled = false</c> y su <see cref="Reason"/> (se dibuja atenuado con el motivo debajo).
/// <see cref="Col"/> 0/1 = mosaico de media anchura (dos seguidos comparten fila); -1 = ancho completo.
/// </summary>
public sealed record Widget(WidgetKind Kind, string Id, string Text = "", string? Sub = null, bool On = false, bool Enabled = true, double Value = 0, int Bars = -1,
    string? Reason = null, int Col = -1, string? Glyph = null, object? Data = null);

public sealed record MonthData(IReadOnlyList<CalendarDay> Days, IReadOnlyList<string> Headers, DateOnly? Selected);

public sealed record PanelModel(PanelKind Kind, string Title, IReadOnlyList<Widget> Widgets);

public sealed record WidgetFrame(Widget W, double X, double Y, double Width, double Height);
public sealed record PanelFrame(double Width, double Height, IReadOnlyList<WidgetFrame> Items);

/// <summary>Reparto del panel (DIP). Puro: se prueba sin Windows.</summary>
public static class PanelLayout
{
    public const double Width = 320, Pad = 14, Gap = 8, MonthHeader = 20, MonthRow = 32;

    public static double HeightOf(Widget w) => w.Kind switch
    {
        WidgetKind.Header => 34,
        WidgetKind.Nav => 34,
        WidgetKind.Tile => 64,
        WidgetKind.Slider => w.Enabled || string.IsNullOrEmpty(w.Reason) ? 46 : 64,
        WidgetKind.Row => string.IsNullOrEmpty(w.Sub) ? 36 : 46,
        WidgetKind.Info => string.IsNullOrEmpty(w.Sub) ? 30 : 42,
        WidgetKind.Button => 36,
        WidgetKind.Note => Math.Max(24, 14 + 14 * Math.Ceiling(w.Text.Length / 40.0)),
        WidgetKind.Separator => 10,
        WidgetKind.Month => MonthHeader + 6 * MonthRow,
        _ => 30,
    };

    public static PanelFrame Compute(PanelModel m)
    {
        var items = new List<WidgetFrame>(); double y = Pad, inner = Width - 2 * Pad, half = (inner - Gap) / 2;
        var ws = m.Widgets;
        for (int i = 0; i < ws.Count; i++)
        {
            var w = ws[i];
            if (w.Col == 0)
            {
                var next = i + 1 < ws.Count && ws[i + 1].Col == 1 ? ws[i + 1] : null;
                double h = Math.Max(HeightOf(w), next is null ? 0 : HeightOf(next));
                items.Add(new WidgetFrame(w, Pad, y, next is null ? inner : half, h));
                if (next is not null) { items.Add(new WidgetFrame(next, Pad + half + Gap, y, half, h)); i++; }
                y += h + Gap; continue;
            }

            double hh = HeightOf(w); items.Add(new WidgetFrame(w, Pad, y, inner, hh)); y += hh + (w.Kind is WidgetKind.Separator ? 0 : Gap);
        }

        return new PanelFrame(Width, y - Gap + Pad, items);
    }

    /// <summary>Índice del elemento bajo el punto, o -1.</summary>
    public static int HitTest(PanelFrame f, double x, double y)
    {
        for (int i = 0; i < f.Items.Count; i++) { var it = f.Items[i]; if (x >= it.X && x < it.X + it.Width && y >= it.Y && y < it.Y + it.Height) return i; }
        return -1;
    }

    // Geometría del deslizador: icono a la izquierda, pista hasta el borde.
    public static (double Left, double Right) SliderTrack(WidgetFrame f) => (f.X + 40, f.X + f.Width - 12);

    public static double SliderValueAt(WidgetFrame f, double x)
    {
        var (l, r) = SliderTrack(f); return Math.Clamp((x - l) / (r - l) * 100, 0, 100);
    }

    /// <summary>Día de la cuadrícula del mes bajo el punto (índice 0–41) o -1.</summary>
    public static int MonthCellAt(WidgetFrame f, double x, double y)
    {
        double top = f.Y + MonthHeader; if (y < top || y >= top + 6 * MonthRow || x < f.X || x >= f.X + f.Width) return -1;
        int col = (int)((x - f.X) / (f.Width / 7)), row = (int)((y - top) / MonthRow); return Math.Clamp(row, 0, 5) * 7 + Math.Clamp(col, 0, 6);
    }
}
