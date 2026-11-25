using WinMac.Host.SettingsV2;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Medidor de prueba: 0,52 × tamaño por carácter, ajuste por palabras. Es una estimación SOLO de pruebas; en Windows mide DirectWrite.</summary>
internal sealed class FakeMeasurer : ITextMeasurer
{
    public double LineHeight(TextStyle s) => Math.Ceiling(s.Size * 1.35);
    public Size Measure(string text, TextStyle s, double maxWidth)
    {
        double cw = s.Size * (s.Weight == TextWeight.Semibold ? 0.56 : 0.52), line = 0, widest = 0; int lines = 1;
        foreach (var word in text.Split(' '))
        {
            double ww = word.Length * cw, sp = line == 0 ? 0 : cw;
            if (line > 0 && line + sp + ww > maxWidth) { widest = Math.Max(widest, line); lines++; line = ww; } else line += sp + ww;
        }

        widest = Math.Max(widest, line);
        return new Size(widest, lines * LineHeight(s));
    }
}

public class SettingsV2Tests
{
    private static readonly SettingsPalette L = SettingsPalette.Light, D = SettingsPalette.DarkPalette;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Palette_text_and_accent_meet_wcag_aa(bool dark)
    {
        var p = dark ? D : L; var under = p.BackdropBottom;
        foreach (var surface in new[] { p.Surface, p.SurfaceRaised, p.Sidebar })
        {
            Assert.True(Rgba.Contrast(p.Text, surface, under) >= 7, "texto principal");
            Assert.True(Rgba.Contrast(p.TextSecondary, surface, under) >= 4.5, "texto secundario");
            Assert.True(Rgba.Contrast(p.TextFaint, surface, under) >= 3.0, "texto tenue (solo descripciones largas no críticas)");
        }

        Assert.True(Rgba.Contrast(p.OnAccent, p.Accent, under) >= 4.5, "texto sobre el acento");
        Assert.True(Rgba.Contrast(p.Accent, p.Surface, under) >= 3.0, "acento como control (WCAG 1.4.11)");
        Assert.True(Rgba.Contrast(p.Danger, p.Surface, under) >= 4.5 && Rgba.Contrast(p.Success, p.Surface, under) >= 4.5 && Rgba.Contrast(p.Warning, p.Surface, under) >= 4.5, "estados");
    }

    [Fact]
    public void Dark_is_its_own_design_not_an_inverted_light()
    {
        Assert.NotEqual(L.Accent, D.Accent);                                   // el acento oscuro es lavanda claro, el claro es violeta profundo
        Assert.True(D.Surface.Luminance(D.BackdropBottom) < 0.05);              // grafito, no negro absoluto ni gris plano
        Assert.True(D.BackdropBottom != Rgba.Hex(0x000000) && D.Surface != D.BackdropBottom && D.SurfaceRaised != D.Surface);   // superficies diferenciadas
        Assert.True(L.Surface.Luminance(L.BackdropBottom) > 0.6);
    }

    [Fact]
    public void Theme_resolution_follows_mode_and_windows()
    {
        Assert.Same(L, SettingsPalette.Resolve(SettingsThemeMode.System, systemDark: false));
        Assert.Same(D, SettingsPalette.Resolve(SettingsThemeMode.System, systemDark: true));
        Assert.Same(L, SettingsPalette.Resolve(SettingsThemeMode.Light, systemDark: true));
        Assert.Same(D, SettingsPalette.Resolve(SettingsThemeMode.Dark, systemDark: false));
        Assert.Equal(SettingsThemeMode.System, SettingsPalette.ParseMode(null));
        Assert.Equal(SettingsThemeMode.System, SettingsPalette.ParseMode("loquesea"));
        Assert.Equal(SettingsThemeMode.Dark, SettingsPalette.ParseMode("dark"));
    }

    private static FlowNode Row(string title, string? sub, double controlW = 52) =>
        new BoxNode(new SplitNode(new StackNode([new TextNode(title, SettingsTokens.CardTitle) { Tag = "t" }, .. sub is null ? [] : new FlowNode[] { new TextNode(sub, SettingsTokens.Small) { Tag = "s" } }], 2), new FixedNode(controlW, 28) { Tag = "ctl" }, controlW, 28), new Thickness(14)) { Tag = "row" };

    private const string Long = "Descripción larga que debe ocupar varias líneas sin pisar el control de la derecha, incluso con un texto muy extenso y palabras como supercalifragilisticoespialidoso";

    private static IEnumerable<Placed> Leaves(FlowResult r) => r.Boxes.Where(b => b.Node is TextNode or FixedNode);

    [Fact]
    public void Description_wraps_and_row_grows_with_content()
    {
        var m = new FakeMeasurer();
        var wide = FlowNode.Layout(Row("Título", Long), 900, m); var narrow = FlowNode.Layout(Row("Título", Long), 420, m);
        Assert.True(narrow.Height > wide.Height);
        var none = FlowNode.Layout(Row("Título", null), 900, m);
        Assert.True(wide.Height > none.Height);
    }

