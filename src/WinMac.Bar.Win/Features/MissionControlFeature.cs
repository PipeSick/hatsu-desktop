using WinMac.Apps;
using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Hatsu Mission Control: vista general de las ventanas del escritorio virtual ACTUAL en todos los monitores (una superposición por monitor con las ventanas que están en él). Reutiliza el catálogo compartido
/// (<see cref="WindowCatalog"/>: rastreador, miniaturas y acciones del Dock). Con la vista cerrada no hay captura, ni temporizador, ni gancho: solo el atajo (RegisterHotKey).
/// LIMITACIÓN: ve solo el escritorio virtual actual (las ventanas de otros escritorios las oculta Windows y no se usan APIs privadas para listarlos); no es el Mission Control completo de macOS.
/// </summary>
internal sealed class MissionControlFeature : IHatsuFeature
{
    public const string Owner = "mission";
    private readonly BarHost _host; private readonly Action<string> _log;
    private FeatureScope? _scope; private WindowCatalog? _catalog; private readonly McController _ctl = new(); private List<MissionControlWindow> _wins = [];
    private IReadOnlyList<CaptureMonitor> _mons = []; private CancellationTokenSource? _loop; private string _lastKey = "";

    public MissionControlFeature(BarHost host, Action<string> log) { _host = host; _log = log; }
    public string Id => FeatureConfig.MissionControl;
    public string Title => "Mission Control";
    public string Description => "Vista general de las ventanas del escritorio actual, con miniaturas, búsqueda y varios monitores.";
    public bool IsOpen { get; private set; }

    public void Start(FeatureScope scope)
    {
        _scope = scope; _catalog = _host.Catalog.Acquire(scope);
        scope.Add(Close);
        _host.ToggleMissionControl = () => Toggle("acción");
        scope.Add(() => _host.ToggleMissionControl = null);
    }

    public void Reconfigure(BarConfig cfg)
    {
        var r = _host.Binder.Bind(_scope, Owner, "open", "Mission Control", "Barra · Mission Control", cfg.McHotkey, () => Toggle("atajo"));
        if (!r.Ok && cfg.McHotkey.Length > 0) _log($"[mission] atajo {cfg.McHotkey}: {r.Explain()}");
    }

    public string? Info() => _host.Binder.StateOf(Owner, "open") is { Registered: false, Problem: { } p } ? "atajo sin registrar: " + p : "ventanas del escritorio actual";

    public void Toggle(string why) { if (IsOpen) Close(); else Open(why); }

    private void Open(string why)
    {
        if (IsOpen || _catalog is null) return;
        if (FullscreenWatcher.ShouldSuppress()) { _log("[mission] no se abre: hay una app a pantalla completa o una presentación"); return; }
        try
        {
            _mons = ScreenGrabber2.Monitors(); if (_mons.Count == 0) return;
            _ctl.Reset(); _ctl.SetWindows(_catalog.Snapshot(_mons));
            BarNative.GetCursorPos(out var cur); int focusMon = 0;
            for (int i = 0; i < _mons.Count; i++) { var b = _mons[i].Bounds; if (cur.X >= b.X && cur.X < b.X + b.W && cur.Y >= b.Y && cur.Y < b.Y + b.H) focusMon = i; }
            _wins = [];
            for (int i = 0; i < _mons.Count; i++)
            {
                var w = new MissionControlWindow(_ctl, i, _mons[i], h => _catalog.Thumbs.Get(h), OnCommand, _log); _wins.Add(w);
                w.Show(focus: i == focusMon);
            }

            IsOpen = true; RequestThumbs();
            _loop = CancellationTokenSource.CreateLinkedTokenSource(_scope!.Token); var tok = _loop.Token;
            _ = Task.Run(async () => { try { while (!tok.IsCancellationRequested) { await Task.Delay(100, tok).ConfigureAwait(false); _host.PostToUi(Tick); } } catch (OperationCanceledException) { } });
            _log($"[mission] abierto ({why}): {_ctl.Total} ventana(s) en {_mons.Count} monitor(es)");
        }
        catch (Exception e) { _log("[mission] no se pudo abrir: " + e.GetType().Name + ": " + e.Message); Close(); _scope?.Fault(e); }
    }

    private void Tick()
    {
        if (!IsOpen || _catalog is null) return;
        try
        {
            var snap = _catalog.Snapshot(_mons); string key = string.Join('|', snap.Select(w => $"{w.Hwnd}:{w.Title}:{w.Minimized}:{w.Monitor}"));
            if (key != _lastKey) { _lastKey = key; _ctl.SetWindows(snap); foreach (var w in _wins) w.Invalidate(); }
            RequestThumbs();
            if (_catalog.Thumbs.TakeDirty()) foreach (var w in _wins) w.Invalidate();
            foreach (var w in _wins) w.Pump();
        }
        catch (Exception e) { _log("[mission] refresco: " + e.Message); }
    }

    /// <summary>Pide las miniaturas que faltan o están viejas (la cola de miniaturas limita el ritmo; las minimizadas no se capturan).</summary>
    private void RequestThumbs() { foreach (var w in _ctl.Filtered) _catalog!.Thumbs.Request(w.Hwnd, w.Minimized); }

    private void OnCommand(McCommand c)
    {
        switch (c.Kind)
        {
            case McCmdKind.Redraw: foreach (var w in _wins) w.Invalidate(); foreach (var w in _wins) w.Pump(); break;
            case McCmdKind.Close: Close(); break;
            case McCmdKind.Activate: { long h = c.Hwnd; Close(); _catalog?.Activate(h); break; }
            case McCmdKind.CloseWindow: _catalog?.Close(c.Hwnd); _ctl.Forget(c.Hwnd); foreach (var w in _wins) { w.Invalidate(); w.Pump(); } break;
        }
    }

    public void Close()
    {
        if (!IsOpen && _wins.Count == 0) return;
        IsOpen = false; try { _loop?.Cancel(); } catch (Exception) { } _loop = null;
        foreach (var w in _wins) { try { w.Hide(); w.Dispose(); } catch (Exception e) { _log("[mission] cierre: " + e.Message); } }
        _wins = []; _ctl.Reset(); _lastKey = "";
    }
}

/// <summary>Monitores de la pantalla virtual (reutiliza el enumerador del capturador).</summary>
internal static class ScreenGrabber2 { public static IReadOnlyList<CaptureMonitor> Monitors() => WinMac.Bar.Win.Capture.ScreenGrabber.Monitors(); }
