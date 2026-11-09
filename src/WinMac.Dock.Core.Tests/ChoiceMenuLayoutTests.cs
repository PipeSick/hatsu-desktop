using WinMac.Dock;
using Xunit;

/// <summary>WM-035: el desplegable de selección se coloca contra su control, dentro de la pantalla, con nombres completos y con cualquier DPI/monitor.</summary>
public class ChoiceMenuLayoutTests
{
    private static readonly double[] Names = [150, 270, 210];   // «Altavoces (Realtek(R) Audio)», «Altavoces (PRO X Wireless Gaming Headset)»…

    private static bool Inside(PxRect r, PxRect work) => r.Left >= work.Left && r.Right <= work.Right && r.Top >= work.Top && r.Bottom <= work.Bottom;

    [Theory]
    [InlineData(1.0)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(1.75)] [InlineData(2.0)] [InlineData(3.0)]
    public void Opens_below_the_control_inside_the_work_area_at_any_dpi(double s)
    {
        var work = new PxRect(0, 0, (int)(1920 * s / 1.0), (int)(1040 * s)); var anchor = new PxRect((int)(900 * s), (int)(200 * s), (int)(1200 * s), (int)(228 * s));
        var p = ChoiceMenuLayout.Compute(Names, anchor, work, s);
        Assert.False(p.OpensUp); Assert.True(Inside(p.Rect, work)); Assert.True(p.Rect.Top >= anchor.Bottom); Assert.Equal(3, p.VisibleRows);
    }

    [Fact]
    public void Is_wide_enough_for_the_longest_name_and_at_least_as_wide_as_the_control()
    {
        var work = new PxRect(0, 0, 1920, 1040); var narrow = new PxRect(100, 100, 220, 130);   // un selector estrecho (120 px)
        var p = ChoiceMenuLayout.Compute(Names, narrow, work, 1.0);
        Assert.True(p.Rect.Width >= (int)(270 + ChoiceMenuLayout.CheckColumnDip + ChoiceMenuLayout.RightPadDip));   // el nombre más largo cabe entero
        var wide = new PxRect(100, 100, 700, 130); Assert.True(ChoiceMenuLayout.Compute(Names, wide, work, 1.0).Rect.Width >= 600);   // nunca más estrecho que el control
    }

    [Fact]
    public void Never_leaves_the_screen_horizontally_near_either_edge()
    {
        var work = new PxRect(0, 0, 1920, 1040);
        var right = ChoiceMenuLayout.Compute(Names, new PxRect(1800, 100, 1900, 130), work, 1.0); Assert.True(Inside(right.Rect, work));
        Assert.True(right.Rect.Right <= 1920 - ChoiceMenuLayout.EdgeDip);
        var left = ChoiceMenuLayout.Compute(Names, new PxRect(0, 100, 90, 130), work, 1.0); Assert.True(Inside(left.Rect, work));
    }

    [Fact]
    public void Opens_upwards_when_there_is_no_room_below()
    {
        var work = new PxRect(0, 0, 1920, 1040); var anchor = new PxRect(800, 980, 1100, 1010);   // el control está casi al borde inferior
        var p = ChoiceMenuLayout.Compute(Names, anchor, work, 1.0);
        Assert.True(p.OpensUp); Assert.True(p.Rect.Bottom <= anchor.Top); Assert.True(Inside(p.Rect, work)); Assert.Equal(3, p.VisibleRows);
    }

    [Fact]
    public void Scrolls_when_it_fits_neither_below_nor_above()
    {
        var work = new PxRect(0, 0, 1366, 400); var anchor = new PxRect(300, 180, 700, 210); var many = Enumerable.Repeat(200.0, 30).ToList();
        var p = ChoiceMenuLayout.Compute(many, anchor, work, 1.0);
        Assert.True(Inside(p.Rect, work)); Assert.InRange(p.VisibleRows, 1, 29);   // menos filas que opciones: se desplaza
    }

    [Fact]
    public void A_name_longer_than_the_screen_is_capped_to_the_screen_not_beyond()
    {
        var work = new PxRect(0, 0, 800, 600); var p = ChoiceMenuLayout.Compute([5000], new PxRect(100, 100, 300, 130), work, 1.0);
        Assert.True(Inside(p.Rect, work));
    }

    [Fact]
    public void Secondary_monitor_at_negative_coordinates_is_respected()
    {
        var work = new PxRect(-2560, -300, 0, 1100); var anchor = new PxRect(-400, 1000, -150, 1030);
        var p = ChoiceMenuLayout.Compute(Names, anchor, work, 1.5);
        Assert.True(Inside(p.Rect, work)); Assert.True(p.OpensUp);
    }

    [Fact]
    public void A_single_option_and_an_empty_list_do_not_break_the_layout()
    {
        var work = new PxRect(0, 0, 1920, 1040); var a = new PxRect(10, 10, 300, 40);
        Assert.Equal(1, ChoiceMenuLayout.Compute([100], a, work, 1.0).VisibleRows);
        Assert.True(Inside(ChoiceMenuLayout.Compute([], a, work, 1.0).Rect, work));
    }
}

/// <summary>WM-035: contraste del desplegable en los dos temas y sobre la opción resaltada.</summary>
public class ChoiceMenuPaletteTests
{
    [Fact]
    public void Dark_menu_text_is_readable_and_the_highlighted_row_too()
    {
        var text = ChoiceMenuPalette.Over(ChoiceMenuPalette.DarkText, ChoiceMenuPalette.DarkTextAlpha, ChoiceMenuPalette.DarkBackground);
        Assert.True(ChoiceMenuPalette.Contrast(text, ChoiceMenuPalette.DarkBackground) >= 7.0);                      // texto normal (AAA)
        Assert.True(ChoiceMenuPalette.Contrast(ChoiceMenuPalette.HotText, ChoiceMenuPalette.Accent) >= 3.0);        // sobre la fila resaltada (texto de 13,5 pt semibold: umbral de texto grande/UI)
    }

    [Fact]
    public void Light_menu_text_is_readable()
        => Assert.True(ChoiceMenuPalette.Contrast(ChoiceMenuPalette.LightText, ChoiceMenuPalette.LightBackground) >= 7.0);

    [Fact]
    public void The_dark_menu_is_actually_dark_not_the_white_native_menu_of_before()
        => Assert.True(ChoiceMenuPalette.Luminance(ChoiceMenuPalette.DarkBackground) < 0.05);
}
