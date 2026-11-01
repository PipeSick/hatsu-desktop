using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Host.App;

/// <summary>
/// ÚNICA ventana de configuración de WinMac: módulos (Barra, Dock, Isla), inicio con Windows y las opciones centrales de la Isla. Los cambios se aplican EN VIVO: cada control
/// pasa por <see cref="ConfigService"/> (que valida, guarda de forma atómica y avisa a los módulos); no hay botón «Guardar». Win32 puro, sin dependencias nuevas, en el mismo hilo
/// del host. La lógica de cada cambio vive en Host.Core (probada); aquí solo hay controles.
/// </summary>
internal sealed class SettingsWindow : IDisposable
{
    private const uint WM_COMMAND = 0x111, WM_HSCROLL = 0x114, WM_SETFONT = 0x30, WM_CLOSE = 0x10, WM_DESTROY = 2, WM_REFRESH = Native.WM_APP + 2;
    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x10000, WS_GROUP = 0x20000, WS_VSCROLL = 0x200000, WS_BORDER = 0x800000;
    private const uint BS_AUTOCHECKBOX = 3, BS_GROUPBOX = 7, ES_AUTOHSCROLL = 0x80, CBS_DROPDOWNLIST = 3, LBS_NOTIFY = 1, SS_LEFT = 0;
    private const uint BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1, CB_ADDSTRING = 0x143, CB_GETCURSEL = 0x147, CB_SETCURSEL = 0x14E, LB_ADDSTRING = 0x180, LB_RESETCONTENT = 0x184, LB_SETCURSEL = 0x186, LB_GETCURSEL = 0x188;
    private const uint TBM_GETPOS = 0x400, TBM_SETPOS = 0x405, TBM_SETRANGE = 0x406, TB_ENDTRACK = 8;
    private const int ID_BAR = 200, ID_DOCK = 201, ID_NOTCH = 202, ID_AUTOSTART = 203, ID_POS = 210, ID_GLASS = 211, ID_PLAYER = 212, ID_PATH = 213, ID_LYRICS = 214, ID_PILL = 215, ID_MUSIC = 216, ID_AGENTS = 217, ID_POLL = 218,
        ID_BARBASE = 300, ID_HUBMS = 370, ID_RATE = 371, ID_WCITY = 372, ID_WFAV = 373, ID_WSTATUS = 374, ID_BGCOMBO = 360, ID_THEMECOMBO = 361, ID_ICS = 362, ID_LIST = 220, ID_UP = 221, ID_DOWN = 222, ID_TOGGLE = 223, ID_LOCAL = 230, ID_CLOSE = 231, ID_GLASSVAL = 240, ID_NOTE = 241;
    private const string ClassName = "WinMacSettings";
    /// <summary>Opciones booleanas de la Barra: (campo del parche, texto, lectura, escritura). Un solo lugar; la ventana las pinta y aplica por tabla.</summary>
    private static readonly (string Field, string Text, Func<WinMac.Bar.BarConfig, bool> Get)[] BarToggles =
    [
        ("showNetwork", "Wi-Fi / red", b => b.ShowNetwork), ("showVolume", "Volumen", b => b.ShowVolume), ("showBattery", "Batería", b => b.ShowBattery), ("showBatteryPercent", "Porcentaje de batería", b => b.ShowBatteryPercent),
        ("showBluetooth", "Bluetooth", b => b.ShowBluetooth), ("showControlCenter", "Control Center", b => b.ShowControlCenter), ("showSeconds", "Segundos en el reloj", b => b.ShowSeconds), ("showDate", "Fecha junto a la hora", b => b.ShowDate),
        ("use24Hour", "Reloj de 24 horas", b => b.Use24Hour), ("reserveSpace", "Reservar el borde superior", b => b.ReserveSpace), ("confirmDestructive", "Confirmar reiniciar / apagar / cerrar sesión", b => b.ConfirmDestructive),
        ("wifiDetails", "Wi-Fi: desplegar detalle (señal, banda) al tocar una red", b => b.WifiDetails), ("appMenus", "Menús de app activa (experimental)", b => b.AppMenus),
        ("ccWifi", "CC: Wi-Fi", b => b.CcWifi), ("ccBluetooth", "CC: Bluetooth", b => b.CcBluetooth), ("ccSound", "CC: Sonido", b => b.CcSound), ("ccBrightness", "CC: Brillo", b => b.CcBrightness),
        ("ccPower", "CC: Energía", b => b.CcPower), ("ccAppearance", "CC: Apariencia", b => b.CcAppearance), ("ccDisplay", "CC: Pantallas", b => b.CcDisplay), ("ccNotifications", "CC: Notificaciones", b => b.CcNotifications),
        // 21–24: grupo «Panel WinMac y Tiempo» (se dibujan allí, no en la rejilla del Control Center)
        ("showHub", "Icono de WinMac (panel con pestañas)", b => b.ShowHub), ("showWeather", "Icono del Tiempo", b => b.ShowWeather), ("weatherFahrenheit", "Fahrenheit", b => b.WeatherFahrenheit),
        ("weatherAutoLocation", "Usar mi ubicación (Windows pide permiso)", b => b.WeatherAutoLocation),
    ];
    private const int CcEnd = 21, HubGroupStart = 21;
    private static readonly string[] RateNames = ["Automática", "KB/s", "MB/s", "Mb/s"], RateValues = ["Auto", "KBps", "MBps", "Mbps"];
    private static readonly string[] BackgroundNames = ["Vidrio", "Transparente"], BackgroundValues = ["Glass", "Transparent"], ThemeNames = ["Sistema", "Claro", "Oscuro"], ThemeValues = ["System", "Light", "Dark"];
    private static readonly string[] PositionNames = ["Centro", "Izquierda", "Derecha"], PlayerNames = ["Spotify", "Feishin", "Otro (indicar ruta)"];
    private static readonly Dictionary<string, string> SectionNames = new()
    {
        ["music"] = "Música", ["agents"] = "Agentes de IA", ["timer"] = "Temporizador", ["calendar"] = "Calendario", ["downloads"] = "Descargas", ["notifications"] = "Notificaciones",
        ["shelf"] = "Estantería", ["camera"] = "Cámara (espejo)", ["system"] = "Recursos del sistema", ["clipboard"] = "Portapapeles",
    };

