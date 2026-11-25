namespace WinMac.Host;

public sealed record RowFrame(SettingsRow Row, double X, double Y, double W, double H, bool First, bool Last, bool Card = false);
public sealed record GroupFrame(SettingsGroup Group, double X, double Y, double W, double H, double HeaderY, double FooterY, double FooterH);
/// <param name="ContentHeight">Alto total del contenido de la página (sin desplazar).</param>
public sealed record PageFrame(IReadOnlyList<GroupFrame> Groups, IReadOnlyList<RowFrame> Rows, double ContentHeight, double Scroll, double MaxScroll);

/// <summary>Reparto de la ventana de Ajustes (DIP, estilo «lista agrupada» de iOS). Coordenadas LOCALES al área de contenido (origen = esquina del área a la derecha de la barra lateral, bajo el título). Puro.</summary>
public static class SettingsLayout
{
    // Settings V2: la ventana es redimensionable; el tamaño del área de cliente (DIP) lo fija la ventana con <see cref="Configure"/>. Todo lo demás se deriva de él.
    [ThreadStatic] private static double _winW, _winH;   // por hilo: la ventana de Ajustes vive en su hilo y las pruebas no se pisan entre sí (0 = tamaño por defecto)
    public static void Configure(double winW, double winH)
    {
        if (winW <= 0 && winH <= 0) { _winW = 0; _winH = 0; return; }   // restablece el tamaño por defecto
        _winW = Math.Max(SettingsV2.SettingsShell.MinWindowW * 0.7, winW); _winH = Math.Max(300, winH);
    }
    public static double WinW => _winW == 0 ? 1100 : _winW;
    public static double WinH => _winH == 0 ? 720 : _winH;
    public static double SidebarW => SettingsV2.SettingsShell.SidebarWidth(WinW);
    public static bool CompactSidebar => WinW < 760;
    public const double TitleH = 100, Pad = 28, GroupGap = 22, HeaderH = 30, Radius = 16;
    public const double SwitchW = 46, SwitchH = 26, ControlH = 34, ArrowW = 30;
    public static double ContentW => WinW - SidebarW;
    public static double ViewportH => WinH - TitleH;
    public static double ColW => Math.Min(SettingsV2.SettingsShell.ContentMax, ContentW - 2 * Pad);
    /// <summary>X de la columna de contenido: centrada si la ventana es muy ancha.</summary>
    public static double ColX => Pad + Math.Max(0, (ContentW - 2 * Pad - ColW) / 2);

    /// <summary>Mide texto con el mismo motor que dibuja (la ventana lo fija con DirectWrite). Sin medidor (pruebas) se usa una estimación.</summary>
    [ThreadStatic] private static SettingsV2.ITextMeasurer? _measurer;
    public static SettingsV2.ITextMeasurer? Measurer { get => _measurer; set => _measurer = value; }

    /// <summary>Con poco ancho los controles de una fila pasan DEBAJO de su texto en vez de dibujarse encima.</summary>
    public static bool IsStacked(SettingsRow r) => r.Kind switch
    {
        RowKind.Segmented or RowKind.Field or RowKind.Slider => ColW < 640,
        RowKind.Button when r.Options is { Count: > 0 } => ColW < PillsWidth(r) + 260,
        _ => false,
    };

    /// <summary>Ancho reservado a la derecha para el control de la fila (una sola fuente: el dibujo y el cálculo de alturas).</summary>
    public static double ControlSpace(SettingsRow r) => IsStacked(r) ? 0 : r.Kind switch { RowKind.Toggle => HasGrip(r) ? 51 + 24 + GripW : r.CanUp || r.CanDown ? 51 + 16 + 76 : 51 + 24, RowKind.Button when r.Options is { Count: > 0 } => PillsWidth(r) + 16, RowKind.Segmented => SegmentWidth(r, ColW) + 16, RowKind.Field => 220, RowKind.Slider => 300, _ => 0 };

    // ── Piezas del prototipo: asa para arrastrar (⋮⋮) en los iconos de la barra y píldoras azules en las filas de acción
    public const double GripW = 30, PillH = 28, PillGap = 8;
    /// <summary>Las filas de los iconos de la barra se reordenan arrastrando su asa; las demás listas (Isla, centro de notificaciones) siguen con flechas.</summary>
    public static bool HasGrip(SettingsRow r) => r.Kind == RowKind.Toggle && r.Id.StartsWith("baritem:", StringComparison.Ordinal) && (r.CanUp || r.CanDown);
    public static double PillWidth(string text) => Math.Max(68, text.Length * 7.4 + 28);
    public static double PillsWidth(SettingsRow r) => r.Options is { Count: > 0 } o ? o.Sum(PillWidth) + PillGap * (o.Count - 1) : 0;
    /// <summary>Píldora <paramref name="i"/> (de izquierda a derecha) de una fila con opciones de acción, pegada a la derecha.</summary>
    public static (double X, double Y, double W, double H) PillRect(RowFrame f, int i)
    {
        var o = f.Row.Options!; bool st = IsStacked(f.Row); double x = st ? f.X + 16 : f.X + f.W - 16 - PillsWidth(f.Row);
        for (int k = 0; k < i; k++) x += PillWidth(o[k]) + PillGap;
        return (x, st ? f.Y + f.H - PillH - 10 : f.Y + (f.H - PillH) / 2, PillWidth(o[i]), PillH);
    }

