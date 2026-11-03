using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class WorkAreaGuardTests
{
    [Fact]
    public void Wm003_reserved_only_when_work_area_starts_below_the_bar()
    {
        Assert.True(WorkAreaGuard.Reserved(workTop: 36, workBottom: 1032, barTop: 0, barBottom: 36));
        Assert.False(WorkAreaGuard.Reserved(workTop: 0, workBottom: 1032, barTop: 0, barBottom: 36));   // primer arranque sin reserva: tapa los menús
        Assert.True(WorkAreaGuard.Reserved(workTop: 84, workBottom: 1080, barTop: 48, barBottom: 84));  // barra de tareas arriba: la barra va debajo
    }

    [Fact]
    public void Wm003_retries_with_growing_wait_and_stops_when_confirmed()
    {
        var g = new WorkAreaGuard(); g.Placed(0);
        Assert.False(g.Due(1, reserved: false));    // Windows aún recoloca: no se mide antes de 2 s
        Assert.True(g.Due(2, reserved: false));     // intento 1
        Assert.False(g.Due(3, reserved: false));    // espera 2 s
        Assert.True(g.Due(4, reserved: false));     // intento 2 → espera 4 s
        Assert.False(g.Due(7, reserved: false));
        Assert.True(g.Due(8, reserved: false));
        Assert.False(g.Due(9, reserved: true));     // confirmado: se reinicia
        Assert.Equal(0, g.Failures);
        Assert.Equal(30, WorkAreaGuard.Backoff(20));
    }
}
