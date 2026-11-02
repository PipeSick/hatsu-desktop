using WinMac.Bar.Hub;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Atribución de eventos del registro «Sistema» a discos: un evento solo cuenta para el disco que su ruta nombra; lo dudoso se descarta, no se reparte.
// Los casos reproducen lo que se vio en un PC real: 51 de disk en \Device\Harddisk0\DR0 y 11/129 de stornvme en \Device\RaidPort2 (disco 0 en el puerto SCSI 2).
public class DiskIncidentsTests
{
    private static readonly DateTime T = new(2026, 10, 29, 23, 24, 50);
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> Ports = new Dictionary<int, IReadOnlyList<int>> { [2] = [0], [3] = [1] };
    private static DiskEventRec Ev(string provider, int id, string path, int minutes = 0) => new(T.AddMinutes(minutes), provider, id, [path]);

    [Fact]
    public void Paging_errors_and_controller_resets_are_attributed_to_the_disk_their_path_names()
    {
        var map = DiskEventLog.Attribute([
            Ev("disk", 51, @"\Device\Harddisk0\DR0"), Ev("disk", 51, @"\Device\Harddisk0\DR0", 1),
            Ev("stornvme", 11, @"\Device\RaidPort2", 2000), Ev("stornvme", 129, @"\Device\RaidPort2", 2000)], Ports);
        var d0 = Assert.Single(map).Value;
        Assert.Equal(0, Assert.Single(map).Key);
        Assert.Equal((2, 1, 1), (d0.IoErrors, d0.ControllerErrors, d0.Resets));
        Assert.Equal(T.AddMinutes(2000), d0.Last);
        Assert.Equal("2 errores de E/S · 1 error de la controladora · 1 reinicio de la controladora · último 31/10 08:44", d0.Summary());
    }

    [Fact]
    public void Disk_numbers_are_not_confused_and_unknown_or_shared_ports_are_not_attributed()
    {
        var shared = new Dictionary<int, IReadOnlyList<int>> { [2] = [0, 1] };
        var map = DiskEventLog.Attribute([
            Ev("disk", 51, @"\Device\Harddisk10\DR10"),          // disco 10, no 1
            Ev("stornvme", 129, @"\Device\RaidPort2"),           // puerto con dos discos: no se sabe cuál
            Ev("stornvme", 129, @"\Device\RaidPort7"),           // puerto desconocido
            Ev("disk", 11, @"\Device\Harddisk1\DR1"),            // id que no cuenta para el proveedor disk
            Ev("storahci", 51, @"\Device\RaidPort2")], shared);  // id que no cuenta para una controladora
        Assert.Equal(10, Assert.Single(map).Key);
        Assert.Equal(1, map[10].IoErrors);
    }

    [Fact]
    public void Nothing_registered_means_no_incidents()
    {
        Assert.Empty(DiskEventLog.Attribute([], Ports));
        Assert.Empty(DiskEventLog.Attribute([new DiskEventRec(T, "disk", 51, [])], Ports));
    }
}