    public const string CustomizeLabel = "Personalizar iconos…";
    /// <summary>La píldora azul dentro de la tira de vista previa de la barra.</summary>
    public static (double X, double Y, double W, double H) PreviewPill(RowFrame f) { double w = PillWidth(CustomizeLabel); return (f.X + f.W - 16 - 10 - w, f.Y + (f.H - PillH) / 2, w, PillH); }
    public static (double X, double Y, double W, double H) GripRect(RowFrame f) => (f.X + 6, f.Y, GripW, f.H);

    /// <summary>Texto secundario que se ve en la fila: el motivo si está desactivada, si no el subtítulo.</summary>
    public static string? SubText(SettingsRow r) => !r.Enabled && !string.IsNullOrEmpty(r.Reason) ? r.Reason : r.Subtitle;

    /// <summary>
    /// WM-013: los subtítulos ya no se recortan con «…»: se parten en líneas y la fila crece. Estimación conservadora del ancho medio de carácter de la letra
    /// secundaria (12 px Segoe UI ≈ 6,4 DIP) para que el cálculo puro coincida con lo que DirectWrite pinta.
    /// </summary>
    public static int Lines(string? text, double width)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (Measurer is { } m) { var st = SettingsV2.SettingsTokens.Small; return Math.Max(1, (int)Math.Round(m.Measure(text, st, Math.Max(60, width)).H / m.LineHeight(st))); }
        return Math.Max(1, (int)Math.Ceiling(text.Length * 6.4 / Math.Max(60, width)));
    }
    public const double SubLineH = 17;

    public static double RowHeight(SettingsRow r)
    {
        string? sub = SubText(r);
        if (r.Kind == RowKind.Hero) return 150;
        double stack = IsStacked(r) ? ControlH + 14 : 0;
        if (r.Kind == RowKind.Info) return Math.Max(54, 16 + SubLineH * Lines(r.Title, ColW - 62));
        if (r.Kind == RowKind.Button) return (r.Options is { Count: > 0 } ? (string.IsNullOrEmpty(sub) ? 48 : 58 + Math.Max(0, Lines(sub, ColW - 32 - ControlSpace(r)) - 1) * SubLineH) : 48) + stack;
        if (r.Kind == RowKind.Preview) return 58;
        if (r.Kind == RowKind.Slider && string.IsNullOrEmpty(sub)) return 54 + stack;
        int extra = Math.Max(0, Lines(sub, ColW - 32 - ControlSpace(r)) - 1);
        double baseH = r.Kind switch
        {
            RowKind.Toggle => string.IsNullOrEmpty(sub) ? 46 : 58,
            RowKind.Segmented or RowKind.Field or RowKind.Slider => string.IsNullOrEmpty(sub) ? 52 : 62,
            _ => 46,
        };
        return baseH + extra * SubLineH + stack;
    }

    public const double CardGap = 12, CardMinW = 290;
    public static int CardColumns(int count) => Math.Clamp((int)Math.Floor((ColW + CardGap) / (CardMinW + CardGap)), 1, Math.Max(1, Math.Min(3, count)));
    /// <summary>Alto de una tarjeta de componente: icono, título, descripción en las líneas que haga falta (medida real) e interruptor.</summary>
    public static double CardHeight(SettingsRow r, double cardW) => Math.Max(96, 20 + 22 + 4 + SubLineH * Lines(SubText(r), cardW - 16 - 48 - 14 - SwitchW - 24) + 20);

    public static double FooterHeight(string? footer) => string.IsNullOrWhiteSpace(footer) ? 0 : 8 + 17 * Math.Max(1, Math.Ceiling(footer.Length / 74.0));

    public static PageFrame Compute(SettingsPage page, double scroll = 0, double viewportH = double.NaN)
    {
        if (double.IsNaN(viewportH)) viewportH = ViewportH;
        var groups = new List<GroupFrame>(); var rows = new List<RowFrame>(); double y = 4;
        foreach (var g in page.Groups)
        {
            double headerY = y; if (!string.IsNullOrEmpty(g.Header)) y += HeaderH; else y += 2;
            double top = y, cardH;
            if (g.Cards)
            {   // cuadrícula de tarjetas: tantas columnas como quepan; cada tarjeta es la misma fila (mismo id, misma acción)
                int cols = CardColumns(g.Rows.Count); double cw = (ColW - CardGap * (cols - 1)) / cols, gy = y;
                for (int i = 0; i < g.Rows.Count; i += cols)
                {
                    double rowH = 0; for (int k = i; k < Math.Min(i + cols, g.Rows.Count); k++) rowH = Math.Max(rowH, CardHeight(g.Rows[k], cw));
                    for (int k = i; k < Math.Min(i + cols, g.Rows.Count); k++) rows.Add(new RowFrame(g.Rows[k], ColX + (k - i) * (cw + CardGap), gy, cw, rowH, false, false, Card: true));
                    gy += rowH + CardGap;
                }

                cardH = Math.Max(0, gy - CardGap - y); y = gy - CardGap;
            }
            else
            {
                cardH = g.Rows.Sum(RowHeight);
                for (int i = 0; i < g.Rows.Count; i++) { double h = RowHeight(g.Rows[i]); rows.Add(new RowFrame(g.Rows[i], ColX, y, ColW, h, i == 0, i == g.Rows.Count - 1)); y += h; }
            }

            double footerY = y + 2, footerH = FooterHeight(g.Footer); y += footerH == 0 ? 0 : footerH + 2;
            groups.Add(new GroupFrame(g, ColX, top, ColW, cardH, headerY, footerY, footerH)); y += GroupGap;
        }

        double content = y + 6, max = Math.Max(0, content - viewportH), off = Math.Clamp(scroll, 0, max);
        if (off != 0) { groups = groups.Select(g => g with { Y = g.Y - off, HeaderY = g.HeaderY - off, FooterY = g.FooterY - off }).ToList(); rows = rows.Select(r => r with { Y = r.Y - off }).ToList(); }
        return new PageFrame(groups, rows, content, off, max);
    }

    // ── Geometría de los controles de cada fila (una sola fuente para el dibujo y el ratón)
    public static (double X, double Y, double W, double H) SwitchRect(RowFrame f) => (f.X + f.W - 16 - SwitchW, f.Card ? f.Y + 20 : f.Y + (f.H - SwitchH) / 2, SwitchW, SwitchH);
    public static (double X, double Y, double W, double H) DownRect(RowFrame f) { var s = SwitchRect(f); return (s.X - 10 - ArrowW, f.Y + (f.H - ControlH) / 2, ArrowW, ControlH); }
    public static (double X, double Y, double W, double H) UpRect(RowFrame f) { var d = DownRect(f); return (d.X - 4 - ArrowW, d.Y, ArrowW, ControlH); }
    /// <summary>Equal segments wide enough for the longest label (WM-013: «Ciudad elegida» was cut), at most 62 % of the row.</summary>
    public static double SegmentWidth(SettingsRow r, double rowW)
    {
        int n = Math.Max(1, r.Options?.Count ?? 1); int longest = r.Options is { Count: > 0 } o ? o.Max(t => t.Length) : 4;
        return Math.Min(rowW * 0.62, n * Math.Max(84.0, longest * 7.4 + 24));
    }

    private static double StackedY(RowFrame f) => f.Y + f.H - ControlH - 10;
    public static (double X, double Y, double W, double H) SegmentRect(RowFrame f) { double w = SegmentWidth(f.Row, f.W); return IsStacked(f.Row) ? (f.X + 16, StackedY(f), Math.Min(w, f.W - 32), ControlH) : (f.X + f.W - 16 - w, f.Y + (f.H - ControlH) / 2, w, ControlH); }
    public static (double X, double Y, double W, double H) FieldRect(RowFrame f) { if (IsStacked(f.Row)) return (f.X + 16, StackedY(f), f.W - 32, ControlH); double w = Math.Min(f.W * 0.56, 290); return (f.X + f.W - 16 - w, f.Y + (f.H - ControlH) / 2, w, ControlH); }
    public static (double X0, double X1, double Cy) SliderTrack(RowFrame f) => IsStacked(f.Row) ? (f.X + 20, f.X + f.W - 16 - 56, StackedY(f) + ControlH / 2) : (f.X + f.W - 16 - 250, f.X + f.W - 16 - 46, f.Y + f.H / 2);
    public static double SliderValueAt(RowFrame f, double x) { var (x0, x1, _) = SliderTrack(f); return Math.Clamp((x - x0) / (x1 - x0) * 100, 0, 100); }

    // ── Barra lateral
    public const double SideItemH = 34, SideTop = 112, SideHeaderH = 22, SideHeaderGap = 8;   // doce secciones y cinco grupos caben sin desplazar la barra lateral
    /// <summary>La caja «Buscar ajuste…» bajo el título de la barra lateral.</summary>
    public static RowFrame SearchFrame(string text) { double w = SidebarW - 24, rowW = w / 0.56; return new RowFrame(new SettingsRow(RowKind.Field, "search", "Buscar ajuste", Text: text, Placeholder: "Buscar ajuste…"), 12 - rowW + 16 + w, 62, rowW, ControlH + 2, true, true); }
    public sealed record SideEntry(int PageIndex, (double X, double Y, double W, double H) Rect, string? Header, double HeaderY);

    /// <summary>Posición de cada sección en la barra lateral; el nombre del grupo se dibuja encima de la primera sección de cada grupo.</summary>
    public static IReadOnlyList<SideEntry> SideEntries(IReadOnlyList<SettingsPage> pages)
    {
        var list = new List<SideEntry>(); string? last = null; int headers = pages.Select(p => p.Section).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count();
        // si no caben con el alto de la ventana, las separaciones se estrechan (hasta un mínimo) en vez de cortar la lista
        double item = SideItemH, head = SideHeaderH, gap = SideHeaderGap, top = SideTop;
        double need() => top + pages.Count * (item + 2) + headers * (head + gap) + 8;
        if (need() > WinH) { top = 96; gap = 3; head = 18; }
        if (need() > WinH) item = 30;
        if (need() > WinH) item = 27;
        double y = top;
        for (int i = 0; i < pages.Count; i++)
        {
            string? header = null; double hy = 0;
            if (!string.IsNullOrEmpty(pages[i].Section) && pages[i].Section != last) { y += gap; hy = y; header = CompactSidebar ? null : pages[i].Section; y += CompactSidebar ? 2 : head; }
            last = pages[i].Section;
            list.Add(new SideEntry(i, (CompactSidebar ? 8 : 12, y, SidebarW - (CompactSidebar ? 16 : 24), item), header, hy)); y += item + 2;
        }

        return list;
    }

    public static (double X, double Y, double W, double H) SideItemRect(IReadOnlyList<SettingsPage> pages, int i) => SideEntries(pages)[i].Rect;

    // ── Lista de sugerencias bajo un campo (coordenadas locales)
    public const double SuggestRowH = 38;
    public static (double X, double Y, double W, double H) SuggestRect(RowFrame field, int count)
    {
        var f = FieldRect(field); double w = Math.Max(f.W, 300), h = count * SuggestRowH + 8, y = f.Y + f.H + 4;
        if (y + h > ViewportH - 6)                                  // no cabe debajo: se abre hacia arriba, y si tampoco cabe, se pega al borde visible
        {
            double up = f.Y - 4 - h; y = up >= 6 ? up : Math.Max(6, ViewportH - h - 6);
        }

        return (f.X + f.W - w, y, w, h);
    }

    /// <summary>Qué hay bajo el punto (coordenadas locales): fila y parte («switch», «seg:i», «field», «slider», «up», «down», «row»).</summary>
    public static (RowFrame Row, string Part)? Hit(PageFrame f, double x, double y)
    {
        foreach (var r in f.Rows)
        {
            if (x < r.X || x >= r.X + r.W || y < r.Y || y >= r.Y + r.H) continue;
            static bool In((double X, double Y, double W, double H) b, double px, double py) => px >= b.X && px < b.X + b.W && py >= b.Y - 4 && py < b.Y + b.H + 4;
            switch (r.Row.Kind)
            {
                case RowKind.Toggle:
                    if (r.Card) return (r, "switch");
                    if (HasGrip(r.Row)) return In(GripRect(r), x, y) ? (r, "grip") : (r, "switch");
                    if (r.Row.CanUp && In(UpRect(r), x, y)) return (r, "up");
                    if (r.Row.CanDown && In(DownRect(r), x, y)) return (r, "down");
                    return (r, "switch");   // toda la fila alterna (como en iOS)
                case RowKind.Segmented:
                { var s = SegmentRect(r); if (In(s, x, y) && x >= s.X) { int n = r.Row.Options?.Count ?? 1; return (r, "seg:" + Math.Clamp((int)((x - s.X) / (s.W / n)), 0, n - 1)); } return (r, "row"); }
                case RowKind.Field: return In(FieldRect(r), x, y) ? (r, "field") : (r, "row");
                case RowKind.Slider: { var (a, b, _) = SliderTrack(r); return x >= a - 10 && x <= b + 10 ? (r, "slider") : (r, "row"); }
                case RowKind.Button:
                    if (r.Row.Options is { Count: > 0 } o)
                    {
                        if (o.Count == 1) return (r, "pill:0");   // con una sola píldora, toda la fila actúa
                        for (int i = 0; i < o.Count; i++) if (In(PillRect(r, i), x, y)) return (r, "pill:" + i);
                        return (r, "row");
                    }

                    return (r, "button");
                case RowKind.Preview: return In(PreviewPill(r), x, y) ? (r, "customize") : (r, "row");
                default: return (r, "row");
            }
        }

        return null;
    }
}
