namespace WinMac.Dock;

/// <summary>
/// Sombra por capas concéntricas SIN acumulación excesiva. El diseño anterior daba a cada capa una opacidad propia y las apilaba:
/// con 14 capas la opacidad total junto al borde era ≈ 51 % aunque la intención era 30 % (docs/09). Aquí se parte de la opacidad ACUMULADA deseada a cada distancia
/// y se calcula la opacidad de cada capa para obtenerla.
/// </summary>
public static class ShadowMath
{
    /// <summary>Opacidad acumulada objetivo a distancia normalizada u∈[0,1] del borde (1 = alcance máximo): peak·(1−smoothstep(u)).</summary>
    public static double Target(double u, double peak)
    {
        u = Math.Clamp(u, 0, 1); double s = u * u * (3 - 2 * u); return peak * (1 - s);
    }

    /// <summary>Opacidad de cada capa k=1..n (índice k−1). La capa k se extiende hasta k/n del alcance; la franja k queda cubierta por las capas k..n.</summary>
    public static double[] LayerAlphas(int n, double peak)
    {
        var a = new double[n]; double next = 0; // C_{n+1} = 0
        for (int k = n; k >= 1; k--)
        {
            double c = Target((k - 0.5) / n, peak);
            a[k - 1] = Math.Clamp(1 - (1 - c) / (1 - next), 0, 1); next = c;
        }
        return a;
    }

    /// <summary>Opacidad acumulada en la franja k (1..n) dada la lista de capas.</summary>
    public static double Cumulative(double[] alphas, int k)
    {
        double prod = 1; for (int j = k; j <= alphas.Length; j++) prod *= 1 - alphas[j - 1]; return 1 - prod;
    }
}
