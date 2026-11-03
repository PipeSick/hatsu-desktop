using Xunit;

namespace WinMac.Host.Tests;

/// <summary>WM-034: lógica pura de «Ocultar barra de tareas de Windows» (las llamadas a Windows se prueban a mano: docs/38, lista de validación).</summary>
public class TaskbarTests
{
    [Fact]
    public void Wm034_is_off_by_default_and_the_setting_lives_in_desktop_and_dock_independent_of_the_dock()
    {
        Assert.False(new HostConfig().HideTaskbar);
        string f = Path.Combine(Path.GetTempPath(), "winmac-tb-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new ConfigService(f, new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { });
            foreach (bool dockAutoHide in new[] { false, true })
                foreach (bool hide in new[] { false, true })   // las cuatro combinaciones con el Dock
                {
                    var dock = new WinMac.Dock.DockConfig { AutoHide = dockAutoHide }; a.LoadDock = () => dock; a.SaveDock = d => dock = d;
                    Assert.True(a.Apply(new SettingsEdit("host.hidetaskbar", "toggle", On: hide)));
                    Assert.Equal(hide, cfg.Current.HideTaskbar); Assert.Equal(dockAutoHide, dock.AutoHide);
                    var rows = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false) { Dock = dock, TaskbarStatus = "Oculta" }).Single(p => p.Id == "dock").Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id);
                    Assert.Equal(hide, rows["host.hidetaskbar"].On); Assert.True(rows["host.hidetaskbar"].Enabled);
                    Assert.Equal(hide, rows.ContainsKey("host.taskbarstate"));
                }
            Assert.True(HostConfig.Load(f).HideTaskbar);   // persiste en winmac.json
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Wm034_policy_hides_restores_and_never_fights_the_guard()
    {
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(enabled: false, leaseExists: false, hiddenByUs: false, anyVisible: true, suspended: false));   // apagado: ni se mira
        Assert.Equal(TaskbarStep.Hide, TaskbarPolicy.Decide(true, false, false, true, false));
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(true, true, true, false, false));   // ya oculta
        Assert.Equal(TaskbarStep.Hide, TaskbarPolicy.Decide(true, true, true, true, false));       // Explorer la volvió a mostrar (o la recreó)
        Assert.Equal(TaskbarStep.Restore, TaskbarPolicy.Decide(false, true, true, false, false));  // desactivado
        Assert.Equal(TaskbarStep.Restore, TaskbarPolicy.Decide(false, true, false, false, false)); // registro huérfano
        Assert.Equal(TaskbarStep.GiveUp, TaskbarPolicy.Decide(true, false, true, true, false));    // el guardián restauró: no se vuelve a ocultar
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(true, false, false, true, true));   // suspendida en esta sesión
    }

    [Fact]
    public void Wm034_the_reshow_limiter_gives_up_instead_of_flickering_and_forgets_old_hits()
    {
        var l = new ReshowLimiter(3, TimeSpan.FromSeconds(60)); var t = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(l.Allow(t)); Assert.True(l.Allow(t.AddSeconds(5))); Assert.True(l.Allow(t.AddSeconds(10)));
        Assert.False(l.Allow(t.AddSeconds(15)));
        var l2 = new ReshowLimiter(3, TimeSpan.FromSeconds(60));
        for (int i = 0; i < 10; i++) Assert.True(l2.Allow(t.AddMinutes(i)));   // una reaparición por minuto (cambiar de monitor, DPI) no es una pelea
    }

    [Fact]
    public void Wm045_the_cursor_revealing_the_taskbar_never_makes_winmac_give_up_and_a_give_up_expires()
    {
        var l = new ReshowLimiter(); var t = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 200; i++) Assert.True(l.Allow(t.AddSeconds(i * 1.5)));   // el cursor toca el borde cada 1,5 s durante 5 min: siempre se vuelve a ocultar
        var fight = new ReshowLimiter(); bool tripped = false;
        for (int i = 0; i < 40 && !tripped; i++) tripped = !fight.Allow(t.AddMilliseconds(i * 50));   // Explorer la muestra 20 veces por segundo: pelea real
        Assert.True(tripped);
        var until = t + TaskbarPolicy.RetryAfter;
        Assert.True(TaskbarPolicy.Suspended(t.AddSeconds(30), until)); Assert.False(TaskbarPolicy.Suspended(until.AddSeconds(1), until));   // se reintenta solo
        Assert.Equal(TaskbarStep.Hide, TaskbarPolicy.Decide(true, false, false, true, TaskbarPolicy.Suspended(until.AddSeconds(1), until)));
    }

    [Fact]
    public void Wm045_reconciliation_is_idempotent_at_start_after_explorer_restart_and_on_hot_changes()
    {
        // inicio con la barra visible → ocultar; ya oculta → nada; Explorer la recreó (visible, registro presente) → ocultar otra vez; desactivar en caliente → restaurar
        Assert.Equal(TaskbarStep.Hide, TaskbarPolicy.Decide(true, false, false, true, false));
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(true, true, true, false, false));
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(true, true, true, false, false));
        Assert.Equal(TaskbarStep.Hide, TaskbarPolicy.Decide(true, true, true, true, false));
        Assert.Equal(TaskbarStep.Restore, TaskbarPolicy.Decide(false, true, true, false, false));
        Assert.Equal(TaskbarStep.Nothing, TaskbarPolicy.Decide(false, false, false, true, false));   // apagado y restaurada: la barra queda como la tenga el usuario
    }

    [Fact]
    public void Wm034_the_lease_is_written_atomically_and_recovery_decisions_are_safe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "winmac-lease-" + Guid.NewGuid().ToString("N")), f = Path.Combine(dir, "taskbar-restore.json");
        try
        {
            Assert.Null(TaskbarLease.Load(f));
            var lease = new TaskbarLease(1234, true, DateTime.UtcNow); lease.Save(f);
            Assert.Equal(lease, TaskbarLease.Load(f)); Assert.False(File.Exists(f + ".tmp"));
            Assert.True(TaskbarPolicy.StaleOnStart(lease, _ => false, selfPid: 99));   // el WinMac que la ocultó ya no está: se restaura al arrancar
            Assert.False(TaskbarPolicy.StaleOnStart(lease, _ => true, selfPid: 99));   // sigue vivo (otro WinMac): no se toca
            Assert.False(TaskbarPolicy.StaleOnStart(null, _ => false, 99));
            Assert.True(TaskbarPolicy.GuardShouldRestore(lease, hostAlive: false)); Assert.False(TaskbarPolicy.GuardShouldRestore(lease, hostAlive: true)); Assert.False(TaskbarPolicy.GuardShouldRestore(null, false));
            File.WriteAllText(f, "{ roto");
            var broken = TaskbarLease.Load(f);
            Assert.NotNull(broken); Assert.False(broken!.KnowsOriginal);   // ilegible: se muestran las barras, el ajuste del usuario no se toca a ciegas
            Assert.True(TaskbarPolicy.StaleOnStart(broken, _ => true, 99));
            TaskbarLease.Delete(f); Assert.False(File.Exists(f)); TaskbarLease.Delete(f);
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }
}

