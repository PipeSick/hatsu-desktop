// S2 — Prototipo mínimo del Dock sobre WPF. Escena, geometría y guion de medición compartidos (WinMac.Spikes.Common).
// Uso:  S2.DockWpf [--variant layered|dwm] [--pose rest|hover|hidden] [--interactive] [--quick] [--out DIR]
//   layered : AllowsTransparency=true, barra con forma propia, "vidrio" simulado (degradados + borde especular + sombra); SIN blur real del fondo.
//   dwm     : AllowsTransparency=false, acrílico real del sistema sobre TODA la ventana (incluida la holgura de magnificación) y esquinas del sistema.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WinMac.Spikes.Common;

internal static class Entry
{
    [STAThread]
    private static void Main(string[] args) => new Application().Run(new DockWindow(CliOptions.Parse(args, "layered")));
}

internal sealed class DockWindow : Window
{
    private readonly CliOptions _opt; private readonly BenchRun _run;
    private readonly Canvas _canvas = new();
    private readonly Border _bar = new();
    private readonly Rectangle _sep = new();
    private readonly Path[] _icons = new Path[DockScene.Items.Length];
    private readonly TextBlock[] _glyphs = new TextBlock[DockScene.Items.Length];
    private readonly Ellipse[] _dots = new Ellipse[DockScene.Items.Length];
    private readonly double _baseTop;
    private double? _mouseX;

    public DockWindow(CliOptions opt)
    {
        _opt = opt; _run = new BenchRun("S2.DockWpf", opt);
        Width = DockScene.WindowWidth; Height = DockScene.WindowHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        bool layered = opt.Variant == "layered";
        AllowsTransparency = layered; Background = layered ? Brushes.Transparent : null;
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2; _baseTop = wa.Bottom - Height; Top = _baseTop;

        BuildScene(layered);
        Content = _canvas;
        ContentRendered += (_, _) => OnRendered();
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        if (opt.Interactive) MouseMove += (_, e) => _mouseX = e.GetPosition(_canvas).X - (Width - DockScene.RestWidth) / 2;
        if (opt.Interactive) MouseLeave += (_, _) => _mouseX = null;
        Apply(StaticPose());
    }

    private DockPose StaticPose() => _opt.Pose switch
    {
        Pose.Hover => new DockPose(DockScene.PadX + 5 * (DockScene.BaseSize + DockScene.Gap) + DockScene.BaseSize / 2, 0),
        Pose.Hidden => new DockPose(null, 1),
        _ => new DockPose(null, 0),
    };

