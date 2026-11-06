using WinMac.Bar;
using WinMac.Bar.Vpn;
using Xunit;

public class VpnTests
{
    private const string Pbk = "\ufeff[Trabajo]\r\nEncoding=1\r\nType=2\r\nAutoLogon=0\r\n\r\n[Casa WireGuard]\r\nType=2\r\n\r\n[Banda ancha]\r\nType=5\r\n\r\n[Trabajo]\r\nType=2\r\n\r\n[Sin tipo]\r\nEncoding=1\r\n";
    private static BarState St(bool? vpn = null, string? tip = null) => new(new DateTime(2026, 11, 6, 9, 0, 0), "App", new BatteryInfo(true, 80, false, true), new VolumeInfo(true, 50, false), new NetworkInfo(NetKind.Wifi), VpnConnected: vpn, VpnTip: tip);

    [Fact]
    public void Phonebook_only_lists_vpn_entries_once()
    {
        var l = PhonebookParser.Vpns(Pbk, @"C:\x\rasphone.pbk");
        Assert.Equal(["Trabajo", "Casa WireGuard"], l.Select(x => x.Name));
        Assert.All(l, x => Assert.Equal(@"C:\x\rasphone.pbk", x.Phonebook));
        Assert.Empty(PhonebookParser.Vpns("", "p"));
    }

    [Fact]
    public void Bar_item_only_with_the_option_and_dim_when_disconnected()
    {
        Assert.DoesNotContain(BarModel.Build(St(), new BarConfig()).Right, i => i.Kind == BarItemKind.Vpn);
        var on = BarModel.Build(St(true, "VPN · conectada: Trabajo"), new BarConfig { ShowVpn = true }).Right.Single(i => i.Kind == BarItemKind.Vpn);
        Assert.Equal(BarItemLook.Normal, on.Look); Assert.Equal("VPN · conectada: Trabajo", on.Tip);
        var off = BarModel.Build(St(false), new BarConfig { ShowVpn = true }).Right.Single(i => i.Kind == BarItemKind.Vpn);
        Assert.Equal(BarItemLook.Dim, off.Look);
        Assert.Equal(PanelKind.Vpn, BarModel.ClickAction(off).Panel);
    }

    [Fact]
    public void Panel_lists_profiles_and_blocks_other_rows_while_busy()
    {
        var cfg = new BarConfig { ShowVpn = true }; var st = new VpnState(true, [new("Trabajo", true), new("Casa", false)]);
        var p = PanelBuilder.VpnPanel(PanelContext.Initial(cfg, DateTime.Now) with { Vpn = new VpnView(st) });
        var rows = p.Widgets.Where(w => w.Kind == WidgetKind.Row).ToList();
        Assert.Equal(["vpn:toggle:Trabajo", "vpn:toggle:Casa"], rows.Select(r => r.Id));
        Assert.True(rows[0].On); Assert.False(rows[1].On); Assert.All(rows, r => Assert.True(r.Enabled));
        var busy = PanelBuilder.VpnPanel(PanelContext.Initial(cfg, DateTime.Now) with { Vpn = new VpnView(st, "Casa", "Conectando…", null) });
        Assert.All(busy.Widgets.Where(w => w.Kind == WidgetKind.Row), r => Assert.False(r.Enabled));
        Assert.Contains(busy.Widgets, w => w.Sub == "Conectando…");
        Assert.Contains(p.Widgets, w => w.Id == PanelBuilder.VpnOpenSettings);
    }

    [Fact]
    public void Panel_explains_empty_unavailable_and_errors()
    {
        var cfg = new BarConfig { ShowVpn = true };
        Assert.Contains(PanelBuilder.VpnPanel(PanelContext.Initial(cfg, DateTime.Now) with { Vpn = new VpnView(new VpnState(true, [])) }).Widgets, w => w.Id == "vpn-empty");
        Assert.Contains(PanelBuilder.VpnPanel(PanelContext.Initial(cfg, DateTime.Now) with { Vpn = new VpnView(VpnState.None("sin acceso")) }).Widgets, w => w.Id == "vpn-na" && w.Text == "sin acceso");
        var err = PanelBuilder.VpnPanel(PanelContext.Initial(cfg, DateTime.Now) with { Vpn = new VpnView(new VpnState(true, [new("A", false)]), null, null, VpnText.Explain(691)) });
        Assert.Contains(err.Widgets, w => w.Id == "vpn-msg" && w.Text.Contains("Credenciales"));
        Assert.Contains("999", VpnText.Explain(999));
    }

    [Fact]
    public void Summary_texts()
    {
        Assert.Equal("Sin conexiones VPN", VpnText.Summary(new VpnState(true, [])));
        Assert.Equal("Conectada", VpnText.Summary(new VpnState(true, [new("A", true)])));
        Assert.Equal("2 conectadas", VpnText.Summary(new VpnState(true, [new("A", true), new("B", true)])));
        Assert.Equal("Desconectada", VpnText.Summary(new VpnState(true, [new("A", false)])));
        Assert.Equal("VPN", VpnText.Tip(new VpnState(true, [])));
    }
}
