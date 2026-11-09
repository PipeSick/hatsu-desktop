using WinMac.Apps;
using WinMac.Bar.Features;
using WinMac.Bar.Launcher;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class ActionRegistryTests
{
    private static SpotAction A(string id, string title, string? module = null, ActionRisk risk = ActionRisk.Normal, Func<bool>? avail = null, Func<ActionResult>? run = null, string kw = "")
        => new(id, title, "d", kw, module, risk, [], avail, run ?? (() => ActionResult.Success));

    [Fact] public void Registered_actions_are_found_searched_and_executed_by_id()
    {
        var r = new ActionRegistry(); int n = 0; r.Register(A("x:one", "Mission Control", "mission", run: () => { n++; return ActionResult.Success; }));
        Assert.Contains(r.Search("mission"), x => x.Item.Target == "x:one"); Assert.True(r.Execute("x:one").Ok); Assert.Equal(1, n);
        Assert.False(r.Register(A("x:one", "otra")));   // no se pisa
    }

    [Fact] public void Actions_of_a_switched_off_module_disappear_and_cannot_be_run()
    {
        var r = new ActionRegistry(); r.Register(A("snap:next", "Snap siguiente", "snap")); r.Register(A("core:a", "Capturar región"));
        Assert.Equal(1, r.UnregisterModule("snap")); Assert.DoesNotContain(r.Search("snap siguiente"), x => x.Item.Target == "snap:next");
        var res = r.Execute("snap:next"); Assert.False(res.Ok); Assert.Contains("ya no está disponible", res.Error); Assert.True(r.Execute("core:a").Ok);
    }

    [Fact] public void Availability_hides_the_action_and_blocks_its_execution()
    {
        bool rec = false; var r = new ActionRegistry(); r.Register(A("rec:stop", "Detener la grabación", avail: () => rec));
        Assert.Empty(r.Search("detener la grabación")); Assert.False(r.Execute("rec:stop").Ok); rec = true; Assert.NotEmpty(r.Search("detener la grabación")); Assert.True(r.Execute("rec:stop").Ok);
    }

    [Fact] public void Risky_actions_ask_for_a_second_enter_and_a_failing_action_returns_an_error_instead_of_throwing()
    {
        var r = new ActionRegistry(); r.Register(A("sys:off", "Apagar el equipo", risk: ActionRisk.Confirm)); r.Register(A("bad", "Rota", run: () => throw new InvalidOperationException("boom")));
        Assert.True(r.Search("apagar el equipo").Single(x => x.Item.Target == "sys:off").Item.Confirm);
        var res = r.Execute("bad"); Assert.False(res.Ok); Assert.Contains("boom", res.Error);
    }

    [Fact] public void Keywords_find_an_action_by_a_synonym_but_only_for_four_or_more_letters()
    {
        var r = new ActionRegistry(); r.Register(A("c", "Grabar la pantalla", kw: "grabacion video record"));
        Assert.Contains(r.Search("video"), x => x.Item.Target == "c"); Assert.Empty(r.Search("vi"));
    }

    private static IEnumerable<SpotAction> Broken(string q) => throw new Exception("x");
    private static SpotAction? BrokenResolve(string id) => throw new Exception("y");

    [Fact] public void Providers_add_dynamic_actions_resolve_them_by_id_and_a_broken_provider_does_not_hide_the_rest()
    {
        var r = new ActionRegistry(); r.Register(A("core:a", "Capturar región"));
        r.RegisterProvider("snap", q => new[] { "Mitades", "Tercios" }.Select(n => A("snap:layout:" + n, "Snap: usar la distribución «" + n + "»", "snap")), id => id == "snap:layout:Tercios" ? A(id, "Tercios", "snap") : null);
        r.RegisterProvider("rota", Broken, BrokenResolve);
        Assert.Equal(2, r.Search("snap usar la distribución").Count(x => x.Item.Target.StartsWith("snap:layout:")));
        Assert.NotNull(r.Find("snap:layout:Tercios")); Assert.Null(r.Find("snap:layout:Nada")); Assert.NotEmpty(r.Search("capturar región"));
        r.UnregisterModule("snap"); Assert.Null(r.Find("snap:layout:Tercios"));
    }

    [Fact] public void Spotlight_uses_the_registry_when_given_one_and_the_classic_list_otherwise()
    {
        var r = new ActionRegistry(); r.Register(A("snap:next", "Snap: ventana a la zona siguiente", "snap"));
        IReadOnlyList<SpotItem> Q(string t, ActionRegistry? reg) => SpotlightEngine.Query(t, [], null, [], [], null, false, null, null, null, reg);
        Assert.Contains(Q("snap ventana", r), x => x.Target == "snap:next"); Assert.DoesNotContain(Q("snap ventana", null), x => x.Target == "snap:next");
        Assert.Contains(Q("capturar región", null), x => x.Target == "winmac:capture:region");        // lo de siempre sigue sin registro
        Assert.DoesNotContain(Q("capturar región", r), x => x.Target == "winmac:capture:region");     // con registro vacío no hay acciones de núcleo: las registra el módulo al arrancar
    }

    [Fact] public void Concurrent_registration_and_search_do_not_throw()
    {
        var r = new ActionRegistry(); Parallel.For(0, 200, i => { r.Register(A("a" + i, "Acción " + i)); _ = r.Search("acción").ToList(); if (i % 3 == 0) r.Unregister("a" + i); });
        Assert.NotEmpty(r.All);
    }
}
