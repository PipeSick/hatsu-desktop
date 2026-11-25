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

/// <summary>Layout de Ajustes V2 sobre el modelo REAL: a cualquier tamaño de ventana nada se sale de su fila ni se dibuja encima de su texto.</summary>
public class SettingsV2LayoutTests
{
    private static IReadOnlyList<SettingsPage> Pages() => SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), true) { Dock = new WinMac.Dock.DockConfig(), Extensions = [] });

    [Theory]
    [InlineData(700, 500)] [InlineData(900, 600)] [InlineData(1280, 720)] [InlineData(1920, 1080)] [InlineData(2560, 1440)] [InlineData(3840, 2160)]
    public void Every_page_fits_without_overlaps_at_any_window_size(int w, int h)
    {
        SettingsLayout.Configure(w, h); SettingsLayout.Measurer = new FakeMeasurer();
        try
        {
            foreach (var page in Pages())
            {
                var f = SettingsLayout.Compute(page); Assert.True(f.ContentHeight > 40, page.Id);
                for (int i = 0; i < f.Rows.Count; i++)
                {
                    var r = f.Rows[i]; Assert.True(r.X >= SettingsLayout.Pad - 0.01 && r.X + r.W <= SettingsLayout.ContentW - SettingsLayout.Pad + 0.01, $"{page.Id} {r.Row.Id} fuera de la columna a {w}");
                    for (int j = i + 1; j < f.Rows.Count; j++) { var q = f.Rows[j]; Assert.False(r.X < q.X + q.W - 0.01 && q.X < r.X + r.W - 0.01 && r.Y < q.Y + q.H - 0.01 && q.Y < r.Y + r.H - 0.01, $"{page.Id} {r.Row.Id} / {q.Row.Id} a {w}"); }
                    void Inside((double X, double Y, double W, double H) c, string what) => Assert.True(c.X >= r.X - 0.01 && c.Y >= r.Y - 0.01 && c.X + c.W <= r.X + r.W + 0.01 && c.Y + c.H <= r.Y + r.H + 0.01, $"{page.Id} {r.Row.Id} {what} se sale de su fila a {w}");
                    if (r.Row.Kind == RowKind.Field) { var c = SettingsLayout.FieldRect(r); Inside(c, "campo"); if (SettingsLayout.IsStacked(r.Row, r.W)) Assert.True(c.Y >= r.Y + 28, $"{r.Row.Id} campo sobre el texto a {w}"); else Assert.True(c.X >= r.X + 16 + 120, $"{r.Row.Id} campo sin sitio para el texto a {w}"); }
                    if (r.Row.Kind == RowKind.Segmented) { var c = SettingsLayout.SegmentRect(r); Inside(c, "selector"); if (SettingsLayout.IsStacked(r.Row, r.W)) Assert.True(c.Y >= r.Y + 28, $"{r.Row.Id} selector sobre el texto a {w}"); else Assert.True(c.X >= r.X + 16 + 120, $"{r.Row.Id} selector sin sitio para el texto a {w}"); }
                    if (r.Row.Kind == RowKind.Toggle) Inside(SettingsLayout.SwitchRect(r), "interruptor");
                }

                if (SettingsLayout.CompactSidebar == false) { var side = SettingsLayout.SideEntries(Pages()); Assert.True(side[^1].Rect.Y + side[^1].Rect.H <= h + 0.5, $"barra lateral cortada a {w}x{h}"); }
            }
        }
        finally { SettingsLayout.Configure(0, 0); SettingsLayout.Measurer = null; }
    }

    [Fact]
    public void Component_cards_reflow_from_three_columns_to_one()
    {
        var page = Pages().Single(p => p.Id == "general");
        try
        {
            SettingsLayout.Configure(1920, 1080); var wide = SettingsLayout.Compute(page).Rows.Where(r => r.Card).ToList();
            SettingsLayout.Configure(900, 700); var narrow = SettingsLayout.Compute(page).Rows.Where(r => r.Card).ToList();
            Assert.Equal(4, wide.Count); Assert.Equal(4, narrow.Count);
            Assert.True(wide.Select(r => r.X).Distinct().Count() > narrow.Select(r => r.X).Distinct().Count());
            SettingsLayout.Configure(700, 700); Assert.Single(SettingsLayout.Compute(page).Rows.Where(r => r.Card).Select(r => r.X).Distinct());
        }
        finally { SettingsLayout.Configure(0, 0); }
    }

    [Fact]
    public void Theme_choice_round_trips_and_old_configs_still_load()
    {
        var cfg = new HostConfig { SettingsTheme = "Dark" }; var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        Assert.Equal("Dark", System.Text.Json.JsonSerializer.Deserialize<HostConfig>(json)!.SettingsTheme);
        var old = System.Text.Json.JsonSerializer.Deserialize<HostConfig>("{\"autostart\":true}")!; Assert.Equal("System", old.SettingsTheme);
        var row = Pages().Single(p => p.Id == "general").Groups.SelectMany(g => g.Rows).Single(r => r.Id == "settings.theme"); Assert.Equal(0, row.Selected);
    }

    [Fact]
    public void Iphone_and_peripherals_are_independent_pages_and_no_option_was_lost()
    {
        var pages = Pages(); var ids = pages.SelectMany(p => p.Groups).SelectMany(g => g.Rows).Select(r => r.Id).ToHashSet();
        Assert.Contains("connect.module", pages.Single(p => p.Id == "connect").Groups.SelectMany(g => g.Rows).Select(r => r.Id));
        Assert.DoesNotContain("bar:showPeripherals", pages.Single(p => p.Id == "connect").Groups.SelectMany(g => g.Rows).Select(r => r.Id));
        Assert.Contains("bar:showPeripherals", pages.Single(p => p.Id == "periph").Groups.SelectMany(g => g.Rows).Select(r => r.Id));
        // la matriz de la auditoría lista cada id actual: todos deben seguir existiendo en V2
        var matrix = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "settings-v2-matriz.tsv")).Skip(1).Select(l => l.Split('\t')[0]).ToList();
        Assert.Equal(207, matrix.Count);
        foreach (var id in matrix.Where(id => !id.StartsWith("bar.key:", StringComparison.Ordinal))) Assert.Contains(id, ids);
    }
}

