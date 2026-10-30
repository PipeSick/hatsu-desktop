// S3 — Prototipo mínimo del Dock sobre WinUI 3 (Windows App SDK, sin empaquetar, SIN archivos .xaml: todo en código).
// Uso:  S3.DockWinUI [--variant acrylic|plain] [--pose rest|hover|hidden] [--interactive] [--quick] [--out DIR]
//   acrylic : Window.SystemBackdrop = DesktopAcrylicBackdrop (cubre TODA la ventana; el material no sigue la forma de la barra).
//   plain   : sin backdrop; sirve para comprobar visualmente si WinUI 3 admite fondo transparente (hipótesis: NO).
// ESTADO: compila (verificado en Linux hasta el ensamblado; el paso makepri/PRI solo corre en Windows). NO ejecutado.
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XPath = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Windows.UI;
using WinMac.Spikes.Common;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var opt = CliOptions.Parse(args, "acrylic");
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App(opt);
        });
    }
}

public sealed class App : Application
{
    private readonly CliOptions _opt; private DockWindow? _w;
    public App(CliOptions opt) { _opt = opt; Resources.MergedDictionaries.Add(new XamlControlsResources()); }
    protected override void OnLaunched(LaunchActivatedEventArgs args) { _w = new DockWindow(_opt); _w.Activate(); }
}

public sealed class DockWindow : Window
{
    private readonly CliOptions _opt; private readonly BenchRun _run;
    private readonly Canvas _root = new();
    private readonly Border _bar = new();
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _sep = new();
    private readonly XPath[] _icons = new XPath[DockScene.Items.Length];
    private readonly TextBlock[] _glyphs = new TextBlock[DockScene.Items.Length];
    private readonly Ellipse[] _dots = new Ellipse[DockScene.Items.Length];
    private readonly AppWindow _aw; private readonly IntPtr _hwnd;
    private double _scale = 1; private int _baseX, _baseY; private double? _mouseX;

