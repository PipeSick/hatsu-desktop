using System.Runtime.InteropServices;
using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Launcher;
using WinMac.Bar.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Hatsu Gestures: esquinas activas (el puntero se queda un momento en una esquina de la pantalla), los botones laterales del ratón y hasta dos atajos de teclado, cada uno con una acción configurable.
/// Comparte el ÚNICO gancho de ratón de bajo nivel de la Barra (<see cref="WinKeyHook"/>), que solo existe mientras alguien lo usa. NO captura gestos de touchpad (Windows no ofrece una vía compatible y los
/// de 3/4 dedos son del sistema). Los botones laterales solo se consumen si tienen una acción asignada y disponible; las esquinas nunca consumen nada ni disparan arrastrando con un botón pulsado.
/// </summary>
internal sealed class GesturesFeature : IHatsuFeature
{
    public const string Owner = "gestures";
    private const int WM_MOUSEMOVE = 0x200, WM_XBUTTONDOWN = 0x20B, WM_XBUTTONUP = 0x20C;
    private readonly BarHost _host; private readonly Action<string> _log;
    private FeatureScope? _scope; private volatile BarConfig _cfg = new(); private readonly HotCornerEngine _engine = new(); private int _pending; private int _evalScheduled;

    public GesturesFeature(BarHost host, Action<string> log) { _host = host; _log = log; }
    public string Id => FeatureConfig.Gestures;
    public string Title => "Gestures";
    public string Description => "Esquinas activas, botones laterales del ratón y atajos para abrir Mission Control, Spotlight y más.";
    public string? Info() => "esquinas, botones laterales y atajos (sin gestos de touchpad)";

    public void Start(FeatureScope scope)
    {
        _scope = scope; var hook = _host.AcquireInputHook(scope);
        hook.MouseSink = OnMouse; scope.Add(() => { hook.MouseSink = null; _engine.Reset(); });
    }

    public void Reconfigure(BarConfig cfg)
    {
        _cfg = cfg;
        Bind("key1", cfg.GestKey1, cfg.GestKey1Action); Bind("key2", cfg.GestKey2, cfg.GestKey2Action);
    }

    private void Bind(string id, string keys, string action)
    {
        string name = GestureActions.All.FirstOrDefault(a => a.Id == action)?.Name ?? action;
        bool use = keys.Length > 0 && action != "none";
        var r = _host.Binder.Bind(_scope, Owner, id, "Gestures: " + name, "Barra · Gestures", use ? keys : "", () => Run(action));
        if (!r.Ok && use) _log($"[gestures] atajo {keys}: {r.Explain()}");
    }

    // ── ratón (hilo del gancho: solo anotar y avisar)
    private bool OnMouse(int msg, int x, int y, uint data)
    {
        var cfg = _cfg;
        if (msg == WM_MOUSEMOVE)
        {
            if (cfg.GestCornerTL == "none" && cfg.GestCornerTR == "none" && cfg.GestCornerBL == "none" && cfg.GestCornerBR == "none") return false;
            if (Interlocked.Exchange(ref _pending, 1) == 0) _host.PostToUi(() => { Interlocked.Exchange(ref _pending, 0); Evaluate(); });
            return false;
        }

        if (msg is WM_XBUTTONDOWN or WM_XBUTTONUP)
        {
            int btn = (int)(data >> 16); string action = btn == 1 ? cfg.GestMouseX1 : btn == 2 ? cfg.GestMouseX2 : "none";
            if (!GestureRules.IsAvailable(action, cfg)) return false;               // sin acción (o su módulo está apagado): el botón sigue funcionando como siempre (atrás/adelante)
            if (msg == WM_XBUTTONUP) _host.PostToUi(() => Run(action));
            return true;
        }

        return false;
    }

    private void Evaluate()
    {
        try
        {
            var cfg = _cfg; if (_scope is null || _scope.IsDisposed) return;
            if (FullscreenWatcher.ShouldSuppress()) { _engine.Reset(); return; }        // juegos, vídeo o presentaciones a pantalla completa
            BarNative.GetCursorPos(out var p);
            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;
            var mons = ScreenGrabber.Monitors().Select(m => new HotCornerEngine.Mon(m.Bounds.X, m.Bounds.Y, m.Bounds.W, m.Bounds.H)).ToList();
            var fired = _engine.Update(p.X, p.Y, down, mons, Environment.TickCount64, cfg.GestCornerDelayMs, c => GestureRules.IsAvailable(GestureRules.ForCorner(cfg, c), cfg), out int wait);
            if (wait > 0 && Interlocked.Exchange(ref _evalScheduled, 1) == 0)
            {
                var tok = _scope.Token;
                _ = Task.Run(async () => { try { await Task.Delay(wait + 20, tok).ConfigureAwait(false); } catch (OperationCanceledException) { return; } _host.PostToUi(() => { Interlocked.Exchange(ref _evalScheduled, 0); Evaluate(); }); });
            }

            if (fired is { } corner) Run(GestureRules.ForCorner(cfg, corner));
        }
        catch (Exception e) { _log("[gestures] error: " + e.GetType().Name + ": " + e.Message); _scope?.Fault(e); }
    }

    /// <summary>Ejecuta una acción. Una acción de un módulo apagado no se ejecuta.</summary>
    public void Run(string action)
    {
        var cfg = _cfg;
        if (!GestureRules.IsAvailable(action, cfg)) { _log($"[gestures] «{action}» no está disponible (¿su módulo está apagado?)"); return; }
        try
        {
            switch (action)
            {
                case "mission": _host.ToggleMissionControl?.Invoke(); break;
                case "spotlight": _host.ToggleSpotlightFromFeature("gesto"); break;
                case "launchpad": _host.ToggleLaunchpadFromFeature("gesto"); break;
                case "livetext": _host.LiveTextRun?.Invoke(); break;
                case "snapnext": _host.SnapNextZone?.Invoke(); break;
                case "capture": _host.BeginRegionCaptureFromFeature("gesto"); break;
                case "dock": _host.HostCommandFromFeature("dock.autohide.toggle"); break;
                case "desktopprev": SendDesktopKeys(0x25); break;
                case "desktopnext": SendDesktopKeys(0x27); break;
            }
        }
        catch (Exception e) { _log($"[gestures] «{action}» falló: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Cambiar de escritorio virtual con el atajo documentado de Windows Win+Ctrl+←/→ (no se usan interfaces privadas de escritorios virtuales).</summary>
    private static void SendDesktopKeys(ushort arrow)
    {
        var inputs = new INPUT[6]; ushort[] seq = [0x5B, 0x11, arrow];
        for (int i = 0; i < 3; i++) { inputs[i].type = 1; inputs[i].ki.wVk = seq[i]; inputs[5 - i].type = 1; inputs[5 - i].ki.wVk = seq[i]; inputs[5 - i].ki.dwFlags = 2; }
        SendInput(6, inputs, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}