public class ConnectSettingsTests
{
    [Fact]
    public void Connect_is_off_by_default_and_its_page_has_only_real_controls()
    {
        Assert.False(new HostConfig().Modules["connect"].Enabled);
        Assert.Contains(HostConfig.Known, k => k.Id == "connect" && k.Optional);   // sin el ejecutable: «no instalado», no un fallo
        var page = SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "connect");
        var rows = page.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id);
        Assert.False(rows["connect.module"].On); Assert.False(rows["connect.notifs"].Enabled);   // apagado: lo demás atenuado con su motivo
        Assert.DoesNotContain(rows.Keys, k => k.Contains("phonelink") || k.Contains("answer") || k.Contains("reject"));   // ni Phone Link ni contestar/rechazar sin validar
        Assert.Equal(SettingsBuilder.ContentNames.Length, rows["connect.content"].Options!.Count);
    }

    [Fact]
    public void Connect_settings_are_saved_and_published_to_the_module()
    {
        string f = Path.Combine(Path.GetTempPath(), "winmac-cn-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new ConfigService(f, new HostConfig()); var opened = new List<string>(); var a = new SettingsActions(cfg, () => { }, _ => { }) { OpenUri = opened.Add };
            Assert.True(a.Apply(new SettingsEdit("connect.module", "toggle", On: true))); Assert.True(cfg.Current.Modules["connect"].Enabled);
            Assert.True(a.Apply(new SettingsEdit("connect.content", "segment", Index: 2))); Assert.Equal(WinMac.Connect.ContentLevel.AppOnly, cfg.Connect.Content);
            Assert.True(a.Apply(new SettingsEdit("connect.device", "text", Text: "  iPhone de PipeSick "))); Assert.Equal("iPhone de PipeSick", cfg.Connect.DeviceName);
            Assert.True(a.Apply(new SettingsEdit("connect.calls", "toggle", On: false))); Assert.False(cfg.Connect.Calls);
            Assert.True(a.Apply(new SettingsEdit("connect.bt", "button"))); Assert.Equal(["ms-settings:bluetooth"], opened);
            var back = HostConfig.Load(f); Assert.Equal(cfg.Connect, back.Connect); Assert.True(back.Modules["connect"].Enabled);
        }
        finally { File.Delete(f); }
    }
}
