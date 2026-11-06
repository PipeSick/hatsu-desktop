using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Rendering;
using WinMac.Bar.Win.Workspaces;
using WinMac.Bar.Workspaces;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Hatsu Snap: mientras se arrastra una ventana y se mantiene la tecla elegida (Mayús por defecto) aparecen las zonas del monitor; al soltar dentro de una, la ventana se coloca ahí. SIN la tecla no se hace nada
/// (el Snap nativo de Windows no se toca). Usa el evento de sistema de «empieza/termina de moverse una ventana» (<c>SetWinEventHook</c>, documentado, sin inyección) y las funciones de colocación de Workspaces
/// (<see cref="WindowArranger"/>). No fuerza ventanas sin marco redimensionable, ni en pantalla completa, ni elevadas (Windows lo impide y se registra).
/// </summary>
internal sealed class SnapFeature : IHatsuFeature
{
    public const string Owner = "snap";
    private const uint WINEVENT_OUTOFCONTEXT = 0, EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public static string LayoutsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "snap-layouts.json");

    private readonly BarHost _host; private readonly Action<string> _log;
    private FeatureScope? _scope; private BarConfig _cfg = new(); private IntPtr _hook; private WinEventProc? _proc;
    private IntPtr _drag; private IReadOnlyList<CaptureMonitor> _mons = []; private D2DOverlay? _overlay; private CancellationTokenSource? _loop; private int _posted;
    private int _lastMon = -1, _lastZone = -2; private CaptureRect[] _rects = []; private int _zone = -1; private int _editSeen; private SnapEditor? _editor;

    public SnapFeature(BarHost host, Action<string> log) { _host = host; _log = log; }
    public string Id => FeatureConfig.Snap;
    public string Title => "Snap";
    public string Description => "Zonas para colocar ventanas al arrastrarlas con una tecla pulsada, distribuciones guardadas y editor de zonas.";
    public string? Info() => $"distribución «{_cfg.SnapLayout}» · {_cfg.SnapModifier}+arrastrar";

    public void Start(FeatureScope scope)
    {
        _scope = scope; _editSeen = _host.CurrentConfigEditToken;
        _proc = OnEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Windows no permitió instalar el aviso de movimiento de ventanas (error " + Marshal.GetLastWin32Error() + ")");
        scope.Add(() => { if (_hook != IntPtr.Zero) UnhookWinEvent(_hook); _hook = IntPtr.Zero; _proc = null; });
        scope.Add(EndDrag); scope.Add(() => { _editor?.Dispose(); _editor = null; });
        scope.Add(() => { _overlay?.Dispose(); _overlay = null; });
        _host.SnapNextZone = NextZone; scope.Add(() => _host.SnapNextZone = null);
        _host.SnapEdit = OpenEditor; scope.Add(() => _host.SnapEdit = null);
    }

    public void Reconfigure(BarConfig cfg)
    {
        _cfg = cfg;
        var r = _host.Binder.Bind(_scope, Owner, "next", "Siguiente zona de Snap", "Barra · Snap", cfg.SnapNextHotkey, NextZone);
        if (!r.Ok && cfg.SnapNextHotkey.Length > 0) _log($"[snap] atajo {cfg.SnapNextHotkey}: {r.Explain()}");
        if (cfg.SnapEditToken != _editSeen) { _editSeen = cfg.SnapEditToken; OpenEditor(); }
    }

    private int ModVk => _cfg.SnapModifier switch { "Ctrl" => 0x11, "Alt" => 0x12, _ => 0x10 };

    private void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0) return;
            if (ev == EVENT_SYSTEM_MOVESIZESTART) BeginDrag(hwnd); else if (ev == EVENT_SYSTEM_MOVESIZEEND) FinishDrag(hwnd);
        }
        catch (Exception e) { _log("[snap] evento: " + e.GetType().Name + ": " + e.Message); EndDrag(); }
    }

    private static bool Eligible(IntPtr h)
    {
        if (h == IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h)) return false;
        GetWindowThreadProcessId(h, out uint pid); if (pid == Environment.ProcessId) return false;
        long style = GetWindowLongPtr(h, -16).ToInt64(), ex = GetWindowLongPtr(h, -20).ToInt64();
        return (style & 0x40000) != 0 && (ex & 0x80) == 0;        // con marco redimensionable y no es una ventana de herramientas
    }

    private void BeginDrag(IntPtr hwnd)
    {
        EndDrag();
        if (!Eligible(hwnd)) return;
        if (FullscreenWatcher.ShouldSuppress()) return;
        _drag = hwnd; _mons = ScreenGrabber.Monitors(); _lastMon = -1; _lastZone = -2; _zone = -1;
        _loop = CancellationTokenSource.CreateLinkedTokenSource(_scope!.Token); var tok = _loop.Token;
        _ = Task.Run(async () => { try { while (!tok.IsCancellationRequested) { await Task.Delay(25, tok).ConfigureAwait(false); if (Interlocked.Exchange(ref _posted, 1) == 0) _host.PostToUi(() => { Interlocked.Exchange(ref _posted, 0); Tick(); }); } } catch (OperationCanceledException) { } });
    }

    private void Tick()
    {
        if (_drag == IntPtr.Zero) return;
        bool down = (GetAsyncKeyState(ModVk) & 0x8000) != 0; BarNative.GetCursorPos(out var cur);
        int mi = MonitorIndexAt(cur.X, cur.Y);
        if (!down || mi < 0) { if (_overlay?.IsVisible == true) { _overlay.Hide(); _lastMon = -1; _lastZone = -2; _zone = -1; } return; }
        var m = _mons[mi]; var layout = SnapLayouts.Find(_cfg.SnapLayout, LoadCustom());
        var rects = SnapMath.Rects(layout, m.Work.W > 0 ? m.Work : m.Bounds, _cfg.SnapGap); int zone = SnapMath.ZoneAt(rects, cur.X, cur.Y);
        if (mi == _lastMon && zone == _lastZone && _overlay?.IsVisible == true) return;
        _lastMon = mi; _lastZone = zone; _rects = rects; _zone = zone;
        _overlay ??= new D2DOverlay("Snap", clickThrough: true, null, _log);
        var area = m.Work.W > 0 ? m.Work : m.Bounds;
        _overlay.Show(area, m.Scale, focus: false);
        _overlay.Draw((c, w, h) => PaintZones(c, rects, zone, area, m.Scale));
    }

    private static void PaintZones(ID2D1DeviceContext c, CaptureRect[] rects, int active, CaptureRect area, double scale)
    {
        using var b = c.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i]; float x = (float)((r.X - area.X) / scale), y = (float)((r.Y - area.Y) / scale), w = (float)(r.W / scale), h = (float)(r.H / scale);
            var rr = new RoundedRectangle { Rect = new RawRectF(x + 4, y + 4, x + w - 4, y + h - 4), RadiusX = 12, RadiusY = 12 };
            bool on = i == active;
            b.Color = on ? new Color4(0.04f, 0.52f, 1f, 0.38f) : new Color4(1, 1, 1, 0.10f); c.FillRoundedRectangle(rr, b);
            b.Color = on ? new Color4(0.04f, 0.52f, 1f, 0.95f) : new Color4(1, 1, 1, 0.55f); c.DrawRoundedRectangle(rr, b, on ? 3f : 1.5f);
        }
    }

    private void FinishDrag(IntPtr hwnd)
    {
        if (_drag == IntPtr.Zero || hwnd != _drag) return;
        bool show = _overlay?.IsVisible == true && _zone >= 0 && _zone < _rects.Length; var target = show ? _rects[_zone] : default; var h = _drag;
        EndDrag();
        if (!show) return;
        _ = Task.Run(async () => { await Task.Delay(90).ConfigureAwait(false); _host.PostToUi(() => Place(h, target)); });   // después de que Windows termine su propio movimiento
    }

    private void EndDrag()
    {
        try { _loop?.Cancel(); } catch (Exception) { }
        _loop = null; _drag = IntPtr.Zero; _zone = -1; _lastZone = -2; _lastMon = -1; Interlocked.Exchange(ref _posted, 0);
        _overlay?.Hide();
    }

    private int MonitorIndexAt(int x, int y) { for (int i = 0; i < _mons.Count; i++) { var b = _mons[i].Bounds; if (x >= b.X && x < b.X + b.W && y >= b.Y && y < b.Y + b.H) return i; } return -1; }

    /// <summary>Coloca la ventana para que su borde VISIBLE llene la zona (compensa el borde invisible de redimensión de Windows).</summary>
    private void Place(IntPtr hwnd, CaptureRect visible)
    {
        try
        {
            if (!IsWindow(hwnd)) return;
            var rect = visible;
            if (GetWindowRect(hwnd, out var wr) && DwmGetWindowAttribute(hwnd, 9, out var fb, Marshal.SizeOf<RECT>()) == 0)
                rect = SnapMath.Compensate(visible, Math.Max(0, fb.L - wr.L), Math.Max(0, fb.T - wr.T), Math.Max(0, wr.R - fb.R), Math.Max(0, wr.B - fb.B));
            var err = WindowArranger.Apply(new WsMove(hwnd, rect, WinShow.Normal, ""));
            if (err is not null) _log("[snap] no se pudo colocar la ventana: " + err);
        }
        catch (Exception e) { _log("[snap] colocar: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>Atajo: la ventana activa pasa a la zona siguiente de la distribución activa (del monitor donde está).</summary>
    public void NextZone()
    {
        try
        {
            var h = GetForegroundWindow(); if (!Eligible(h)) return;
            var mons = ScreenGrabber.Monitors(); if (mons.Count == 0 || !GetWindowRect(h, out var r)) return;
            int cx = (r.L + r.R) / 2, cy = (r.T + r.B) / 2; int mi = 0;
            for (int i = 0; i < mons.Count; i++) { var b = mons[i].Bounds; if (cx >= b.X && cx < b.X + b.W && cy >= b.Y && cy < b.Y + b.H) { mi = i; break; } }
            var area = mons[mi].Work.W > 0 ? mons[mi].Work : mons[mi].Bounds; var rects = SnapMath.Rects(SnapLayouts.Find(_cfg.SnapLayout, LoadCustom()), area, _cfg.SnapGap);
            Place(h, rects[SnapMath.Next(rects.Length, SnapMath.ZoneAt(rects, cx, cy))]);
        }
        catch (Exception e) { _log("[snap] siguiente zona: " + e.GetType().Name + ": " + e.Message); }
    }

    public static IReadOnlyList<SnapLayout> LoadCustom()
    {
        try { return File.Exists(LayoutsFile) ? SnapLayouts.Parse(File.ReadAllText(LayoutsFile)) : []; } catch (Exception) { return []; }
    }

    public static bool SaveCustom(IReadOnlyList<SnapLayout> list)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LayoutsFile)!); var tmp = LayoutsFile + ".tmp"; File.WriteAllText(tmp, SnapLayouts.Serialize(list)); File.Move(tmp, LayoutsFile, true); return true; } catch (Exception) { return false; }
    }

    public void OpenEditor()
    {
        if (_editor is not null) return;
        try
        {
            BarNative.GetCursorPos(out var cur); var mons = ScreenGrabber.Monitors(); if (mons.Count == 0) return;
            var m = mons.FirstOrDefault(x => cur.X >= x.Bounds.X && cur.X < x.Bounds.X + x.Bounds.W && cur.Y >= x.Bounds.Y && cur.Y < x.Bounds.Y + x.Bounds.H) ?? mons[0];
            _editor = new SnapEditor(m, saved => { _editor = null; if (saved is null) return; var (list, name) = SnapLayouts.Add(LoadCustom(), saved.Value.Name, saved.Value.Zones); if (SaveCustom(list)) { _host.PatchConfig(new { snapLayout = name }); _log($"[snap] distribución «{name}» guardada"); } else _log("[snap] no se pudo guardar la distribución"); }, _log);
            _editor.Show(LoadCustom());
        }
        catch (Exception e) { _log("[snap] editor: " + e.GetType().Name + ": " + e.Message); _editor?.Dispose(); _editor = null; }
    }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
}

