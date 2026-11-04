using WinMac.Calendar;

namespace WinMac.Bar;

public enum PanelKind { ControlCenter, Wifi, Bluetooth, Sound, Battery, Calendar, Display, Hub, Weather, Notifications, AppleMenu, Tray, AppMenu, Phone }

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
    // ── Centro de Notificaciones
    NcHeader,    // día y fecha grandes + botón de cerrar (Data = NcHeaderData; clic en la «×» = "nc:close")
    NcSection,   // título de sección con una acción opcional a la derecha («Borrar todo», «Editar»; Data = NcSectionData)
    NotifGroup,  // cabecera del grupo de una app: icono, nombre, cantidad, «Borrar» y desplegar/plegar (Data = NotifGroupData)
    NotifCard,   // una notificación: icono, app, hora, título, texto y «×» (Data = NotifCardData)
    NcEmpty,     // estado vacío o de permiso: icono grande, título y explicación
    // ── menú de WinMac (el de la manzana)
    MenuItem,    // opción de menú: texto, ✓ si On, atenuada si no está disponible; resalte azul al pasar el ratón
    UserHeader,  // WM-009: avatar (iniciales), nombre, sesión y botón de apagado a la derecha (Data = UserHeaderData)
    BatteryHero, // WM-027: porcentaje grande, estado, barra de nivel y potencia (Value = %, On = cargando; Data = BatteryHeroData)
    StatCards,   // WM-027: tarjetas compactas en fila (Data = StatCardsData)
    ModeCard,    // WM-027: opción de modo de energía: nombre, descripción, selección (On), área de clic de toda la tarjeta
    LinkRow,     // WM-022: pie con enlaces de texto en una fila (Ajustes de WinMac · Acerca de · Forzar salida; Data = LinkRowData)
    ActionStrip, // WM-009: fila de botones redondos con icono y texto (Suspender, Reiniciar…; Data = ActionStripData)
    PhoneHeader, // It7: cabecera del panel del iPhone: icono, nombre, estado con punto de color y ajustes discretos (Data = PhoneHeaderData; clic en el engranaje)
    PhoneBattery,// It7: tarjeta de batería del iPhone: % destacado, barra, última lectura y «carga» solo si se sabe (Data = PhoneBatteryData)
    PhoneNotifs, // It7: tarjeta de notificaciones del iPhone: estado de la integración y cuántas se han recibido (Data = PhoneNotifData)
    TrayGrid,    // cuadrícula de iconos de las apps en segundo plano, como el desplegable de la bandeja de Windows (Data = TrayGridData); clic = abrir, clic derecho = su menú
}