    private readonly ConfigService _cfg; private readonly Func<IReadOnlyDictionary<string, ModuleState>> _states; private readonly Action _openLocal; private readonly Action<string> _log;
    private readonly string _exe; private readonly Native.WndProc _proc; private readonly Dictionary<int, IntPtr> _c = []; private IntPtr _hwnd, _font; private bool _updating;
    public IntPtr Handle => _hwnd;
    public bool IsOpen => _hwnd != IntPtr.Zero;

    public SettingsWindow(ConfigService cfg, Func<IReadOnlyDictionary<string, ModuleState>> states, Action openLocal, string exePath, Action<string> log)
    { _cfg = cfg; _states = states; _openLocal = openLocal; _exe = exePath; _log = log; _proc = WndProcImpl; _cfg.Changed += OnConfigChanged; }

    private void OnConfigChanged(HostConfig a, HostConfig b) { if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }   // puede llegar de otro hilo

    /// <summary>Abre la ventana, o la trae al frente si ya estaba abierta.</summary>
    public void Show() => Show(null);

    /// <param name="section">Sección a destacar (la pide el panel de la Barra según su pestaña activa).</param>
    public void Show(string? section)
    {
        if (_hwnd != IntPtr.Zero) { Native.ShowWindow(_hwnd, 9); Native.SetForegroundWindow(_hwnd); Refresh(); FocusSection(section); return; }
        try { Build(); Refresh(); Native.ShowWindow(_hwnd, 5); Native.SetForegroundWindow(_hwnd); FocusSection(section); }
        catch (Exception e) { _log("[host] no se pudo abrir la ventana de ajustes: " + e.Message); if (_hwnd != IntPtr.Zero) { Native.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; } }
    }

