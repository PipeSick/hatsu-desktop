namespace WinMac.Bar;

public enum BarItemKind { WinMacMenu, AppName, Network, Volume, Battery, Clock }

/// <summary>
/// Un elemento de la barra. <see cref="Text"/> puede estar vacío (solo icono). <see cref="GlyphWidth"/> es el ancho fijo del icono (0 = sin icono).
/// El render mide el texto y el núcleo reparte el espacio (<see cref="BarLayout"/>).
/// </summary>
public sealed record BarItem(BarItemKind Kind, string Text, double GlyphWidth, bool Bold = false)
{
    public string Id => Kind.ToString();
}

public enum BarActionKind { None, OpenSettings, ShowMenu, Quit }
public sealed record BarAction(BarActionKind Kind, string? Uri = null);

/// <summary>Estado → elementos visibles (izquierda y derecha, en orden de pantalla). Sin lectura del sistema.</summary>
public static class BarModel
{
    public const double IconW = 16, BatteryW = 24;

    public static (IReadOnlyList<BarItem> Left, IReadOnlyList<BarItem> Right) Build(BarState s, BarConfig c)
    {
        var left = new List<BarItem> { new(BarItemKind.WinMacMenu, "", IconW) };
        if (!string.IsNullOrWhiteSpace(s.ActiveApp)) left.Add(new BarItem(BarItemKind.AppName, s.ActiveApp!, 0, Bold: true));

        var right = new List<BarItem>();
        if (c.ShowNetwork && s.Network.Kind != NetKind.Unknown) right.Add(new BarItem(BarItemKind.Network, "", IconW));
        if (c.ShowVolume && s.Volume.Available) right.Add(new BarItem(BarItemKind.Volume, "", IconW));
        if (c.ShowBattery && s.Battery.Present) right.Add(new BarItem(BarItemKind.Battery, c.ShowBatteryPercent ? $"{s.Battery.Percent}%" : "", BatteryW));
        right.Add(new BarItem(BarItemKind.Clock, ClockFormat.Format(s.Now, c), 0));
        return (left, right);
    }

    /// <summary>Qué hace cada elemento al hacer clic. Las páginas son URI documentadas de Configuración (ms-settings:).</summary>
    public static BarAction ActionFor(BarItemKind kind) => kind switch
    {
        BarItemKind.WinMacMenu => new BarAction(BarActionKind.ShowMenu),
        BarItemKind.Network => new BarAction(BarActionKind.OpenSettings, "ms-settings:network"),
        BarItemKind.Volume => new BarAction(BarActionKind.OpenSettings, "ms-settings:sound"),
        BarItemKind.Battery => new BarAction(BarActionKind.OpenSettings, "ms-settings:batterysaver"),
        BarItemKind.Clock => new BarAction(BarActionKind.OpenSettings, "ms-settings:dateandtime"),
        _ => new BarAction(BarActionKind.None),
    };

    public static bool IsClickable(BarItemKind kind) => ActionFor(kind).Kind != BarActionKind.None;
}
