using WinMac.Bar;
using WinMac.SystemControl;
using Xunit;

/// <summary>WM-035 (iteración 6): el mezclador es espacioso (tres líneas, panel ancho) y toda su geometría —dibujo y clics— sale de una sola fuente.</summary>
public class MixerLayoutTests
{
    private static AppRowData Row(bool output = false, bool sub = false, string name = "Microsoft Edge WebView2") => new(null, name, 70, false, true, Compact: true, Output: output ? "Altavoces (PRO X Wireless Gaming Headset)" : null, SubRow: sub);

    private static WidgetFrame Frame(AppRowData d, double width = PanelBuilder.MixerWidth - 2 * PanelLayout.Pad)
        => new(new Widget(WidgetKind.AppRow, "a", d.Name, Value: d.Volume, Data: d), PanelLayout.Pad, 100, width, PanelLayout.MixerRowHeight(d));

    private static bool Overlap(PanelLayout.Box a, PanelLayout.Box b) => a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    [Fact]
    public void The_panel_is_considerably_wider_than_the_400_dip_of_before()
    {
        Assert.True(PanelBuilder.MixerWidth >= 480); Assert.True(PanelBuilder.MixerMaxHeight >= 600);
    }

    [Fact]
    public void Rows_have_room_for_three_lines_and_grow_only_with_the_experimental_output_selector()
    {
        double plain = PanelLayout.MixerRowHeight(Row()), withOut = PanelLayout.MixerRowHeight(Row(output: true)), sub = PanelLayout.MixerRowHeight(Row(sub: true));
        Assert.Equal(78, plain); Assert.Equal(114, withOut); Assert.True(sub < plain);
        Assert.True(plain >= 2 * 34, "más del doble que la fila comprimida de antes (34 DIP)");
    }

    [Theory]
    [InlineData(520.0 - 28)] [InlineData(440.0)] [InlineData(360.0)] [InlineData(300.0)]
    public void Name_percentage_and_mute_never_overlap_and_stay_inside_the_row(double width)
    {
        var f = Frame(Row(output: true), width); var n = PanelLayout.AppName(f); var p = PanelLayout.AppPct(f); var m = PanelLayout.AppMute(f); var s = PanelLayout.AppSliderRow(f); var o = PanelLayout.AppOutput(f);
        Assert.False(Overlap(n, p)); Assert.False(Overlap(p, m)); Assert.False(Overlap(n, m));
        foreach (var b in new[] { n, p, m, s, o }) { Assert.True(b.X >= f.X - 0.01 && b.X + b.W <= f.X + f.Width + 0.01); Assert.True(b.Y >= f.Y - 0.01 && b.Y + b.H <= f.Y + f.Height + 0.01); }
        Assert.False(Overlap(s, o)); Assert.True(n.W >= 100, $"el nombre tiene {n.W} DIP");
    }

    [Fact]
    public void At_the_default_width_names_get_a_wide_column_and_the_slider_the_full_row()
    {
        var f = Frame(Row(output: true)); var n = PanelLayout.AppName(f); var (l, r) = PanelLayout.AppSliderTrack(f); var o = PanelLayout.AppOutput(f);
        Assert.True(n.W >= 300, $"nombre: {n.W}");                     // «Microsoft Edge WebView2» cabe sobrado
        Assert.True(r - l >= 0.85 * f.Width, $"deslizador: {r - l}");   // el deslizador ya no comparte línea con nada
        Assert.True(o.W - 88 >= 270, $"nombre del dispositivo: {o.W - 88}");   // «Altavoces (PRO X Wireless Gaming Headset)» (~270 DIP) entero
    }

    [Fact]
    public void Slider_drag_starts_only_on_the_slider_line_and_mute_or_output_do_not_drag()
    {
        var d = Row(output: true); var f = Frame(d); var (l, r) = PanelLayout.AppSliderTrack(f); double sy = PanelLayout.AppSliderCenterY(f);
        Assert.True(PanelLayout.AppSliderHit(f, (l + r) / 2, sy)); Assert.False(PanelLayout.AppSliderHit(f, (l + r) / 2, PanelLayout.AppHeader(f).Y + 4));   // la cabecera no arrastra
        var m = PanelLayout.AppMute(f); Assert.True(PanelLayout.AppMuteHit(f, m.X + m.W / 2, m.Y + m.H / 2)); Assert.False(PanelLayout.AppSliderHit(f, m.X + m.W / 2, m.Y + m.H / 2));
        var o = PanelLayout.AppOutput(f); Assert.True(PanelLayout.AppOutputHit(f, o.X + 20, o.Y + o.H / 2)); Assert.False(PanelLayout.AppMuteHit(f, o.X + 20, o.Y + o.H / 2));
        Assert.False(PanelLayout.AppOutputHit(Frame(Row()), o.X + 20, o.Y + 5));   // sin función experimental no hay selector que pulsar
    }

