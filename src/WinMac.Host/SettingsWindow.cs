using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct2D1;
using Vortice.DXGI;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Host.App;

/// <summary>
/// ÚNICA ventana de configuración de WinMac, en el estilo de las listas agrupadas de iOS. Los cambios se aplican EN VIVO (sin «Guardar») por la configuración central.
/// Toda la lógica (qué filas hay, dónde caen, qué hace cada clic y cómo se aplica) vive en Host.Core y está probada; aquí solo hay ventana, ratón, teclado y dibujo.
/// El ratón y el teclado los maneja la propia ventana (no controles sueltos): un clic siempre llega. El cuadro de texto nativo existe solo mientras se edita un campo
/// (así hay portapapeles, selección e IME de Windows) y se coloca justo encima del campo dibujado.
/// </summary>
internal sealed class SettingsWindow : IDisposable
{
    private const uint WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WM_CLOSE = 0x10, WM_DESTROY = 2, WM_COMMAND = 0x111, WM_TIMER = 0x113, WM_KEYDOWN = 0x100, WM_CHAR = 0x102;
    private const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEWHEEL = 0x20A, WM_MOUSELEAVE = 0x2A3, WM_DPICHANGED = 0x2E0, WM_KILLFOCUS = 8;
    private const uint WM_REFRESH = Native.WM_APP + 2, WM_SUGGEST = Native.WM_APP + 3, WM_EDITDONE = Native.WM_APP + 4;
    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, ES_AUTOHSCROLL = 0x80;
    private const int EN_CHANGE = 0x300, EN_KILLFOCUS = 0x200, ID_EDIT = 1000, GWLP_WNDPROC = -4, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_TAB = 0x09, VK_UP = 0x26, VK_DOWN = 0x28, VK_PRIOR = 0x21, VK_NEXT = 0x22;
    private static readonly UIntPtr TimerSuggest = (UIntPtr)7;
    private const string ClassName = "WinMacSettings2";

    private readonly ConfigService _cfg; private readonly Func<IReadOnlyDictionary<string, ModuleState>> _states; private readonly Action<string> _log; private readonly string _exe;
    private readonly Native.WndProc _proc, _editSub; private readonly SettingsController _ctl = new(); private readonly SettingsActions _actions;
    private IntPtr _hwnd, _edit, _editOld, _editFont; private string? _editId, _editInitial; private bool _editDirty, _tracking, _clickLogged;
    private D2DDevice? _dev; private SettingsPainter? _painter; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private double _scale = 1; private bool _dark; private int _wPx, _hPx;
    public IntPtr Handle => _hwnd;
    public bool IsOpen => _hwnd != IntPtr.Zero;
    private IReadOnlyList<WinMac.Apps.InstalledApp>? _appsCache; private DateTime _appsAt;

    public SettingsWindow(ConfigService cfg, Func<IReadOnlyDictionary<string, ModuleState>> states, Action openLocal, string exePath, Action<string> log)
    {
        _cfg = cfg; _states = states; _exe = exePath; _log = log; _proc = WndProcImpl; _editSub = EditProc;
        _actions = new SettingsActions(cfg, openLocal, on =>
        {
            try { if (on) Autostart.Install(_exe); else Autostart.Remove(); }
            catch (Exception e) { _log("[host] no se pudo cambiar el inicio automático: " + e.Message); throw new InvalidOperationException(e.Message); }
        }, log);
        _actions.FindApps = q =>
        {   // el mismo índice y la misma búsqueda que Launchpad y Spotlight; la lista se lee una vez y se reutiliza un minuto
            try
            {
                if (_appsCache is null || (DateTime.UtcNow - _appsAt).TotalSeconds > 60) { _appsCache = WinMac.Apps.Win.AppsFolderReader.Read(_log); _appsAt = DateTime.UtcNow; }
                return WinMac.Apps.AppMatcher.Search(_appsCache, q, null, 5).Select(a => (a.Id, a.Name)).ToList();
            }
            catch (Exception e) { _log("[host] buscar aplicaciones: " + e.GetType().Name); return []; }
        };
        _actions.AdoptAutostart = () =>
        {   // adopción EXPLÍCITA: confirmación clara y solo si Windows hoy inicia OTRA copia; no se borra nada
            var other = Autostart.ForeignPath(_exe); if (other is null) return false;
            if (!WinMac.Dock.Win.Rendering.DarkDialog.Ask("¿Iniciar esta instalación con Windows?", $"Ahora Windows inicia otra copia:\n{other}\n\nSi continúas, iniciará esta:\n{_exe}\n\nLa carpeta anterior no se borra ni se cierra.", "Usar esta instalación", false, _log)) return false;
            var written = Autostart.Adopt(_exe); _log(written is null ? "[host] adopción del inicio automático: sin cambios" : "[host] el inicio automático ahora apunta a esta instalación"); return written is not null;
        };
        _actions.LoadDock = () => { try { return WinMac.Dock.DockSettings.Sanitize(WinMac.Dock.DockConfig.LoadOrDefault(WinMac.Dock.Win.Shell.DockDefaults.ConfigPath, WinMac.Dock.Win.Shell.DockDefaults.Create)); } catch (Exception e) { _log("[host] dock.json no se pudo leer: " + e.Message); return null; } };
        _actions.SaveDock = d => { if (d.TrySave(WinMac.Dock.Win.Shell.DockDefaults.ConfigPath)) { _log("[host] ajustes del Dock guardados (el Dock los vuelve a leer)"); PostRefresh(); } else _log("[host] dock.json no se pudo guardar"); };
        _actions.OpenUri = uri => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", uri) { UseShellExecute = false }); } catch (Exception e) { _log("[host] no se pudo abrir " + uri + ": " + e.Message); } };
        _cfg.Changed += OnConfigChanged;
    }

