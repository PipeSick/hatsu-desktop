using WinMac.Bar;
using WinMac.Bar.Hub;
using Xunit;

namespace WinMac.Bar.Core.Tests;

/// <summary>Altura máxima y desplazamiento del panel multifunción (pantallas de 1080p o con DPI alto) y geometría sin superposiciones.</summary>
public class PanelScrollTests
{
    private static PanelModel Hub(HubTab tab) => new HubRig().LoadAll(tab).Hub();

    [Theory]
    [InlineData(HubTab.Sound)]
    [InlineData(HubTab.System)]
    [InlineData(HubTab.Network)]
    [InlineData(HubTab.Disks)]
    public void When_it_does_not_fit_the_height_is_capped_and_tabs_and_footer_stay_in_place(HubTab tab)
    {
        var m = Hub(tab); var natural = PanelLayout.Compute(m); double max = 600;
        var top = PanelLayout.Compute(m, max, 0); Assert.True(natural.Height > max || top.View is null);
        if (top.View is null) return;
        Assert.Equal(max, top.Height, 3);
        var bottom = PanelLayout.Compute(m, max, 1e9); Assert.NotNull(bottom.View); Assert.True(bottom.View!.Offset > 0);
        int pinTop = top.View!.From; for (int i = 0; i < pinTop; i++) Assert.Equal(top.Items[i].Y, bottom.Items[i].Y);   // logotipo, pestañas y título no se mueven
        for (int i = top.View.To; i < top.Items.Count; i++) Assert.Equal(top.Items[i].Y, bottom.Items[i].Y);            // Ajustes y Salir tampoco
        var footer = top.Items[top.View.To]; Assert.Equal("hub:settings", footer.W.Id); Assert.True(footer.Y + footer.Height <= top.Height);
        Assert.Equal("tabs", top.Items[1].W.Id); Assert.True(top.Items[1].Y + top.Items[1].Height <= top.View.Top + 0.01);
        Assert.True(bottom.Items[top.View.To - 1].Y + bottom.Items[top.View.To - 1].Height <= bottom.View.Bottom + 0.01 + 8);   // al final, el último elemento del contenido queda visible
    }

    [Fact]
    public void Scrolled_out_items_cannot_be_clicked_and_the_footer_buttons_always_can()
    {
        var m = Hub(HubTab.Disks); var f = PanelLayout.Compute(m, 560, 300); var v = f.View!;
        // un punto dentro del hueco de las pestañas/título que coincidiría con un elemento desplazado hacia arriba no lo activa
        for (int i = v.From; i < v.To; i++) { var it = f.Items[i]; double cy = it.Y + it.Height / 2; if (cy < v.Top || cy >= v.Bottom) Assert.NotEqual(i, PanelLayout.HitTest(f, it.X + it.Width / 2, cy)); }
        var settings = f.Items[v.To]; Assert.Equal(v.To, PanelLayout.HitTest(f, settings.X + 5, settings.Y + 5));
        var tabs = f.Items[1]; Assert.Equal(1, PanelLayout.HitTest(f, tabs.X + 20, tabs.Y + 20));
    }

