using WinMac.Dock;
using Xunit;

/// <summary>It7: paleta común claro/oscuro y diálogos de confirmación (diseño de la referencia aprobada).</summary>
public class UiPaletteTests
{
    [Theory]
    [InlineData(DockTheme.Dark)] [InlineData(DockTheme.Light)]
    public void Text_levels_used_by_the_windows_are_readable_in_both_themes(DockTheme t)
    {
        var p = UiPalette.For(t);
        Assert.True(UiPalette.Contrast(p.Ink(0.95f), p.Window) >= 7.0);    // título
        Assert.True(UiPalette.Contrast(p.Ink(0.74f), p.Window) >= 4.5);    // descripción
        Assert.True(UiPalette.Contrast(p.Ink(0.50f), p.Window) >= 3.0);    // texto terciario (estado, ayudas)
        Assert.True(UiPalette.Contrast(p.Ink(0.95f), p.Surface) >= 4.5);   // botón neutro
    }

    [Theory]
    [InlineData(DockTheme.Dark)] [InlineData(DockTheme.Light)]
    public void White_text_on_the_action_buttons_is_readable(DockTheme t)
    {
        var p = UiPalette.For(t);
        Assert.True(UiPalette.Contrast(p.OnColor(), p.Danger) >= 4.5, "acción destructiva");
        Assert.True(UiPalette.Contrast(p.OnColor(), p.DangerHover) >= 4.5);
        Assert.True(UiPalette.Contrast(p.OnColor(), p.Accent) >= 3.0, "acción normal (texto grande)");
    }

    [Fact]
    public void Themes_are_actually_different_and_follow_the_enum()
    {
        Assert.True(UiPalette.Luminance(UiPalette.Dark.Window) < 0.05); Assert.True(UiPalette.Luminance(UiPalette.Light.Window) > 0.85);
        Assert.Same(UiPalette.Dark, UiPalette.For(DockTheme.Dark)); Assert.Same(UiPalette.Light, UiPalette.For(DockTheme.Light));
    }

    [Fact]
    public void Light_ink_is_dark_and_dark_ink_is_light()
    {
        Assert.True(UiPalette.Light.Ink(1).R < 0.2f); Assert.True(UiPalette.Dark.Ink(1).R > 0.9f);
        Assert.True(UiPalette.Light.Ink(0.5f).A > 0.5f);   // el alfa sube en claro para conservar el contraste
    }
}

public class ConfirmDialogModelTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(1, 3)] [InlineData(2, 2)] [InlineData(1, 6)]
    public void Frame_has_no_overlaps_and_equal_buttons(int titleLines, int bodyLines)
    {
        var f = ConfirmLayout.Compute(titleLines * 28f, bodyLines * 18f, onlyOk: false);
        Assert.Equal(f.Cancel.W, f.Action.W); Assert.Equal(f.Cancel.H, f.Action.H); Assert.True(f.Cancel.Right < f.Action.X);   // dos botones grandes y equilibrados
        Assert.True(f.Title.Bottom <= f.Body.Y + 0.01f); Assert.True(f.Body.Bottom <= f.SeparatorY); Assert.True(f.SeparatorY < f.Cancel.Y);
        Assert.True(f.Tile.Bottom <= f.SeparatorY); Assert.True(f.Tile.Right < f.Title.X);
        Assert.True(f.Action.Right <= f.Width - ConfirmLayout.Pad + 0.01f); Assert.Equal(f.Cancel.Bottom + ConfirmLayout.Pad, f.Height, 2);
    }

    [Fact]
    public void A_notice_has_a_single_full_width_button()
    {
        var f = ConfirmLayout.Compute(28, 36, onlyOk: true);
        Assert.Equal(0, f.Cancel.W); Assert.Equal(f.Width - 2 * ConfirmLayout.Pad, f.Action.W, 2);
    }

    [Fact]
    public void Taller_text_makes_a_taller_dialog_and_the_icon_stays_centred_in_the_header()
    {
        var a = ConfirmLayout.Compute(28, 18, false); var b = ConfirmLayout.Compute(28, 90, false);
        Assert.True(b.Height > a.Height);
        Assert.Equal((b.Title.Y + b.Body.Bottom) / 2, b.Tile.Y + b.Tile.H / 2, 1);
    }

    [Theory]
    [InlineData(ConfirmKind.Power, true)] [InlineData(ConfirmKind.Restart, true)] [InlineData(ConfirmKind.SignOut, true)] [InlineData(ConfirmKind.Trash, true)]
    [InlineData(ConfirmKind.Warning, false)] [InlineData(ConfirmKind.Info, false)]
    public void Destructive_actions_are_visibly_red_and_the_rest_are_not(ConfirmKind k, bool red)
    {
        foreach (var t in new[] { DockTheme.Dark, DockTheme.Light })
        {
            var c = ConfirmStyle.Colors(k, danger: false, UiPalette.For(t)); var p = UiPalette.For(t);
            Assert.Equal(red, c.Action == p.Danger);
            Assert.True(UiPalette.Contrast(c.Glyph, p.Window) >= 3.0, $"icono {k} {t}");   // el icono se ve sobre el fondo
        }
    }

    [Fact]
    public void A_dangerous_flag_always_wins_over_the_icon()
        => Assert.Equal(UiPalette.Dark.Danger, ConfirmStyle.Colors(ConfirmKind.Info, danger: true, UiPalette.Dark).Action);
}

