namespace WinMac.Host;

public sealed record RowFrame(SettingsRow Row, double X, double Y, double W, double H, bool First, bool Last);
public sealed record GroupFrame(SettingsGroup Group, double X, double Y, double W, double H, double HeaderY, double FooterY, double FooterH);
/// <param name="ContentHeight">Alto total del contenido de la página (sin desplazar).</param>
public sealed record PageFrame(IReadOnlyList<GroupFrame> Groups, IReadOnlyList<RowFrame> Rows, double ContentHeight, double Scroll, double MaxScroll);

/// <summary>Reparto de la ventana de Ajustes (DIP, estilo «lista agrupada» de iOS). Coordenadas LOCALES al área de contenido (origen = esquina del área a la derecha de la barra lateral, bajo el título). Puro.</summary>
public static class SettingsLayout
{
    public const double WinW = 860, WinH = 580, SidebarW = 240, TitleH = 76, Pad = 28, GroupGap = 22, HeaderH = 30, Radius = 12;
    public const double SwitchW = 51, SwitchH = 31, ControlH = 32, ArrowW = 30;
    public static double ContentW => WinW - SidebarW;
    public static double ViewportH => WinH - TitleH;
    public static double ColW => ContentW - 2 * Pad;

    /// <summary>Ancho reservado a la derecha para el control de la fila (una sola fuente: el dibujo y el cálculo de alturas).</summary>
    public static double ControlSpace(SettingsRow r) => r.Kind switch { RowKind.Toggle => r.CanUp || r.CanDown ? 51 + 16 + 76 : 51 + 24, RowKind.Segmented => SegmentWidth(r, ColW) + 16, RowKind.Field => 220, RowKind.Slider => 300, _ => 0 };

    /// <summary>Texto secundario que se ve en la fila: el motivo si está desactivada, si no el subtítulo.</summary>
    public static string? SubText(SettingsRow r) => !r.Enabled && !string.IsNullOrEmpty(r.Reason) ? r.Reason : r.Subtitle;