    public DockWindow(CliOptions opt)
    {
        _opt = opt; _run = new BenchRun("S3.DockWinUI", opt);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _aw = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        if (_aw.Presenter is OverlappedPresenter p) { p.SetBorderAndTitleBar(false, false); p.IsResizable = false; p.IsAlwaysOnTop = true; p.IsMaximizable = false; p.IsMinimizable = false; }
        _aw.IsShownInSwitchers = false;
        if (opt.Variant == "acrylic") SystemBackdrop = new DesktopAcrylicBackdrop();

        _scale = GetDpiForWindow(_hwnd) / 96.0;
        var wa = DisplayArea.GetFromWindowId(_aw.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(DockScene.WindowWidth * _scale), h = (int)(DockScene.WindowHeight * _scale);
        _baseX = wa.X + (wa.Width - w) / 2; _baseY = wa.Y + wa.Height - h;
        _aw.MoveAndResize(new Windows.Graphics.RectInt32(_baseX, _baseY, w, h));

        BuildScene();
        Content = _root;
        _root.KeyboardAccelerators.Add(MakeEsc());
        _root.Loaded += (_, _) => OnLoaded();
        if (opt.Interactive)
        {
            _root.PointerMoved += (_, e) => _mouseX = e.GetCurrentPoint(_root).Position.X - (DockScene.WindowWidth - DockScene.RestWidth) / 2;
            _root.PointerExited += (_, _) => _mouseX = null;
        }
        Apply(StaticPose());
    }

    private KeyboardAccelerator MakeEsc()
    {
        var k = new KeyboardAccelerator { Key = VirtualKey.Escape };
        k.Invoked += (_, e) => { e.Handled = true; Close(); };
        return k;
    }

    private DockPose StaticPose() => _opt.Pose switch
    {
        Pose.Hover => new DockPose(DockScene.PadX + 5 * (DockScene.BaseSize + DockScene.Gap) + DockScene.BaseSize / 2, 0),
        Pose.Hidden => new DockPose(null, 1),
        _ => new DockPose(null, 0),
    };

    private static SolidColorBrush Br(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    private void BuildScene()
    {
        _bar.CornerRadius = new CornerRadius(DockScene.BarRadius); _bar.BorderThickness = new Thickness(1);
        _bar.Background = Br(_opt.Variant == "acrylic" ? (byte)22 : (byte)90, 255, 255, 255);
        _bar.BorderBrush = Br(110, 255, 255, 255);
        _root.Children.Add(_bar);
        _sep.Width = 1; _sep.Fill = Br(90, 255, 255, 255); _root.Children.Add(_sep);

        var fig = new PathFigure { IsClosed = true, IsFilled = true };
        var pts = Squircle.Unit();
        fig.StartPoint = new Point(pts[0].X, pts[0].Y);
        var seg = new PolyLineSegment();
        foreach (var q in pts.Skip(1)) seg.Points.Add(new Point(q.X, q.Y));
        fig.Segments.Add(seg);
        var geo = new PathGeometry(); geo.Figures.Add(fig);

        for (int i = 0; i < _icons.Length; i++)
        {
            var it = DockScene.Items[i];
            var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, (byte)Math.Min(255, it.R + 70), (byte)Math.Min(255, it.G + 70), (byte)Math.Min(255, it.B + 70)), Offset = 0 });
            grad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, it.R, it.G, it.B), Offset = 1 });
            _icons[i] = new XPath { Data = geo, Stretch = Stretch.Fill, Fill = grad, Stroke = Br(90, 255, 255, 255), StrokeThickness = 1 };
            _glyphs[i] = new TextBlock { Text = it.Glyph, Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsHitTestVisible = false };
            _dots[i] = new Ellipse { Width = DockScene.DotSize, Height = DockScene.DotSize, Fill = Br(210, 255, 255, 255), Visibility = it.IsOpen ? Visibility.Visible : Visibility.Collapsed };
            _root.Children.Add(_icons[i]); _root.Children.Add(_glyphs[i]); _root.Children.Add(_dots[i]);
        }
    }

    private void Apply(DockPose pose)
    {
        var f = DockScene.Layout(pose.CursorX);
        double W = DockScene.WindowWidth, H = DockScene.WindowHeight;
        double off = (W - f.BarWidth) / 2, barBottom = H - DockScene.ShadowMargin, barTop = barBottom - DockScene.BarHeight;
        _bar.Width = f.BarWidth; _bar.Height = DockScene.BarHeight; Canvas.SetLeft(_bar, off); Canvas.SetTop(_bar, barTop);
        _sep.Height = DockScene.BarHeight * .6; Canvas.SetLeft(_sep, off + f.SeparatorX); Canvas.SetTop(_sep, barTop + DockScene.BarHeight * .2);
        for (int i = 0; i < _icons.Length; i++)
        {
            double s = f.Size[i], x = off + f.X[i], y = barBottom - DockScene.PadBottom - s;
            _icons[i].Width = _icons[i].Height = s; Canvas.SetLeft(_icons[i], x); Canvas.SetTop(_icons[i], y);
            _glyphs[i].FontSize = s * .42; Canvas.SetLeft(_glyphs[i], x + s * .5 - _glyphs[i].FontSize * .3); Canvas.SetTop(_glyphs[i], y + s * .5 - _glyphs[i].FontSize * .62);
            Canvas.SetLeft(_dots[i], x + s / 2 - DockScene.DotSize / 2); Canvas.SetTop(_dots[i], barBottom - DockScene.PadBottom + 4);
        }
        _aw.Move(new Windows.Graphics.PointInt32(_baseX, _baseY + (int)(pose.HideT * DockScene.HideDistance * _scale)));
    }

    private void OnLoaded()
    {
        _run.MarkFirstRender();
        if (_opt.Interactive) { CompositionTarget.Rendering += (_, _) => Apply(new DockPose(_mouseX, 0)); return; }
        if (!_opt.IsBench) return;
        var dq = this.DispatcherQueue;
        _ = _run.RunIdleAsync(a => dq.TryEnqueue(() => a()), () => CompositionTarget.Rendering += OnFrame);
    }

    private void OnFrame(object? s, object e)
    {
        var pose = _run.Frame();
        if (pose is null)
        {
            CompositionTarget.Rendering -= OnFrame;
            Console.WriteLine("-> " + _run.Finish(_opt.Variant,
                ["geometría compartida", "squircle", "borde", "indicadores de app abierta", "separador", "magnificación", "ocultar/mostrar (mover ventana)", .. _opt.Variant == "acrylic" ? new[] { "acrílico REAL del sistema (toda la ventana)" } : []],
                ["sombra", "forma propia del material", "radio de esquina propio del material", "blur real con forma de barra", "refracción/lensing"],
                "Cadencia = CompositionTarget.Rendering (no es presentación real; usar PresentMon). Sombras no implementadas en este prototipo mínimo."));
            Application.Current.Exit();
            return;
        }
        long t0 = _run.ApplyBegin(); Apply(pose.Value); _run.ApplyEnd(t0);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