/// <summary>Editor de zonas: arrastra para crear una zona (se pega a una rejilla de 24×24), clic derecho borra la zona bajo el cursor, Retroceso quita la última, Intro guarda y Esc cancela.</summary>
internal sealed class SnapEditor : IDisposable
{
    private readonly CaptureMonitor _m; private readonly Action<(string Name, IReadOnlyList<SnapZone> Zones)?> _done; private readonly Action<string> _log;
    private D2DOverlay? _ov; private readonly List<SnapZone> _zones = []; private (double X, double Y)? _from, _to; private IDWriteFactory? _dw; private IDWriteTextFormat? _fmt; private IReadOnlyList<SnapLayout> _custom = []; private bool _closed;
    public SnapEditor(CaptureMonitor m, Action<(string Name, IReadOnlyList<SnapZone> Zones)?> done, Action<string> log) { _m = m; _done = done; _log = log; }

    public void Show(IReadOnlyList<SnapLayout> custom)
    {
        _custom = custom; _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _fmt = _dw.CreateTextFormat("Segoe UI", 15); _fmt.TextAlignment = TextAlignment.Center;
        _ov = new D2DOverlay("SnapEdit", clickThrough: false, Msg, _log);
        var area = _m.Work.W > 0 ? _m.Work : _m.Bounds; _ov.Show(area, _m.Scale, focus: true); Redraw();
    }

