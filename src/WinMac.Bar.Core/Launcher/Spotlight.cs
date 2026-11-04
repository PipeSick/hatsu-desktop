using System.Globalization;
using WinMac.Apps;

namespace WinMac.Bar.Launcher;

public enum SpotlightHotkey { AltSpace, CtrlSpace, Off }

public enum SpotKind { Calc, Convert, App, Setting, Folder, File, Path, Action, Tool, Command, Script, Clipboard }

/// <summary>Un resultado de Spotlight. <see cref="Target"/>: el Id de la app, la ruta, el URI <c>ms-settings:</c> o el valor a copiar.</summary>
public sealed record SpotItem(SpotKind Kind, string Title, string? Subtitle, string Target, InstalledApp? App = null)
{
    /// <summary>Needs a second Enter (user scripts).</summary>
    public bool Confirm { get; init; }
    public string? Arguments { get; init; }
    /// <summary>Extra lines for the preview pane (e.g. a tool's result, filled when it finishes).</summary>
    public IReadOnlyList<string>? Details { get; init; }

    public string Section => Kind switch
    {
        SpotKind.Calc or SpotKind.Convert => "Calculadora",
        SpotKind.App => "Aplicaciones",
        SpotKind.Setting => "Configuración",
        SpotKind.Folder or SpotKind.Path => "Carpetas y rutas",
        SpotKind.Action => "Hatsu Desktop",
        SpotKind.Tool => "Herramientas",
        SpotKind.Command => "Terminal",
        SpotKind.Script => "Scripts",
        SpotKind.Clipboard => "Portapapeles",
        _ => "Archivos",
    };
}

/// <summary>Calculadora de Spotlight: + − × ÷ ^ %, paréntesis, decimales con punto o coma. Sin variables ni funciones: nada que pueda ejecutar código.</summary>
public static class SpotCalculator
{
    public static bool LooksLikeMath(string s) => s.Any(char.IsDigit) && s.Any(c => "+-*/^×÷x%()".Contains(c)) && s.All(c => char.IsDigit(c) || " .,+-*/^×÷x%()".Contains(c));

    public static double? Evaluate(string s)
    {
        if (!LooksLikeMath(s)) return null;
        var p = new Parser(s.Replace('×', '*').Replace('÷', '/').Replace('x', '*').Replace(',', '.'));
        try { double v = p.Expr(); p.Ws(); return p.End && double.IsFinite(v) ? v : null; } catch (FormatException) { return null; }
    }

