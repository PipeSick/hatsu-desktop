using WinMac.Bar;
using WinMac.Bar.Tray;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Iconos de la bandeja de Windows en la barra. Búferes sintéticos con la disposición documentada: NO prueban que Windows los envíe (eso se mide en el PC).
public class TrayTests
{
    private static TrayMessage Msg(TrayCommand c, uint hwnd = 0x1234, uint id = 1, uint flags = TrayProtocol.NIF_MESSAGE | TrayProtocol.NIF_ICON | TrayProtocol.NIF_TIP, uint icon = 0x55, string tip = "Discord", uint state = 0, uint mask = 0, uint version = 0, Guid? guid = null) =>
        TrayProtocol.Parse(TrayProtocol.Build(c, hwnd, id, flags, 0x8001, icon, tip, state, mask, version, guid))!;

    [Fact]
    public void The_tray_message_is_read_from_its_documented_offsets()
    {
        var g = Guid.NewGuid();
        var m = TrayProtocol.Parse(TrayProtocol.Build(TrayCommand.Add, 0xABCD, 7, TrayProtocol.NIF_MESSAGE | TrayProtocol.NIF_ICON | TrayProtocol.NIF_TIP | TrayProtocol.NIF_GUID, 0x8042, 0x99, "Steam — conectado", version: 4, guid: g))!;
        Assert.Equal((TrayCommand.Add, 0xABCDu, 7u, 0x8042u, 0x99u, "Steam — conectado", 4u, g), (m.Command, m.Hwnd, m.Id, m.CallbackMessage, m.Icon, m.Tip, m.Version, m.Guid!.Value));
        Assert.Null(TrayProtocol.Parse(new byte[100]));                                   // truncado
        var bad = TrayProtocol.Build(TrayCommand.Add, 1, 1, 0); bad[0] = 0; Assert.Null(TrayProtocol.Parse(bad));   // sin la firma de la bandeja
        Assert.Equal("", TrayProtocol.Parse(TrayProtocol.Build(TrayCommand.Add, 1, 1, TrayProtocol.NIF_ICON, tip: "no cuenta"))!.Tip);   // sin NIF_TIP el texto no vale
    }

    [Fact]
    public void Icons_are_added_changed_hidden_and_removed_and_unknown_changes_are_ignored()
    {
        var s = new TrayIconStore();
        Assert.False(s.Apply(Msg(TrayCommand.Modify)));                                   // Explorer también lo rechaza
        Assert.True(s.Apply(Msg(TrayCommand.Add))); Assert.Equal("Discord", Assert.Single(s.Visible).Tip);
        Assert.True(s.Apply(Msg(TrayCommand.Modify, flags: TrayProtocol.NIF_TIP, tip: "Discord — 3 menciones")));
        Assert.Equal(0x55u, s.Visible[0].Icon);                                           // lo no marcado se conserva
        Assert.True(s.Apply(Msg(TrayCommand.Modify, flags: TrayProtocol.NIF_STATE, state: TrayProtocol.NIS_HIDDEN, mask: TrayProtocol.NIS_HIDDEN)));
        Assert.Empty(s.Visible); Assert.Equal(1, s.Count);
        s.Apply(Msg(TrayCommand.SetVersion, version: 4)); Assert.Equal(4u, s.Get(TrayIconStore.KeyOf(Msg(TrayCommand.Add)))!.Version);
        Assert.True(s.Apply(Msg(TrayCommand.Delete))); Assert.Equal(0, s.Count);
        s.Apply(Msg(TrayCommand.Add, icon: 0)); Assert.Empty(s.Visible);                  // sin imagen todavía: no se muestra
    }

