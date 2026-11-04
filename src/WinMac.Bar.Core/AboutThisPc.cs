using System.Text.RegularExpressions;

namespace WinMac.Bar;

/// <summary>Una tarjeta gráfica tal como la informa Windows (sin la memoria: <c>AdapterRAM</c> se corta en 4 GB y daría datos falsos).</summary>
public sealed record GpuInfo(string Name);

/// <summary>Lo leído del equipo, sin interpretar. Todo es opcional: lo que Windows no informa queda en null y la ventana dice «No disponible» (nunca datos inventados).</summary>
public sealed record AboutRaw(string? Manufacturer, string? Model, string? Family, string? ComputerName, string? Cpu, int? CpuCores, int? CpuThreads, ulong? InstalledRamBytes, ulong? VisibleRamBytes,
    IReadOnlyList<GpuInfo> Gpus, string? WindowsProduct, string? WindowsDisplayVersion, int? Build, int? Revision, string? Architecture, string? BiosVersion, string WinMacVersion);

public sealed record AboutRow(string Label, string Value, bool Available = true);

/// <summary>Lo que muestra la ventana «Acerca de este PC».</summary>
public sealed record AboutView(string Title, string? Subtitle, IReadOnlyList<AboutRow> Main, IReadOnlyList<AboutRow> More, int OtherGpus);

/// <summary>Interpretación PURA de <see cref="AboutRaw"/> (se prueba sin Windows).</summary>
public static class AboutFormatter
{
    public const string NotAvailable = "No disponible";

    // Marcadores de posición que los fabricantes de placas dejan en la BIOS (muy comunes en equipos de escritorio armados a mano).
    private static readonly string[] Placeholders = ["to be filled by o.e.m.", "to be filled by oem", "system product name", "system manufacturer", "system version", "default string", "default", "not applicable", "n/a", "none", "unknown", "o.e.m.", "oem", "type1productconfigid", "0", "123456789"];

