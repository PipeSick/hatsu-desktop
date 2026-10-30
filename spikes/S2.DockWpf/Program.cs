// S2 — Dock sobre WPF. Mide cadencia de frames, CPU en reposo y en animación, memoria y arranque, y deja ver la
// fidelidad visual alcanzable. Los íconos son rectángulos con degradado y una letra (sin recursos de Apple).
// Uso:  S2.DockWpf [--variant layered|dwm] [--interactive]
//   layered : AllowsTransparency=true, forma propia (radio 22), "vidrio" simulado con degradado (sin blur real)
//   dwm     : AllowsTransparency=false + backdrop acrílico del sistema + esquinas del sistema (sin forma propia)
// Fases automáticas: calentamiento 2 s -> reposo 10 s -> barrido determinista del cursor 15 s -> reporte JSON.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WinMac.Spikes.Common;

internal static class Entry
{
    [STAThread]
    private static void Main(string[] args)
    {
        string variant = args.SkipWhile(a => a != "--variant").Skip(1).FirstOrDefault() ?? "layered";
        bool interactive = args.Contains("--interactive");
        new Application().Run(new DockWindow(variant, interactive));
    }
}

internal sealed class DockWindow : Window
{
    private readonly string _variant; private readonly bool _interactive;
    private readonly Canvas _canvas = new();
    private readonly Border[] _icons = new Border[DockMath.IconCount];
    private readonly Border _bar = new();
    private readonly FrameRecorder _frames = new(); private readonly CpuSampler _cpu = new();
    private readonly Stopwatch _clock = new();
    private double _idleCpu, _animCpu; private double _startupMs; private string _phase = "warmup"; private double _phaseStart;
    private double _cursorX = DockMath.RestWidth / 2;

    public DockWindow(string variant, bool interactive)
    {
        _variant = variant; _interactive = interactive;
        Width = DockMath.MaxWidth; Height = DockMath.BaseSize * DockMath.MaxScale + 2 * DockMath.Pad + 8;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
        bool layered = variant == "layered";
        AllowsTransparency = layered; Background = layered ? Brushes.Transparent : null;

        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Bottom - Height - 6;

        _bar.CornerRadius = new CornerRadius(22); _bar.Height = DockMath.BaseSize + 2 * DockMath.Pad - 6;
        _bar.BorderThickness = new Thickness(1);
        if (layered)
        {
            _bar.Background = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), 90);
            _bar.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            _bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = .35 };
        }
        else _bar.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
        _canvas.Children.Add(_bar);

        for (int i = 0; i < _icons.Length; i++)
        {
            var hue = Color.FromRgb((byte)(80 + 14 * i), (byte)(120 + 9 * (i % 5)), (byte)(220 - 11 * i));
            _icons[i] = new Border
            {
                CornerRadius = new CornerRadius(13),
                Background = new LinearGradientBrush(Colors.White, hue, 90) { Opacity = .95 },
                Child = new TextBlock { Text = ((char)('A' + i)).ToString(), Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            _canvas.Children.Add(_icons[i]);
        }
        Content = _canvas;
        Loaded += OnLoaded; ContentRendered += (_, _) => { _startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; Begin(); };
        if (interactive) MouseMove += (_, e) => _cursorX = e.GetPosition(_canvas).X - (Width - DockMath.RestWidth) / 2;
        Layout(_cursorX);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (_variant != "dwm") return;
        IntPtr h = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(h)!.CompositionTarget.BackgroundColor = Colors.Transparent;
        int backdrop = 3 /* DWMSBT_TRANSIENTWINDOW = acrílico */, round = 2 /* DWMWCP_ROUND */;
        var margins = new MARGINS { l = -1, r = -1, t = -1, b = -1 };
        Console.WriteLine($"[dwm] extend=0x{DwmExtendFrameIntoClientArea(h, ref margins):X} backdrop=0x{DwmSetWindowAttribute(h, 38, ref backdrop, 4):X} corner=0x{DwmSetWindowAttribute(h, 33, ref round, 4):X}");
    }

    private void Begin()
    {
        _clock.Start();
        if (!_interactive) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering += (_, _) => Layout(_cursorX);
    }

    private void Layout(double cursorX)
    {
        var (x, size, total) = DockMath.Layout(cursorX);
        double off = (Width - total) / 2, baseY = Height - 6;
        _bar.Width = total; Canvas.SetLeft(_bar, off); Canvas.SetTop(_bar, baseY - _bar.Height);
        for (int i = 0; i < _icons.Length; i++)
        {
            _icons[i].Width = _icons[i].Height = size[i];
            Canvas.SetLeft(_icons[i], off + x[i]); Canvas.SetTop(_icons[i], baseY - DockMath.Pad / 2 - size[i]);
        }
    }

    private void OnFrame(object? s, EventArgs e)
    {
        double t = _clock.Elapsed.TotalSeconds;
        switch (_phase)
        {
            case "warmup" when t > 2: _phase = "idle"; _phaseStart = t; CompositionTarget.Rendering -= OnFrame; _cpu.Start();
                new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(10), System.Windows.Threading.DispatcherPriority.Normal, (tm, _) =>
                { ((System.Windows.Threading.DispatcherTimer)tm!).Stop(); _idleCpu = _cpu.StopPercent(); StartSweep(); }, Dispatcher).Start();
                return;
            case "sweep":
                _frames.Tick(); Layout(DockMath.SweepX(t - _phaseStart));
                if (t - _phaseStart > 15) Finish();
                return;
        }
        Layout(DockMath.SweepX(t));
    }

    private void StartSweep()
    {
        _phase = "sweep"; _phaseStart = _clock.Elapsed.TotalSeconds; _frames.Reset(); _cpu.Start();
        CompositionTarget.Rendering += OnFrame;
    }

    private void Finish()
    {
        CompositionTarget.Rendering -= OnFrame;
        _animCpu = _cpu.StopPercent();
        double hz = Native.PrimaryRefreshHz();
        var rep = new BenchReport("S2.DockWpf", _variant, Environment.OSVersion.VersionString, hz, Native.GetDpiForSystem(),
            _startupMs, _idleCpu, _animCpu, Process.GetCurrentProcess().WorkingSet64 / 1048576.0, _frames.Summarize(hz));
        System.IO.File.WriteAllText("last-path.txt", rep.Save());
        Application.Current.Shutdown();
    }

    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int l, r, t, b; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
}