    [Fact]
    public void Order_is_arrival_and_closed_apps_icons_are_cleaned_up()
    {
        var s = new TrayIconStore(); s.Apply(Msg(TrayCommand.Add, hwnd: 1, tip: "A")); s.Apply(Msg(TrayCommand.Add, hwnd: 2, tip: "B")); s.Apply(Msg(TrayCommand.Add, hwnd: 3, tip: "C"));
        Assert.Equal(["A", "B", "C"], s.Visible.Select(i => i.Tip));
        Assert.True(s.RemoveDead(h => h != 2)); Assert.Equal(["A", "C"], s.Visible.Select(i => i.Tip));
        var g = Guid.NewGuid(); s.Apply(Msg(TrayCommand.Add, hwnd: 9, guid: g, flags: TrayProtocol.NIF_ICON | TrayProtocol.NIF_GUID)); s.Apply(Msg(TrayCommand.Modify, hwnd: 10, guid: g, flags: TrayProtocol.NIF_TIP | TrayProtocol.NIF_GUID, tip: "por GUID"));
        Assert.Equal("por GUID", s.Get("g:" + g.ToString("N"))!.Tip);                      // con GUID, la identidad no depende de la ventana
    }

    [Fact]
    public void Clicks_send_what_explorer_sends_for_each_icon_version()
    {
        var v4 = new TrayIcon("k", 1, 7, 0x8001, 1, "", 4, false, 0); var old = v4 with { Version = 0 };
        var left = TrayClick.Messages(v4, TrayButton.Left, 1000, 14);
        Assert.Equal([(nint)((14 << 16) | 1000)], left.Select(m => m.WParam).Distinct());
        Assert.Equal([TrayClick.WM_LBUTTONDOWN, TrayClick.WM_LBUTTONUP, TrayClick.NIN_SELECT], left.Select(m => (uint)(m.LParam & 0xFFFF)));
        Assert.All(left, m => Assert.Equal(7, (int)((m.LParam >> 16) & 0xFFFF)));
        Assert.Equal(TrayClick.WM_CONTEXTMENU, (uint)(TrayClick.Messages(v4, TrayButton.Right, 0, 0)[^1].LParam & 0xFFFF));
        Assert.Equal([((nint)7, (nint)TrayClick.WM_RBUTTONDOWN), ((nint)7, (nint)TrayClick.WM_RBUTTONUP)], TrayClick.Messages(old, TrayButton.Right, 5, 5));
    }

    [Fact]
    public void Tray_icons_sit_first_on_the_right_go_first_when_space_runs_out_and_right_click_opens_their_menu()
    {
        var icons = Enumerable.Range(0, 30).Select(i => new TrayIcon("k" + i, (uint)i + 1, 1, 0, 9, "app" + i, 4, false, i)).ToList();
        var st = BarState.Empty(new DateTime(2026, 11, 2, 10, 0, 0)) with { TrayIcons = icons };
        var (_, right) = BarModel.Build(st, new BarConfig());
        Assert.Equal(BarItemKind.TrayIcon, right[0].Kind); Assert.Equal("tray:k0", right[0].Id); Assert.Equal(BarItemKind.Clock, right[^1].Kind);
        var f = BarLayout.Compute(new BarMetrics(), new BarConfig(), 1280, [], right, (t, _) => t.Length * 7);
        var shown = f.Items.Where(i => i.Item.Kind == BarItemKind.TrayIcon).ToList();
        Assert.InRange(shown.Count, 1, 29); Assert.Equal("tray:k29", shown[^1].Item.Id);   // no caben todos: se quedan los de la derecha
        Assert.Contains(f.Items, i => i.Item.Kind == BarItemKind.Clock);
        Assert.DoesNotContain(BarModel.Build(st, new BarConfig { ShowTrayIcons = false }).Right, i => i.Kind == BarItemKind.TrayIcon);
        var item = right[3];
        Assert.Equal(new BarAction(BarActionKind.TrayClick, Uri: "k3"), BarModel.ClickAction(item));
        Assert.Equal(new BarAction(BarActionKind.TrayRightClick, Uri: "k3"), BarModel.RightClickAction(item));
        Assert.Equal(BarActionKind.ShowMenu, BarModel.RightClickAction(null).Kind);
    }
}