    private int _dpi = 96; private int S(int v) => v * _dpi / 96;

    private void Build()
    {
        var icc = new Native.INITCOMMONCONTROLSEX { dwSize = (uint)Marshal.SizeOf<Native.INITCOMMONCONTROLSEX>(), dwICC = 0x4 /* ICC_BAR_CLASSES */ }; Native.InitCommonControlsEx(ref icc);
        _dpi = (int)Native.GetDpiForSystem(); if (_dpi < 96) _dpi = 96;
        _font = Native.CreateFontW(-S(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var inst = Native.GetModuleHandle(null);
        var cls = new Native.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = ClassName, hbrBackground = (IntPtr)16 /* COLOR_BTNFACE+1 */, hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)32512) };
        Native.RegisterClassEx(ref cls);   // si ya estaba registrada (segunda apertura) falla sin consecuencias
        const uint style = 0x00CA0000;   // WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX: tamaño fijo
        var r = new Native.RECT { Right = S(1010), Bottom = S(790) }; Native.AdjustWindowRectEx(ref r, style, false, 0);
        int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
        _hwnd = Native.CreateWindowEx(0, ClassName, "Ajustes de WinMac", style, Math.Max(0, (sw - (r.Right - r.Left)) / 2), Math.Max(0, (sh - (r.Bottom - r.Top)) / 2), r.Right - r.Left, r.Bottom - r.Top, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());

        int y = 10, x = 14, w = 470;
        Ctl(0, "BUTTON", "Módulos", BS_GROUPBOX, x, y, w, 100); Ctl(ID_BAR, "BUTTON", "Barra superior", BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 22, 200, 22); Ctl(ID_DOCK, "BUTTON", "Dock", BS_AUTOCHECKBOX | WS_TABSTOP, x + 230, y + 22, 200, 22);
        Ctl(ID_NOTCH, "BUTTON", "Isla (notch, música, agentes)", BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 48, 250, 22); Ctl(ID_AUTOSTART, "BUTTON", "Iniciar con Windows", BS_AUTOCHECKBOX | WS_TABSTOP, x + 230, y + 48, 220, 22);
        Ctl(ID_NOTE, "STATIC", "", SS_LEFT, x + 14, y + 74, w - 28, 20);
        y += 108;
        Ctl(0, "BUTTON", "Isla", BS_GROUPBOX, x, y, w, 395);
        Ctl(0, "STATIC", "Posición", SS_LEFT, x + 14, y + 26, 90, 20); Combo(ID_POS, PositionNames, x + 110, y + 22, 150);
        Ctl(0, "STATIC", "Vidrio", SS_LEFT, x + 14, y + 58, 90, 20); Ctl(ID_GLASS, "msctls_trackbar32", "", 0x1 /* TBS_AUTOTICKS */ | WS_TABSTOP, x + 110, y + 52, 270, 32); Ctl(ID_GLASSVAL, "STATIC", "60", SS_LEFT, x + 390, y + 58, 50, 20);
        Ctl(0, "STATIC", "Reproductor", SS_LEFT, x + 14, y + 94, 90, 20); Combo(ID_PLAYER, PlayerNames, x + 110, y + 90, 200);
        Ctl(0, "STATIC", "Ruta del reproductor", SS_LEFT, x + 14, y + 126, 140, 20); Ctl(ID_PATH, "EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 160, y + 124, 285, 22);
        Ctl(ID_MUSIC, "BUTTON", "Música", BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 156, 130, 22); Ctl(ID_AGENTS, "BUTTON", "Agentes de IA", BS_AUTOCHECKBOX | WS_TABSTOP, x + 160, y + 156, 150, 22);
        Ctl(0, "STATIC", "Actualizar agentes cada (s)", SS_LEFT, x + 14, y + 188, 190, 20); Ctl(ID_POLL, "EDIT", "30", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 210, y + 186, 60, 22);
        Ctl(ID_LYRICS, "BUTTON", "Letras sincronizadas (envía título y artista a NetEase)", BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 218, 440, 22);
        Ctl(ID_PILL, "BUTTON", "Mostrar la letra en el notch cerrado", BS_AUTOCHECKBOX | WS_TABSTOP, x + 34, y + 244, 400, 22);
        Ctl(0, "STATIC", "Pestañas (orden y visibilidad):", SS_LEFT, x + 14, y + 276, 300, 20);
        Ctl(ID_LIST, "LISTBOX", "", LBS_NOTIFY | WS_VSCROLL | WS_BORDER | WS_TABSTOP, x + 14, y + 298, 250, 88);
        Ctl(ID_UP, "BUTTON", "Subir", WS_TABSTOP, x + 280, y + 298, 160, 26); Ctl(ID_DOWN, "BUTTON", "Bajar", WS_TABSTOP, x + 280, y + 330, 160, 26); Ctl(ID_TOGGLE, "BUTTON", "Mostrar / ocultar", WS_TABSTOP, x + 280, y + 362, 160, 26);
        y += 405;
        Ctl(ID_LOCAL, "BUTTON", "Rutas y datos de la Isla…", WS_TABSTOP, x, y + 4, 220, 28); Ctl(ID_CLOSE, "BUTTON", "Cerrar", WS_TABSTOP, x + w - 110, y + 4, 110, 28);
        BuildBar(500, 10, 490); BuildHubGroup(500, 578, 490);
        Native.SendMessage(_c[ID_GLASS], TBM_SETRANGE, (IntPtr)1, (IntPtr)((100 << 16) | 0));
    }

