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

public class CaptureEditTests
{
    [Fact]
    public void UndoRedoKeepsTheOriginalAndRedoIsClearedByANewStep()
    {
        var d = new EditDocument(800, 600);
        d.Add(new RectShape(EditColor.Red, 4, 10, 10, 100, 100));
        d.Add(new ArrowShape(EditColor.Red, 4, 0, 0, 50, 50));
        Assert.True(d.Undo()); Assert.Single(d.Annotations); Assert.True(d.CanRedo);
        Assert.True(d.Redo()); Assert.Equal(2, d.Annotations.Count);
        d.Undo(); d.Add(new HighlightShape(EditColor.Yellow, 0, 0, 20, 20)); Assert.False(d.CanRedo);
        while (d.Undo()) { }
        Assert.False(d.IsDirty); Assert.Equal(new CaptureRect(0, 0, 800, 600), d.Crop);
    }

    [Fact]
    public void CropIsClippedUndoableAndTinyCropsAreIgnored()
    {
        var d = new EditDocument(800, 600);
        Assert.False(d.SetCrop(new CaptureRect(5, 5, 2, 2)));
        Assert.True(d.SetCrop(new CaptureRect(700, 500, 400, 400)));
        Assert.Equal(new CaptureRect(700, 500, 100, 100), d.Crop);
        d.Undo(); Assert.Equal(new CaptureRect(0, 0, 800, 600), d.Crop);
    }

    [Fact]
    public void EmptyTextAndSinglePointStrokesAreNotSteps()
    {
        var d = new EditDocument(10, 10);
        d.Add(new TextShape(EditColor.White, 20, 1, 1, "  "));
        d.Add(new PenStroke(EditColor.Red, 3, [(1f, 1f)]));
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void ViewMappingRoundTripsAndNeverUpscales()
    {
        var v = EditView.Fit(new CaptureRect(100, 50, 400, 300), 0, 60, 1600, 900);
        Assert.Equal(1f, v.Scale);
        var big = EditView.Fit(new CaptureRect(0, 0, 3840, 2160), 0, 0, 1920, 1000);
        var (ix, iy) = big.ToImage(big.ToView(1234, 567).X, big.ToView(1234, 567).Y);
        Assert.Equal(1234, ix, 2); Assert.Equal(567, iy, 2);
        Assert.True(big.Scale < 0.5f);
    }

    [Fact]
    public void EditedCopyNeverOverwrites()
    {
        var existing = new HashSet<string> { System.IO.Path.Combine("c", "Captura (editada).png") };
        Assert.Equal(System.IO.Path.Combine("c", "Captura (editada 2).png"), EditDocument.EditedPath(System.IO.Path.Combine("c", "Captura.png"), existing.Contains));
    }

    [Fact]
    public void ArrowHeadPointsBackFromTheTip()
    {
        var (a, b) = EditDocument.ArrowHead(0, 0, 100, 0, 20);
        Assert.True(a.X < 100 && b.X < 100); Assert.True(a.Y * b.Y < 0);
    }
}

public class RecordingGeometryTests
{
    [Fact]
    public void RecordingAreaIsEvenAndNamedLikeWindows()
    {
        Assert.Equal(new CaptureRect(-2559, 10, 440, 300), CaptureGeometry.EvenRect(new CaptureRect(-2559, 10, 441, 301)));
        Assert.Equal("Grabación 2026-11-02 a las 17.20.05.mp4", CaptureGeometry.RecordingName(new DateTime(2026, 11, 2, 17, 20, 5)));
    }
}

public class FramePoolTests
{
    [Fact]
    public void ThePoolNeverGrowsBeyondItsLimitHoweverLongTheRecording()
    {
        var pool = new FramePool(16, 3);
        for (int i = 0; i < 1000; i++) { var b = pool.Rent(TimeSpan.FromMilliseconds(10))!; pool.Return(b); }
        Assert.Equal(1, pool.Allocated);
        var held = new[] { pool.Rent(TimeSpan.Zero), pool.Rent(TimeSpan.Zero), pool.Rent(TimeSpan.Zero) };
        Assert.All(held, Assert.NotNull);
        Assert.Null(pool.Rent(TimeSpan.FromMilliseconds(20)));   // all with the encoder: the frame is skipped, nothing new is allocated
        Assert.Equal(3, pool.Allocated); Assert.Equal(1, pool.Starved);
        pool.Return(held[0]!); Assert.NotNull(pool.Rent(TimeSpan.Zero)); Assert.Equal(3, pool.Allocated);
    }
}