    private void OnConfigChanged(HostConfig a, HostConfig b) { if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }   // puede llegar de otro hilo

    /// <summary>Extensions (docs/30): operations and the list to show; set by the host.</summary>
    public WinMac.Host.IExtensionOps? ExtensionOps { get => _actions.Extensions; set => _actions.Extensions = value; }
    /// <summary>Reabrir el asistente de primera ejecución (Ajustes › General). Lo pone el host.</summary>
    public Action? RunOnboarding { get => _actions.RunOnboarding; set => _actions.RunOnboarding = value; }
    /// <summary>Fase 3.1: causa de los módulos que no arrancaron, reintento por módulo e informe de diagnóstico (los pone el host).</summary>
    public Func<IReadOnlyDictionary<string, ModuleFailure>>? ModuleFailures { get; set; }
    public Func<string, bool>? RetryModule { get => _actions.RetryModule; set { _actions.RetryModule = id => { bool ok = value?.Invoke(id) ?? false; _ = Task.Delay(1200).ContinueWith(_ => Refresh()); return ok; }; } }
    /// <summary>Diagnóstico de Bluetooth y llamadas (Ajustes › Hatsu Connect): lo pone el host.</summary>
    public ConnectDiagnosticsService? ConnectDiag
    {
        get => _connectDiag;
        set { _connectDiag = value; _actions.DiagRun = value is null ? null : value.Run; _actions.DiagOpen = value is null ? null : value.Open; _actions.DiagCopy = value is null ? null : value.Copy; _actions.DiagSave = value is null ? null : value.Save; _actions.DiagObserve = value is null ? null : value.Observe; _actions.DiagExperiment = value is null ? null : value.RunExperiment; }
    }
    private ConnectDiagnosticsService? _connectDiag;
    public Action? RunDiagnostics { get => _actions.RunDiagnostics; set => _actions.RunDiagnostics = value; }
    public Action? ClearClipboard { get => _actions.ClearClipboard; set => _actions.ClearClipboard = value; }
    /// <summary>WM-034: estado real de la ocultación de la barra de tareas para la página Escritorio y Dock.</summary>
    public Func<string>? TaskbarStatus { get; set; }
    public Func<IReadOnlyList<(WinMac.Host.Extensions.InstalledExtension Ext, string Status)>>? ExtensionList { get; set; }
    /// <summary>Redraw with fresh data (safe from any thread).</summary>
    public void Refresh() { if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }

    public void Show() => Show(null);

    /// <param name="section">Sección pedida desde el panel de la Barra: «weather» abre Clima y ciudades con el campo de ciudad listo; «sound», «system», «network», «disks» abren Barra superior; también el id de cualquier sección.</param>
    public void Show(string? section)
    {
        try
        {
            if (_hwnd == IntPtr.Zero) { Create(); }
            else { Native.ShowWindow(_hwnd, 9); }
            RebuildPages(); if (section is not null) GoTo(section);
            Native.SetForegroundWindow(_hwnd); Invalidate();
        }
        catch (Exception e) { _log("[host] no se pudo abrir la ventana de ajustes: " + e.GetType().Name + ": " + e.Message); DestroyAll(); }
    }

    private void GoTo(string section)
    {
        if (section == "weather") _ctl.SelectPage("weather");
        if (section is "sound" or "system" or "network" or "disks") _ctl.SelectPage("bar");   // «Panel de WinMac» está en Barra superior
        if (section == "notifications") _ctl.SelectPage("nc");
        if (section == "clip") _ctl.SelectPage("nc");   // el portapapeles ahora vive en «Notificaciones y portapapeles»
        if (section is "dock" or "capture" or "spot" or "general" or "notch" or "ext" or "connect" or "gestures") _ctl.SelectPage(section);
        if (section == "weather" && _ctl.RowOf("hub.city") is { } city)
        {
            double y = city.Y; if (y < 0 || y > SettingsLayout.ViewportH - 120) _ctl.Scroll(y - 40);
            ShowEdit("hub.city");
        }
    }

    // ───────────────────────────── ventana y superficie
    private static bool SystemDark() { try { return Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0; } catch (Exception) { return false; } }
    private int S(double v) => (int)Math.Round(v * _scale);