/// <summary>It8: el Dock tiene un material propio por tema y lo cambia en caliente.</summary>
public class DockThemeTests
{
    private static double Lum(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    private static double L(double r, double g, double b) => 0.2126 * Lum(r) + 0.7152 * Lum(g) + 0.0722 * Lum(b);
    private static double Ratio(double a, double b) { if (a < b) (a, b) = (b, a); return (a + 0.05) / (b + 0.05); }

    [Fact]
    public void Light_and_dark_glass_are_clearly_different_materials()
    {
        var l = GlassStyle.For(DockTheme.Light); var d = GlassStyle.For(DockTheme.Dark);
        // sobre el MISMO fondo rosado, claro y oscuro deben diferir mucho (antes casi no se distinguían)
        var (lr, lg, lb) = l.EffectiveOver(0.75); var (dr, dg, db) = d.EffectiveOver(0.75);
        Assert.True(L(lr, lg, lb) > 0.55 && L(dr, dg, db) < 0.30, $"claro {L(lr, lg, lb):0.00} oscuro {L(dr, dg, db):0.00}");
        Assert.True(L(lr, lg, lb) / Math.Max(0.001, L(dr, dg, db)) > 2.5);
    }

    [Fact]
    public void Dark_glass_is_graphite_with_a_subtle_violet_tint()
    {
        var d = GlassStyle.For(DockTheme.Dark);
        Assert.True(d.TintB > d.TintR && d.TintB > d.TintG);          // violeta
        Assert.True(d.TintB - d.TintG < 0.12 && d.TintB < 0.35);        // muy sutil y oscuro
    }

    [Theory]
    [InlineData(DockTheme.Light)]
    [InlineData(DockTheme.Dark)]
    public void Indicators_and_separators_stay_visible_over_any_wallpaper(DockTheme t)
    {
        var g = GlassStyle.For(t);
        foreach (double wall in new[] { 0.0, 0.5, 1.0 })
        {
            var (r, gg, b) = g.EffectiveOver(wall); double glass = L(r, gg, b);
            double a = g.MarkAlpha; double mark = L(a * g.MarkR + (1 - a) * r, a * g.MarkG + (1 - a) * gg, a * g.MarkB + (1 - a) * b);
            Assert.True(Ratio(mark, glass) >= 3.0, $"{t} fondo {wall}: {Ratio(mark, glass):0.0}:1");   // indicador de app abierta: no texto, 3:1
        }
    }

    [Theory]
    [InlineData(ThemeMode.System, DockTheme.Dark, DockTheme.Light, true, DockTheme.Dark)]
    [InlineData(ThemeMode.System, DockTheme.Light, DockTheme.Light, false, DockTheme.Light)]
    [InlineData(ThemeMode.System, DockTheme.Light, DockTheme.Dark, true, DockTheme.Light)]
    [InlineData(ThemeMode.Dark, DockTheme.Light, DockTheme.Dark, false, DockTheme.Dark)]   // elegido a mano: el sistema no manda
    [InlineData(ThemeMode.Light, DockTheme.Dark, DockTheme.Dark, true, DockTheme.Light)]
    public void Restyle_follows_windows_unless_the_user_chose_a_theme(ThemeMode mode, DockTheme system, DockTheme current, bool expected, DockTheme target)
    {
        Assert.Equal(expected, DockThemeTracker.NeedsRestyle(mode, system, current, out var t)); Assert.Equal(target, t);
    }
}
