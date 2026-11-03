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
    public void Connect_page_has_only_real_actions_and_no_fake_device_data()
    {
        var page = SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "connect");
        var rows = page.Groups.SelectMany(g => g.Rows).ToList();
        Assert.All(rows, r => Assert.Contains(r.Kind, new[] { RowKind.Info, RowKind.Button }));   // ni interruptores ni estados de un teléfono que WinMac no lee
        Assert.Equal(["connect.phonelink", "connect.bt"], rows.Where(r => r.Kind == RowKind.Button).Select(r => r.Id));
        string f = Path.Combine(Path.GetTempPath(), "winmac-cn-" + Guid.NewGuid().ToString("N") + ".json");
        var opened = new List<string>(); var a = new SettingsActions(new ConfigService(f, new HostConfig()), () => { }, _ => { });
        Assert.False(a.Apply(new SettingsEdit("connect.phonelink", "button")));   // sin host no se finge
        a.OpenUri = opened.Add;
        Assert.True(a.Apply(new SettingsEdit("connect.phonelink", "button"))); Assert.True(a.Apply(new SettingsEdit("connect.bt", "button")));
        Assert.Equal([SettingsActions.PhoneLinkApp, "ms-settings:bluetooth"], opened);
        Assert.False(File.Exists(f));
    }
}