    private double W => _ov!.Area.W / _m.Scale; private double H => _ov!.Area.H / _m.Scale;
    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _m.Scale; private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _m.Scale;

    private IntPtr? Msg(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch ((int)msg)
        {
            case 0x201: BarNative.SetCapture(h); _from = _to = (X(l), Y(l)); Redraw(); return IntPtr.Zero;                     // botón izquierdo
            case 0x200 when _from is not null: _to = (X(l), Y(l)); Redraw(); return IntPtr.Zero;
            case 0x202 when _from is { } f:
                BarNative.ReleaseCapture(); var z = SnapMath.FromDrag(f.X, f.Y, X(l), Y(l), W, H); _from = _to = null;
                if (z is { } nz && _zones.Count < SnapLayouts.MaxZones) _zones.Add(nz); Redraw(); return IntPtr.Zero;
            case 0x205: { double x = X(l) / W, y = Y(l) / H; int i = _zones.FindLastIndex(q => x >= q.X && x <= q.X + q.W && y >= q.Y && y <= q.Y + q.H); if (i >= 0) _zones.RemoveAt(i); Redraw(); return IntPtr.Zero; }   // derecho
            case 0x100:
                int vk = (int)w;
                if (vk == 0x1B) { Finish(null); return IntPtr.Zero; }
                if (vk == 0x0D) { if (_zones.Count > 0) Finish((SnapLayouts.NextCustomName(_custom), _zones.ToList())); return IntPtr.Zero; }
                if (vk == 0x08 && _zones.Count > 0) { _zones.RemoveAt(_zones.Count - 1); Redraw(); }
                return IntPtr.Zero;
            case 0x6 when ((long)w & 0xFFFF) == 0 && !_closed: Finish(null); return IntPtr.Zero;                                 // perdió el foco
        }

        return null;
    }