    [Fact]
    public void The_controller_scrolls_clamps_resets_on_tab_change_and_keeps_the_offset_across_refreshes()
    {
        var r = new HubRig().LoadAll(HubTab.Disks); var c = new PanelController(); c.SetMaxHeight(560); c.SetModel(r.Hub());
        Assert.True(c.CanScroll); Assert.False(c.Scroll(-50)); Assert.Equal(0, c.ScrollOffset);
        Assert.True(c.Scroll(100)); Assert.Equal(100, c.ScrollOffset, 3);
        c.SetModel(r.Hub()); Assert.Equal(100, c.ScrollOffset, 3);   // un refresco de datos no devuelve el panel arriba
        Assert.True(c.Scroll(1e6)); Assert.Equal(c.Frame.View!.MaxOffset, c.ScrollOffset, 3); Assert.False(c.Scroll(10));
        r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Tab = HubTab.Network }; c.SetModel(r.Hub()); Assert.Equal(0, c.ScrollOffset);   // otra pestaña: arriba
        c.SetMaxHeight(double.PositiveInfinity); c.SetModel(r.Hub()); Assert.Null(c.Frame.View); Assert.False(c.CanScroll);
    }

    [Fact]
    public void A_panel_that_fits_is_not_scrollable_and_non_hub_panels_never_scroll()
    {
        var sound = PanelLayout.Compute(Hub(HubTab.Network), 5000, 0); Assert.Null(sound.View);
        var cc = new PanelModel(PanelKind.ControlCenter, "", [new(WidgetKind.Header, "h"), new(WidgetKind.Slider, "s")]); Assert.Null(PanelLayout.Compute(cc, 10, 0).View);
    }

    [Fact]
    public void Hub_panels_at_1080p_fit_with_scroll_and_are_close_to_the_reference_heights()
    {
        // 1080 px a 100 %: 1080 - barra (~32) - márgenes; a 150 %: 720 DIP de pantalla. Ninguna pestaña supera esa altura (si no cabe, se desplaza).
        foreach (var tab in new[] { HubTab.Sound, HubTab.System, HubTab.Network, HubTab.Disks })
        {
            foreach (double max in new[] { 1080 - 32 - 16.0, 720 - 32 - 16.0 }) Assert.True(PanelLayout.Compute(Hub(tab), max, 0).Height <= max + 0.01);
        }
        // referencias (DIP): Sonido 763, Sistema 763, Red 509, Discos 911: la composición compactada queda cerca (antes pasaba de 840 a 1260)
        Assert.InRange(PanelLayout.Compute(Hub(HubTab.Network)).Height, 380, 720);   // más alta que la referencia (509) porque añade adaptador, tipo y velocidad del enlace; ya no 780
    }

    [Fact]
    public void Network_speed_pill_never_overlaps_the_upload_figure_and_app_rows_leave_room_for_percent_and_speaker()
    {
        var net = PanelLayout.Compute(Hub(HubTab.Network)); var stats = net.Items.Single(i => i.W.Kind == WidgetKind.Stats);
        Assert.True(PanelLayout.StatsPillHit(stats, stats.X + stats.Width - 10, stats.Y + 10)); Assert.False(PanelLayout.StatsPillHit(stats, stats.X + stats.Width * 0.5, stats.Y + 10));
        var snd = PanelLayout.Compute(Hub(HubTab.Sound)); var app = snd.Items.First(i => i.W.Kind == WidgetKind.AppRow); var (l, rgt) = PanelLayout.AppSliderTrack(app);
        Assert.True(rgt + 8 + 36 <= app.X + app.Width - 28);   // el porcentaje termina antes del icono del altavoz
        Assert.True(PanelLayout.AppMuteHit(app, app.X + app.Width - 10, app.Y + 20));
    }

    [Fact]
    public void Unavailable_temperature_reasons_are_shown_in_full_notes_not_cut_inside_the_tile()
    {
        var m = Hub(HubTab.System); var cpu = m.Widgets.First(w => w.Id == "temp-cpu"); Assert.Equal("CPU", cpu.Sub);
        if (!cpu.Enabled) Assert.Contains(m.Widgets, w => w.Id == "temp-cpu-why");
    }

    [Fact]
    public void The_fixed_top_items_are_always_before_the_scroll_zone_and_inside_the_panel_so_the_painter_can_draw_them_unclipped()
    {
        var f = PanelLayout.Compute(Hub(HubTab.System), 480, 120); var v = f.View!;
        Assert.True(v.From >= 3);   // logotipo, pestañas y título
        for (int i = 0; i < v.From; i++) Assert.True(f.Items[i].Y >= 0 && f.Items[i].Y + f.Items[i].Height <= v.Top + 0.01);   // por encima de la zona de recorte: dibujarlos dentro de ella los haría desaparecer
    }
}