/// <param name="AccountId">Event of the «Cuenta» button on the right (Windows account settings).</param>
public sealed record UserHeaderData(string Initials, string AccountId, string? PictureKey = null);
public sealed record BatteryHeroData(string? Detail);
/// <param name="Tone">«good» (conectado), «warn» (conectando), «off» (sin conexión).</param>
public sealed record PhoneHeaderData(string Tone, string SettingsId);
/// <param name="Big">«82 %» o «—» (sin lectura válida).</param><param name="Caption">Estado de la lectura («Conectado», «Última lectura: 82 % · hace 20 min»).</param>
/// <param name="Updated">«Actualizada hace 1 min» o null.</param><param name="Charge">Solo si el iPhone lo indica (por Bluetooth no lo hace): «Cargando» / «Sin cargar»; null = no se muestra.</param>
public sealed record PhoneBatteryData(string Big, string Caption, string? Updated, string? Charge, double? Fraction);
/// <param name="Status">Estado de la integración (Activas, Desactivadas…).</param><param name="Detail">Lo recibido de verdad («Recibidas ahora: 3») o qué falta.</param><param name="Tone">«good», «warn» u «off».</param>
public sealed record PhoneNotifData(string Status, string Detail, string Tone);
/// <param name="Tone">«good», «warn», «bad» or null (neutral).</param>
public sealed record StatCard(string Label, string Value, string? Tone);
public sealed record StatCardsData(IReadOnlyList<StatCard> Cards);
public sealed record LinkItem(string Id, string Label, bool Enabled);
public sealed record LinkRowData(IReadOnlyList<LinkItem> Links);
public sealed record StripAction(string Id, string Glyph, string Label);
public sealed record ActionStripData(IReadOnlyList<StripAction> Actions);
public sealed record TabsData(IReadOnlyList<WinMac.Bar.Hub.TabItem> Tabs, int Selected);
public sealed record ChartSeries(IReadOnlyList<double> Values, string Color);
/// <param name="Max">Valor al que equivale el borde superior de la gráfica (el «techo» del eje).</param>
public sealed record ChartData(IReadOnlyList<ChartSeries> Series, double Max, string? MaxLabel);
public sealed record MeterData(string Color, string? Detail = null);
public sealed record SelectData(string Glyph, string Label, string Current);
/// <param name="Compact">WM-035: fila del mezclador del menú Sonido, en TRES líneas: (1) icono · nombre · % · silencio, (2) deslizador con todo el ancho, (3) selector de salida (solo con la función experimental).</param>
/// <param name="Output">WM-035: salida de audio de esta app (selector), solo si la función experimental está activa y comprobada. Null = sin selector.</param>
/// <param name="SubRow">Otra sesión de la misma app: va debajo de la primera, sin repetir icono ni nombre (agrupada a la vista, cada sesión con su propio volumen).</param>
public sealed record AppRowData(string? ExePath, string Name, int Volume, bool Muted, bool Active, bool Compact = false, string? Output = null, bool SubRow = false);
public sealed record StatItem(string Arrow, string Value, string Label, string Color);
public sealed record StatsData(StatItem Left, StatItem Right, string? Pill);
public sealed record WeatherHeadData(string Place, bool Located, string Temp, string IconKey, string Description, string? HighLow);
public sealed record StripData(IReadOnlyList<WinMac.Bar.Hub.StripItem> Items);
public sealed record NcHeaderData(string Day, string Date);
/// <summary>Una app en segundo plano de la cuadrícula: su clave en el almacén de la bandeja, la clave de su icono y su nombre (texto de ayuda de la app).</summary>
public sealed record TrayCellData(string Key, string IconKey, string Name);
public sealed record TrayGridData(IReadOnlyList<TrayCellData> Cells, int Columns);
/// <param name="ActionId">Evento al pulsar la acción de la derecha (null = sin acción).</param>
public sealed record NcSectionData(string? ActionId, string? ActionLabel);
/// <param name="IconKey">Clave del icono de la app para el dibujo (null = sin icono: se dibuja la inicial).</param>
/// <param name="ClearLabel">Texto del botón del grupo («Borrar» elimina en Windows, «Ocultar» solo en WinMac); null = sin botón.</param>
public sealed record NotifGroupData(string GroupKey, string? IconKey, string AppName, int Count, bool Expanded, string? ClearLabel, string? Origin = null);
/// <param name="Secondary">Segunda línea (el cuerpo cuando hay título); null = una sola línea.</param>
/// <param name="CanDismiss">Muestra la «×» (Eliminar u Ocultar según el ajuste).</param>
/// <param name="Stacked">Cuántas notificaciones más de la misma app hay debajo (grupo plegado): se dibujan como tarjetas apiladas.</param>
/// <param name="ShowApp">Muestra el nombre de la app en la tarjeta (las tarjetas de un grupo desplegado no lo repiten).</param>
/// <param name="Origin">null = Windows; «iphone» = WinMac Connect (sus acciones solo ocultan en WinMac y no abren nada).</param>
public sealed record NotifCardData(uint NotificationId, string GroupKey, string? IconKey, string AppName, string Primary, string? Secondary, string When, bool CanDismiss, int Stacked, bool ShowApp, string? Origin = null);

/// <summary>
/// Un elemento del panel. NO hay controles decorativos: un control cuyo respaldo no existe en este equipo lleva <c>Enabled = false</c> y su <see cref="Reason"/> (se dibuja atenuado con el motivo debajo).
/// <see cref="Col"/> 0/1 = mosaico de media anchura (dos seguidos comparten fila); -1 = ancho completo.
/// </summary>
public sealed record Widget(WidgetKind Kind, string Id, string Text = "", string? Sub = null, bool On = false, bool Enabled = true, double Value = 0, int Bars = -1,
    string? Reason = null, int Col = -1, string? Glyph = null, object? Data = null);

public sealed record MonthData(IReadOnlyList<CalendarDay> Days, IReadOnlyList<string> Headers, DateOnly? Selected);

