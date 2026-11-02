using WinMac.Bar.Capture;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Capturas: geometría pura con distribuciones de monitores SIMULADAS (no es una validación física con monitores reales).
public class CaptureGeometryTests
{
    // Primario 1920×1080 al 100 %, uno a la IZQUIERDA 2560×1440 al 150 % (coordenadas negativas) y otro ENCIMA 1280×1024 al 125 %.
    private static readonly CaptureMonitor[] Mons =
    [
        new(new CaptureRect(0, 0, 1920, 1080), 1.0, true),
        new(new CaptureRect(-2560, -180, 2560, 1440), 1.5, false),
        new(new CaptureRect(320, -1024, 1280, 1024), 1.25, false),
    ];

    [Fact]
    public void VirtualScreenCoversAllMonitorsIncludingNegativeCoordinates()
    {
        var v = CaptureGeometry.VirtualScreen(Mons);
        Assert.Equal(new CaptureRect(-2560, -1024, 4480, 2284), v);
        Assert.Equal(1.5, CaptureGeometry.MonitorAt(Mons, -100, 500)!.Scale);
        Assert.Equal(1.25, CaptureGeometry.MonitorAt(Mons, 400, -10)!.Scale);
        Assert.True(CaptureGeometry.MonitorAt(Mons, 5000, 5000)!.Primary);   // outside every monitor: the primary
    }

    [Fact]
    public void DragInAnyDirectionAcrossMonitors()
    {
        var r = CaptureRect.FromPoints(100, 200, -300, -50);   // dragged up and left, into the left monitor
        Assert.Equal(new CaptureRect(-300, -50, 400, 250), r);
        Assert.Equal(r, CaptureGeometry.Target(CaptureMode.Region, Mons, [], 0, 0, r));
        Assert.True(CaptureGeometry.Target(CaptureMode.Region, Mons, [], 0, 0, CaptureRect.FromPoints(10, 10, 12, 40)).IsEmpty);   // a click is not a region
        Assert.Equal("400 × 250", CaptureGeometry.Dimensions(r));
    }

    [Fact]
    public void WindowAndMonitorModes()
    {
        var wins = new[] { new CaptureWindow(1, "Bloc de notas", new CaptureRect(100, 100, 800, 600)), new CaptureWindow(2, "Escritorio detrás", new CaptureRect(0, 0, 1920, 1080)) };
        Assert.Equal(new CaptureRect(100, 100, 800, 600), CaptureGeometry.Target(CaptureMode.Window, Mons, wins, 150, 150, null));   // the front one
        Assert.Equal(new CaptureRect(0, 0, 1920, 1080), CaptureGeometry.Target(CaptureMode.Window, Mons, wins, 1000, 900, null));
        var half = new CaptureWindow(3, "Medio fuera", new CaptureRect(1800, 900, 400, 400));
        Assert.Equal(new CaptureRect(1800, 900, 120, 180), CaptureGeometry.Target(CaptureMode.Window, Mons, [half], 1850, 950, null));   // clipped to the screen
        Assert.Equal(Mons[1].Bounds, CaptureGeometry.Target(CaptureMode.Monitor, Mons, [], -1000, 0, null));
        Assert.Equal(CaptureGeometry.VirtualScreen(Mons), CaptureGeometry.Target(CaptureMode.AllMonitors, Mons, [], 0, 0, null));
    }

    [Fact]
    public void CropCopiesTheRightPixelsFromTheFrozenFrame()
    {
        var frameRect = new CaptureRect(-2, -1, 4, 3);   // 4×3 frame starting at (-2,-1)
        var frame = new byte[4 * 3 * 4]; for (int i = 0; i < frame.Length; i += 4) frame[i] = (byte)(i / 4);   // pixel index in the blue channel
        var px = CaptureGeometry.Crop(frame, frameRect, new CaptureRect(-1, 0, 2, 2));
        Assert.Equal([5, 6, 9, 10], Enumerable.Range(0, 4).Select(i => (int)px[i * 4]));
        Assert.Empty(CaptureGeometry.Crop(frame, frameRect, new CaptureRect(10, 10, 5, 5)));
    }

    [Fact]
    public void FileNamesAreValidAndUnique()
    {
        var t = new DateTime(2026, 11, 2, 16, 24, 31);
        var taken = new HashSet<string> { "Captura 2026-11-02 a las 16.24.31.png" };
        Assert.Equal("Captura 2026-11-02 a las 16.24.31 (2).png", CaptureGeometry.FileName(t, ".png", taken.Contains));
        Assert.DoesNotContain(CaptureGeometry.FileName(t, ".png", _ => false), Path.GetInvalidFileNameChars());
    }
}