    private void Redraw() => _ov?.Draw((c, w, h) =>
    {
        using var b = c.CreateSolidColorBrush(new Color4(0, 0, 0, 0.55f)); c.FillRectangle(new RawRectF(0, 0, (float)w, (float)h), b);
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i]; var rr = new RoundedRectangle { Rect = new RawRectF((float)(z.X * w + 4), (float)(z.Y * h + 4), (float)((z.X + z.W) * w - 4), (float)((z.Y + z.H) * h - 4)), RadiusX = 12, RadiusY = 12 };
            b.Color = new Color4(0.04f, 0.52f, 1f, 0.30f); c.FillRoundedRectangle(rr, b); b.Color = new Color4(0.04f, 0.52f, 1f, 0.95f); c.DrawRoundedRectangle(rr, b, 2.5f);
            b.Color = new Color4(1, 1, 1, 0.9f); using var l = _dw!.CreateTextLayout((i + 1).ToString(), _fmt!, 60, 24); c.DrawTextLayout(new Vector2((float)((z.X + z.W / 2) * w - 30), (float)((z.Y + z.H / 2) * h - 12)), l, b);
        }

        if (_from is { } f && _to is { } t)
        {
            b.Color = new Color4(1, 1, 1, 0.20f); var r = new RawRectF((float)Math.Min(f.X, t.X), (float)Math.Min(f.Y, t.Y), (float)Math.Max(f.X, t.X), (float)Math.Max(f.Y, t.Y)); c.FillRectangle(r, b); b.Color = new Color4(1, 1, 1, 0.8f); c.DrawRectangle(r, b, 1.5f);
        }

        b.Color = new Color4(1, 1, 1, 0.95f); using var hint = _dw!.CreateTextLayout("Arrastra para crear zonas · clic derecho borra · Retroceso quita la última · Intro guarda · Esc cancela", _fmt!, (float)w, 30); c.DrawTextLayout(new Vector2(0, 20), hint, b);
    });

    private void Finish((string Name, IReadOnlyList<SnapZone> Zones)? result) { if (_closed) return; _closed = true; var cb = _done; Dispose(); cb(result); }

    public void Dispose() { _closed = true; _ov?.Dispose(); _ov = null; _fmt?.Dispose(); _dw?.Dispose(); }
}