/// <param name="Width">Ancho del panel en DIP (el Control Center y los demás, 320; el multifunción y el Tiempo, a la medida de sus referencias).</param>
/// <param name="Card">Rango de elementos (índices, ambos incluidos) que van dentro de una tarjeta redondeada con margen interior, como el contenido de las pestañas de referencia.</param>
/// <param name="PinTop">Cuántos elementos del principio (logotipo, pestañas, título) quedan fijos arriba cuando el contenido se desplaza.</param>
/// <param name="PinBottom">Cuántos elementos del final (Ajustes y Salir) quedan fijos abajo cuando el contenido se desplaza.</param>
/// <param name="FillHeight">El panel ocupa siempre la altura máxima (panel lateral): si el contenido es más corto, el pie fijo baja al borde inferior.</param>
/// <param name="Pad">Margen interior (DIP); null = el de siempre (14). Los menús usan uno pequeño.</param>
/// <param name="Spacing">Separación entre elementos (DIP); null = la de siempre (8). Los menús van seguidos.</param>
public sealed record PanelModel(PanelKind Kind, string Title, IReadOnlyList<Widget> Widgets, double Width = PanelLayout.Width, (int From, int To)? Card = null, int PinTop = 0, int PinBottom = 0, bool FillHeight = false,
    double? Pad = null, double? Spacing = null, double? MaxHeight = null);

public sealed record WidgetFrame(Widget W, double X, double Y, double Width, double Height);
public sealed record CardRect(double X, double Y, double W, double H);
/// <summary>Zona desplazable de un panel más alto que la pantalla: los elementos [From, To) de <see cref="PanelFrame.Items"/> se ven solo entre Top y Bottom.</summary>
public sealed record ScrollView(int From, int To, double Top, double Bottom, double ContentHeight, double Offset, double MaxOffset);
public sealed record PanelFrame(double Width, double Height, IReadOnlyList<WidgetFrame> Items, CardRect? Card = null, ScrollView? View = null);

/// <summary>Reparto del panel (DIP). Puro: se prueba sin Windows.</summary>
public static class PanelLayout
{
    public const double Width = 320, Pad = 14, Gap = 8, MonthHeader = 20, MonthRow = 32, CardInset = 12, CardPad = 10, HubWidth = 348, WeatherWidth = 304;

    public static double HeightOf(Widget w) => w.Kind switch
    {
        WidgetKind.Header => w.Glyph == "logo" ? 56 : 34,
        WidgetKind.Nav => 34,
        WidgetKind.Tile => w.Id.StartsWith("temp-", StringComparison.Ordinal) ? 46 : w.Id.StartsWith("disk:", StringComparison.Ordinal) ? 40 : 64,
        WidgetKind.Slider => w.Enabled || string.IsNullOrEmpty(w.Reason) ? (w.Text.Length == 0 ? 30 : 46) : 64,
        WidgetKind.Row => string.IsNullOrEmpty(w.Sub) ? 36 : 46,
        WidgetKind.Info => string.IsNullOrEmpty(w.Sub) ? 30 : 42,
        WidgetKind.Button => w.Id.StartsWith("hub:", StringComparison.Ordinal) ? 32 : 34,
        WidgetKind.Note => Math.Max(24, 14 + 14 * Math.Ceiling(w.Text.Length / 40.0)),
        WidgetKind.Separator => 8,
        WidgetKind.Month => MonthHeader + 6 * MonthRow,
        WidgetKind.Tabs => 46,
        WidgetKind.TabHeader => 28,
        WidgetKind.Caption => 22,
        WidgetKind.Chart => 50,
        WidgetKind.MeterRow => 24,
        WidgetKind.KeyValue => 22,
        WidgetKind.Stats => 54,
        WidgetKind.Select => 32,
        WidgetKind.AppRow => w.Data is AppRowData { Compact: true } cad ? MixerRowHeight(cad) : 54,
        WidgetKind.WeatherHead => 96,
        WidgetKind.Strip => 92,
        WidgetKind.CityRow => 30,
        WidgetKind.NcHeader => 58,
        WidgetKind.NcSection => 26,
        WidgetKind.NotifGroup => 30,
        WidgetKind.NotifCard => NotifCardHeight(w.Data as NotifCardData),
        WidgetKind.MenuItem => 24,
        WidgetKind.UserHeader => 62,
        WidgetKind.ActionStrip => 80,
        WidgetKind.LinkRow => 30,
        WidgetKind.BatteryHero => 96,
        WidgetKind.PhoneHeader => 66,
        WidgetKind.PhoneBattery => 124,
        WidgetKind.PhoneNotifs => 82,
        WidgetKind.StatCards => 62,
        WidgetKind.ModeCard => 54,
        WidgetKind.TrayGrid => w.Data is TrayGridData g ? Math.Ceiling(g.Cells.Count / (double)Math.Max(1, g.Columns)) * TrayCell : TrayCell,
        WidgetKind.NcEmpty => Math.Max(118, 76 + 14 * Math.Ceiling((w.Sub?.Length ?? 0) / 44.0)),
        _ => 30,
    };

