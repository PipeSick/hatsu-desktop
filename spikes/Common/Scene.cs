namespace WinMac.Spikes.Common;

public enum ItemKind { App, Folder, Trash }

/// <summary>Elemento del dock de prueba. Íconos 100 % originales (degradado + letra), sin recursos de Apple.</summary>
public sealed record DockItem(ItemKind Kind, string Glyph, byte R, byte G, byte B, bool IsOpen);

/// <summary>
/// Escena y geometría COMPARTIDAS por todos los prototipos (WPF, WinUI 3, Win32+DirectComposition).
/// Los valores son una APROXIMACIÓN [H] de la referencia macOS Tahoe: deben calibrarse contra capturas de
/// referencia tomadas por el usuario en su Mac (ver docs/02-fidelidad.md). No se usan medidas ni recursos oficiales de Apple.
/// </summary>
public static class DockScene
{
    public static readonly DockItem[] Items = BuildItems();
    /// <summary>Índice del primer ítem DESPUÉS del separador (apps | carpeta + papelera).</summary>
    public const int SeparatorBefore = 12;

    public const double BaseSize = 60;      // lado del ícono en reposo (DIP)
    public const double MaxScale = 1.7;     // magnificación máxima
    public const double Radius = 150;       // alcance de la magnificación (DIP)
    public const double Gap = 6;            // espacio entre íconos
    public const double PadX = 10;          // margen lateral de la barra
    public const double PadTop = 8, PadBottom = 14; // PadBottom incluye el espacio del indicador de app abierta
    public const double BarRadius = 26;     // radio de la barra
    public const double SeparatorGap = 10;  // espacio a cada lado del separador
    public const double DotSize = 4;        // indicador de app abierta
    public const double ShadowMargin = 28;  // margen para sombra alrededor de la barra

    public static double BarHeight => BaseSize + PadTop + PadBottom;
    /// <summary>Altura necesaria de ventana: barra + holgura de magnificación + margen de sombra.</summary>
    public static double WindowHeight => BarHeight + BaseSize * (MaxScale - 1) + ShadowMargin * 2;
    public static double RestWidth => Layout(null).BarWidth;
    public static double WindowWidth => RestWidth + (MaxScale - 1) * BaseSize * 3 + ShadowMargin * 2;
    /// <summary>Distancia que recorre el dock al ocultarse.</summary>
    public static double HideDistance => BarHeight + ShadowMargin + 10;

    private static DockItem[] BuildItems()
    {
        var list = new List<DockItem>();
        bool[] open = [true, false, true, true, false, false, true, false, false, true, false, false];
        for (int i = 0; i < 12; i++)
            list.Add(new DockItem(ItemKind.App, ((char)('A' + i)).ToString(),
                (byte)(60 + 14 * i), (byte)(110 + 9 * (i % 5)), (byte)(225 - 12 * i), open[i]));
        list.Add(new DockItem(ItemKind.Folder, "D", 70, 150, 230, false));
        list.Add(new DockItem(ItemKind.Trash, "T", 150, 155, 165, false));
        return list.ToArray();
    }

    public readonly record struct Frame(double[] X, double[] Size, double SeparatorX, double BarWidth);

    private static double ScaleAt(double distance)
    {
        double d = Math.Abs(distance);
        return d >= Radius ? 1 : 1 + (MaxScale - 1) * 0.5 * (1 + Math.Cos(Math.PI * d / Radius));
    }

    /// <summary>
    /// Posiciones X (relativas a la barra) y tamaños. <paramref name="cursorX"/> está en coordenadas del dock en reposo;
    /// null = sin cursor encima. La barra crece con la magnificación (los íconos nunca se solapan).
    /// </summary>
    public static Frame Layout(double? cursorX)
    {
        int n = Items.Length;
        var size = new double[n]; var x = new double[n];
        double restX = PadX;
        for (int i = 0; i < n; i++)
        {
            if (i == SeparatorBefore) restX += SeparatorGap * 2 + 1;
            double center = restX + BaseSize / 2;
            size[i] = cursorX is double c ? BaseSize * ScaleAt(center - c) : BaseSize;
            restX += BaseSize + Gap;
        }
        double pos = PadX, sepX = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == SeparatorBefore) { sepX = pos + SeparatorGap; pos += SeparatorGap * 2 + 1; }
            x[i] = pos; pos += size[i] + Gap;
        }
        return new Frame(x, size, sepX, pos - Gap + PadX);
    }

    /// <summary>Barrido determinista del cursor: va y viene sobre el dock en reposo cada <paramref name="period"/> s.</summary>
    public static double SweepX(double t, double restWidth, double period = 2.5)
        => restWidth * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / period - Math.PI / 2));
}

/// <summary>Silueta "squircle" (superelipse) compartida. Aproxima las esquinas continuas de iOS/macOS [H]; no es la curva oficial.</summary>
public static class Squircle
{
    public const double N = 5;
    /// <summary>Puntos normalizados 0..1 recorriendo el contorno.</summary>
    public static (double X, double Y)[] Unit(int segments = 72)
    {
        var pts = new (double, double)[segments];
        for (int i = 0; i < segments; i++)
        {
            double a = 2 * Math.PI * i / segments, c = Math.Cos(a), s = Math.Sin(a);
            double x = Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / N), y = Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / N);
            pts[i] = ((x + 1) / 2, (y + 1) / 2);
        }
        return pts;
    }
}