    [Theory]
    [InlineData(280)] [InlineData(360)] [InlineData(480)] [InlineData(640)] [InlineData(900)] [InlineData(1280)] [InlineData(1920)] [InlineData(3840)]
    public void No_text_or_control_overlaps_at_any_width(int width)
    {
        var m = new FakeMeasurer();
        var page = new StackNode(Enumerable.Range(0, 12).Select(i => Row("Opción " + i + (i % 3 == 0 ? " con un título bastante largo para forzar ajuste" : ""), i % 2 == 0 ? Long : "Corta", i % 4 == 0 ? 220 : 52)).ToList(), 8);
        var r = FlowNode.Layout(page, width, m); var leaves = Leaves(r).ToList();
        for (int i = 0; i < leaves.Count; i++)
        {
            Assert.True(leaves[i].Rect.X >= -0.01 && leaves[i].Rect.Right <= width + 0.01 || leaves[i].Node is TextNode, $"fuera de la página: {leaves[i].Node.Tag}");
            for (int j = i + 1; j < leaves.Count; j++) Assert.False(leaves[i].Rect.Intersects(leaves[j].Rect), $"{leaves[i].Node.Tag}#{i} se superpone con {leaves[j].Node.Tag}#{j} a {width}");
        }
    }

    [Fact]
    public void Control_moves_below_the_text_when_it_does_not_fit_beside()
    {
        var m = new FakeMeasurer(); var node = new SplitNode(new TextNode(Long, SettingsTokens.Body) { Tag = "s" }, new FixedNode(220, 32) { Tag = "ctl" }, 220, 32);
        var wide = FlowNode.Layout(node, 900, m); var narrow = FlowNode.Layout(node, 300, m);
        var t1 = wide.Boxes.First(b => b.Node.Tag == "s").Rect; var c1 = wide.Boxes.First(b => b.Node.Tag == "ctl").Rect;
        var t2 = narrow.Boxes.First(b => b.Node.Tag == "s").Rect; var c2 = narrow.Boxes.First(b => b.Node.Tag == "ctl").Rect;
        Assert.True(c1.X >= t1.Right);                 // a su lado
        Assert.True(c2.Y >= t2.Bottom - 0.01);         // debajo
        Assert.True(c2.Right <= 300.01);
    }

    [Fact]
    public void Wrap_reorganizes_columns_and_keeps_items_apart()
    {
        var m = new FakeMeasurer(); var items = Enumerable.Range(0, 7).Select(i => (FlowNode)new BoxNode(new TextNode("Módulo " + i + (i == 2 ? " " + Long : ""), SettingsTokens.Body) { Tag = "x" + i }, new Thickness(12)) { Tag = "card" + i }).ToList();
        var wrap = new WrapNode(items, 300, 12);
        Assert.Equal(1, wrap.Columns(320)); Assert.Equal(2, wrap.Columns(640)); Assert.Equal(3, wrap.Columns(980)); Assert.Equal(7, new WrapNode(items, 100, 0).Columns(5000));
        foreach (var w in new[] { 320, 640, 980, 1500 })
        {
            var cards = FlowNode.Layout(wrap, w, m).Boxes.Where(b => b.Node.Tag?.StartsWith("card") == true).ToList();
            Assert.Equal(7, cards.Count);
            for (int i = 0; i < cards.Count; i++) for (int j = i + 1; j < cards.Count; j++) Assert.False(cards[i].Rect.Intersects(cards[j].Rect));
            Assert.All(cards, c => Assert.True(c.Rect.X >= -0.01 && c.Rect.Right <= w + 0.01));
        }
    }

    [Fact]
    public void Unbreakable_word_and_zero_width_do_not_break_the_layout()
    {
        var m = new FakeMeasurer();
        var r = FlowNode.Layout(new StackNode([new TextNode(new string('x', 400), SettingsTokens.Body)]), 10, m);
        Assert.True(r.Height > 0 && !double.IsNaN(r.Height));
        var z = FlowNode.Layout(new StackNode([new TextNode("hola", SettingsTokens.Body)]), 0, m);
        Assert.True(z.Height > 0);
    }

    [Theory]
    [InlineData(700, true)] [InlineData(759, true)] [InlineData(760, false)] [InlineData(1280, false)] [InlineData(3840, false)]
    public void Shell_collapses_sidebar_when_narrow_and_limits_content_width(int w, bool compact)
    {
        var f = SettingsShell.Compute(w, 800);
        Assert.Equal(compact, f.CompactSidebar);
        Assert.True(f.Sidebar.Right <= f.Content.X + 0.01);
        Assert.True(f.ContentInnerW <= SettingsShell.ContentMax + 0.01 && f.ContentInnerW > 0);
        Assert.True(f.ContentInnerX >= f.Content.X + SettingsShell.ContentPad - 0.01 && f.ContentInnerX + f.ContentInnerW <= w + 0.01);
        if (!compact) Assert.InRange(f.Sidebar.W, SettingsShell.SidebarMin, SettingsShell.SidebarMax);
    }
}
