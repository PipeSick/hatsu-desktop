namespace WinMac.Dock;

/// <summary>Silueta «squircle» (superelipse) compartida. Aproxima las esquinas continuas de iOS/macOS [H]; no es la curva oficial.</summary>
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