public class SettingsV2TextVsControlTests
{
    [Fact]
    public void A_field_never_covers_its_own_description()
    {
        foreach (double w in new[] { 640.0, 700, 900, 1280 })
        {
            var row = new SettingsRow(RowKind.Field, "x", "Nombre del teclado", "Un texto largo que ocupa varias líneas junto al campo de la derecha", Text: "Teclado ROG");
            var f = new RowFrame(row, 0, 0, w, SettingsLayout.RowHeight(row, w), true, true);
            if (SettingsLayout.IsStacked(row, w)) continue;
            double textRight = f.X + 16 + (w - 32 - SettingsLayout.ControlSpace(row, w)), fieldLeft = SettingsLayout.FieldRect(f).X;
            Assert.True(textRight <= fieldLeft - 8, $"el texto llega a {textRight:0} y el campo empieza en {fieldLeft:0} con ancho {w}");
        }
    }
}

public class SettingsV2DevicePagesTests
{
    private static IReadOnlyList<SettingsPage> Build(SettingsDevices? dev, bool connectOn = true, bool periphOn = true)
    {
        var mods = new Dictionary<string, ModuleConfig>(new HostConfig().Modules) { ["connect"] = new ModuleConfig { Enabled = connectOn } };
        var cfg = new HostConfig { Bar = new WinMac.Bar.BarConfig { ShowPeripherals = periphOn }, Modules = mods };
        return SettingsBuilder.Build(new SettingsInput(cfg, new Dictionary<string, ModuleState>(), false) { Dock = new WinMac.Dock.DockConfig(), Extensions = [], Devices = dev });
    }