    private void Create()
    {
        uint dpi = Native.GetDpiForSystem(); if (dpi < 96) dpi = 96; _scale = dpi / 96.0; _dark = SystemDark();
        var inst = Native.GetModuleHandle(null);
        var cls = new Native.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = ClassName, hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)32512) };
        Native.RegisterClassEx(ref cls);   // si ya estaba registrada (segunda apertura) falla sin consecuencias
        const uint style = 0x00CA0000;   // WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX: tamaño fijo
        var r = new Native.RECT { Right = S(SettingsLayout.WinW), Bottom = S(SettingsLayout.WinH) }; Native.AdjustWindowRectExForDpi(ref r, style, false, 0, dpi);
        int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1), ww = r.Right - r.Left, wh = r.Bottom - r.Top;
        _hwnd = Native.CreateWindowEx(0, ClassName, "Ajustes de Hatsu Desktop", style, Math.Max(0, (sw - ww) / 2), Math.Max(0, (sh - wh) / 2), ww, wh, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
        int dark = _dark ? 1 : 0; Native.DwmSetWindowAttribute(_hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
        WindowChrome.Apply(_hwnd, _dark, _dark ? 0x161618 : 0xE8E8ED);   // icono de Hatsu y barra de título del color de la barra lateral
        _dev = new D2DDevice(); _dev.SetDpi(_scale); _painter = new SettingsPainter(_dev, _dark); CreateSurface();
        Native.ShowWindow(_hwnd, 5); _clickLogged = false;
    }

    private void CreateSurface()
    {
        Native.GetClientRect(_hwnd, out var rc); _wPx = Math.Max(1, rc.Right - rc.Left); _hPx = Math.Max(1, rc.Bottom - rc.Top); DisposeSurface();
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        _swap = factory.CreateSwapChainForHwnd(_dev!.D3D, _hwnd, new SwapChainDescription1
        {
            Width = (uint)_wPx, Height = (uint)_hPx, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Ignore, Scaling = Scaling.None,
        });
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }

    private void DisposeSurface() { if (_dev is not null) _dev.Context.Target = null; _bmp?.Dispose(); _bmp = null; _swap?.Dispose(); _swap = null; }

    private void Render()
    {
        if (_painter is null || _swap is null || _hwnd == IntPtr.Zero) return;
        try { _painter.Paint(_ctl, SettingsLayout.WinW, SettingsLayout.WinH, CurrentEditView()); _swap.Present(1, PresentFlags.None); }
        catch (Exception e) { _log("[host] ajustes: error al dibujar: " + e.GetType().Name + ": " + e.Message); }
    }

    private bool _caretOn = true; private double _lastX, _lastY;
    private static readonly UIntPtr TimerCaret = (UIntPtr)9;

    /// <summary>Lo que contiene el cuadro de entrada nativo, para dibujarlo aquí (un control hijo no se ve sobre una superficie DXGI).</summary>
    private EditView? CurrentEditView()
    {
        if (_edit == IntPtr.Zero || _editId is null) return null;
        long sel = (long)Native.SendMessage(_edit, 0xB0 /* EM_GETSEL */, IntPtr.Zero, IntPtr.Zero); int a = (int)(sel & 0xFFFF), b = (int)((sel >> 16) & 0xFFFF);
        return new EditView(_editId, EditText(), a, b, _caretOn);
    }

    /// <summary>Un clic dentro del campo que ya se edita: coloca el cursor donde se pulsó.</summary>
    private void PlaceCaretFromClick()
    {
        if (_edit == IntPtr.Zero || _editId is null || _painter is null || _ctl.RowOf(_editId) is not { } row) return;
        var f = SettingsLayout.FieldRect(row); string text = EditText(); long sel = (long)Native.SendMessage(_edit, 0xB0, IntPtr.Zero, IntPtr.Zero); int caret = (int)((sel >> 16) & 0xFFFF);
        double scroll = _painter.EditScroll(text, caret, f.W), xIn = _lastX - SettingsLayout.SidebarW - f.X - 11 + scroll; int i = _painter.IndexAt(text, xIn);
        Native.SendMessage(_edit, 0xB1 /* EM_SETSEL */, (IntPtr)i, (IntPtr)i); _caretOn = true; Invalidate();
    }

    private void Invalidate() { if (_hwnd != IntPtr.Zero) Native.InvalidateRect(_hwnd, IntPtr.Zero, false); }

    /// <summary>
    /// WM-015: permiso de ubicación tal como lo guarda Windows (Configuración › Privacidad › Ubicación), leído del registro del usuario SIN pedirlo ni cambiarlo.
    /// Vacío si no se puede leer: la página no inventa el estado.
    /// </summary>
    private static string LocationConsent()
    {
        try
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location";
            string? v(Microsoft.Win32.RegistryKey root, string sub) { using var k = root.OpenSubKey(sub); return k?.GetValue("Value") as string; }
            string? machine = v(Microsoft.Win32.Registry.LocalMachine, key), user = v(Microsoft.Win32.Registry.CurrentUser, key), desktop = v(Microsoft.Win32.Registry.CurrentUser, key + @"\NonPackaged");
            if (machine == "Deny" || user == "Deny" || desktop == "Deny") return "Denegado (Configuración › Privacidad › Ubicación)";
            return user == "Allow" || desktop == "Allow" ? "Permitido" : "";
        }
        catch (Exception) { return ""; }
    }

    private void RebuildPages()
    {
        IReadOnlyList<(WinMac.Host.Extensions.InstalledExtension, string)>? exts = null;
        try { exts = ExtensionList?.Invoke(); } catch (Exception e) { _log("[extensiones] lista no disponible: " + e.Message); }
        _ctl.SetPages(SettingsBuilder.Build(new SettingsInput(_cfg.Current, _states(), Autostart.Current() is not null, _weatherStatus) { ModuleFailures = ModuleFailures?.Invoke() ?? new Dictionary<string, ModuleFailure>(), AutostartForeignPath = Autostart.ForeignPath(_exe), Extensions = exts, Dock = _actions.LoadDock?.Invoke(), LocationPermission = LocationConsent(), TaskbarStatus = TaskbarStatus?.Invoke() ?? "", ConnectDiag = _connectDiag?.View() ?? new() }));
        PositionEdit();
    }

    // ───────────────────────────── ratón y teclado
    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch (msg)
            {
                case WM_PAINT: Render(); Native.ValidateRect(h, IntPtr.Zero); return IntPtr.Zero;
                case WM_ERASEBKGND: return (IntPtr)1;
                case WM_REFRESH: RebuildPages(); Invalidate(); return IntPtr.Zero;
                case WM_MOUSEMOVE:
                {
                    if (!_tracking) { var t = new Native.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = 2 /* TME_LEAVE */, hwndTrack = _hwnd }; _tracking = Native.TrackMouseEvent(ref t); }
                    if (_ctl.PointerMove(X(l), Y(l))) Invalidate(); return IntPtr.Zero;
                }
                case WM_MOUSELEAVE: _tracking = false; _ctl.PointerLeave(); Invalidate(); return IntPtr.Zero;
                case WM_LBUTTONDOWN:
                {
                    if (!_clickLogged) { _clickLogged = true; _log($"[host] ajustes: primer clic recibido en ({X(l):0},{Y(l):0}) con escala {_scale:0.00}"); }
                    _lastX = X(l); _lastY = Y(l); Native.SetCapture(_hwnd); bool hadEdit = _edit != IntPtr.Zero; var e = _ctl.PointerDown(_lastX, _lastY);
                    if (e is null && hadEdit && _ctl.Suggestions.Count == 0) EndEdit(commit: true);   // pulsar fuera del campo cierra la edición
                    if (e is not null) HandleEdit(e); Invalidate(); return IntPtr.Zero;
                }
                case WM_LBUTTONUP: { Native.ReleaseCapture(); var e = _ctl.PointerUp(X(l), Y(l)); if (e is not null) HandleEdit(e); Invalidate(); return IntPtr.Zero; }
                case WM_MOUSEWHEEL: { int delta = (short)(((long)w >> 16) & 0xFFFF); if (_ctl.Scroll(-delta / 120.0 * 56)) { PositionEdit(); Invalidate(); } return IntPtr.Zero; }
                case WM_KEYDOWN:
                {
                    int vk = (int)w;
                    if (vk == VK_ESCAPE) { if (_ctl.Suggestions.Count > 0) _ctl.ClearSuggestions(); else Native.DestroyWindow(_hwnd); Invalidate(); }
                    else if (vk == VK_DOWN) { if (_ctl.Scroll(56)) PositionEdit(); Invalidate(); }
                    else if (vk == VK_UP) { if (_ctl.Scroll(-56)) PositionEdit(); Invalidate(); }
                    else if (vk == VK_NEXT) { if (_ctl.Scroll(SettingsLayout.ViewportH - 60)) PositionEdit(); Invalidate(); }
                    else if (vk == VK_PRIOR) { if (_ctl.Scroll(-(SettingsLayout.ViewportH - 60))) PositionEdit(); Invalidate(); }
                    return IntPtr.Zero;
                }
                case WM_DPICHANGED:
                {
                    uint dpi = (uint)((long)w & 0xFFFF); _scale = dpi / 96.0; _dev!.SetDpi(_scale); if (_editFont != IntPtr.Zero) { Native.DeleteObject(_editFont); _editFont = IntPtr.Zero; } var rc = Marshal.PtrToStructure<Native.RECT>(l);
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, 0x0004 | 0x0010 /* NOZORDER | NOACTIVATE */); CreateSurface(); PositionEdit(); Invalidate(); return IntPtr.Zero;
                }
                case WM_TIMER when (UIntPtr)(ulong)(long)w == TimerSuggest: Native.KillTimer(_hwnd, TimerSuggest); StartSuggestSearch(); return IntPtr.Zero;
                case WM_TIMER when (UIntPtr)(ulong)(long)w == TimerCaret: _caretOn = !_caretOn; if (_edit != IntPtr.Zero) Invalidate(); return IntPtr.Zero;
                case WM_SUGGEST: ApplySuggestResults(); return IntPtr.Zero;
                case WM_EDITDONE: DestroyEdit(); return IntPtr.Zero;
                case WM_COMMAND when (int)((long)w & 0xFFFF) == ID_EDIT:
                {
                    int code = (int)(((long)w >> 16) & 0xFFFF);
                    if (code == EN_CHANGE && _edit != IntPtr.Zero && Native.GetFocus() == _edit) { _editDirty = true; _caretOn = true; Invalidate(); if (_editId == "hub.city") Native.SetTimer(_hwnd, TimerSuggest, 320, IntPtr.Zero); else if (_editId == "gest.find") ShowAppSuggestions(); else if (_editId == "search") { _ctl.SetSearch(EditText()); Invalidate(); } }
                    else if (code == EN_KILLFOCUS && _edit != IntPtr.Zero && _ctl.Suggestions.Count == 0) EndEdit(commit: true);
                    else if (code == EN_KILLFOCUS && _edit != IntPtr.Zero) { /* sigue la lista de sugerencias: el clic en una sugerencia se atiende antes (PointerDown) */ _suggestBlur = true; Native.SetTimer(_hwnd, (UIntPtr)8, 180, IntPtr.Zero); }
                    return IntPtr.Zero;
                }
                case WM_TIMER when (UIntPtr)(ulong)(long)w == (UIntPtr)8: Native.KillTimer(_hwnd, (UIntPtr)8); if (_suggestBlur && _edit != IntPtr.Zero && Native.GetFocus() != _edit) EndEdit(commit: true); _suggestBlur = false; return IntPtr.Zero;
                case WM_CLOSE: Native.DestroyWindow(h); return IntPtr.Zero;
                case WM_DESTROY: DestroyAll(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[host] error en la ventana de ajustes: " + e.GetType().Name + ": " + e.Message); }
        return Native.DefWindowProc(h, msg, w, l);
    }

    private volatile bool _suggestBlur; private readonly List<IntPtr> _dying = [];

    private void HandleEdit(SettingsEdit e)
    {
        switch (e.Kind)
        {
            case "focus": if (_edit != IntPtr.Zero && _editId == e.Id) PlaceCaretFromClick(); else ShowEdit(e.Id); return;
            case "suggest": if (e.Id.StartsWith("gest.pick:", StringComparison.Ordinal)) ChoosePick(e.Id, e.Index); else if (e.Id == "gest.find") ChooseApp(e.Index); else ChooseSuggestion(e.Index); return;
        }

        if (e.Kind == "button" && e.Id == "bar.preview") { _ctl.ScrollToGroup("Disponibles para agregar"); return; }   // «Personalizar iconos…»
        if (e.Kind == "button" && e.Id == "gest.pick.actions")
        {   // Cancelar (0) descarta; Guardar (1) confirma lo escrito en el campo de la página web
            if (e.Index == 0) { EndEdit(commit: false); _actions.Apply(e); } else EndEdit(commit: true);
            RebuildPages(); return;
        }

        if (e.Kind == "button" && e.Id.StartsWith("gest.pick:", StringComparison.Ordinal)) { EndEdit(commit: true); ShowGestureMenu(e.Id[10..]); return; }
        EndEdit(commit: true); _actions.Apply(e); RebuildPages();
    }

    private List<string> _pickIds = [];
    private IReadOnlyList<(string Id, string Name)> _appResults = [];

    /// <summary>Mientras se escribe en «Buscar aplicación…»: las apps instaladas que coinciden (la misma búsqueda de Launchpad y Spotlight), como lista bajo el campo.</summary>
    private void ShowAppSuggestions()
    {
        string q = EditText().Trim();
        _appResults = q.Length == 0 ? [] : _actions.FindApps?.Invoke(q) ?? [];
        _ctl.SetSuggestions("gest.find", _appResults.Select(a => a.Name).ToList()); Invalidate();
    }

    private void ChooseApp(int index)
    {
        if (_ctl.SuggestFor != "gest.find" || SettingsBuilder.GestPick is not { } pick || index < 0 || index >= _appResults.Count) return;
        var app = _appResults[index]; _editDirty = false; _ctl.ClearSuggestions();
        _actions.AssignApp(pick.Field, app.Id, app.Name); EndEdit(commit: false); RebuildPages(); Invalidate();
    }

    /// <summary>Desplegable de acciones de Gestures: la misma lista de la ventana que usan las sugerencias de ciudad (mismo estilo), con la acción actual marcada.</summary>
    private void ShowGestureMenu(string field)
    {
        if (_actions.GestureChoices(field) is not { Count: > 0 } choices) return;
        _pickIds = choices.Select(c => c.Id).ToList();
        _ctl.OpenDropdown("gest.pick:" + field, choices.Select(c => c.Name).ToList(), choices.ToList().FindIndex(c => c.Current));
        Invalidate();
    }

    private void ChoosePick(string rowId, int index)
    {
        string field = rowId["gest.pick:".Length..]; _ctl.ClearSuggestions();
        if (index >= 0 && index < _pickIds.Count && _pickIds[index] is SettingsBuilder.PickApp or SettingsBuilder.PickUrl)
        {   // «Aplicación…» abre el buscador de apps instaladas; «Página web…», el cuadro para pegar el enlace
            bool app = _pickIds[index] == SettingsBuilder.PickApp; SettingsBuilder.GestPick = (field, app ? "app" : "url"); SettingsBuilder.GestNote = null;
            if (app) _ = Task.Run(() => _actions.FindApps?.Invoke("a"));   // se lee la lista de apps mientras se abre el campo, para que escribir sea instantáneo
            RebuildPages(); Invalidate(); ShowEdit(app ? "gest.find" : "gest.url"); return;
        }

        if (index >= 0 && index < _pickIds.Count) _actions.Apply(new SettingsEdit("gest.set:" + field, "text", Text: _pickIds[index]));
        RebuildPages(); Invalidate();
    }

    // ───────────────────────────── campo de texto nativo (solo mientras se edita)
    private void ShowEdit(string id)
    {
        if (_ctl.RowOf(id) is not { } row) return;
        if (_edit != IntPtr.Zero && _editId == id) { Native.SetFocus(_edit); return; }
        EndEdit(commit: true);
        if (_editFont == IntPtr.Zero) _editFont = Native.CreateFontW(-S(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _edit = Native.CreateWindowEx(0, "EDIT", "", WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL, 0, 0, 10, 10, _hwnd, (IntPtr)ID_EDIT, Native.GetModuleHandle(null), IntPtr.Zero);
        if (_edit == IntPtr.Zero) return;
        Native.SendMessage(_edit, 0x30 /* WM_SETFONT */, _editFont, (IntPtr)1); _editId = id; _editInitial = row.Row.Text ?? ""; Native.SetWindowText(_edit, _editInitial); _editDirty = false;
        _editOld = Native.SetWindowLongPtr(_edit, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_editSub));
        PositionEdit(); Native.SetFocus(_edit); Native.SendMessage(_edit, 0xB1 /* EM_SETSEL */, IntPtr.Zero, (IntPtr)(-1)); _caretOn = true; Native.SetTimer(_hwnd, TimerCaret, 530, IntPtr.Zero); Invalidate();
    }

    /// <summary>El cuadro nativo solo recibe el teclado (escritura, portapapeles, IME): se deja fuera de la pantalla y el texto se dibuja en el campo.</summary>
    private void PositionEdit()
    {
        if (_edit == IntPtr.Zero || _editId is null) return;
        var row = _ctl.RowOf(_editId); if (row is null) return;
        var f = SettingsLayout.FieldRect(row); Native.MoveWindow(_edit, -S(3000), -S(3000), S(f.W - 22), S(f.H - 12), true);
    }

    private string EditText() { var sb = new StringBuilder(520); Native.GetWindowText(_edit, sb, sb.Capacity); return sb.ToString(); }

    /// <summary>Cierra la edición. Con <paramref name="commit"/>, aplica el texto si se escribió algo (aunque sea igual al guardado: así «Molina» se puede volver a buscar).</summary>
    private void EndEdit(bool commit)
    {
        if (_edit == IntPtr.Zero || _editId is null) return;
        string id = _editId, text = EditText(); bool dirty = _editDirty; _editId = null; Native.KillTimer(_hwnd, TimerSuggest); Native.KillTimer(_hwnd, TimerCaret);
        var old = _edit; _edit = IntPtr.Zero; Native.ShowWindow(old, 0); _dying.Add(old); Native.PostMessage(_hwnd, WM_EDITDONE, IntPtr.Zero, IntPtr.Zero);   // se destruye fuera de su propia notificación
        _ctl.ClearSuggestions(); _suggestCts?.Cancel();
        if (commit && dirty) CommitField(id, text.Trim());
        RebuildPages(); Invalidate();
    }

    private void DestroyEdit()
    {
        foreach (var h in _dying) Native.DestroyWindow(h);
        _dying.Clear();
    }

    private void CommitField(string id, string text)
    {
        switch (id)
        {
            case "hub.city": _ = ResolveCityAsync(text); break;
            case "hub.favs": _ = ResolveFavoritesAsync(text); break;
            case "hub.favadd": _ = AddFavoriteAsync(text); break;
            case "search": break;   // el buscador filtra mientras se escribe: no hay nada que confirmar
            case "gest.find": break;   // el texto suelto no asigna nada: hay que elegir una app de la lista
            case "gest.url": if (SettingsBuilder.GestPick is { } up) _actions.AssignUrl(up.Field, text); break;
            default: _actions.Apply(new SettingsEdit(id, "text", Text: text)); break;
        }
    }

    /// <summary>
    /// Campo de atajo: en vez de escribir, se pulsa la combinación. Solo modificadores = se espera; Esc cancela; Retroceso o Supr quitan el atajo; una combinación válida (Ctrl o Alt + tecla) se guarda al instante.
    /// Si la combinación ya la tiene un atajo global de Hatsu, Windows se la entrega a ese atajo y no llega aquí (se dice en el estado del módulo).
    /// </summary>
    private void CaptureHotkey(int vk)
    {
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return;   // solo un modificador: se sigue esperando
        string id = _editId!;
        bool ctrl = (Native.GetKeyState(0x11) & 0x8000) != 0, alt = (Native.GetKeyState(0x12) & 0x8000) != 0, shift = (Native.GetKeyState(0x10) & 0x8000) != 0;
        if (vk == VK_ESCAPE && !ctrl && !alt) { EndEdit(commit: false); Native.SetFocus(_hwnd); return; }
        if ((vk == 0x08 || vk == 0x2E) && !ctrl && !alt) { Native.SetWindowText(_edit, ""); _editDirty = true; EndEdit(commit: true); Native.SetFocus(_hwnd); return; }   // quitar el atajo
        if (WinMac.Bar.Features.HotkeySpec.FromKey((uint)vk, ctrl, alt, shift) is not { } spec) { _log("[host] ajustes: combinación no válida (necesita Ctrl o Alt y una tecla compatible)"); return; }
        Native.SetWindowText(_edit, spec.Format()); _editDirty = true; EndEdit(commit: true); Native.SetFocus(_hwnd);
    }

    private IntPtr EditProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (_editId is { } hid && hid.StartsWith("mod.key:", StringComparison.Ordinal) && msg is WM_KEYDOWN or 0x104 /* WM_SYSKEYDOWN */ or WM_CHAR or 0x106 /* WM_SYSCHAR */)
            {
                if (msg is WM_CHAR or 0x106) return IntPtr.Zero;                         // captura de teclado: nunca se escribe texto, solo se lee la combinación
                CaptureHotkey((int)w); return IntPtr.Zero;
            }

            if (msg == WM_KEYDOWN)
            {
                int vk = (int)w;
                if (vk == VK_DOWN && _ctl.Suggestions.Count > 0) { _ctl.MoveSuggestion(1); Invalidate(); return IntPtr.Zero; }
                if (vk == VK_UP && _ctl.Suggestions.Count > 0) { _ctl.MoveSuggestion(-1); Invalidate(); return IntPtr.Zero; }
                if (vk == VK_RETURN) { if (_ctl.SuggestIndex >= 0) ChooseSuggestion(_ctl.SuggestIndex); else { EndEdit(commit: true); Native.SetFocus(_hwnd); } return IntPtr.Zero; }
                if (vk == VK_ESCAPE) { if (_ctl.Suggestions.Count > 0) { _ctl.ClearSuggestions(); Invalidate(); } else { EndEdit(commit: false); Native.SetFocus(_hwnd); } return IntPtr.Zero; }
                if (vk == VK_TAB) { EndEdit(commit: true); Native.SetFocus(_hwnd); return IntPtr.Zero; }
            }
            else if (msg == WM_CHAR && (int)w is VK_RETURN or VK_ESCAPE or VK_TAB) return IntPtr.Zero;   // sin pitidos
        }
        catch (Exception e) { _log("[host] ajustes: error en el campo: " + e.GetType().Name + ": " + e.Message); }
        var res = Native.CallWindowProc(_editOld, h, msg, w, l);
        if (msg is WM_KEYDOWN or WM_CHAR or 0x101 /* WM_KEYUP */ or 0x302 /* WM_PASTE */ or 0x300 /* WM_CUT */ or 0x303 /* WM_CLEAR */ or 0x7 /* WM_SETFOCUS */) { _caretOn = true; Invalidate(); }   // el cursor y la selección se dibujan en la ventana
        return res;
    }

    // ───────────────────────────── ciudad: autocompletar y resolución
    private volatile string _weatherStatus = ""; private WinMac.Bar.Hub.OpenMeteoSource? _wx; private CancellationTokenSource? _suggestCts;
    private IReadOnlyList<WinMac.Bar.Hub.GeoPoint> _suggestPlaces = []; private string _suggestFor = "";
    private WinMac.Bar.Hub.OpenMeteoSource WeatherSource => _wx ??= new WinMac.Bar.Hub.OpenMeteoSource(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
    /// <summary>País del equipo (según el idioma de Windows): ante varias ciudades con el mismo nombre, se prefiere la de aquí.</summary>
    private static string? HomeCountry() { try { return System.Globalization.RegionInfo.CurrentRegion.DisplayName; } catch (ArgumentException) { return null; } }

    private void StartSuggestSearch()
    {
        if (_edit == IntPtr.Zero || _editId != "hub.city") return;
        string text = EditText().Trim(), query = CityResolver.QueryOf(text);
        if (query.Length < 2 || GeoPointLooksDirect(text)) { _ctl.ClearSuggestions(); Invalidate(); return; }
        _suggestCts?.Cancel(); var cts = _suggestCts = new CancellationTokenSource(); _suggestFor = text; var ct = cts.Token; var hwnd = _hwnd;
        _ = Task.Run(async () =>
        {
            try
            {
                var found = await WeatherSource.SearchAsync(query, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return; _suggestPlaces = CityResolver.Suggest(found, text, HomeCountry()); _suggestSeq = text;
                Native.PostMessage(hwnd, WM_SUGGEST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or FormatException) { /* sin conexión o cancelada: sin sugerencias; al salir del campo se informa */ }
        });
    }

    private volatile string _suggestSeq = "";
    private static bool GeoPointLooksDirect(string s) => s.Contains('|');

    private void ApplySuggestResults()
    {
        if (_edit == IntPtr.Zero || _editId != "hub.city" || _suggestSeq != EditText().Trim()) return;   // llegó tarde: el texto ya cambió
        _ctl.SetSuggestions("hub.city", _suggestPlaces.Select(CityResolver.DisplayLabel).ToList()); Invalidate();
    }

    private void ChooseSuggestion(int index)
    {
        if (_ctl.SuggestFor == "gest.find") { ChooseApp(index); return; }   // Intro sobre una app de la lista
        if (index < 0 || index >= _suggestPlaces.Count) return; var p = _suggestPlaces[index];
        _editDirty = false; _ctl.ClearSuggestions(); _suggestCts?.Cancel();
        _cfg.ApplyBarPatch(Patch("weatherCity", p.Serialize())); _weatherStatus = "Ciudad: " + CityResolver.DisplayLabel(p);
        EndEdit(commit: false); Native.SetFocus(_hwnd); RebuildPages(); Invalidate();
    }

    private static System.Text.Json.JsonElement Patch(string field, object? value) => System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [field] = value });

    private async Task ResolveCityAsync(string text)
    {
        try
        {
            if (text.Length == 0) { _cfg.ApplyBarPatch(Patch("weatherCity", null)); _weatherStatus = "Sin ciudad: el Tiempo no hace peticiones."; }
            else
            {
                _weatherStatus = "Buscando…"; PostRefresh(); var r = await CityResolver.ResolveOneAsync(text, WeatherSource, CancellationToken.None, HomeCountry()).ConfigureAwait(false);
                if (r.Place is not null) { _cfg.ApplyBarPatch(Patch("weatherCity", r.Place.Serialize())); _weatherStatus = "Ciudad: " + r.Message; } else _weatherStatus = r.Message;
            }
        }
        catch (Exception e) { _weatherStatus = "No se pudo buscar la ciudad"; _log("[host] ciudad: " + e.GetType().Name + ": " + e.Message); }
        PostRefresh();
    }

    private async Task ResolveFavoritesAsync(string text)
    {
        try
        {
            var (places, problems) = await CityResolver.ResolveManyAsync(text, WeatherSource, CancellationToken.None, preferCountry: HomeCountry()).ConfigureAwait(false);
            _cfg.ApplyBarPatch(Patch("weatherFavorites", places.Count == 0 ? null : WinMac.Bar.Hub.GeoPoint.SerializeList(places)));
            _weatherStatus = problems.Count == 0 ? _weatherStatus : string.Join(" · ", problems);
        }
        catch (Exception e) { _weatherStatus = "No se pudieron buscar las ciudades"; _log("[host] ciudades: " + e.GetType().Name + ": " + e.Message); }
        PostRefresh();
    }

    /// <summary>WM-015: añade UNA ciudad a la lista de ciudades adicionales (sin duplicados), resuelta con el mismo buscador que la principal.</summary>
    private async Task AddFavoriteAsync(string text)
    {
        text = text.Trim(); if (text.Length == 0) return;
        try
        {
            _weatherStatus = "Buscando…"; PostRefresh();
            var r = await CityResolver.ResolveOneAsync(text, WeatherSource, CancellationToken.None, HomeCountry()).ConfigureAwait(false);
            if (r.Place is { } p)
            {
                var favs = WinMac.Bar.Hub.GeoPoint.ParseList((_cfg.Current.Bar ?? new WinMac.Bar.BarConfig()).WeatherFavorites).ToList();
                if (favs.Any(f => Math.Abs(f.Lat - p.Lat) < 0.01 && Math.Abs(f.Lon - p.Lon) < 0.01)) _weatherStatus = CityResolver.DisplayLabel(p) + " ya está en la lista";
                else { favs.Add(p); _cfg.ApplyBarPatch(Patch("weatherFavorites", WinMac.Bar.Hub.GeoPoint.SerializeList(favs))); _weatherStatus = "Añadida: " + CityResolver.DisplayLabel(p); }
            }
            else _weatherStatus = r.Message;
        }
        catch (Exception e) { _weatherStatus = "No se pudo buscar la ciudad"; _log("[host] ciudad adicional: " + e.GetType().Name + ": " + e.Message); }
        PostRefresh();
    }

    private void PostRefresh() { if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }

    // ───────────────────────────── cierre
    private void DestroyAll()
    {
        _suggestCts?.Cancel(); Native.KillTimer(_hwnd, TimerSuggest); Native.KillTimer(_hwnd, TimerCaret);
        if (_editFont != IntPtr.Zero) { Native.DeleteObject(_editFont); _editFont = IntPtr.Zero; }
        foreach (var d in _dying) Native.DestroyWindow(d); _dying.Clear(); _edit = IntPtr.Zero; _editId = null; DisposeSurface(); _painter?.Dispose(); _painter = null; _dev?.Dispose(); _dev = null; _hwnd = IntPtr.Zero;
    }

    public void Dispose() { _cfg.Changed -= OnConfigChanged; var h = _hwnd; if (h != IntPtr.Zero) { Native.DestroyWindow(h); } DestroyAll(); }
}
