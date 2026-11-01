using WinMac.Calendar;

namespace WinMac.Bar;

public enum PanelKind { ControlCenter, Wifi, Bluetooth, Sound, Battery, Calendar, Display, Hub, Weather }

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
    // ── panel multifunción y Tiempo
    Tabs,        // selector horizontal de pestañas con iconos (Data = TabsData)
    TabHeader,   // título de la pestaña en mayúsculas + icono de ajustes de esa pestaña (Id = "settings:<pestaña>")
    Caption,     // subtítulo pequeño dentro de la tarjeta («Uso del disco»)
    Chart,       // gráfica histórica (Data = ChartData)
    MeterRow,    // «> CPU  [barra]  25 %» (Value 0–1, Data = MeterData)
    KeyValue,    // «Estado ........ Verified»: Text a la izquierda, Sub a la derecha
    Stats,       // dos cifras grandes (↓ descarga / ↑ subida) con botón de unidad (Data = StatsData)
    Select,      // «Salida  [Altavoces…⌄]»: abre un menú de opciones (Data = SelectData)
    AppRow,      // aplicación con icono, nombre, volumen y silencio (Data = AppRowData)
    WeatherHead, // ciudad, temperatura grande, icono, descripción y máx/mín (Data = WeatherHeadData)
    Strip,       // pronóstico horizontal por horas (Data = StripData)
    CityRow,     // ciudad adicional: nombre a la izquierda, icono y temperatura a la derecha
}

public sealed record TabsData(IReadOnlyList<WinMac.Bar.Hub.TabItem> Tabs, int Selected);
public sealed record ChartSeries(IReadOnlyList<double> Values, string Color);
/// <param name="Max">Valor al que equivale el borde superior de la gráfica (el «techo» del eje).</param>
public sealed record ChartData(IReadOnlyList<ChartSeries> Series, double Max, string? MaxLabel);
public sealed record MeterData(string Color, string? Detail = null);
public sealed record SelectData(string Glyph, string Label, string Current);
public sealed record AppRowData(string? ExePath, string Name, int Volume, bool Muted, bool Active);
public sealed record StatItem(string Arrow, string Value, string Label, string Color);
public sealed record StatsData(StatItem Left, StatItem Right, string? Pill);
public sealed record WeatherHeadData(string Place, bool Located, string Temp, string IconKey, string Description, string? HighLow);
public sealed record StripData(IReadOnlyList<WinMac.Bar.Hub.StripItem> Items);

/// <summary>
/// Un elemento del panel. NO hay controles decorativos: un control cuyo respaldo no existe en este equipo lleva <c>Enabled = false</c> y su <see cref="Reason"/> (se dibuja atenuado con el motivo debajo).
/// <see cref="Col"/> 0/1 = mosaico de media anchura (dos seguidos comparten fila); -1 = ancho completo.
/// </summary>
public sealed record Widget(WidgetKind Kind, string Id, string Text = "", string? Sub = null, bool On = false, bool Enabled = true, double Value = 0, int Bars = -1,
    string? Reason = null, int Col = -1, string? Glyph = null, object? Data = null);

public sealed record MonthData(IReadOnlyList<CalendarDay> Days, IReadOnlyList<string> Headers, DateOnly? Selected);

/// <param name="Width">Ancho del panel en DIP (el Control Center y los demás, 320; el multifunción y el Tiempo, a la medida de sus referencias).</param>
/// <param name="Card">Rango de elementos (índices, ambos incluidos) que van dentro de una tarjeta redondeada con margen interior, como el contenido de las pestañas de referencia.</param>
public sealed record PanelModel(PanelKind Kind, string Title, IReadOnlyList<Widget> Widgets, double Width = PanelLayout.Width, (int From, int To)? Card = null);

public sealed record WidgetFrame(Widget W, double X, double Y, double Width, double Height);
public sealed record CardRect(double X, double Y, double W, double H);
public sealed record PanelFrame(double Width, double Height, IReadOnlyList<WidgetFrame> Items, CardRect? Card = null);

/// <summary>Reparto del panel (DIP). Puro: se prueba sin Windows.</summary>
public static class PanelLayout
{
    public const double Width = 320, Pad = 14, Gap = 8, MonthHeader = 20, MonthRow = 32, CardInset = 12, CardPad = 10, HubWidth = 348, WeatherWidth = 304;

    public static double HeightOf(Widget w) => w.Kind switch
    {
        WidgetKind.Header => w.Glyph == "logo" ? 62 : 34,
        WidgetKind.Nav => 34,
        WidgetKind.Tile => 64,
        WidgetKind.Slider => w.Enabled || string.IsNullOrEmpty(w.Reason) ? 46 : 64,
        WidgetKind.Row => string.IsNullOrEmpty(w.Sub) ? 36 : 46,
        WidgetKind.Info => string.IsNullOrEmpty(w.Sub) ? 30 : 42,
        WidgetKind.Button => 36,
        WidgetKind.Note => Math.Max(24, 14 + 14 * Math.Ceiling(w.Text.Length / 40.0)),
        WidgetKind.Separator => 10,
        WidgetKind.Month => MonthHeader + 6 * MonthRow,
        WidgetKind.Tabs => 58,
        WidgetKind.TabHeader => 34,
        WidgetKind.Caption => 24,
        WidgetKind.Chart => 64,
        WidgetKind.MeterRow => 26,
        WidgetKind.KeyValue => 26,
        WidgetKind.Stats => 62,
        WidgetKind.Select => 36,
        WidgetKind.AppRow => 62,
        WidgetKind.WeatherHead => 96,
        WidgetKind.Strip => 98,
        WidgetKind.CityRow => 32,
        _ => 30,
    };

