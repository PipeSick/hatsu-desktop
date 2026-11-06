using WinMac.Bar;
using WinMac.Bar.Features;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class FeatureTests
{
    private sealed class Fake(string name) : IHatsuFeature
    {
        public string Id => name; public string Title => name; public string Description => "";
        public int Started, Stopped, Reconfigured; public Exception? FailStart, FailReconfigure; public bool OpenBeforeFail = true;
        public FeatureScope? Scope; public readonly List<string> Opened = [];
        public void Start(FeatureScope scope)
        {
            Started++; Scope = scope;
            if (OpenBeforeFail) scope.Add(() => { Stopped++; Opened.Add("closed"); });
            if (FailStart is not null) throw FailStart;
        }
        public void Reconfigure(BarConfig cfg) { Reconfigured++; if (FailReconfigure is not null) throw FailReconfigure; }
    }

    private static FeatureManager M(out Fake a, out Fake b)
    {
        var m = new FeatureManager(); a = new Fake("a"); b = new Fake("b");
        m.Register(a, c => c.McEnabled); m.Register(b, c => c.SnapEnabled); return m;
    }

    [Fact] public void Disabled_by_default_and_starts_only_the_enabled_ones()
    {
        var m = M(out var a, out var b); m.Apply(new BarConfig { McEnabled = true });
        Assert.Equal(FeatureState.Running, m.StateOf("a")); Assert.Equal(FeatureState.Disabled, m.StateOf("b")); Assert.Equal(1, a.Started); Assert.Equal(0, b.Started);
    }

    [Fact] public void Turning_a_feature_off_releases_everything_it_opened_and_a_second_apply_does_not_restart_it()
    {
        var m = M(out var a, out _); m.Apply(new BarConfig { McEnabled = true }); m.Apply(new BarConfig { McEnabled = true });
        Assert.Equal(1, a.Started); Assert.Equal(2, a.Reconfigured);   // una al arrancar, otra al repetir la configuración
        m.Apply(new BarConfig { McEnabled = false });
        Assert.Equal(FeatureState.Disabled, m.StateOf("a")); Assert.Equal(1, a.Stopped); Assert.True(a.Scope!.IsDisposed); Assert.True(a.Scope.Token.IsCancellationRequested);
        m.Apply(new BarConfig { McEnabled = true }); Assert.Equal(2, a.Started);   // se puede volver a encender
    }

    [Fact] public void A_failing_start_cleans_up_the_partial_work_and_does_not_stop_the_others()
    {
        var m = M(out var a, out var b); a.FailStart = new InvalidOperationException("sin permiso");
        m.Apply(new BarConfig { McEnabled = true, SnapEnabled = true });
        var st = m.Snapshot().Single(s => s.Id == "a");
        Assert.Equal(FeatureState.Failed, st.State); Assert.Contains("sin permiso", st.Detail); Assert.Equal(1, a.Stopped);
        Assert.Equal(FeatureState.Running, m.StateOf("b"));
    }

    [Fact] public void Failed_feature_can_be_retried_manually_and_the_counter_resets()
    {
        var m = M(out var a, out _); a.FailStart = new Exception("x"); var on = new BarConfig { McEnabled = true };
        m.Apply(on); Assert.Equal(FeatureState.Failed, m.StateOf("a"));
        a.FailStart = null; Assert.True(m.Retry("a")); Assert.Equal(FeatureState.Running, m.StateOf("a")); Assert.False(m.Retry("a"));
    }

    [Fact] public void Runtime_faults_restart_up_to_the_limit_then_stop_with_a_cause()
    {
        var m = M(out var a, out _); m.Apply(new BarConfig { McEnabled = true });
        for (int i = 0; i < FeatureManager.MaxRestarts; i++) { a.Scope!.Fault(new Exception("boom" + i)); m.Pump(); Assert.Equal(FeatureState.Running, m.StateOf("a")); }
        a.Scope!.Fault(new Exception("final")); m.Pump();
        var st = m.Snapshot().Single(s => s.Id == "a"); Assert.Equal(FeatureState.Failed, st.State); Assert.Contains("final", st.Detail); Assert.Equal(FeatureManager.MaxRestarts + 1, a.Started);
    }

    [Fact] public void A_reconfigure_error_keeps_it_running_with_a_warning()
    {
        var m = M(out var a, out _); m.Apply(new BarConfig { McEnabled = true }); a.FailReconfigure = new Exception("ajuste raro");
        m.Apply(new BarConfig { McEnabled = true, SnapGap = 3 });
        var st = m.Snapshot().Single(s => s.Id == "a"); Assert.Equal(FeatureState.Running, st.State); Assert.Contains("ajuste raro", st.Detail);
    }

    [Fact] public void Scope_cleans_in_reverse_order_survives_errors_and_late_additions_run_at_once()
    {
        var order = new List<int>(); var sc = new FeatureScope("t");
        sc.Add(() => order.Add(1)); sc.Add(() => throw new Exception("x")); sc.Add(() => order.Add(3));
        sc.Dispose(); sc.Dispose(); Assert.Equal([3, 1], order);
        sc.Add(() => order.Add(9)); Assert.Equal([3, 1, 9], order);
    }

    [Fact] public void Disposing_the_manager_releases_every_running_feature()
    {
        var m = M(out var a, out var b); m.Apply(new BarConfig { McEnabled = true, SnapEnabled = true }); m.Dispose();
        Assert.Equal(1, a.Stopped); Assert.Equal(1, b.Stopped);
    }

    [Fact] public void Status_changes_are_announced()
    {
        var m = M(out _, out _); int n = 0; m.StatusChanged += () => n++; m.Apply(new BarConfig { McEnabled = true }); Assert.True(n > 0);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Up", true, "Ctrl+Alt+Up")] [InlineData("ctrl + alt + space", true, "Ctrl+Alt+Space")] [InlineData("Alt+F9", true, "Alt+F9")]
    [InlineData("Shift+A", false, "")] [InlineData("A", false, "")] [InlineData("Ctrl+", false, "")] [InlineData("Ctrl+A+B", false, "")] [InlineData("Ctrl+F25", false, "")]
    public void Hotkey_parsing_requires_ctrl_or_alt_and_one_key(string text, bool ok, string canon)
    {
        Assert.Equal(ok, HotkeySpec.TryParse(text, out var s)); if (ok) Assert.Equal(canon, s.Format());
    }

    [Fact] public void Windows_reserved_combinations_are_refused_and_others_conflict_by_owner()
    {
        var r = new HotkeyRegistry();
        Assert.Equal(ClaimStatus.Reserved, r.TryClaim("mission", "open", "Mission Control", "Alt+Tab").Status);
        Assert.Equal(ClaimStatus.Reserved, r.TryClaim("mission", "open", "Mission Control", "Ctrl+Shift+Esc").Status);
        Assert.Equal(ClaimStatus.Invalid, r.TryClaim("mission", "open", "Mission Control", "Up").Status);
        Assert.True(r.TryClaim("mission", "open", "Mission Control", "Ctrl+Alt+Up").Ok);
        var c = r.TryClaim("livetext", "open", "Live Text", "ctrl+alt+up");
        Assert.Equal(ClaimStatus.Conflict, c.Status); Assert.Contains("Mission Control", c.With);
        Assert.True(r.TryClaim("mission", "open", "Mission Control", "Ctrl+Alt+Up").Ok);   // el mismo dueño puede repetir su propia combinación
    }

    [Fact] public void Reassign_releases_the_old_combination_and_a_failed_reassign_keeps_it()
    {
        var r = new HotkeyRegistry(); r.TryClaim("a", "x", "A", "Ctrl+Alt+1"); r.TryClaim("b", "y", "B", "Ctrl+Alt+2");
        Assert.False(r.TryClaim("a", "x", "A", "Ctrl+Alt+2").Ok); Assert.Contains(r.Claims, c => c.Owner == "a" && c.Spec.Format() == "Ctrl+Alt+1");
        Assert.True(r.TryClaim("a", "x", "A", "Ctrl+Alt+3").Ok); Assert.True(r.IsAvailable("Ctrl+Alt+1", "b", "y"));
        Assert.Equal(1, r.ReleaseOwner("a")); Assert.True(r.IsAvailable("Ctrl+Alt+3", "b", "y"));
    }

    [Fact] public void Existing_shortcuts_noted_by_the_bar_are_seen_as_conflicts_for_new_modules()
    {
        var r = new HotkeyRegistry(); r.Note("Spotlight", "main", "Spotlight", "Ctrl+Alt+Space");
        Assert.Equal(ClaimStatus.Conflict, r.TryClaim("livetext", "open", "Live Text", "Ctrl+Alt+Space").Status);
    }

    [Fact] public void Config_sanitizes_the_new_module_fields_and_keeps_an_intentionally_empty_hotkey()
    {
        var c = new BarConfig { McHotkey = "Alt+Tab", LtHotkey = "", SnapModifier = "Win", SnapGap = 999, GestCornerDelayMs = 1, GestCornerTL = "inventada", GestCornerTR = "mission" }.Sanitize();
        Assert.Equal("Alt+Tab", c.McHotkey is "Alt+Tab" ? "Alt+Tab" : "x");   // sintácticamente válida: la reserva la decide el registro, no la configuración
        Assert.Equal("", c.LtHotkey); Assert.Equal("Shift", c.SnapModifier); Assert.Equal(64, c.SnapGap); Assert.Equal(150, c.GestCornerDelayMs);
        Assert.Equal("none", c.GestCornerTL); Assert.Equal("mission", c.GestCornerTR);
        Assert.Equal("Ctrl+Alt+Up", new BarConfig { McHotkey = "basura" }.Sanitize().McHotkey);
    }

    [Fact] public void Module_fields_survive_a_patch_round_trip_and_all_default_off_except_spotlight_actions()
    {
        var d = new BarConfig(); Assert.False(d.McEnabled); Assert.False(d.SnapEnabled); Assert.False(d.LiveTextEnabled); Assert.False(d.GesturesEnabled); Assert.True(d.SpotActionsEnabled);
        var p = System.Text.Json.JsonDocument.Parse("{\"mcEnabled\":true,\"mcHotkey\":\"Ctrl+Alt+M\",\"snapGap\":12,\"nope\":1}").RootElement;
        var c = d.ApplyPatch(p); Assert.True(c.McEnabled); Assert.Equal("Ctrl+Alt+M", c.McHotkey); Assert.Equal(12, c.SnapGap);
        Assert.Equal(c, BarConfig.FromJson(c.ToJson()));
    }
}