    public static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null; s = Regex.Replace(s.Trim(), @"\s+", " ");
        return Placeholders.Contains(s.ToLowerInvariant()) ? null : s;
    }

    /// <summary>«Fabricante Modelo» sin repetir el fabricante si el modelo ya lo incluye; null si no hay un modelo real.</summary>
    public static string? ModelName(string? manufacturer, string? model, string? family)
    {
        var m = Clean(model); var f = Clean(manufacturer); if (m is null) return null;
        if (f is not null && !m.StartsWith(f, StringComparison.OrdinalIgnoreCase)) m = f + " " + m;
        return m;
    }

    private static readonly string[] VirtualGpu = ["microsoft basic", "remote display", "hyper-v", "virtual", "parsec", "citrix", "vmware", "virtualbox", "spacedesk", "displaylink", "idd", "meta "];
    private static readonly Regex Discrete = new(@"(?i)\b(geforce|rtx|gtx|quadro|nvidia|radeon\s+(rx|pro|r[79]|vii)|arc\b|a\d{3,4}\b|firepro|titan)", RegexOptions.Compiled);
    private static readonly Regex Integrated = new(@"(?i)(uhd|iris|hd graphics|radeon\(tm\) graphics|radeon graphics|vega\s*\d*\s*graphics|intel\(r\)\s*graphics|adreno)", RegexOptions.Compiled);

    public static bool IsVirtualGpu(string name) => VirtualGpu.Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase));

    /// <summary>GPU principal: la dedicada antes que la integrada, ignorando adaptadores virtuales (pantallas remotas, controladores genéricos). Devuelve también cuántas más hay.</summary>
    public static (string? Primary, int Others) PrimaryGpu(IReadOnlyList<GpuInfo> gpus)
    {
        var real = gpus.Select(g => Clean(g.Name)).Where(n => n is not null).Select(n => n!).Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !IsVirtualGpu(n)).ToList();
        if (real.Count == 0) return (null, 0);
        int Score(string n) => Discrete.IsMatch(n) && !Integrated.IsMatch(n) ? 2 : Integrated.IsMatch(n) ? 0 : 1;
        var primary = real.OrderByDescending(Score).First();
        return (primary, real.Count - 1);
    }

    public static string FormatRam(ulong bytes)
    {
        double gib = bytes / (double)(1UL << 30); if (gib < 1) return $"{bytes / (double)(1UL << 20):0} MB";
        double r = Math.Round(gib); if (Math.Abs(gib - r) < 0.35 || gib >= 16) return $"{Math.Round(gib):0} GB";   // 15,9 GiB visibles = 16 GB instalados
        return $"{gib:0.#} GB";
    }

    /// <summary>«Windows 11 Pro»: el registro dice «Windows 10» incluso en Windows 11 (compilación ≥ 22000).</summary>
    public static string? WindowsName(string? product, int? build)
    {
        var p = Clean(product); if (p is null) return build is >= 22000 ? "Windows 11" : build is not null ? "Windows 10" : null;
        if (build is >= 22000 && p.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)) p = "Windows 11" + p[10..];
        return p;
    }

    public static string CleanCpu(string? cpu)
    {
        var c = Clean(cpu); if (c is null) return NotAvailable;
        c = c.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("(r)", ""); c = Regex.Replace(c, @"\s+", " ").Trim();
        c = Regex.Replace(c, @"\s*@\s*", " @ "); c = Regex.Replace(c, @"\s+(CPU|Processor)\b", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Replace("Processor", "").Trim();
        c = Regex.Replace(c, @"\s+\d+-Core\b", "", RegexOptions.IgnoreCase);
        return c.Length == 0 ? NotAvailable : c;
    }

    public static AboutView Build(AboutRaw r)
    {
        string? model = ModelName(r.Manufacturer, r.Model, r.Family), pc = Clean(r.ComputerName);
        string title = model ?? pc ?? "Este PC"; string? subtitle = model is not null ? pc : null;
        ulong? ram = r.InstalledRamBytes is > 0 ? r.InstalledRamBytes : r.VisibleRamBytes is > 0 ? r.VisibleRamBytes : null;
        var (gpu, others) = PrimaryGpu(r.Gpus); string? win = WindowsName(r.WindowsProduct, r.Build);
        string? winLine = win is null ? null : Clean(r.WindowsDisplayVersion) is { } dv ? $"{win} {dv}" : win;
        string? buildLine = r.Build is { } b ? r.Revision is { } rev ? $"{b}.{rev}" : b.ToString() : null;
        AboutRow Row(string l, string? v) => new(l, v ?? NotAvailable, v is not null);
        var main = new List<AboutRow>
        {
            r.Cpu is null || Clean(r.Cpu) is null ? Row("Procesador", null) : Row("Procesador", CleanCpu(r.Cpu)),
            Row("Memoria", ram is { } rb ? FormatRam(rb) : null),
            Row("Gráficos", gpu),
            Row("Windows", winLine),
            Row("WinMac", Clean(r.WinMacVersion)),
        };
        var more = new List<AboutRow>
        {
            Row("Nombre del equipo", pc), Row("Fabricante", Clean(r.Manufacturer)), Row("Compilación de Windows", buildLine), Row("Arquitectura", Clean(r.Architecture)),
            Row("Núcleos e hilos", r.CpuThreads is { } t ? r.CpuCores is { } cores ? $"{cores} núcleos · {t} hilos" : $"{t} hilos" : null), Row("BIOS", Clean(r.BiosVersion)),
        };
        return new AboutView(title, subtitle, main, more, others);
    }
}

/// <summary>Reparto (DIP) de la ventana «Acerca de este PC»; puro. Las alturas de las filas las mide quien dibuja (los valores largos pasan a dos líneas).</summary>
public sealed record AboutFrame(float Width, float Height, DipBox Icon, DipBox Title, DipBox Subtitle, IReadOnlyList<DipBox> Rows, float SeparatorY, DipBox MoreButton, DipBox WindowsLink, float LabelW, float ValueX);
public readonly record struct DipBox(float X, float Y, float W, float H) { public float Right => X + W; public float Bottom => Y + H; public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom; }

public static class AboutLayout
{
    public const float Width = 392, Pad = 28, IconH = 56, RowGap = 6, LabelW = 118, ButtonH = 36;

    public static AboutFrame Compute(float titleH, bool hasSubtitle, IReadOnlyList<float> rowHeights)
    {
        float y = 30, cx = Width / 2; var icon = new DipBox(cx - 36, y, 72, IconH); y += IconH + 14;
        var title = new DipBox(Pad, y, Width - 2 * Pad, Math.Max(26, titleH)); y += title.H;
        var sub = new DipBox(Pad, y + 2, Width - 2 * Pad, hasSubtitle ? 20 : 0); y += hasSubtitle ? 24 : 0; y += 18;
        var rows = new List<DipBox>(); float valueX = Pad + LabelW + 12;
        foreach (var h in rowHeights) { rows.Add(new DipBox(Pad, y, Width - 2 * Pad, h)); y += h + RowGap; }
        y += 10; float sepY = y; y += 18;
        var more = new DipBox(Pad, y, Width - 2 * Pad, ButtonH); y += ButtonH + 10;
        var link = new DipBox(Pad, y, Width - 2 * Pad, 24); y += 24 + 22;
        return new AboutFrame(Width, y, icon, title, sub, rows, sepY, more, link, LabelW, valueX);
    }
}