    /// <summary>
    /// WM-013: los subtítulos ya no se recortan con «…»: se parten en líneas y la fila crece. Estimación conservadora del ancho medio de carácter de la letra
    /// secundaria (12 px Segoe UI ≈ 6,4 DIP) para que el cálculo puro coincida con lo que DirectWrite pinta.
    /// </summary>
    public static int Lines(string? text, double width) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length * 6.4 / Math.Max(60, width)));
    public const double SubLineH = 17;

    public static double RowHeight(SettingsRow r)
    {
        string? sub = SubText(r);
        if (r.Kind == RowKind.Info) return Math.Max(54, 16 + SubLineH * Lines(r.Title, ColW - 62));
        if (r.Kind == RowKind.Button) return 48;
        if (r.Kind == RowKind.Slider && string.IsNullOrEmpty(sub)) return 54;
        int extra = Math.Max(0, Lines(sub, ColW - 32 - ControlSpace(r)) - 1);
        double baseH = r.Kind switch
        {
            RowKind.Toggle => string.IsNullOrEmpty(sub) ? 46 : 58,
            RowKind.Segmented or RowKind.Field or RowKind.Slider => string.IsNullOrEmpty(sub) ? 52 : 62,
            _ => 46,
        };
        return baseH + extra * SubLineH;
    }

    public static double FooterHeight(string? footer) => string.IsNullOrWhiteSpace(footer) ? 0 : 8 + 17 * Math.Max(1, Math.Ceiling(footer.Length / 74.0));

    public static PageFrame Compute(SettingsPage page, double scroll = 0, double viewportH = double.NaN)
    {
        if (double.IsNaN(viewportH)) viewportH = ViewportH;
        var groups = new List<GroupFrame>(); var rows = new List<RowFrame>(); double y = 4;
        foreach (var g in page.Groups)
        {
            double headerY = y; if (!string.IsNullOrEmpty(g.Header)) y += HeaderH; else y += 2;
            double top = y, cardH = g.Rows.Sum(RowHeight);
            for (int i = 0; i < g.Rows.Count; i++) { double h = RowHeight(g.Rows[i]); rows.Add(new RowFrame(g.Rows[i], Pad, y, ColW, h, i == 0, i == g.Rows.Count - 1)); y += h; }
            double footerY = y + 2, footerH = FooterHeight(g.Footer); y += footerH == 0 ? 0 : footerH + 2;
            groups.Add(new GroupFrame(g, Pad, top, ColW, cardH, headerY, footerY, footerH)); y += GroupGap;
        }

        double content = y + 6, max = Math.Max(0, content - viewportH), off = Math.Clamp(scroll, 0, max);
        if (off != 0) { groups = groups.Select(g => g with { Y = g.Y - off, HeaderY = g.HeaderY - off, FooterY = g.FooterY - off }).ToList(); rows = rows.Select(r => r with { Y = r.Y - off }).ToList(); }
        return new PageFrame(groups, rows, content, off, max);
    }

    // ── Geometría de los controles de cada fila (una sola fuente para el dibujo y el ratón)
    public static (double X, double Y, double W, double H) SwitchRect(RowFrame f) => (f.X + f.W - 16 - SwitchW, f.Y + (f.H - SwitchH) / 2, SwitchW, SwitchH);
    public static (double X, double Y, double W, double H) DownRect(RowFrame f) { var s = SwitchRect(f); return (s.X - 10 - ArrowW, f.Y + (f.H - ControlH) / 2, ArrowW, ControlH); }
    public static (double X, double Y, double W, double H) UpRect(RowFrame f) { var d = DownRect(f); return (d.X - 4 - ArrowW, d.Y, ArrowW, ControlH); }
    /// <summary>Equal segments wide enough for the longest label (WM-013: «Ciudad elegida» was cut), at most 62 % of the row.</summary>
    public static double SegmentWidth(SettingsRow r, double rowW)
    {
        int n = Math.Max(1, r.Options?.Count ?? 1); int longest = r.Options is { Count: > 0 } o ? o.Max(t => t.Length) : 4;
        return Math.Min(rowW * 0.62, n * Math.Max(84.0, longest * 7.4 + 24));
    }

    public static (double X, double Y, double W, double H) SegmentRect(RowFrame f) { double w = SegmentWidth(f.Row, f.W); return (f.X + f.W - 16 - w, f.Y + (f.H - ControlH) / 2, w, ControlH); }
    public static (double X, double Y, double W, double H) FieldRect(RowFrame f) { double w = Math.Min(f.W * 0.56, 290); return (f.X + f.W - 16 - w, f.Y + (f.H - ControlH) / 2, w, ControlH); }
    public static (double X0, double X1, double Cy) SliderTrack(RowFrame f) => (f.X + f.W - 16 - 250, f.X + f.W - 16 - 46, f.Y + f.H / 2);
    public static double SliderValueAt(RowFrame f, double x) { var (x0, x1, _) = SliderTrack(f); return Math.Clamp((x - x0) / (x1 - x0) * 100, 0, 100); }

    // ── Barra lateral
    public const double SideItemH = 36, SideTop = 92;   // diez secciones (WM-013) caben sin desplazar la barra lateral
    public static (double X, double Y, double W, double H) SideItemRect(int i) => (12, SideTop + i * (SideItemH + 4), SidebarW - 24, SideItemH);

    // ── Lista de sugerencias bajo un campo (coordenadas locales)
    public const double SuggestRowH = 38;
    public static (double X, double Y, double W, double H) SuggestRect(RowFrame field, int count) { var f = FieldRect(field); double w = Math.Max(f.W, 300); return (f.X + f.W - w, f.Y + f.H + 4, w, count * SuggestRowH + 8); }

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
                    if (r.Row.CanUp && In(UpRect(r), x, y)) return (r, "up");
                    if (r.Row.CanDown && In(DownRect(r), x, y)) return (r, "down");
                    return (r, "switch");   // toda la fila alterna (como en iOS)
                case RowKind.Segmented:
                { var s = SegmentRect(r); if (In(s, x, y) && x >= s.X) { int n = r.Row.Options?.Count ?? 1; return (r, "seg:" + Math.Clamp((int)((x - s.X) / (s.W / n)), 0, n - 1)); } return (r, "row"); }
                case RowKind.Field: return In(FieldRect(r), x, y) ? (r, "field") : (r, "row");
                case RowKind.Slider: { var (a, b, _) = SliderTrack(r); return x >= a - 10 && x <= b + 10 ? (r, "slider") : (r, "row"); }
                case RowKind.Button: return (r, "button");
                default: return (r, "row");
            }
        }

        return null;
    }
}