    private void BuildBar(int x, int y, int w)
    {
        Ctl(0, "BUTTON", "Barra superior", BS_GROUPBOX, x, y, w, 560);
        Ctl(0, "STATIC", "Fondo", SS_LEFT, x + 14, y + 26, 60, 20); Combo(ID_BGCOMBO, BackgroundNames, x + 80, y + 22, 140);
        Ctl(0, "STATIC", "Tema", SS_LEFT, x + 250, y + 26, 50, 20); Combo(ID_THEMECOMBO, ThemeNames, x + 300, y + 22, 140);
        for (int i = 0; i < 13; i++) Ctl(ID_BARBASE + i, "BUTTON", BarToggles[i].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 58 + i * 26, w - 28, 22);
        Ctl(0, "STATIC", "Archivo .ics del calendario (opcional):", SS_LEFT, x + 14, y + 404, w - 28, 20); Ctl(ID_ICS, "EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 14, y + 426, w - 28, 22);
        for (int i = 13; i < CcEnd; i++) Ctl(ID_BARBASE + i, "BUTTON", BarToggles[i].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 14 + ((i - 13) % 2) * 235, y + 458 + ((i - 13) / 2) * 22, 225, 20);
    }

    /// <summary>Grupo «Panel WinMac y Tiempo»: iconos, frecuencia de lectura, unidad de red y ciudades. Todo se aplica en vivo por la configuración central.</summary>
    private void BuildHubGroup(int x, int y, int w)
    {
        Ctl(0, "BUTTON", "Panel de WinMac y Tiempo", BS_GROUPBOX, x, y, w, 200);
        Ctl(ID_BARBASE + 21, "BUTTON", BarToggles[21].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 22, 280, 22); Ctl(ID_BARBASE + 22, "BUTTON", BarToggles[22].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 300, y + 22, 180, 22);
        Ctl(0, "STATIC", "Leer sensores cada (ms)", SS_LEFT, x + 14, y + 54, 160, 20); Ctl(ID_HUBMS, "EDIT", "1000", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 180, y + 52, 64, 22);
        Ctl(0, "STATIC", "Red en", SS_LEFT, x + 270, y + 54, 50, 20); Combo(ID_RATE, RateNames, x + 326, y + 50, 140);
        Ctl(0, "STATIC", "Ciudad", SS_LEFT, x + 14, y + 86, 60, 20); Ctl(ID_WCITY, "EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 80, y + 84, 386, 22);
        Ctl(ID_WSTATUS, "STATIC", "", SS_LEFT, x + 80, y + 108, w - 94, 30);   // dos líneas: dice qué ciudad se eligió y cómo pedir otra
        Ctl(0, "STATIC", "Otras", SS_LEFT, x + 14, y + 142, 60, 20); Ctl(ID_WFAV, "EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP | WS_BORDER, x + 80, y + 140, 386, 22);
        Ctl(ID_BARBASE + 23, "BUTTON", BarToggles[23].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 14, y + 166, 110, 22); Ctl(ID_BARBASE + 24, "BUTTON", BarToggles[24].Text, BS_AUTOCHECKBOX | WS_TABSTOP, x + 130, y + 166, 340, 22);
    }

    private void Combo(int id, string[] items, int x, int y, int w)
    {
        Ctl(id, "COMBOBOX", "", CBS_DROPDOWNLIST | WS_VSCROLL | WS_TABSTOP, x, y, w, 200);
        foreach (var s in items) Native.SendMessage(_c[id], CB_ADDSTRING, IntPtr.Zero, s);
    }

    private void Ctl(int id, string cls, string text, uint style, int x, int y, int w, int h)
    {
        var hw = Native.CreateWindowEx(0, cls, text, WS_CHILD | WS_VISIBLE | style, S(x), S(y), S(w), S(h), _hwnd, (IntPtr)id, Native.GetModuleHandle(null), IntPtr.Zero);
        if (hw == IntPtr.Zero) return; Native.SendMessage(hw, WM_SETFONT, _font, (IntPtr)1); if (id != 0) _c[id] = hw;
    }

    private static bool Checked(IntPtr h) => Native.SendMessage(h, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero;
    private void SetCheck(int id, bool on) { if (_c.TryGetValue(id, out var h)) Native.SendMessage(h, BM_SETCHECK, (IntPtr)(on ? 1 : 0), IntPtr.Zero); }
    private void Enable(int id, bool on) { if (_c.TryGetValue(id, out var h)) Native.EnableWindow(h, on); }
    private string TextOf(int id) { var sb = new StringBuilder(520); Native.GetWindowText(_c[id], sb, sb.Capacity); return sb.ToString(); }
    private static int Index(string[] arr, string v) => Math.Max(0, Array.FindIndex(arr, x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Vuelca la configuración vigente en los controles (sin que eso dispare cambios).</summary>
    private void Refresh()
    {
        if (_hwnd == IntPtr.Zero || _c.Count == 0) return;
        _updating = true;
        try
        {
            var cfg = _cfg.Current; var n = _cfg.Notch; var st = _states();
            bool installed = !(st.TryGetValue("notch", out var ns) && ns == ModuleState.NotInstalled);
            SetCheck(ID_BAR, cfg.Modules["bar"].Enabled); SetCheck(ID_DOCK, cfg.Modules["dock"].Enabled); SetCheck(ID_NOTCH, installed && cfg.Modules["notch"].Enabled); SetCheck(ID_AUTOSTART, Autostart.Current() is not null);
            Enable(ID_NOTCH, installed); Native.SetWindowText(_c[ID_NOTE], installed ? "" : "La Isla (NotchWin) no está instalada junto a WinMac.");
            Native.SendMessage(_c[ID_POS], CB_SETCURSEL, (IntPtr)Index(NotchSettings.Positions, n.Position), IntPtr.Zero);
            Native.SendMessage(_c[ID_GLASS], TBM_SETPOS, (IntPtr)1, (IntPtr)n.GlassLevel); Native.SetWindowText(_c[ID_GLASSVAL], n.GlassLevel.ToString());
            Native.SendMessage(_c[ID_PLAYER], CB_SETCURSEL, (IntPtr)Index(NotchSettings.Players, n.MusicPlayer), IntPtr.Zero);
            SetIfNotFocused(ID_PATH, n.CustomPlayerPath ?? ""); SetIfNotFocused(ID_POLL, n.PollIntervalSeconds.ToString());
            SetCheck(ID_MUSIC, n.MusicEnabled); SetCheck(ID_AGENTS, n.AgentsEnabled); SetCheck(ID_LYRICS, n.LyricsEnabled); SetCheck(ID_PILL, n.LyricsInPill);
            Enable(ID_PILL, n.LyricsEnabled); Enable(ID_PATH, n.MusicPlayer == "Custom");
            var bar = _cfg.Bar;
            for (int i = 0; i < BarToggles.Length; i++) SetCheck(ID_BARBASE + i, BarToggles[i].Get(bar));
            Native.SendMessage(_c[ID_BGCOMBO], CB_SETCURSEL, (IntPtr)Index(BackgroundValues, bar.Background.ToString()), IntPtr.Zero);
            Native.SendMessage(_c[ID_THEMECOMBO], CB_SETCURSEL, (IntPtr)Index(ThemeValues, bar.Theme.ToString()), IntPtr.Zero);
            SetIfNotFocused(ID_ICS, bar.CalendarIcsPath ?? "");
            SetIfNotFocused(ID_HUBMS, bar.HubRefreshMs.ToString()); Native.SendMessage(_c[ID_RATE], CB_SETCURSEL, (IntPtr)Index(RateValues, bar.RateUnit.ToString()), IntPtr.Zero);
            SetIfNotFocused(ID_WCITY, WinMac.Bar.Hub.GeoPoint.Parse(bar.WeatherCity)?.Name ?? ""); SetIfNotFocused(ID_WFAV, string.Join(", ", WinMac.Bar.Hub.GeoPoint.ParseList(bar.WeatherFavorites).Select(g => g.Name)));
            Native.SetWindowText(_c[ID_WSTATUS], _weatherStatus);
            int sel = (int)Native.SendMessage(_c[ID_LIST], LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
            Native.SendMessage(_c[ID_LIST], LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
            foreach (var s in n.Sections) Native.SendMessage(_c[ID_LIST], LB_ADDSTRING, IntPtr.Zero, (s.Enabled ? "✓  " : "      ") + (SectionNames.TryGetValue(s.Id, out var nm) ? nm : s.Id));
            if (sel >= 0) Native.SendMessage(_c[ID_LIST], LB_SETCURSEL, (IntPtr)Math.Min(sel, n.Sections.Count - 1), IntPtr.Zero);
            foreach (var id in new[] { ID_POS, ID_GLASS, ID_PLAYER, ID_PATH, ID_MUSIC, ID_AGENTS, ID_POLL, ID_LYRICS, ID_PILL, ID_LIST, ID_UP, ID_DOWN, ID_TOGGLE, ID_LOCAL }) if (id is not (ID_PATH or ID_PILL)) Enable(id, installed);
            if (!installed) { Enable(ID_PATH, false); Enable(ID_PILL, false); }
        }
        finally { _updating = false; }
    }
    private void SetIfNotFocused(int id, string v) { if (GetFocus() != _c[id]) Native.SetWindowText(_c[id], v); }
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch (msg)
            {
                case WM_REFRESH: Refresh(); return IntPtr.Zero;
                case WM_COMMAND when !_updating: OnCommand((int)((long)w & 0xFFFF), (int)(((long)w >> 16) & 0xFFFF)); return IntPtr.Zero;
                case WM_HSCROLL when l == (_c.TryGetValue(ID_GLASS, out var g) ? g : IntPtr.Zero) && !_updating:
                    int pos = (int)Native.SendMessage(g, TBM_GETPOS, IntPtr.Zero, IntPtr.Zero); Native.SetWindowText(_c[ID_GLASSVAL], pos.ToString());
                    if (((long)w & 0xFFFF) == TB_ENDTRACK) _cfg.UpdateNotch(n => n with { GlassLevel = pos });   // solo al soltar: no se guarda 100 veces por arrastre
                    return IntPtr.Zero;
                case WM_CLOSE: Native.DestroyWindow(h); return IntPtr.Zero;
                case WM_DESTROY: _hwnd = IntPtr.Zero; _c.Clear(); if (_font != IntPtr.Zero) { Native.DeleteObject(_font); _font = IntPtr.Zero; } return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[host] error en la ventana de ajustes: " + e.Message); }
        return Native.DefWindowProc(h, msg, w, l);
    }

    private volatile bool _cityDirty, _favDirty;
    /// <summary>País del equipo (según el idioma de Windows): ante varias ciudades con el mismo nombre, se prefiere la de aquí.</summary>
    private static string? HomeCountry() { try { return System.Globalization.RegionInfo.CurrentRegion.DisplayName; } catch (ArgumentException) { return null; } }
    private volatile string _weatherStatus = ""; private WinMac.Bar.Hub.OpenMeteoSource? _wx;
    private WinMac.Bar.Hub.OpenMeteoSource WeatherSource => _wx ??= new WinMac.Bar.Hub.OpenMeteoSource(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });

    /// <summary>Busca la ciudad escrita en el proveedor del tiempo (en segundo plano) y, si la encuentra, la guarda. Si no, NO cambia la ciudad y lo dice.</summary>
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

    private void PostRefresh() { if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_REFRESH, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>Lleva el foco al primer control de la sección pedida desde el panel de la Barra («sound», «system», «network», «disks» → Panel de WinMac; «weather» → Ciudad).</summary>
    private void FocusSection(string? section)
    {
        if (section is null || _hwnd == IntPtr.Zero) return;
        int id = section == "weather" ? ID_WCITY : section is "sound" or "system" or "network" or "disks" ? ID_HUBMS : 0;
        if (id != 0 && _c.TryGetValue(id, out var h)) SetFocus(h);
    }
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);

    private static System.Text.Json.JsonElement Patch(string field, object? value) => System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [field] = value });

    private void OnCommand(int id, int code)
    {
        const int BN_CLICKED = 0, CBN_SELCHANGE = 1, EN_KILLFOCUS = 0x200, EN_CHANGE = 0x300, LBN_DBLCLK = 2;
        if (id >= ID_BARBASE && id < ID_BARBASE + BarToggles.Length && code == BN_CLICKED)
        { var f = BarToggles[id - ID_BARBASE].Field; bool on = Checked(_c[id]); _cfg.ApplyBarPatch(Patch(f, on)); return; }
        switch (id)
        {
            case ID_HUBMS when code == EN_KILLFOCUS: { if (int.TryParse(TextOf(id).Trim(), out int ms)) _cfg.ApplyBarPatch(Patch("hubRefreshMs", ms)); Refresh(); break; }
            case ID_RATE when code == CBN_SELCHANGE: { int i = (int)Native.SendMessage(_c[id], CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (i >= 0 && i < RateValues.Length) _cfg.ApplyBarPatch(Patch("rateUnit", RateValues[i])); break; }
            case ID_WCITY when code == EN_CHANGE: if (GetFocus() == _c[id]) _cityDirty = true; break;   // solo cuenta lo que TÚ escribes (no el texto que pone la propia ventana al refrescar)
            case ID_WFAV when code == EN_CHANGE: if (GetFocus() == _c[id]) _favDirty = true; break;
            case ID_WCITY when code == EN_KILLFOCUS: if (_cityDirty) { _cityDirty = false; _ = ResolveCityAsync(TextOf(id).Trim()); } break;
            case ID_WFAV when code == EN_KILLFOCUS: if (_favDirty) { _favDirty = false; _ = ResolveFavoritesAsync(TextOf(id).Trim()); } break;
            case ID_BGCOMBO when code == CBN_SELCHANGE: { int i = (int)Native.SendMessage(_c[id], CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (i >= 0 && i < BackgroundValues.Length) _cfg.ApplyBarPatch(Patch("background", BackgroundValues[i])); break; }
            case ID_THEMECOMBO when code == CBN_SELCHANGE: { int i = (int)Native.SendMessage(_c[id], CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (i >= 0 && i < ThemeValues.Length) _cfg.ApplyBarPatch(Patch("theme", ThemeValues[i])); break; }
            case ID_ICS when code == EN_KILLFOCUS: { var t = TextOf(id).Trim(); _cfg.ApplyBarPatch(Patch("calendarIcsPath", t.Length == 0 ? null : t)); break; }
            case ID_BAR or ID_DOCK or ID_NOTCH when code == BN_CLICKED:
                _cfg.SetModuleEnabled(id == ID_BAR ? "bar" : id == ID_DOCK ? "dock" : "notch", Checked(_c[id])); break;
            case ID_AUTOSTART when code == BN_CLICKED:
                try { if (Checked(_c[id])) Autostart.Install(_exe); else Autostart.Remove(); _cfg.SetAutostartFlag(Checked(_c[id])); }
                catch (Exception e) { _log("[host] no se pudo cambiar el inicio automático: " + e.Message); Refresh(); }
                break;
            case ID_POS when code == CBN_SELCHANGE: { int i = (int)Native.SendMessage(_c[id], CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (i >= 0 && i < NotchSettings.Positions.Length) _cfg.UpdateNotch(n => n with { Position = NotchSettings.Positions[i] }); break; }
            case ID_PLAYER when code == CBN_SELCHANGE: { int i = (int)Native.SendMessage(_c[id], CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (i >= 0 && i < NotchSettings.Players.Length) _cfg.UpdateNotch(n => n with { MusicPlayer = NotchSettings.Players[i] }); break; }
            case ID_PATH when code == EN_KILLFOCUS: { var t = TextOf(id); _cfg.UpdateNotch(n => n with { CustomPlayerPath = t }); break; }
            case ID_POLL when code == EN_KILLFOCUS: { if (int.TryParse(TextOf(id).Trim(), out int s)) _cfg.UpdateNotch(n => n with { PollIntervalSeconds = s }); Refresh(); break; }
            case ID_MUSIC when code == BN_CLICKED: _cfg.UpdateNotch(n => n with { MusicEnabled = Checked(_c[id]) }); break;
            case ID_AGENTS when code == BN_CLICKED: _cfg.UpdateNotch(n => n with { AgentsEnabled = Checked(_c[id]) }); break;
            case ID_LYRICS when code == BN_CLICKED: { bool on = Checked(_c[id]); _cfg.UpdateNotch(n => n with { LyricsEnabled = on, LyricsInPill = on && n.LyricsInPill }); break; }
            case ID_PILL when code == BN_CLICKED: _cfg.UpdateNotch(n => n with { LyricsInPill = Checked(_c[id]) }); break;
            case ID_UP or ID_DOWN or ID_TOGGLE when code == BN_CLICKED || code == LBN_DBLCLK:
            {
                int sel = (int)Native.SendMessage(_c[ID_LIST], LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (sel < 0) break;
                int to = id == ID_UP ? sel - 1 : id == ID_DOWN ? sel + 1 : sel;
                _cfg.UpdateNotch(n => id == ID_TOGGLE ? n.ToggleSection(sel) : n.MoveSection(sel, to - sel));
                Refresh(); Native.SendMessage(_c[ID_LIST], LB_SETCURSEL, (IntPtr)Math.Clamp(to, 0, NotchSettings.SectionIds.Length - 1), IntPtr.Zero); break;
            }
            case ID_LIST when code == LBN_DBLCLK: { int sel = (int)Native.SendMessage(_c[ID_LIST], LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); if (sel >= 0) { _cfg.UpdateNotch(n => n.ToggleSection(sel)); Refresh(); Native.SendMessage(_c[ID_LIST], LB_SETCURSEL, (IntPtr)sel, IntPtr.Zero); } break; }
            case ID_LOCAL when code == BN_CLICKED: _openLocal(); break;
            case ID_CLOSE when code == BN_CLICKED: Native.DestroyWindow(_hwnd); break;
        }
    }

    public void Dispose() { _cfg.Changed -= OnConfigChanged; if (_hwnd != IntPtr.Zero) { Native.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; } }
}