    public static string Format(double v) => Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0) ? v.ToString("G10", CultureInfo.GetCultureInfo("es-ES")) : Math.Round(v, 10).ToString("#,0.##########", CultureInfo.GetCultureInfo("es-ES"));

    private sealed class Parser(string s)
    {
        private int _i;
        public bool End => _i >= s.Length;
        public void Ws() { while (_i < s.Length && s[_i] == ' ') _i++; }
        private bool Eat(char c) { Ws(); if (_i < s.Length && s[_i] == c) { _i++; return true; } return false; }

        public double Expr()
        {
            double v = Term();
            while (true) { if (Eat('+')) v += Term(); else if (Eat('-')) v -= Term(); else return v; }
        }

        private double Term()
        {
            double v = Power();
            while (true)
            {
                if (Eat('*')) v *= Power();
                else if (Eat('/')) { double d = Power(); if (d == 0) throw new FormatException(); v /= d; }
                else return v;
            }
        }

        private double Power() { double b = Unary(); return Eat('^') ? Math.Pow(b, Power()) : b; }

        private double Unary()
        {
            if (Eat('-')) return -Unary();
            if (Eat('+')) return Unary();
            double v = Atom();
            if (Eat('%')) v /= 100;
            return v;
        }

        private double Atom()
        {
            if (Eat('(')) { double v = Expr(); if (!Eat(')')) throw new FormatException(); return v; }
            Ws(); int st = _i;
            while (_i < s.Length && (char.IsDigit(s[_i]) || s[_i] == '.')) _i++;
            if (st == _i) throw new FormatException();
            return double.Parse(s[st.._i], NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>Conversiones básicas: «10 km en mi», «5 kg a lb», «100 f to c», «2 gb en mb».</summary>
public static class SpotConverter
{
    private static readonly Dictionary<string, (string Dim, double Factor, string Name)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = ("len", 0.001, "mm"), ["cm"] = ("len", 0.01, "cm"), ["m"] = ("len", 1, "m"), ["km"] = ("len", 1000, "km"),
        ["in"] = ("len", 0.0254, "in"), ["pulgadas"] = ("len", 0.0254, "in"), ["ft"] = ("len", 0.3048, "ft"), ["pies"] = ("len", 0.3048, "ft"), ["yd"] = ("len", 0.9144, "yd"), ["mi"] = ("len", 1609.344, "mi"), ["millas"] = ("len", 1609.344, "mi"),
        ["mg"] = ("mass", 0.001, "mg"), ["g"] = ("mass", 1, "g"), ["kg"] = ("mass", 1000, "kg"), ["t"] = ("mass", 1e6, "t"), ["oz"] = ("mass", 28.349523125, "oz"), ["lb"] = ("mass", 453.59237, "lb"), ["libras"] = ("mass", 453.59237, "lb"),
        ["ml"] = ("vol", 0.001, "ml"), ["l"] = ("vol", 1, "l"), ["gal"] = ("vol", 3.785411784, "gal"),
        ["b"] = ("data", 1, "B"), ["kb"] = ("data", 1024, "KB"), ["mb"] = ("data", 1024d * 1024, "MB"), ["gb"] = ("data", 1024d * 1024 * 1024, "GB"), ["tb"] = ("data", 1024d * 1024 * 1024 * 1024, "TB"),
        ["s"] = ("time", 1, "s"), ["min"] = ("time", 60, "min"), ["h"] = ("time", 3600, "h"), ["dias"] = ("time", 86400, "días"), ["días"] = ("time", 86400, "días"),
        ["kmh"] = ("speed", 1 / 3.6, "km/h"), ["km/h"] = ("speed", 1 / 3.6, "km/h"), ["mph"] = ("speed", 0.44704, "mph"), ["m/s"] = ("speed", 1, "m/s"),
        ["c"] = ("temp", 0, "°C"), ["°c"] = ("temp", 0, "°C"), ["f"] = ("temp", 0, "°F"), ["°f"] = ("temp", 0, "°F"), ["k"] = ("temp", 0, "K"),
    };

    public static (string Result, string Expression)? Convert(string q)
    {
        var parts = q.Trim().Replace(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[2].ToLowerInvariant() is not ("en" or "a" or "to" or "in")) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
        if (!Units.TryGetValue(parts[1], out var from) || !Units.TryGetValue(parts[3], out var to) || from.Dim != to.Dim) return null;
        double r = from.Dim == "temp" ? FromKelvin(ToKelvin(v, from.Name), to.Name) : v * from.Factor / to.Factor;
        r = Math.Abs(r) >= 1 ? Math.Round(r, 4) : r;
        return ($"{SpotCalculator.Format(r)} {to.Name}", $"{SpotCalculator.Format(v)} {from.Name} =");
    }

    private static double ToKelvin(double v, string u) => u switch { "°C" => v + 273.15, "°F" => (v - 32) * 5 / 9 + 273.15, _ => v };
    private static double FromKelvin(double k, string u) => u switch { "°C" => k - 273.15, "°F" => (k - 273.15) * 9 / 5 + 32, _ => k };
}

/// <summary>Accesos a Configuración de Windows: solo URIs <c>ms-settings:</c> de una lista fija (documentados por Microsoft). Nunca se abre un URI escrito por el usuario.</summary>
public static class SpotSettings
{
    public static readonly (string Title, string Uri, string Keywords)[] All =
    [
        ("Wi-Fi", "ms-settings:network-wifi", "wifi red inalambrica internet"), ("Bluetooth y dispositivos", "ms-settings:bluetooth", "bluetooth auriculares raton teclado"),
        ("Pantalla", "ms-settings:display", "pantalla resolucion monitor escala brillo"), ("Sonido", "ms-settings:sound", "sonido audio volumen altavoces microfono"),
        ("Notificaciones", "ms-settings:notifications", "notificaciones no molestar"), ("Batería y energía", "ms-settings:powersleep", "bateria energia suspension apagar"),
        ("Almacenamiento", "ms-settings:storagesense", "almacenamiento disco espacio"), ("Fondo de pantalla", "ms-settings:personalization-background", "fondo escritorio personalizacion"),
        ("Colores y tema", "ms-settings:colors", "colores tema oscuro claro modo"), ("Aplicaciones instaladas", "ms-settings:appsfeatures", "aplicaciones desinstalar programas"),
        ("Aplicaciones de inicio", "ms-settings:startupapps", "inicio arranque aplicaciones"), ("Windows Update", "ms-settings:windowsupdate", "actualizaciones update windows"),
        ("Privacidad y seguridad", "ms-settings:privacy", "privacidad permisos seguridad"), ("Red e Internet", "ms-settings:network", "red ethernet vpn proxy internet"),
        ("Fecha y hora", "ms-settings:dateandtime", "fecha hora zona horaria reloj"), ("Idioma y región", "ms-settings:regionlanguage", "idioma region teclado"),
        ("Accesibilidad", "ms-settings:easeofaccess", "accesibilidad lupa narrador contraste"), ("Ratón", "ms-settings:mousetouchpad", "raton puntero cursor"),
        ("Impresoras y escáneres", "ms-settings:printers", "impresora escaner imprimir"), ("Cuentas", "ms-settings:yourinfo", "cuenta usuario perfil"),
    ];

    public static IEnumerable<(int Score, SpotItem Item)> Search(string q)
    {
        foreach (var (title, uri, kw) in All)
        {
            int s = AppMatcher.Score(q, title);
            if (s is >= 0 and < SpotlightEngine.Weak) s = -1;   // letras sueltas en orden: en Configuración no (con «con» salía «Notificaciones»)
            // Palabra clave: con 4+ letras («resolu» → Pantalla); con menos, una palabra clave corta coincide con demasiadas cosas («con» → «contraste» → Accesibilidad).
            if (s < 0 && AppMatcher.Fold(q.Trim()).Length >= 4) s = kw.Split(' ').Any(k => k.StartsWith(AppMatcher.Fold(q.Trim()), StringComparison.Ordinal)) ? 300 : -1;
            if (s >= 0) yield return (s, new SpotItem(SpotKind.Setting, title, "Configuración de Windows", uri));
        }
    }
}

/// <summary>
/// Motor de Spotlight: junta calculadora, conversiones, apps (el MISMO índice y la misma búsqueda que Launchpad), Configuración, carpetas del usuario, rutas escritas y
/// archivos (los recientes y, si Windows Search responde, los que encuentre; los aporta el anfitrión). Puro: los archivos llegan ya leídos.
/// </summary>
public static class SpotlightEngine
{
    public const int MaxResults = 9;
    /// <summary>Puntuaciones de <see cref="AppMatcher.Score"/>: desde «iniciales» (550) es una coincidencia fuerte; por debajo de «contiene» (≈300) es débil (letras sueltas).</summary>
    public const int Strong = 550, Weak = 300;

    public static IReadOnlyList<SpotItem> Query(string text, IReadOnlyList<InstalledApp> apps, IReadOnlyDictionary<string, int>? usage,
        IReadOnlyList<(string Name, string Path)> folders, IReadOnlyList<SpotItem> files, Func<string, bool>? pathExists = null,
        bool recording = false, IReadOnlyList<SpotScript>? scripts = null, IReadOnlyList<string>? workspaces = null, IReadOnlyList<SpotItem>? clipboard = null)
    {
        var q = text.Trim(); var list = new List<SpotItem>();
        if (q.Length == 0) return list;
        list.AddRange(SpotPro.Tools(q));
        if (q.StartsWith('>')) return list.Take(MaxResults).ToList();   // «> …» is for the terminal only
        if (SpotPro.PercentOf(q) is { } pc) { var r = SpotCalculator.Format(pc); list.Add(new SpotItem(SpotKind.Calc, r, q + " =", r)); }
        if (SpotCalculator.Evaluate(q) is { } v) { var r = SpotCalculator.Format(v); list.Add(new SpotItem(SpotKind.Calc, r, q + " =", r)); }
        if (SpotConverter.Convert(q) is { } c) list.Add(new SpotItem(SpotKind.Convert, c.Result, c.Expression, c.Result));
        if (LooksLikePath(q) && pathExists is not null)
        {
            var full = ExpandPath(q);
            if (full is not null && pathExists(full)) list.Add(new SpotItem(SpotKind.Path, System.IO.Path.GetFileName(full.TrimEnd('\\')) is { Length: > 0 } n ? n : full, full, full));
        }

        // Apps, Configuración y carpetas compiten por relevancia (una coincidencia floja de app no tapa una carpeta que empieza por lo escrito); en empate, apps primero.
        var ranked = new List<(int Score, int Order, SpotItem Item)>();
        ranked.AddRange(AppMatcher.Search(apps, q, usage, 5).Select(a => (AppMatcher.Score(q, a.Name) + 1, 0, new SpotItem(SpotKind.App, a.Name, "Aplicación", a.Id, a))));
        ranked.AddRange(SpotSettings.Search(q).OrderByDescending(x => x.Score).Take(2).Select(x => (x.Score, 1, x.Item)));
        ranked.AddRange(SpotPro.SearchActions(q, recording).OrderByDescending(x => x.Score).Take(3).Select(x => (x.Score, 1, x.Item)));
        if (workspaces is { Count: > 0 }) ranked.AddRange(SpotPro.SearchWorkspaces(q, workspaces).OrderByDescending(x => x.Score).Take(3).Select(x => (x.Score, 1, x.Item)));
        if (scripts is { Count: > 0 }) ranked.AddRange(SpotPro.SearchScripts(q, scripts).Take(2).Select(s => (AppMatcher.Score(q, s.Title), 3, s)));
        ranked.AddRange(folders.Select(f => (f, s: AppMatcher.Score(q, f.Name))).Where(x => x.s >= 400).OrderByDescending(x => x.s).Take(2)
            .Select(x => (x.s, 2, new SpotItem(SpotKind.Folder, x.f.Name, x.f.Path, x.f.Path))));
        // Relevancia: si hay coincidencias fuertes (exacta, empieza por, palabra, iniciales), las débiles (letras sueltas en orden) no se muestran.
        int best = ranked.Count == 0 ? -1 : ranked.Max(x => x.Score);
        if (best >= Strong) ranked.RemoveAll(x => x.Score < Weak);
        list.AddRange(ranked.OrderByDescending(x => x.Score).ThenBy(x => x.Order).Select(x => x.Item));
        // Clipboard (from the Island, already filtered by its privacy rules): «portapapeles» / «cb …» show it first; otherwise up to 3 matches after the rest
        if (clipboard is { Count: > 0 })
        {
            bool asked = q.Equals("portapapeles", StringComparison.OrdinalIgnoreCase) || q.Equals("clipboard", StringComparison.OrdinalIgnoreCase) || q.StartsWith("cb ", StringComparison.OrdinalIgnoreCase);
            if (asked) list.InsertRange(0, clipboard.Take(MaxResults)); else list.AddRange(clipboard.Take(3));
        }
        list.AddRange(files.Where(f => !list.Any(l => string.Equals(l.Target, f.Target, StringComparison.OrdinalIgnoreCase))).Take(4));
        return list.Take(MaxResults).ToList();
    }

    public static bool LooksLikePath(string q) => q.Length >= 3 && ((char.IsLetter(q[0]) && q[1] == ':' && q[2] is '\\' or '/') || q.StartsWith("\\\\", StringComparison.Ordinal) || q.StartsWith('%') || q.StartsWith("~\\", StringComparison.Ordinal) || q.StartsWith("~/", StringComparison.Ordinal));

    public static string? ExpandPath(string q)
    {
        try
        {
            if (q.StartsWith('~')) q = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + q[1..];
            return Environment.ExpandEnvironmentVariables(q).Replace('/', '\\');
        }
        catch (Exception) { return null; }
    }

    /// <summary>Archivos que al «abrirse» se ejecutarían: Spotlight los muestra en el Explorador en vez de abrirlos (nunca ejecuta nada por haber escrito su nombre).</summary>
    public static bool IsExecutable(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".bat" or ".cmd" or ".com" or ".ps1" or ".vbs" or ".js" or ".jse" or ".wsf" or ".msi" or ".msp" or ".scr" or ".pif" or ".reg" or ".hta" or ".cpl";

    /// <summary>Nombres de los archivos recientes de Windows (accesos .lnk de la carpeta Recent) que coinciden, del más reciente al más antiguo.</summary>
    public static IReadOnlyList<SpotItem> MatchRecent(string q, IReadOnlyList<(string Name, string LinkPath, DateTime Modified)> recent, int max = 4) =>
        string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2 ? [] :
        recent.Select(r => (r, s: AppMatcher.Score(q, r.Name))).Where(x => x.s >= 400).OrderByDescending(x => x.s).ThenByDescending(x => x.r.Modified).Take(max)
            .Select(x => new SpotItem(SpotKind.File, x.r.Name, "Reciente", x.r.LinkPath)).ToList();
}

public enum SpotCommandKind { None, Redraw, Close, Open, Reveal, Delete, ToolAction }

/// <summary><see cref="Action"/>: for <see cref="SpotCommandKind.ToolAction"/>, the tool button pressed («repeat», «cancel», «copy»).</summary>
public readonly record struct SpotCommand(SpotCommandKind Kind, SpotItem? Item = null, string? Action = null)
{
    public static readonly SpotCommand None = new(SpotCommandKind.None), Redraw = new(SpotCommandKind.Redraw), Close = new(SpotCommandKind.Close);
}

/// <summary>Teclado y ratón de Spotlight: escribir, ↑/↓, Intro (abrir), Ctrl+Intro (mostrar en el Explorador), Esc (borrar y cerrar), clic en un resultado. Puro.</summary>
public sealed class SpotlightController
{
    public const double Width = 900, ListW = 540, FieldH = 56, RowH = 46, MinRows = 5;
    public string Query { get; private set; } = "";
    public IReadOnlyList<SpotItem> Results { get; private set; } = [];
    public int Selected { get; private set; } = -1;
    public int Hover { get; private set; } = -1;
    /// <summary>The result waiting for its confirming Enter (user scripts), or -1.</summary>
    public int Pending { get; private set; } = -1;

    /// <summary>List on the left, preview pane on the right (docs/27); room for the preview even with few results.</summary>
    /// <summary>
    /// Only the field while nothing is typed; once there is text the panel keeps its full height (an empty list shows «Sin resultados»). WM-021: the height
    /// used to jump between the two on every keystroke that had no results for a moment (typing «ping 8.» → «ping 8.8»), which made the window flicker.
    /// </summary>
    public double Height => FieldH + (Results.Count == 0 && Query.Length == 0 ? 0 : 8 + Math.Max(Results.Count, MinRows) * RowH + 8);
    /// <summary>The tallest the panel can be (the window's surface is created once at this size).</summary>
    public static double MaxHeight => FieldH + 8 + Math.Max(SpotlightEngine.MaxResults, MinRows) * RowH + 8;
    public LpRect Row(int i) => new(8, FieldH + 8 + i * RowH, ListW - 16, RowH);
    public LpRect PreviewRect => new(ListW, FieldH + 8, Width - ListW - 8, Height - FieldH - 16);
    public SpotItem? Current => Selected >= 0 && Selected < Results.Count ? Results[Selected] : null;

    public void Reset() { Query = ""; Results = []; Selected = -1; Hover = -1; Pending = -1; }

    /// <summary>A provider finished (ping, DNS…): replace that result in place, keeping the selection.</summary>
    public void Update(SpotItem item)
    {
        var l = Results.ToList(); int i = l.FindIndex(r => r.Target == item.Target);
        if (i < 0) return; l[i] = item; Results = l;
    }

    public void SetResults(IReadOnlyList<SpotItem> results)
    {
        string? keep = Selected >= 0 && Selected < Results.Count ? Results[Selected].Target : null;
        // a result that is the same as before keeps what a running tool wrote into it (WM-021: a refresh used to wipe «Ejecutando…»)
        var old = Results;
        results = results.Select(r => r.Details is null && old.FirstOrDefault(o => o.Target == r.Target && o.Kind == r.Kind) is { Details: { } d } ? r with { Details = d } : r).ToList();
        Results = results; Pending = -1; int i = keep is null ? -1 : results.ToList().FindIndex(r => r.Target == keep);
        Selected = results.Count == 0 ? -1 : i >= 0 ? i : 0;
    }

    /// <summary>Devuelve true si el texto cambió (el anfitrión vuelve a buscar).</summary>
    public bool Char(char c)
    {
        if (char.IsControl(c) || Query.Length >= 200) return false;
        Query += c; Pending = -1; return true;
    }

    public (SpotCommand Command, bool TextChanged) Key(int vk, bool ctrl) => Key(vk, ctrl, shift: false);

    public (SpotCommand Command, bool TextChanged) Key(int vk, bool ctrl, bool shift)
    {
        switch (vk)
        {
            case 0x1B: if (Query.Length > 0) { Reset(); return (SpotCommand.Redraw, true); } return (SpotCommand.Close, false);
            case 0x08:
                if (Query.Length == 0) return (SpotCommand.None, false);
                if (ctrl) { var t = Query.TrimEnd(); int sp = t.LastIndexOf(' '); Query = sp < 0 ? "" : t[..(sp + 1)]; } else Query = Query[..^1];
                return (SpotCommand.Redraw, true);
            case 0x09:   // Tab / Mayús+Tab: same as ↓ / ↑ (focus stays in the field)
                Pending = -1; if (Results.Count == 0) return (SpotCommand.None, false);
                Selected = shift ? (Selected <= 0 ? Results.Count - 1 : Selected - 1) : (Selected >= Results.Count - 1 ? 0 : Selected + 1);
                return (SpotCommand.Redraw, false);
            case 0x43 when ctrl && Current is { Kind: SpotKind.Tool, Details.Count: > 0 } tool:   // Ctrl+C: copy the tool's result
                return (new SpotCommand(SpotCommandKind.ToolAction, tool, "copy"), false);
            case 0x2E when ctrl && Selected >= 0 && Selected < Results.Count && Results[Selected].Kind == SpotKind.Clipboard:
                return (new SpotCommand(SpotCommandKind.Delete, Results[Selected]), false);
            case 0x26: Pending = -1; if (Results.Count > 0) Selected = Selected <= 0 ? Results.Count - 1 : Selected - 1; return (SpotCommand.Redraw, false);
            case 0x28: Pending = -1; if (Results.Count > 0) Selected = Selected >= Results.Count - 1 ? 0 : Selected + 1; return (SpotCommand.Redraw, false);
            case 0x0D:
                if (Selected < 0 || Selected >= Results.Count) return (SpotCommand.None, false);
                var it = Results[Selected];
                if (it.Confirm && Pending != Selected) { Pending = Selected; return (SpotCommand.Redraw, false); }   // first Enter only asks
                Pending = -1;
                return (new SpotCommand(ctrl && it.Kind is SpotKind.File or SpotKind.Folder or SpotKind.Path or SpotKind.Clipboard ? SpotCommandKind.Reveal : SpotCommandKind.Open, it), false);
        }

        return (SpotCommand.None, false);
    }

    public SpotCommand PointerMove(double x, double y) { int h = RowAt(x, y); if (h == Hover) return SpotCommand.None; Hover = h; return SpotCommand.Redraw; }

    public SpotCommand Click(double x, double y)
    {
        int r = RowAt(x, y); if (r < 0) return SpotCommand.None;
        if (Results[r].Confirm && Pending != r) { Selected = r; Pending = r; return SpotCommand.Redraw; }
        Pending = -1; return new SpotCommand(SpotCommandKind.Open, Results[r]);
    }

    private int RowAt(double x, double y) { for (int i = 0; i < Results.Count; i++) if (Row(i).Contains(x, y)) return i; return -1; }
}
