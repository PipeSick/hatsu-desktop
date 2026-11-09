namespace WinMac.Bar.Tray;

/// <summary>
/// It7: legibilidad de los iconos de la bandeja (los que Windows dibuja para una barra OSCURA: Volumen, «Quitar hardware con seguridad»…) sobre el panel CLARO.
/// Causa: esos iconos son casi monocromos y blancos; el panel los pintaba tal cual sobre un fondo claro. Aquí se detecta ese caso por los píxeles reales del icono
/// (no por su nombre) y SOLO entonces se oscurecen los píxeles grises; un icono a color no se toca, y los píxeles de color de un icono mixto tampoco.
/// El simétrico (iconos casi negros sobre el panel oscuro) también se adapta. Trabaja sobre BGRA premultiplicado, como lo pide el dibujo.
/// </summary>
public static class IconAdapt
{
    private const int MinAlpha = 40, SatMax = 40, LightMin = 170, DarkMax = 85;
    private const double MajorityShare = 0.6;

    private static (int R, int G, int B) Straight(byte b, byte g, byte r, byte a) => (Math.Min(255, r * 255 / a), Math.Min(255, g * 255 / a), Math.Min(255, b * 255 / a));
    private static int Lum(int r, int g, int b) => (r * 299 + g * 587 + b * 114) / 1000;

    /// <summary>¿Hace falta adaptarlo? True si la mayoría de sus píxeles visibles son grises del tono que NO se ve en este tema.</summary>
    public static bool NeedsAdapting(ReadOnlySpan<byte> bgra, bool darkTheme)
    {
        int vis = 0, bad = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3]; if (a < MinAlpha) continue; vis++;
            var (r, g, b) = Straight(bgra[i], bgra[i + 1], bgra[i + 2], a); int sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)), l = Lum(r, g, b);
            if (sat <= SatMax && (darkTheme ? l <= DarkMax : l >= LightMin)) bad++;
        }
        return vis > 0 && bad >= vis * MajorityShare;
    }

    /// <summary>El icono con los píxeles grises invertidos hacia el tono visible en el tema (alfa intacto). Null si no hace falta tocarlo.</summary>
    public static byte[]? Adapt(byte[] bgra, bool darkTheme)
    {
        if (!NeedsAdapting(bgra, darkTheme)) return null;
        var o = (byte[])bgra.Clone();
        for (int i = 0; i + 3 < o.Length; i += 4)
        {
            byte a = o[i + 3]; if (a == 0) continue;
            var (r, g, b) = Straight(o[i], o[i + 1], o[i + 2], a); int sat = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)), l = Lum(r, g, b);
            if (sat > SatMax) continue;   // un píxel de color se queda como está
            int v = darkTheme ? 255 - (28 + l / 2) : 28 + (255 - l) / 2;
            if (darkTheme ? l > DarkMax + 60 : l < LightMin - 60) continue;   // grises intermedios (se leen en ambos temas)
            byte p = (byte)(v * a / 255); o[i] = p; o[i + 1] = p; o[i + 2] = p;
        }
        return o;
    }
}