    public static PanelFrame Compute(PanelModel m)
    {
        var items = new List<WidgetFrame>(); double y = Pad, inner = m.Width - 2 * Pad, half = (inner - Gap) / 2;
        var ws = m.Widgets; double? cardTop = null; double cardBottom = 0; bool cardOpen = false;
        for (int i = 0; i < ws.Count; i++)
        {
            bool inCard = m.Card is { } cd && i >= cd.From && i <= cd.To;
            if (inCard && !cardOpen) { cardOpen = true; cardTop = y; y += CardPad; }
            double x0 = inCard ? Pad + CardInset : Pad, wInner = inCard ? inner - 2 * CardInset : inner, wHalf = (wInner - Gap) / 2;
            var w = ws[i];
            if (w.Col == 0)
            {
                var next = i + 1 < ws.Count && ws[i + 1].Col == 1 ? ws[i + 1] : null;
                double h = Math.Max(HeightOf(w), next is null ? 0 : HeightOf(next));
                items.Add(new WidgetFrame(w, x0, y, next is null ? wInner : wHalf, h));
                if (next is not null) { items.Add(new WidgetFrame(next, x0 + wHalf + Gap, y, wHalf, h)); i++; }
                y += h + Gap;
            }
            else { double hh = HeightOf(w); items.Add(new WidgetFrame(w, x0, y, wInner, hh)); y += hh + (w.Kind is WidgetKind.Separator ? 0 : Gap); }
            if (cardOpen && (m.Card is { } c2 && (i >= c2.To || i == ws.Count - 1))) { cardBottom = y - Gap + CardPad; cardOpen = false; y = cardBottom + Gap; }
        }

        CardRect? card = cardTop is { } t ? new CardRect(Pad, t, inner, cardBottom - t) : null;
        return new PanelFrame(m.Width, y - Gap + Pad, items, card);
    }

    /// <summary>Índice del elemento bajo el punto, o -1.</summary>
    public static int HitTest(PanelFrame f, double x, double y)
    {
        for (int i = 0; i < f.Items.Count; i++) { var it = f.Items[i]; if (x >= it.X && x < it.X + it.Width && y >= it.Y && y < it.Y + it.Height) return i; }
        return -1;
    }

    // Geometría del deslizador: icono a la izquierda, pista hasta el borde.
    public static (double Left, double Right) SliderTrack(WidgetFrame f) => f.W.Kind == WidgetKind.AppRow ? AppSliderTrack(f) : (f.X + 40, f.X + f.Width - 12 - (string.IsNullOrEmpty(f.W.Sub) ? 0 : 46));

    // ── Geometría de las filas con sub-áreas pulsables (la usan el dibujo y el controlador: una sola fuente)
    /// <summary>Fila de aplicación: icono a la izquierda, pista del volumen bajo el nombre, porcentaje y silencio a la derecha.</summary>
    public static (double Left, double Right) AppSliderTrack(WidgetFrame f) => (f.X + 46, f.X + f.Width - 76);
    public static bool AppMuteHit(WidgetFrame f, double x, double y) => x >= f.X + f.Width - 30 && x <= f.X + f.Width && y >= f.Y + 4 && y <= f.Y + f.Height - 4;
    public static bool AppSliderHit(WidgetFrame f, double x, double y) { var (l, r) = AppSliderTrack(f); return x >= l - 8 && x <= r + 8 && y >= f.Y + f.Height - 30 && y <= f.Y + f.Height; }
    /// <summary>Deslizador con icono de silencio a la izquierda (Data = id del clic de silencio): pulsar el icono silencia, pulsar la pista arrastra.</summary>
    public static bool SliderMuteHit(WidgetFrame f, double x) => f.W.Data is string && x < f.X + 36;
    /// <summary>Botón de unidad de las estadísticas (arriba a la derecha).</summary>
    public static bool StatsPillHit(WidgetFrame f, double x, double y) => f.W.Data is StatsData { Pill: not null } && x >= f.X + f.Width - 64 && y <= f.Y + 36;
    /// <summary>Pestaña bajo el punto (0…n-1) o -1.</summary>
    public static int TabAt(WidgetFrame f, double x)
    {
        if (f.W.Data is not TabsData td || td.Tabs.Count == 0) return -1; double inset = 6, cw = (f.Width - 2 * inset) / td.Tabs.Count; int i = (int)((x - f.X - inset) / cw);
        return x < f.X + inset || i < 0 || i >= td.Tabs.Count ? -1 : i;
    }

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