    [Fact]
    public void Sub_sessions_keep_the_same_columns_as_their_app()
    {
        var a = Frame(Row()); var b = Frame(Row(sub: true));
        Assert.Equal(PanelLayout.AppSliderTrack(a).Left, PanelLayout.AppSliderTrack(b).Left); Assert.Equal(PanelLayout.AppName(a).X, PanelLayout.AppName(b).X);
    }

    [Theory]
    [InlineData(1920.0, 520.0)] [InlineData(910.0, 520.0)] [InlineData(500.0, 468.0)] [InlineData(360.0, 328.0)]
    public void The_panel_fits_small_screens(double monitorDip, double expected)
    {
        var m = new PanelModel(PanelKind.Sound, "Sonido", [], PanelBuilder.MixerWidth);
        Assert.Equal(expected, PanelLayout.FitWidth(m, monitorDip).Width);
    }

    [Fact]
    public void Fit_never_widens_a_panel_and_leaves_the_other_panels_alone()
    {
        var cc = new PanelModel(PanelKind.ControlCenter, "", [], PanelLayout.Width);
        Assert.Equal(PanelLayout.Width, PanelLayout.FitWidth(cc, 1920).Width); Assert.Equal(PanelLayout.Width, PanelLayout.FitWidth(cc, 5000).Width);
    }

    [Fact]
    public void Many_apps_with_output_selectors_scroll_inside_the_panel_and_keep_header_and_footer_pinned()
    {
        var apps = Enumerable.Range(0, 12).Select(i => new AudioSession("s" + i, 100 + i, "Aplicación " + i, null, 50 + i, false, false, true)).ToList();
        var outs = new List<AudioDevice> { new("a", "Altavoces", true), new("b", "Audífonos", false) };
        var routing = new AppRoutingState(true, null, apps.ToDictionary(a => a.Pid, a => (string?)null));
        var rows = PanelBuilder.MixerRows(apps, routing, outs).ToList();
        Assert.All(rows, r => Assert.NotNull(((AppRowData)r.Data!).Output));   // cada fila con selector de salida
        var m = new PanelModel(PanelKind.Sound, "Sonido", [new Widget(WidgetKind.Header, "h", "Sonido"), .. rows, new Widget(WidgetKind.Button, "b1", "Mezclador"), new Widget(WidgetKind.Button, "b2", "Sonido")], PanelBuilder.MixerWidth, PinTop: 1, PinBottom: 2, MaxHeight: PanelBuilder.MixerMaxHeight);
        var f = PanelLayout.Compute(m, 700); Assert.True(f.Height <= PanelBuilder.MixerMaxHeight + 0.5); Assert.NotNull(f.View);
        var small = PanelLayout.Compute(m, 420); Assert.True(small.Height <= 420.5);   // pantalla baja: se adapta
    }

    [Fact]
    public void Names_are_the_full_device_and_app_names_not_shortened_in_the_model()
    {
        var outs = new List<AudioDevice> { new("a", "Altavoces (PRO X Wireless Gaming Headset)", true) };
        string label = PanelBuilder.OutputLabel("a", outs); Assert.Equal("Altavoces (PRO X Wireless Gaming Headset)", label);   // el recorte, si hace falta, solo lo hace el dibujo
        Assert.Equal("No conectada", PanelBuilder.OutputLabel("zzz", outs)); Assert.Equal("Predeterminada", PanelBuilder.OutputLabel(null, outs));
    }

    [Fact]
    public void Select_rows_give_wide_panels_a_fixed_label_column_and_a_pill_that_fits_the_long_name()
    {
        var wide = new WidgetFrame(new Widget(WidgetKind.Select, "pick:out", "Salida"), 14, 0, 492, 32); var narrow = new WidgetFrame(wide.W, 14, 0, 320, 32);
        Assert.Equal(92, PanelLayout.SelectLabelWidth(wide.Width)); Assert.Equal(0.4 * 320, PanelLayout.SelectLabelWidth(narrow.Width), 3);   // los paneles estrechos, como antes
        Assert.True(PanelLayout.SelectPill(wide).W - 38 >= 330);   // nombre completo de dispositivo (~270 DIP) sin recorte
    }
}