    /// <summary>Alto de una tarjeta: fila de la app (si se muestra), título, hasta 3 líneas de texto y el borde de las tarjetas apiladas.</summary>
    public static double NotifCardHeight(NotifCardData? d)
    {
        if (d is null) return 60;
        double lines = d.Secondary is null ? 0 : Math.Clamp(Math.Ceiling(d.Secondary.Length / 42.0), 1, 3);
        return 12 + (d.ShowApp ? 18 : 0) + 19 + lines * 16 + 10 + (d.Stacked > 0 ? 8 : 0);
    }

    /// <summary>Separación entre elementos dentro de la tarjeta del contenido (más apretada que fuera: como las filas de las referencias).</summary>
    public const double CardGap = 2, ScrollBarGutter = 6;

    /// <summary>
    /// Reparte el panel. Si <paramref name="maxHeight"/> es menor que la altura natural y el modelo tiene elementos fijos (<see cref="PanelModel.PinTop"/>/<see cref="PanelModel.PinBottom"/>),
    /// la altura se limita y SOLO el centro se desplaza (<paramref name="scroll"/> en DIP): el logotipo, las pestañas y los botones Ajustes/Salir siguen siempre accesibles.
    /// </summary>
    public static PanelFrame Compute(PanelModel m, double maxHeight = double.PositiveInfinity, double scroll = 0)
    {
        if (m.MaxHeight is { } cap) maxHeight = Math.Min(maxHeight, cap);   // WM-035: el menú Sonido tiene un alto máximo propio; el centro se desplaza
        double Pad = m.Pad ?? PanelLayout.Pad, Gap = m.Spacing ?? PanelLayout.Gap;   // márgenes del modelo (los menús los quieren pequeños)
        var items = new List<WidgetFrame>(); double y = Pad, inner = m.Width - 2 * Pad, half = (inner - Gap) / 2;
        var ws = m.Widgets; double? cardTop = null; double cardBottom = 0; bool cardOpen = false;
        int scrollFrom = -1, scrollTo = -1; double topEnd = 0, bottomStart = 0; int bottomIdx = ws.Count - m.PinBottom;
        for (int i = 0; i < ws.Count; i++)
        {
            if (m.PinTop > 0 && i == m.PinTop) { scrollFrom = items.Count; topEnd = y; }
            if (m.PinBottom > 0 && i == bottomIdx) { scrollTo = items.Count; bottomStart = y; }
            bool inCard = m.Card is { } cd && i >= cd.From && i <= cd.To; double gap = inCard ? CardGap : Gap;
            if (inCard && !cardOpen) { cardOpen = true; cardTop = y; y += CardPad; }
            double x0 = inCard ? Pad + CardInset : Pad, wInner = inCard ? inner - 2 * CardInset : inner, wHalf = (wInner - Gap) / 2;
            var w = ws[i];
            if (w.Col == 0)
            {
                var next = i + 1 < ws.Count && ws[i + 1].Col == 1 ? ws[i + 1] : null;
                double h = Math.Max(HeightOf(w), next is null ? 0 : HeightOf(next));
                items.Add(new WidgetFrame(w, x0, y, next is null ? wInner : wHalf, h));
                if (next is not null) { items.Add(new WidgetFrame(next, x0 + wHalf + Gap, y, wHalf, h)); i++; }
                y += h + gap;
            }
            else { double hh = HeightOf(w); items.Add(new WidgetFrame(w, x0, y, wInner, hh)); y += hh + (w.Kind is WidgetKind.Separator ? 0 : gap); }
            if (cardOpen && (m.Card is { } c2 && (i >= c2.To || i == ws.Count - 1))) { cardBottom = y - gap + CardPad; cardOpen = false; y = cardBottom + Gap; }
        }

        CardRect? card = cardTop is { } t ? new CardRect(Pad, t, inner, cardBottom - t) : null;
        double natural = y - Gap + Pad;
        if (m.FillHeight && double.IsFinite(maxHeight) && natural < maxHeight)   // panel lateral más alto que su contenido: el pie baja al borde, el resto queda arriba
        {
            double extra = maxHeight - natural; int from = scrollTo >= 0 ? scrollTo : items.Count;
            var filled = items.Select((it, idx) => idx >= from ? it with { Y = it.Y + extra } : it).ToList();
            return new PanelFrame(m.Width, maxHeight, filled, card);
        }

        if (natural <= maxHeight || scrollFrom < 0 || scrollTo < 0 || scrollTo <= scrollFrom) return new PanelFrame(m.Width, natural, items, card);

        // Más alto que la pantalla: el centro se desplaza entre las zonas fijas.
        double bottomH = natural - bottomStart, viewBottom = Math.Max(topEnd + 40, maxHeight - bottomH), viewH = viewBottom - topEnd, content = bottomStart - topEnd;
        double maxOff = Math.Max(0, content - viewH), off = Math.Clamp(scroll, 0, maxOff), toBottom = viewBottom - bottomStart + 0;
        var moved = new List<WidgetFrame>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            moved.Add(i >= scrollTo ? it with { Y = it.Y + toBottom } : i >= scrollFrom ? it with { Y = it.Y - off } : it);
        }