    [Fact]
    public void The_iphone_card_shows_each_state_with_real_data_only()
    {
        var card = (SettingsDevices? d, bool on) => Build(d, on).Single(p => p.Id == "connect").Groups[0].Rows[0];
        Assert.Contains("Desactivado", card(null, false).Options!);
        var dev = new SettingsDevices(); Assert.Contains("Iniciando", card(dev, true).Options!);
        dev.Seed(new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Connected, DeviceName = "iPhone de Ana", Battery = 82, BatteryAtUtc = DateTime.UtcNow }, []);
        var c = card(dev, true); Assert.Equal("iPhone de Ana", c.Title); Assert.Contains("Conectado", c.Options!); Assert.Contains("Batería 82 %", c.Options!); Assert.DoesNotContain(c.Options!, o => o.Contains("Carg"));   // la carga no se informa por Bluetooth: no se inventa
        dev.Seed(new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Connected, DeviceName = "iPhone", NotificationsStatus = WinMac.Connect.PhoneFeatureStatus.NotAuthorized }, []);
        var d2 = card(dev, true); Assert.Contains("Batería sin lectura", d2.Options!); Assert.Contains(d2.Options!, o => o.StartsWith("Permiso"));
        dev.Seed(new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.NoDevice }, []); Assert.Contains("Sin emparejar", card(dev, true).Options!);
        dev.Seed(new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.NoBluetooth }, []); Assert.Contains("Sin Bluetooth", card(dev, true).Options!);
        dev.Seed(new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Error, Message = "No se pudo abrir la conexión" }, []); Assert.Contains("Error de conexión", card(dev, true).Options!);
    }

    [Fact]
    public void Peripheral_cards_never_invent_a_battery_level()
    {
        var dev = new SettingsDevices(); dev.Seed(null, [new("Teclado", "keyboard", "bluetooth", true, 64, false), new("Auriculares", "headset", "bluetooth", true, null, false), new("Mando", "gamepad", "usb", false, null, false)]);
        var rows = Build(dev).Single(p => p.Id == "periph").Groups[0].Rows; Assert.All(rows, r => Assert.Equal(RowKind.Device, r.Kind));
        Assert.Equal("64 %", rows[0].Text); Assert.Equal(64, rows[0].Value);
        Assert.Equal("Sin dato de batería", rows[1].Text); Assert.True(rows[1].Value < 0);
        Assert.Equal("Desconectado", rows[2].Text); Assert.False(rows[2].Enabled);
    }

    [Fact]
    public void Without_data_the_peripherals_page_explains_why_instead_of_showing_cards()
    {
        Assert.Contains("Activa", Build(null, periphOn: false).Single(p => p.Id == "periph").Groups[0].Rows[0].Title);
        Assert.Contains("Esperando", Build(new SettingsDevices()).Single(p => p.Id == "periph").Groups[0].Rows[0].Title);
        var d = new SettingsDevices(); d.Seed(null, []); Assert.Contains("No se detectó", Build(d).Single(p => p.Id == "periph").Groups[0].Rows[0].Title);
    }

    [Fact]
    public void The_devices_feed_round_trips_and_ignores_garbage()
    {
        var all = new List<WinMac.Bar.Peripherals.DeviceView>();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(new { kind = "devices", items = new object[] { new { name = "Ratón", type = "mouse", connection = "dongle", connected = true, percent = 23, charging = true }, new { name = "", type = "x" }, new { name = "Sin batería", percent = 150 } } });
        var parsed = WinMac.Bar.Notch.DevicesFeed.Parse(json); Assert.Equal(2, parsed.Count); Assert.Equal(23, parsed[0].Percent); Assert.True(parsed[0].Charging); Assert.Null(parsed[1].Percent);   // 150 no es un porcentaje válido
        Assert.Empty(WinMac.Bar.Notch.DevicesFeed.Parse(null)); Assert.Empty(WinMac.Bar.Notch.DevicesFeed.Parse(System.Text.Json.JsonSerializer.SerializeToElement("basura")));
        Assert.NotNull(WinMac.Bar.Notch.DevicesFeed.Payload(all));
    }
}
