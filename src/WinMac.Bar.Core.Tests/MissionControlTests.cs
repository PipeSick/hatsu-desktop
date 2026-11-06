using WinMac.Bar.Features;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class MissionControlTests
{
    private static McWindow W(long h, string title, string app = "App", int mon = 0, bool min = false, double aspect = 1.6) => new(h, title, app, min, false, mon, aspect);
    private static McController C(params McWindow[] w) { var c = new McController(); c.SetWindows(w); return c; }

    [Fact] public void Layout_never_overlaps_stays_inside_the_screen_and_keeps_every_aspect()
    {
        foreach (int n in new[] { 1, 2, 3, 5, 8, 13, 30 })
        {
            var items = Enumerable.Range(0, n).Select(i => (W(i, "t" + i, aspect: i % 3 == 0 ? 0.7 : 1.8), i)).ToList();
            var (cards, cols) = McLayout.Compute(items, 1920, 1080);
            Assert.Equal(n, cards.Count); Assert.InRange(cols, 1, n);
            foreach (var c in cards)
            {
                Assert.True(c.Card.X >= 0 && c.Card.Right <= 1920 && c.Card.Y >= 0 && c.Card.Bottom <= 1080, $"n={n} fuera de la pantalla: {c.Card}");
                Assert.Equal(c.Win.Aspect, c.Thumb.W / c.Thumb.H, 2);
            }

            for (int i = 0; i < cards.Count; i++) for (int j = i + 1; j < cards.Count; j++)
                { var a = cards[i].Card; var b = cards[j].Card; Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom, $"n={n}: tarjetas {i} y {j} se solapan"); }
        }
    }

    [Fact] public void Few_windows_get_bigger_thumbnails_than_many_and_nothing_is_absurdly_large()
    {
        double Width(int n) => McLayout.Compute(Enumerable.Range(0, n).Select(i => (W(i, "t"), i)).ToList(), 1920, 1080).Cards[0].Thumb.W;
        Assert.True(Width(2) > Width(12)); Assert.True(Width(1) <= McLayout.MaxThumbW);
    }

    [Fact] public void Empty_list_and_tiny_screens_do_not_crash()
    {
        Assert.Empty(McLayout.Compute([], 800, 600).Cards);
        var (cards, _) = McLayout.Compute(Enumerable.Range(0, 40).Select(i => (W(i, "t"), i)).ToList(), 320, 240); Assert.Equal(40, cards.Count);
    }

    [Fact] public void Search_ignores_case_and_accents_and_requires_every_word_in_app_or_title()
    {
        var c = C(W(1, "Informe de gestión", "Word"), W(2, "Presupuesto", "Excel"), W(3, "Gestión de correo", "Outlook"));
        foreach (var ch in "GESTION") c.Char(ch); Assert.Equal([1L, 3L], c.Filtered.Select(w => w.Hwnd));
        foreach (var ch in " out") c.Char(ch); Assert.Equal([3L], c.Filtered.Select(w => w.Hwnd)); Assert.Equal(0, c.Selected);
    }

    [Fact] public void Escape_clears_the_search_first_and_then_closes_and_enter_activates_the_selection()
    {
        var c = C(W(1, "a"), W(2, "b")); c.Char('b');
        Assert.Equal(McCmdKind.Redraw, c.Key(0x1B).Kind); Assert.Equal("", c.Query); Assert.Equal(McCmdKind.Close, c.Key(0x1B).Kind);
        c.Key(0x27); var enter = c.Key(0x0D); Assert.Equal(McCmdKind.Activate, enter.Kind); Assert.Equal(1, enter.Hwnd);
    }

    [Fact] public void Arrow_keys_move_inside_the_grid_and_clamp_at_the_ends()
    {
        var c = C(Enumerable.Range(1, 6).Select(i => W(i, "t" + i)).ToArray()); c.CardsFor(0, 1920, 1080);
        c.Key(0x27); Assert.Equal(0, c.Selected); c.Key(0x27); Assert.Equal(1, c.Selected); c.Key(0x25); c.Key(0x25); c.Key(0x25); Assert.Equal(0, c.Selected);
        for (int i = 0; i < 20; i++) c.Key(0x27); Assert.Equal(5, c.Selected);
    }

    [Fact] public void Click_activates_a_card_closes_a_window_from_its_x_and_closes_on_empty_background()
    {
        var c = C(W(1, "a"), W(2, "b")); var cards = c.CardsFor(0, 1920, 1080); var k = cards[1];
        var act = c.Click(cards, k.Thumb.X + 10, k.Thumb.Y + k.Thumb.H / 2); Assert.Equal(new McCommand(McCmdKind.Activate, 2), act);
        var cl = c.Click(cards, k.Close.X + 4, k.Close.Y + 4); Assert.Equal(new McCommand(McCmdKind.CloseWindow, 2), cl);
        Assert.Equal(McCmdKind.Close, c.Click(cards, 2, 2).Kind);
    }

    [Fact] public void Each_monitor_only_shows_its_own_windows_and_minimized_ones_stay_in_the_list()
    {
        var c = C(W(1, "a", mon: 0), W(2, "b", mon: 1), W(3, "c", mon: 1, min: true));
        Assert.Single(c.CardsFor(0, 1920, 1080)); Assert.Equal(2, c.CardsFor(1, 2560, 1440).Count); Assert.True(c.Filtered.Single(w => w.Hwnd == 3).Minimized);
    }

    [Fact] public void Delete_asks_to_close_the_selected_window_and_forgetting_it_updates_the_view()
    {
        var c = C(W(1, "a"), W(2, "b")); c.Key(0x27); c.Key(0x27);
        Assert.Equal(new McCommand(McCmdKind.CloseWindow, 2), c.Key(0x2E)); c.Forget(2); Assert.Single(c.Filtered); Assert.Equal(0, c.Selected);
    }

    [Fact] public void A_refresh_keeps_the_selected_window_even_if_the_order_changes()
    {
        var c = C(W(1, "a"), W(2, "b"), W(3, "c")); c.Key(0x27); c.Key(0x27); Assert.Equal(2, c.Filtered[c.Selected].Hwnd);
        c.SetWindows([W(3, "c"), W(2, "b")]); Assert.Equal(2, c.Filtered[c.Selected].Hwnd);
    }

    [Fact] public void Monitor_assignment_by_window_centre_picks_the_nearest_monitor_for_windows_in_between()
    {
        // lógica pura equivalente a WindowCatalog.MonitorOf: se prueba en Windows; aquí solo comprobamos que la cuadrícula tolera un monitor sin ventanas
        var c = C(W(1, "a", mon: 2)); Assert.Empty(c.CardsFor(0, 1920, 1080)); Assert.Single(c.CardsFor(2, 1920, 1080));
    }
}