        return new PanelFrame(m.Width, viewBottom + bottomH, moved, card is null ? null : card with { Y = card.Y - off }, new ScrollView(scrollFrom, scrollTo, topEnd, viewBottom, content, off, maxOff));
    }

    /// <summary>Índice del elemento bajo el punto, o -1. Un elemento desplazado fuera de la zona visible no se puede pulsar.</summary>
    public static int HitTest(PanelFrame f, double x, double y)
    {
        for (int i = 0; i < f.Items.Count; i++)
        {
            var it = f.Items[i];
            if (f.View is { } v && i >= v.From && i < v.To && (y < v.Top || y >= v.Bottom)) continue;
            if (x >= it.X && x < it.X + it.Width && y >= it.Y && y < it.Y + it.Height) return i;
        }

        return -1;
    }

    // Geometría del deslizador: icono a la izquierda, pista hasta el borde.
    public static (double Left, double Right) SliderTrack(WidgetFrame f) => f.W.Kind == WidgetKind.AppRow ? AppSliderTrack(f) : (f.X + 40, f.X + f.Width - 12 - (string.IsNullOrEmpty(f.W.Sub) ? 0 : 46));

    // ── Geometría de las filas con sub-áreas pulsables (la usan el dibujo y el controlador: una sola fuente)
    /// <summary>Fila de aplicación: icono a la izquierda, pista del volumen bajo el nombre, porcentaje y silencio a la derecha.</summary>
    // WM-035 (corrección de la iteración 6): fila del mezclador en tres líneas, con aire. Una sola fuente para el dibujo y las pruebas de clic.
    //   línea 1 (cabecera): icono · nombre (todo el ancho que sobre) · porcentaje · silencio
    //   línea 2: deslizador de volumen, del nombre al borde derecho
    //   línea 3 (solo con la salida por aplicación activada): selector de salida con el nombre completo del dispositivo
    public const double MixPadTop = 8, MixPadBottom = 8, MixHeader = 30, MixSubHeader = 26, MixGap = 4, MixSlider = 28, MixOut = 32, MixIndent = 40, MixPctW = 58, MixMuteW = 34, MixRight = 4;

    /// <summary>Alto de una fila del mezclador (DIP): 78 sin selector de salida, 114 con él (24 menos para una sesión secundaria sin icono).</summary>
    public static double MixerRowHeight(AppRowData d) => MixPadTop + (d.SubRow ? MixSubHeader : MixHeader) + MixGap + MixSlider + (d.Output is null ? 0 : MixGap + MixOut) + MixPadBottom;

    private static bool Compact(WidgetFrame f) => f.W.Data is AppRowData { Compact: true };
    private static AppRowData? CompactData(WidgetFrame f) => f.W.Data as AppRowData is { Compact: true } d ? d : null;
    public readonly record struct Box(double X, double Y, double W, double H);

    /// <summary>Línea 1 de la fila: icono, nombre, porcentaje y silencio caben aquí.</summary>
    public static Box AppHeader(WidgetFrame f) => new(f.X, f.Y + MixPadTop, f.Width, CompactData(f)?.SubRow == true ? MixSubHeader : MixHeader);
    /// <summary>El nombre: desde el icono hasta el porcentaje (todo el espacio libre).</summary>
    public static Box AppName(WidgetFrame f) { var h = AppHeader(f); return new(f.X + MixIndent, h.Y, f.Width - MixIndent - MixPctW - MixMuteW - MixRight - 6, h.H); }
    public static Box AppPct(WidgetFrame f) { var h = AppHeader(f); return new(f.X + f.Width - MixMuteW - MixRight - MixPctW, h.Y, MixPctW, h.H); }
    /// <summary>Botón de silencio (se pinta el altavoz centrado; el área pulsable es todo este recuadro).</summary>
    public static Box AppMute(WidgetFrame f) { var h = AppHeader(f); return new(f.X + f.Width - MixMuteW - MixRight, h.Y, MixMuteW, h.H); }
    /// <summary>Línea 2: el deslizador.</summary>
    public static Box AppSliderRow(WidgetFrame f) { var h = AppHeader(f); return new(f.X, h.Y + h.H + MixGap, f.Width, MixSlider); }
    /// <summary>Línea 3: el selector de salida (alto 0 si la fila no lo tiene).</summary>
    public static Box AppOutput(WidgetFrame f) { var s = AppSliderRow(f); return CompactData(f)?.Output is null ? new(f.X, s.Y + s.H, 0, 0) : new(f.X + MixIndent, s.Y + s.H + MixGap, f.Width - MixIndent - MixRight, MixOut); }

    public static (double Left, double Right) AppSliderTrack(WidgetFrame f) => Compact(f)
        ? (f.X + MixIndent, f.X + f.Width - MixRight - 10)
        : (f.X + 46, f.X + f.Width - 86);
    public static bool AppMuteHit(WidgetFrame f, double x, double y)
    {
        if (!Compact(f)) return x >= f.X + f.Width - 30 && x <= f.X + f.Width && y >= f.Y + 4 && y <= f.Y + f.Height - 4;
        var m = AppMute(f); return x >= m.X - 6 && x <= m.X + m.W + 6 && y >= m.Y - 2 && y <= m.Y + m.H + 2;
    }
    /// <summary>Selector de salida de la app (solo filas del mezclador con <see cref="AppRowData.Output"/>).</summary>
    public static bool AppOutputHit(WidgetFrame f, double x, double y) { var o = AppOutput(f); return o.W > 0 && x >= o.X && x <= o.X + o.W && y >= o.Y && y <= o.Y + o.H; }
    public static bool AppSliderHit(WidgetFrame f, double x, double y)
    {
        var (l, r) = AppSliderTrack(f);
        if (!Compact(f)) return x >= l - 8 && x <= r + 8 && y >= f.Y + f.Height - 30 && y <= f.Y + f.Height;
        var row = AppSliderRow(f); return x >= l - 10 && x <= r + 10 && y >= row.Y - 2 && y <= row.Y + row.H + 2;
    }
    /// <summary>Altura (Y) del centro de la pista del volumen de la fila del mezclador.</summary>
    public static double AppSliderCenterY(WidgetFrame f) { var row = AppSliderRow(f); return row.Y + row.H / 2; }

    /// <summary>El «botón» del selector (píldora con el dispositivo actual): a la derecha de su rótulo. En paneles anchos el rótulo es una columna fija; en los estrechos, el 40 %.</summary>
    public static Box SelectPill(WidgetFrame f) { double lab = SelectLabelWidth(f.Width); return new(f.X + lab, f.Y + 3, f.Width - lab, f.Height - 6); }
    public static double SelectLabelWidth(double width) => width >= 420 ? 92 : width * 0.40;

    /// <summary>Ajusta el ancho de un panel a la pantalla (DIP del monitor): nunca más ancho que ella menos un margen, ni más estrecho que <paramref name="min"/> (si cabe).</summary>
    public static PanelModel FitWidth(PanelModel m, double monitorWidthDip, double margin = 16, double min = 300)
    {
        double w = Math.Min(m.Width, Math.Max(min, monitorWidthDip - 2 * margin)); w = Math.Min(w, Math.Max(200, monitorWidthDip - margin));
        return w < m.Width ? m with { Width = w } : m;
    }
    /// <summary>Deslizador con icono de silencio a la izquierda (Data = id del clic de silencio): pulsar el icono silencia, pulsar la pista arrastra.</summary>
    public static bool SliderMuteHit(WidgetFrame f, double x) => f.W.Data is string && x < f.X + 36;
    /// <summary>Botón de unidad de las estadísticas (arriba a la derecha).</summary>
    public static bool StatsPillHit(WidgetFrame f, double x, double y) => f.W.Data is StatsData { Pill: not null } && x >= f.X + f.Width - 46 && y <= f.Y + 30;
    /// <summary>Lado de cada casilla de la cuadrícula de apps en segundo plano (DIP): icono de 16 con margen, como el desplegable de Windows.</summary>
    public const double TrayCell = 42;

    /// <summary>Casilla de la cuadrícula bajo el punto, o -1.</summary>
    public static int TrayCellAt(WidgetFrame f, double x, double y)
    {
        if (f.W.Data is not TrayGridData g || x < f.X || y < f.Y) return -1;
        int col = (int)((x - f.X) / TrayCell), row = (int)((y - f.Y) / TrayCell), i = row * g.Columns + col;
        return col < g.Columns && i < g.Cells.Count ? i : -1;
    }

    // ── Centro de Notificaciones
    /// <summary>«×» de cerrar del encabezado (arriba a la derecha).</summary>
    /// <summary>WM-022: the «Cuenta» button at the right of the user header.</summary>
    public static (double X, double Y, double W, double H) UserAccountRect(WidgetFrame f) => (f.X + f.Width - 76, f.Y + (f.Height - 28) / 2, 76, 28);
    public static (double X, double Y, double W, double H) PhoneGearRect(WidgetFrame f) => (f.X + f.Width - 36, f.Y + (f.Height - 30) / 2, 30, 30);
    public static bool PhoneGearHit(WidgetFrame f, double x) => x >= PhoneGearRect(f).X;
    public static bool UserPowerHit(WidgetFrame f, double x) => x >= UserAccountRect(f).X;

    /// <summary>WM-022: which link of a LinkRow is under x (equal widths; -1 = none).</summary>
    public static int LinkAt(WidgetFrame f, double x)
    {
        if (f.W.Data is not LinkRowData d || d.Links.Count == 0) return -1;
        int k = (int)Math.Floor((x - f.X) / (f.Width / d.Links.Count)); return k >= 0 && k < d.Links.Count && d.Links[k].Enabled ? k : -1;
    }

    /// <summary>WM-009: which of the evenly spaced round buttons of an action strip is under x (-1 = none).</summary>
    public static int StripActionAt(WidgetFrame f, double x)
    {
        if (f.W.Data is not ActionStripData d || d.Actions.Count == 0) return -1;
        int k = (int)Math.Floor((x - f.X) / (f.Width / d.Actions.Count)); return k >= 0 && k < d.Actions.Count ? k : -1;
    }

    public static bool NcCloseHit(WidgetFrame f, double x, double y) => x >= f.X + f.Width - 36 && y <= f.Y + 36;
    /// <summary>Acción de la derecha de una sección («Borrar todo»).</summary>
    public static bool NcSectionActionHit(WidgetFrame f, double x) => f.W.Data is NcSectionData { ActionId: not null } && x >= f.X + f.Width - 96;
    /// <summary>Botón «Borrar»/«Ocultar» de la cabecera de un grupo.</summary>
    public static bool NotifGroupClearHit(WidgetFrame f, double x) => f.W.Data is NotifGroupData { ClearLabel: not null } && x >= f.X + f.Width - 78;
    /// <summary>«×» de una tarjeta (esquina superior derecha).</summary>
    public static bool NotifDismissHit(WidgetFrame f, double x, double y) => f.W.Data is NotifCardData { CanDismiss: true } && x >= f.X + f.Width - 32 && y <= f.Y + 30;

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