    private void BuildScene(bool layered)
    {
        _bar.CornerRadius = new CornerRadius(DockScene.BarRadius); _bar.BorderThickness = new Thickness(1);
        if (layered)
        {
            _bar.Background = new LinearGradientBrush(Color.FromArgb(120, 255, 255, 255), Color.FromArgb(55, 255, 255, 255), 90);
            _bar.BorderBrush = new LinearGradientBrush(Color.FromArgb(190, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), 90);
            _bar.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 10, Direction = 270, Opacity = .28 };
        }
        else
        {
            _bar.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            _bar.BorderBrush = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(30, 255, 255, 255), 90);
        }
        _canvas.Children.Add(_bar);

        _sep.Width = 1; _sep.Fill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)); _canvas.Children.Add(_sep);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var pts = Squircle.Unit();
            ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
            ctx.PolyLineTo(pts.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, false);
        }
        geo.Freeze();

        for (int i = 0; i < _icons.Length; i++)
        {
            var it = DockScene.Items[i];
            var c = Color.FromRgb(it.R, it.G, it.B);
            _icons[i] = new Path
            {
                Data = geo, Stretch = Stretch.Fill,
                Fill = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, it.R + 70), (byte)Math.Min(255, it.G + 70), (byte)Math.Min(255, it.B + 70)), c, 90),
                Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), StrokeThickness = 1,
                Effect = layered ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = .25 } : null,
            };
            _glyphs[i] = new TextBlock { Text = it.Glyph, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false };
            _dots[i] = new Ellipse { Width = DockScene.DotSize, Height = DockScene.DotSize, Fill = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), Visibility = it.IsOpen ? Visibility.Visible : Visibility.Collapsed };
            _canvas.Children.Add(_icons[i]); _canvas.Children.Add(_glyphs[i]); _canvas.Children.Add(_dots[i]);
        }
    }

    private void Apply(DockPose pose)
    {
        var f = DockScene.Layout(pose.CursorX);
        double off = (Width - f.BarWidth) / 2, barBottom = Height - DockScene.ShadowMargin, barTop = barBottom - DockScene.BarHeight;
        _bar.Width = f.BarWidth; _bar.Height = DockScene.BarHeight; Canvas.SetLeft(_bar, off); Canvas.SetTop(_bar, barTop);
        _sep.Height = DockScene.BarHeight * .6; Canvas.SetLeft(_sep, off + f.SeparatorX); Canvas.SetTop(_sep, barTop + DockScene.BarHeight * .2);
        for (int i = 0; i < _icons.Length; i++)
        {
            double s = f.Size[i], x = off + f.X[i], y = barBottom - DockScene.PadBottom - s;
            _icons[i].Width = _icons[i].Height = s; Canvas.SetLeft(_icons[i], x); Canvas.SetTop(_icons[i], y);
            _glyphs[i].FontSize = s * .42; Canvas.SetLeft(_glyphs[i], x + s * .5 - _glyphs[i].FontSize * .3); Canvas.SetTop(_glyphs[i], y + s * .5 - _glyphs[i].FontSize * .62);
            Canvas.SetLeft(_dots[i], x + s / 2 - DockScene.DotSize / 2); Canvas.SetTop(_dots[i], barBottom - DockScene.PadBottom + 4);
        }
        Top = _baseTop + pose.HideT * DockScene.HideDistance;
    }

    private void OnRendered()
    {
        if (_opt.Variant == "dwm") ApplyDwm();
        _run.MarkFirstRender();
        if (_opt.Interactive) { CompositionTarget.Rendering += (_, _) => Apply(new DockPose(_mouseX, 0)); return; }
        if (!_opt.IsBench) return;
        _ = _run.RunIdleAsync(a => Dispatcher.BeginInvoke(a), () => CompositionTarget.Rendering += OnFrame);
    }

    private void OnFrame(object? s, EventArgs e)
    {
        var pose = _run.Frame();
        if (pose is null)
        {
            CompositionTarget.Rendering -= OnFrame;
            Console.WriteLine("-> " + _run.Finish(_opt.Variant,
                _opt.Variant == "layered"
                    ? ["geometría compartida", "squircle", "barra con forma propia", "borde especular (degradado)", "sombra (DropShadowEffect)", "indicadores de app abierta", "separador", "magnificación", "ocultar/mostrar (mover ventana)"]
                    : ["geometría compartida", "squircle", "acrílico REAL del sistema", "indicadores de app abierta", "separador", "magnificación", "ocultar/mostrar (mover ventana)"],
                _opt.Variant == "layered"
                    ? ["blur real del fondo", "refracción/lensing"]
                    : ["forma propia de la barra (acrílico cubre toda la ventana)", "sombra", "radio de esquina propio", "refracción/lensing"],
                "Cadencia = CompositionTarget.Rendering (no es presentación real; usar PresentMon)."));
            Application.Current.Shutdown();
            return;
        }
        long t0 = _run.ApplyBegin(); Apply(pose.Value); _run.ApplyEnd(t0);
    }

    private void ApplyDwm()
    {
        IntPtr h = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(h)!.CompositionTarget.BackgroundColor = Colors.Transparent;
        int backdrop = 3 /* DWMSBT_TRANSIENTWINDOW = acrílico */, round = 2 /* DWMWCP_ROUND */;
        var m = new MARGINS { l = -1, r = -1, t = -1, b = -1 };
        Console.WriteLine($"[dwm] extend=0x{DwmExtendFrameIntoClientArea(h, ref m):X} backdrop=0x{DwmSetWindowAttribute(h, 38, ref backdrop, 4):X} corner=0x{DwmSetWindowAttribute(h, 33, ref round, 4):X}");
    }

    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int l, r, t, b; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
}
